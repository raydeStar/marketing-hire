using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class MemoryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-memory-" + Guid.NewGuid().ToString("N"));
    private Store store;
    public MemoryTests() { store = new(root); }
    private Runtime Runtime() => new(store, _ => throw new Exception("Memory maintenance must not infer."), new PlanValidator(), new EvidencePolicy());
    private RememberedEntry Remember()
    {
        var source = store.Page("notes/preferences.md") ?? store.Write("notes/preferences.md", "The workshop lasts 45 minutes.\nNever selected: fixture account details.", "absent");
        return store.Remember(Guid.NewGuid().ToString("N"), new("Workshops last 45 minutes.", new(source.Path, source.Version, "The workshop lasts 45 minutes."), "absent"));
    }
    private Run Selected(RememberedEntry entry, PolicyProfile? profile = null)
    {
        var run = CapabilityTests.CreateWorkerRun(store);
        run.Goal = run.Goal with { ReadScope = [], Memories = [new(entry.Id, entry.Version)] };
        run.Profile = profile ?? PolicyProfile.EvidenceMemory; store.Save(run, "test.memory-scope", new { }); return run;
    }
    [Fact] public void CorrectionIsVersionedAndForgettingCannotBeUndoneByDelayedSave()
    {
        var original = Remember(); var corrected = store.Remember(original.Id, new("A remembered workshop duration is 45 minutes.", original.Source!, original.Version));
        Assert.NotEqual(original.Version, corrected.Version);
        Assert.Throws<InvalidOperationException>(() => store.Remember(original.Id, new("Stale edit", original.Source!, original.Version)));
        var forgotten = store.ForgetMemory(original.Id, corrected.Version);
        Assert.True(forgotten.Forgotten); Assert.Equal("", forgotten.Statement); Assert.Null(forgotten.Source); Assert.Empty(store.Memories());
        Assert.Equal(forgotten, store.ForgetMemory(original.Id, corrected.Version));
        Assert.Throws<InvalidOperationException>(() => store.Remember(original.Id, new(original.Statement, original.Source!, "absent")));
        Assert.Equal(new[] { "remembered", "corrected", "forgotten" }, store.MemoryChanges().Select(change => change.Kind));
        Assert.DoesNotContain(original.Statement, Wire.Pack(store.MemoryChanges()));
        store.Dispose(); store = new(root); Assert.True(store.MemoryRecords().Single().Forgotten); Assert.NotNull(store.Page("notes/preferences.md"));
        store.DeletePersonalData(); Assert.Empty(store.MemoryRecords()); Assert.Empty(store.MemoryChanges());
    }
    [Theory] [InlineData("quote")] [InlineData("version")] [InlineData("path")]
    public void InvalidSourceCannotBecomeRememberedEvidence(string mode)
    {
        var original = Remember();
        var source = original.Source! with { Quote = mode == "quote" ? "Invented quotation" : original.Source!.Quote,
            Version = mode == "version" ? new string('0', 64) : original.Source.Version, Path = mode == "path" ? "../private.txt" : original.Source.Path };
        Assert.ThrowsAny<Exception>(() => store.Remember(Guid.NewGuid().ToString("N"), new("Unsupported claim", source, "absent")));
        Assert.Single(store.Memories()); Assert.Single(store.MemoryChanges());
    }
    [Fact] public void SourceEditRequiresExplicitReviewAndDoesNotSilentlyRefreshTheVersion()
    {
        var original = Remember(); var note = store.Page(original.Source!.Path)!;
        store.Write(note.Path, note.Content + "\nThe duration is under review.", note.Version);
        Assert.Equal("source-changed", store.Memories().Single().SourceStatus);
        Assert.Throws<InvalidOperationException>(() => store.Recall(new(original.Id, original.Version)));
        var corrected = store.Remember(original.Id, new(original.Statement, original.Source with { Version = store.Version(note.Path) }, original.Version));
        Assert.Equal("current", store.Memories().Single().SourceStatus); Assert.Equal(corrected, store.Recall(new(corrected.Id, corrected.Version)));
    }
    [Fact] public void MemoryAndChangeReceiptCommitOrRollbackTogether()
    {
        var original = Remember(); store.Dispose(); store = new(root, point => { if (point == "before-memory-commit") throw new IOException("Injected storage interruption."); });
        Assert.Throws<IOException>(() => store.ForgetMemory(original.Id, original.Version));
        Assert.Equal(original, store.Memories().Single().Entry); Assert.Single(store.MemoryChanges());
    }
    [Fact] public async Task ContextIncludesOnlySelectedQuotationAndDoesNotGrantItsWholeSourceNote()
    {
        var selected = Remember(); Remember(); var run = Selected(selected); var runtime = Runtime();
        var context = await runtime.PrepareExecutionContext(run.Id, default);
        Assert.Equal(selected, Assert.Single(context.Memories!)); Assert.Empty(context.Sources);
        Assert.Contains(selected.Statement, context.Text); Assert.DoesNotContain("fixture account details", context.Text);
        Assert.Single(store.Get(run.Id)!.MemoryEvidence); Assert.Equal(1, store.Get(run.Id)!.ToolCalls);
        var denied = await runtime.Call(run.Id, new("whole-note", "thaddeus_read_note", JsonSerializer.SerializeToElement(new { path = selected.Source!.Path })), default);
        Assert.True(denied.IsError); Assert.DoesNotContain("fixture account details", denied.Value.GetRawText());
    }
    [Fact] public async Task BaselineDoesNotActivateMemoryAndLegacyPolicyDigestStaysStable()
    {
        var run = Selected(Remember(), PolicyProfile.Baseline);
        var context = await Runtime().PrepareExecutionContext(run.Id, default);
        Assert.Empty(context.Memories!); Assert.Equal(0, store.Get(run.Id)!.ToolCalls);
        Assert.Equal(Wire.Hash("{\"id\":\"thaddeus-evidence\",\"version\":1,\"sourceContext\":true,\"validateEvidence\":true,\"repairLimit\":1}"), PolicyProfile.Evidence.Digest);
    }
    [Theory] [InlineData("correct")] [InlineData("forget")] [InlineData("source")]
    public async Task ChangedMemoryPreservesHistoricalContextButBlocksFurtherModelDispatch(string change)
    {
        var entry = Remember(); var run = Selected(entry); var runtime = Runtime(); var frozen = await runtime.PrepareExecutionContext(run.Id, default);
        Change(entry, change);
        Assert.Equal(Wire.Pack(frozen), Wire.Pack(await runtime.PrepareExecutionContext(run.Id, default)));
        var transport = new NoInference();
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Infer(run.Id, JsonSerializer.SerializeToElement(new { messages = new[] { new { role = "user", content = "Fixture" } } }), transport, new ModelAccessGate(), default));
        var saved = store.Get(run.Id)!; Assert.Equal(0, saved.ModelCalls); Assert.Equal(0, transport.Calls); Assert.Equal(RunState.NeedsAttention, saved.State);
        Assert.Single(store.AllEvents(), item => item.Type == "context.memory.invalidated");
    }
    [Fact] public async Task ForgettingAfterProposalPreventsImportButStillAllowsDenial()
    {
        var entry = Remember(); var run = Selected(entry); var runtime = Runtime(); await runtime.PrepareExecutionContext(run.Id, default);
        var proposal = await runtime.Call(run.Id, new("proposal", "thaddeus_propose_import", JsonSerializer.SerializeToElement(new { path = "plans/memory.md", artifact = "memory.md", content = "# Remembered duration\nA user assertion, not verified fact." })), default);
        Assert.False(proposal.IsError); var approval = store.Get(run.Id)!.Approval!; store.ForgetMemory(entry.Id, entry.Version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Decide(run.Id, approval.Id, approval.Digest, true));
        Assert.Equal("absent", store.Version("plans/memory.md"));
        Assert.Equal(RunState.Denied, (await runtime.Decide(run.Id, approval.Id, approval.Digest, false)).State);
    }
    [Fact] public async Task InFlightRevocationStillAccountsUsageButDoesNotReleaseTheModelReply()
    {
        var entry = Remember(); var run = Selected(entry); var runtime = Runtime();
        run.Goal = run.Goal with { Provider = new("compatible", "gpt-5.6-luna", "high", "http://127.0.0.1:5181/v1") }; store.Save(run, "test.fixture-provider", new { });
        await runtime.PrepareExecutionContext(run.Id, default);
        var transport = new RevokeDuringInference(() => store.ForgetMemory(entry.Id, entry.Version));
        var request = JsonSerializer.SerializeToElement(new { model = "gpt-5.6-luna", messages = new[] { new { role = "user", content = "Fictional transport request" } } });
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Infer(run.Id, request, transport, new ModelAccessGate(), default));
        var saved = store.Get(run.Id)!; Assert.Equal(1, transport.Calls); Assert.Equal(1, saved.ModelCalls);
        Assert.Equal(15, saved.ChargedTokens); Assert.Equal(0, saved.ReservedTokens); Assert.Equal(12, saved.InputTokens); Assert.Equal(3, saved.OutputTokens);
        Assert.Equal(RunState.NeedsAttention, saved.State); Assert.Equal("completed", saved.ModelDispatches.Single().Status);
    }
    private void Change(RememberedEntry entry, string change)
    {
        if (change == "forget") store.ForgetMemory(entry.Id, entry.Version);
        else if (change == "correct") store.Remember(entry.Id, new("Duration needs review.", entry.Source!, entry.Version));
        else { var page = store.Page(entry.Source!.Path)!; store.Write(page.Path, page.Content + "\nChanged source.", page.Version); }
    }
    private sealed class NoInference : IInferenceTransport
    {
        public int Calls;
        public Task<InferenceReply> Send(ProviderSnapshot provider, JsonElement request, CancellationToken cancellation)
        { Calls++; throw new Exception("Changed memory must block before inference."); }
    }
    private sealed class RevokeDuringInference(Action revoke) : IInferenceTransport
    {
        public int Calls;
        public Task<InferenceReply> Send(ProviderSnapshot provider, JsonElement request, CancellationToken cancellation)
        {
            Calls++; revoke(); return Task.FromResult(new InferenceReply(JsonSerializer.SerializeToElement(new { content = "A fictional reply based on now-forgotten context." }), 12, 3));
        }
    }
    public void Dispose() { store.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
}
