using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class ProposalEvidenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-proposal-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    private Runtime runtime;
    private const string Source = "A fictional workshop lasts 45 minutes.";
    private const string Content = "# Research\nSource: notes/source.md\n> " + Source;
    public ProposalEvidenceTests()
    {
        store = new(root); store.Write("notes/source.md", Source, "absent"); runtime = MakeRuntime();
    }
    private Runtime MakeRuntime(IProposalEvidenceValidator? validator = null) => new(store,
        _ => throw new Exception("Quotation checks may not dispatch a host model."), new PlanValidator(), new EvidencePolicy(), proposalEvidence: validator);
    private Run Create(PolicyProfile? profile = null)
    {
        var run = CapabilityTests.CreateWorkerRun(store); run.Profile = profile ?? PolicyProfile.NativeEvidence;
        run.Evidence.Add(store.Read("notes/source.md").Evidence!); Save(run); return run;
    }
    private void Save(Run run) => store.Save(run, "fixture.proposal", new { syntheticWorker = true });
    private EvidenceCitation Citation(string quote = Source) => new("notes/source.md", store.Version("notes/source.md"), quote);
    private Task<CapabilityResult> Propose(Run run, string id, string content = Content, EvidenceCitation[]? citations = null) => Call(run, id,
        new { path = "plans/research.md", artifact = "research.md", content, citations = citations ?? [Citation()] });
    private Task<CapabilityResult> Call(Run run, string id, object arguments) => runtime.Call(run.Id,
        new(id, "thaddeus_propose_import", JsonSerializer.SerializeToElement(arguments, Wire.Json)), default);
    private Run Saved(Run run) => store.Get(run.Id)!;

    [Fact] public async Task FailedQuotationIsDurableIdempotentAndRepairDoesNotExpandAnyBudget()
    {
        var run = Create(); var first = await Propose(run, "bad", citations: [Citation("Invented statement.")]);
        Assert.True(first.IsError); Assert.Equal("repair-requested", first.Value.GetProperty("status").GetString());
        var failed = Saved(run); Assert.Null(failed.Approval); Assert.Equal(RunState.Running, failed.State);
        Assert.Equal(1, failed.Repairs); Assert.Equal(0, failed.ModelCalls); Assert.Equal(run.Goal.Limits, failed.Goal.Limits);
        Assert.Equal(Content, Assert.Single(failed.NativeProposals).Content); Assert.Equal("absent", store.Version("plans/research.md"));
        Assert.Equal(first.Value.GetRawText(), (await Propose(run, "bad", citations: [Citation("Invented statement.")])).Value.GetRawText());
        Assert.Equal(1, Saved(run).ToolCalls); Assert.Single(Saved(run).NativeProposals);
        Assert.Contains(store.AllEvents(), e => e.Type == "capability.result" && e.Data.GetProperty("isError").GetBoolean());
        Assert.False((await Propose(run, "fixed")).IsError);
        var saved = Saved(run); var approval = saved.Approval!;
        Assert.Equal(new[] { "repair-requested", "passed" }, saved.NativeProposals.Select(r => r.Status));
        Assert.Equal(approval.Id, saved.NativeProposals[^1].ApprovalId); Assert.Equal("absent", store.Version("plans/research.md"));
        await runtime.Decide(run.Id, approval.Id, approval.Digest, true);
        Assert.Equal(Content, store.Page("plans/research.md")!.Content); Assert.Equal(1, Saved(run).Repairs);
        Assert.Equal(0, Saved(run).ModelCalls); Assert.Single(store.Revisions("plans/research.md"));
        Assert.NotEmpty(saved.NativeProposals[^1].Assessment.Unverified);
    }

    [Theory] [InlineData("repeated")] [InlineData("changed")]
    public async Task SecondFailedProposalStopsWithoutApproval(string second)
    {
        var run = Create(); await Propose(run, "bad", citations: []);
        var result = await Propose(run, "bad-again", content: second == "repeated" ? Content : Content + "\nStill missing citations.", citations: []);
        Assert.True(result.IsError); Assert.Equal(second == "repeated" ? "repeated-failure" : "repair-exhausted", result.Value.GetProperty("status").GetString());
        Assert.Equal(RunState.NeedsAttention, Saved(run).State); Assert.Null(Saved(run).Approval); Assert.Equal(1, Saved(run).Repairs);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Propose(run, "third"));
        Assert.Equal(2, Saved(run).NativeProposals.Count); Assert.Equal("absent", store.Version("plans/research.md"));
    }

    [Theory] [InlineData("repairs")] [InlineData("models")] [InlineData("tokens")] [InlineData("reserved")] [InlineData("tools")]
    public async Task ExhaustedAllowanceNeverOffersAnUnfundedRepair(string budget)
    {
        var run = Create();
        if (budget == "repairs") run.Goal = run.Goal with { Limits = run.Goal.Limits with { Repairs = 0 } };
        if (budget == "models") run.ModelCalls = run.Goal.Limits.ModelCalls;
        if (budget == "tokens") run.ChargedTokens = run.Goal.Limits.MaxTotalTokens;
        if (budget == "reserved") run.ReservedTokens = 1;
        if (budget == "tools") run.Goal = run.Goal with { Limits = run.Goal.Limits with { ToolCalls = 2 } };
        Save(run); var result = await Propose(run, "bad", citations: []);
        Assert.True(result.IsError); Assert.Equal("repair-exhausted", result.Value.GetProperty("status").GetString());
        Assert.Equal(0, Saved(run).Repairs); Assert.Null(Saved(run).Approval); Assert.Equal(RunState.NeedsAttention, Saved(run).State);
    }

    [Theory] [InlineData("empty")] [InlineData("extra")] [InlineData("null")]
    public async Task MalformedCitationsReceiveBoundedFeedback(string shape)
    {
        var run = Create(); object? citations = shape switch
        {
            "empty" => new[] { new { source = "notes/source.md", version = store.Version("notes/source.md"), quote = "" } },
            "extra" => new[] { new { source = "notes/source.md", version = store.Version("notes/source.md"), quote = Source, extra = true } },
            _ => null
        };
        var arguments = new { path = "plans/research.md", artifact = "research.md", content = Content, citations };
        Assert.True((await Call(run, "bad", arguments)).IsError); Assert.Equal(1, Saved(run).Repairs);
        Assert.True((await Call(run, "repeat", arguments)).IsError); Assert.Equal(RunState.NeedsAttention, Saved(run).State);
        Assert.Null(Saved(run).Approval);
    }

    [Fact] public async Task MissingCitationFieldCannotUseTheLegacyContractOnANewTask()
    {
        var run = Create();
        var result = await Call(run, "legacy", new { path = "plans/research.md", artifact = "research.md", content = Content });
        Assert.True(result.IsError); Assert.Equal("repair-requested", result.Value.GetProperty("status").GetString()); Assert.Null(Saved(run).Approval);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Propose(run, "legacy"));
    }

    [Theory] [InlineData("throw")] [InlineData("inconsistent")]
    public async Task UnavailableOrInconsistentValidatorFailsClosed(string failure)
    {
        runtime = MakeRuntime(new BrokenValidator(failure)); var run = Create(); var result = await Propose(run, "proposal");
        Assert.True(result.IsError); Assert.Equal("validation-unavailable", result.Value.GetProperty("status").GetString());
        Assert.Equal(RunState.NeedsAttention, Saved(run).State); Assert.Null(Saved(run).Approval); Assert.Equal(0, Saved(run).Repairs);
        Assert.DoesNotContain("internal exception detail", result.Value.GetRawText());
    }
    private sealed class BrokenValidator(string failure) : IProposalEvidenceValidator
    {
        public ProposalEvidenceAssessment Assess(Run run, string content, EvidenceCitation[] citations) =>
            failure == "throw" ? throw new IOException("internal exception detail") : new(true, [], ["Unresolved failure"], []);
    }

    [Theory] [InlineData("before")] [InlineData("after")]
    public async Task SourceChangesRequireNewContextAndNeverAnOldImport(string when)
    {
        var run = Create(); if (when == "after") Assert.False((await Propose(run, "proposal")).IsError);
        store.Write("notes/source.md", "Changed source.", store.Version("notes/source.md"));
        if (when == "before")
        {
            var response = await Propose(run, "proposal"); Assert.Equal("source-changed", response.Value.GetProperty("status").GetString());
            Assert.Null(Saved(run).Approval); Assert.Equal(0, Saved(run).Repairs);
        }
        else
        {
            var approval = Saved(run).Approval!;
            await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Decide(run.Id, approval.Id, approval.Digest, true));
        }
        Assert.Equal("absent", store.Version("plans/research.md"));
    }

    [Theory] [InlineData("approval")] [InlineData("content")] [InlineData("citation")] [InlineData("missing")]
    public async Task ImportRequiresTheMatchingReviewAndExactContent(string drift)
    {
        var run = Create(); await Propose(run, "proposal"); var saved = Saved(run); var review = saved.NativeProposals.Single();
        if (drift == "missing") saved.NativeProposals.Clear();
        else saved.NativeProposals[0] = drift switch
        {
            "approval" => review with { ApprovalId = "wrong" },
            "content" => review with { Content = "Different content" },
            _ => review with { Citations = [Citation("Different quote")] }
        };
        Save(saved); var approval = saved.Approval!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Decide(run.Id, approval.Id, approval.Digest, true));
        Assert.Equal("absent", store.Version("plans/research.md"));
    }

    [Fact] public async Task ExplicitUncheckedControlRecordsNoPassAndStillRequiresExactApproval()
    {
        var run = Create(PolicyProfile.NativeUnchecked); Assert.False((await Propose(run, "control", citations: [])).IsError);
        var saved = Saved(run); var review = saved.NativeProposals.Single();
        Assert.Equal("not-evaluated", review.Status); Assert.False(review.Assessment.Passed); Assert.Empty(review.Assessment.Checks);
        Assert.Equal("absent", store.Version("plans/research.md")); var approval = saved.Approval!;
        await runtime.Decide(run.Id, approval.Id, approval.Digest, true); Assert.Equal(Content, store.Page("plans/research.md")!.Content);
    }

    [Theory] [InlineData("unknown")] [InlineData("version")] [InlineData("quote")] [InlineData("content")] [InlineData("reference")] [InlineData("duplicate")] [InlineData("blank")]
    public void CapturedProvenanceMustMatchEveryDeclaredQuotation(string problem)
    {
        var run = Create(); var citation = Citation(); var content = Content;
        if (problem == "unknown") citation = citation with { Source = "notes/private.md" };
        if (problem == "version") citation = citation with { Version = Wire.Hash("wrong") };
        if (problem == "quote") citation = citation with { Quote = "An invented fact." };
        if (problem == "content") content = "Source: notes/source.md";
        if (problem == "reference") content = Source;
        if (problem == "blank") citation = citation with { Quote = "" };
        var result = new ProposalEvidenceValidator().Assess(run, content, problem == "duplicate" ? [citation, citation] : [citation]);
        Assert.False(result.Passed); Assert.NotEmpty(result.Problems);
    }

    [Fact] public async Task MemoryCitationUsesOnlyTheSelectedQuoteAndRevocationStopsProposal()
    {
        var source = store.Page("notes/source.md")!;
        var memory = store.Remember(Guid.NewGuid().ToString("N"), new("Workshop length", new(source.Path, source.Version, "45 minutes"), "absent"));
        var run = Create(); run.Evidence.Clear();
        run.Goal = run.Goal with { ReadScope = [], Memories = [new(memory.Id, memory.Version)] };
        Save(run); await runtime.PrepareExecutionContext(run.Id, default); run = Saved(run);
        var citation = new EvidenceCitation("memory:" + memory.Id, memory.Version, "45 minutes");
        var validator = new ProposalEvidenceValidator(); Assert.True(validator.Assess(run, Content, [citation]).Passed);
        Assert.False(validator.Assess(run, Content, [citation with { Quote = Source }]).Passed);
        Assert.False(validator.Assess(run, Content, [citation with { Source = "memory:" + Guid.NewGuid().ToString("N") }]).Passed);
        store.ForgetMemory(memory.Id, memory.Version);
        var result = await Propose(run, "forgotten", citations: [citation]);
        Assert.Equal("source-changed", result.Value.GetProperty("status").GetString()); Assert.Null(Saved(run).Approval);
    }

    [Fact] public void PublicCitationRequiresBrokerCapturedTextHashAndGrantedDestination()
    {
        var run = Create(); run.Goal = run.Goal with { Web = new(["docs.example.com"]) };
        const string url = "https://docs.example.com/research"; const string quote = "A public observation.";
        var source = new PublicWebSource(url, "Title", DateTimeOffset.UtcNow, "text/html", 100, Wire.Hash("html"), quote, Wire.Hash(quote), true);
        var receipt = new CapabilityReceipt("fetch", "request", "thaddeus_fetch_public_page", "broker-observed", DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new PublicWebResult(source, []), Wire.Json));
        run.Capabilities.Add(receipt); var validator = new ProposalEvidenceValidator();
        var citation = new EvidenceCitation(url, source.TextSha256, quote);
        var result = validator.Assess(run, quote + "\n" + url, [citation]); Assert.True(result.Passed); Assert.Contains("truncated", result.Checks.Single());
        run.Capabilities[0] = receipt with { Authority = "worker-reported" };
        Assert.Throws<InvalidOperationException>(() => validator.Assess(run, quote + url, [citation]));
        run.Capabilities[0] = receipt with { Result = JsonSerializer.SerializeToElement(new PublicWebResult(source with { Text = "Changed text" }, []), Wire.Json) };
        Assert.Throws<InvalidOperationException>(() => validator.Assess(run, quote + url, [citation]));
        run.Capabilities[0] = receipt; run.Goal = run.Goal with { Web = new(["other.example.com"]) };
        Assert.Throws<ArgumentException>(() => validator.Assess(run, quote + url, [citation]));
    }

    [Fact] public void LegacyProfileDigestsAndToolContractsRemainStable()
    {
        foreach (var profile in new[] { PolicyProfile.Baseline, PolicyProfile.Evidence })
        {
            Assert.Equal(Wire.Hash(Wire.Pack(new { profile.Id, profile.Version, profile.SourceContext, profile.ValidateEvidence, profile.RepairLimit })), profile.Digest);
            var old = Wire.Unpack<PolicyProfile>(Wire.Pack(new { profile.Id, profile.Version, profile.SourceContext, profile.ValidateEvidence, profile.RepairLimit }));
            Assert.Equal(profile, old); old.Validate();
        }
        var p = PolicyProfile.EvidenceMemory;
        Assert.Equal(Wire.Hash(Wire.Pack(new { p.Id, p.Version, p.SourceContext, p.ValidateEvidence, p.RepairLimit, p.MemoryContext })), p.Digest);
        var legacy = Create(PolicyProfile.Evidence); var current = Create();
        Assert.False(runtime.ToolsFor(legacy.Id).Single(t => t.Name == "thaddeus_propose_import").InputSchema.GetProperty("properties").TryGetProperty("citations", out _));
        Assert.True(runtime.ToolsFor(current.Id).Single(t => t.Name == "thaddeus_propose_import").InputSchema.GetProperty("properties").TryGetProperty("citations", out _));
    }
    public void Dispose() { store.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
}
