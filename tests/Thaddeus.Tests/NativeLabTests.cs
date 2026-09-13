using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;
using Thaddeus.Lab;

namespace Thaddeus.Tests;

public sealed class NativeLabTests
{
    private static readonly ProviderSnapshot Provider = new("compatible", "scripted-native-lab-v1", "high", "https://model.fixture.invalid/v1");
    private static readonly Budget Budget = new(ModelCalls: 8, ToolCalls: 16, Seconds: 180, MaxTotalTokens: 96000);
    private static readonly NativeLabCase Fixture = new("false-success", "negative", "A fictional workshop lasts 30 minutes.", 30, "Beginners", "duration-only");
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, Wire.Json);
    // Deliberately fabricated scorer input. Real protocol proof is supplied separately by the owned native campaign.
    private static NativeLabCapture Capture(int duration = 30, string audience = "Beginners")
    {
        var profile = PolicyProfile.NativeEvidence; const string context = "Explicit test source context";
        var body = Json(new { model = Provider.Model, reasoning_effort = "high", max_completion_tokens = 4096,
            messages = new[] { new { role = "system", content = context } }, tools = new[] { new { type = "function", function = new { name = "fixture" } } } });
        var content = "# Workshop brief\n\n" + Wire.Pack(new { durationMinutes = duration, audience, sourcePath = "notes/source.md", sourceQuote = Fixture.Note });
        var response = Json(new { usage = new { prompt_tokens = 100, completion_tokens = 30 } });
        var run = new Run
        {
            Goal = new("Fictional scorer case", ["notes/source.md"], "plans/", [], Budget, Provider, "research"), State = RunState.Succeeded,
            Profile = profile, Execution = new("openclaw", "thaddeus-" + Guid.NewGuid().ToString("N"), "fixture", OpenClawBackend.PinnedVersion),
            ModelCalls = 1, ToolCalls = 3, InputTokens = 100, OutputTokens = 30, ChargedTokens = 130,
            Evidence = [new("notes/source.md", Wire.Hash(Fixture.Note), Fixture.Note)],
            PreparedContext = new(1, profile.Digest, "persona", [], context, Wire.Hash(context), DateTimeOffset.UtcNow),
            Question = new("question", "Audience?", [], DateTimeOffset.UtcNow, audience, DateTimeOffset.UtcNow), OutputPath = "plans/report.md",
            ModelDispatches = [new("model-1", Wire.Hash(body.GetRawText()), DateTimeOffset.UtcNow, "completed", 96000, 100, 30, Wire.Hash(response.GetRawText()), Wire.Hash(context), true)],
            ExecutionCommands = [new("start", "start", "request", DateTimeOffset.UtcNow, "acknowledged"), new("resume", "resume", "request", DateTimeOffset.UtcNow, "acknowledged")]
        };
        var action = new ToolRequest("knowledge.write", "plans/report.md", content); var expiry = DateTimeOffset.UtcNow.AddMinutes(15);
        run.Approval = new("approval", run.Id, action, Runtime.ApprovalDigest(run.Id, "approval", action, "absent", expiry), "absent", expiry, "approved");
        run.Research = new("finished", "Fixture", new("approval", "report.md", Wire.Hash(content), DateTimeOffset.UtcNow), false);
        run.Capabilities.Add(new("proposal", "request", "thaddeus_propose_import", "broker-verified", DateTimeOffset.UtcNow,
            Json(new { approvalId = "approval", contentHash = Wire.Hash(content) })));
        RunEvent[] events = [new(1, "event", run.Id, 1, DateTimeOffset.UtcNow, "tool.result",
            Json(new ToolResult("knowledge.write", true, "Captured fixture write", new("plans/report.md", Wire.Hash(content), content))))];
        return new(new("negative-candidate-0", Fixture.Id, "candidate", 0), "registration", run, events,
            new("plans/report.md", content, Wire.Hash(content), DateTimeOffset.UtcNow), Wire.Hash(content), [new(1, body)], true, "purged", 100, 2, 10000, Responses: [new(1, response)]);
    }
    private static NativeLabGrade Grade(NativeLabCapture capture) => NativeLabScore.Grade(Fixture, capture, Provider, Budget,
        Wire.Hash("Explicit test source context"), PolicyProfile.NativeEvidence.Digest);

    private static NativeLabCapture ArtifactCapture(int duration = 30)
    {
        var capture = Capture(duration); var run = capture.Run; var approval = run.Approval!;
        run.Profile = PolicyProfile.ArtifactEvidence;
        run.PreparedContext = run.PreparedContext! with { ProfileDigest = run.Profile.Digest };
        EvidenceCitation[] citations = [new("notes/source.md", Wire.Hash(Fixture.Note), Fixture.Note)];
        var import = new ArtifactImport("import", "proposal", approval.Action.Path, "report.md", "absent", citations,
            DateTimeOffset.UtcNow, "ready-for-approval", capture.Page!.Version, DateTimeOffset.UtcNow, ApprovalId: approval.Id);
        run.ArtifactImports.Add(import);
        run.NativeProposals.Add(new("review", import.Path, import.Artifact, capture.Page.Content, capture.Page.Version, "fixture-proposal-hash",
            "passed", citations, new(true, ["Fixture source review"], [], []), DateTimeOffset.UtcNow, approval.Id));
        run.Capabilities[0] = run.Capabilities[0] with { Result = Json(new { importId = import.Id, status = "awaiting-artifact-review", artifact = import.Artifact }) };
        run.ExecutionCommands.Insert(1, new("quiesce-question", "quiesce", "fixture", DateTimeOffset.UtcNow, "acknowledged"));
        run.ExecutionCommands.Add(new("quiesce-import", "quiesce", "fixture", DateTimeOffset.UtcNow, "acknowledged"));
        return capture with
        {
            Item = new("live-artifact-0", "workshop", "artifact", 0),
            Events = [new(1, "capture", run.Id, 1, DateTimeOffset.UtcNow, "research.artifact.captured", Json(new { import, authority = "host-readback" })),
                new(1, "ready", run.Id, 2, DateTimeOffset.UtcNow, "research.awaiting-approval", Json(new { research = run.Research })),
                new(1, "approved", run.Id, 3, DateTimeOffset.UtcNow, "approval.approved", Json(approval)), capture.Events[0] with { Sequence = 4 }]
        };
    }
    private static NativeLabGrade GradeArtifact(NativeLabCapture capture) => NativeLabScore.Grade(Fixture, capture, Provider, Budget,
        Wire.Hash("Explicit test source context"), PolicyProfile.ArtifactEvidence.Digest, synthetic: false, importContract: 2, expectedObjective: "Fictional scorer case");

    [Fact] public void ArtifactContractRequiresHostCaptureInsteadOfAWorkerContentClaim()
    {
        var grade = GradeArtifact(ArtifactCapture()); Assert.Equal("VERIFIED", grade.Status); Assert.True(grade.ExactImport); Assert.True(grade.ContentPassed);
        var wrong = GradeArtifact(ArtifactCapture(45)); Assert.Equal("VERIFIED", wrong.Status); Assert.True(wrong.ExactImport);
        Assert.False(wrong.ContentPassed); Assert.True(wrong.FalseSuccess);
        Assert.Equal("INVALID", Grade(ArtifactCapture()).Status);
    }
    [Theory] [InlineData("capture")] [InlineData("hash")] [InlineData("version")] [InlineData("citation")] [InlineData("event")]
    [InlineData("ready")] [InlineData("order")] [InlineData("quiesce")] [InlineData("request")] [InlineData("objective")] [InlineData("source-review")]
    public void ArtifactPilotRejectsMissingOrAlteredIndependentBindings(string defect)
    {
        var capture = ArtifactCapture(); var run = capture.Run;
        if (defect == "capture") run.ArtifactImports.Clear();
        if (defect == "hash") run.ArtifactImports[0] = run.ArtifactImports[0] with { Sha256 = "different" };
        if (defect == "version") run.ArtifactImports[0] = run.ArtifactImports[0] with { ResourceVersion = "changed" };
        if (defect == "citation") run.ArtifactImports[0] = run.ArtifactImports[0] with { Citations = [] };
        if (defect == "event") capture = capture with { Events = capture.Events.Skip(1).ToArray() };
        if (defect == "ready") capture = capture with { Events = capture.Events.Where(record => record.Type != "research.awaiting-approval").ToArray() };
        if (defect == "order") capture = capture with { Events = [capture.Events[0], capture.Events[1] with { Sequence = 3 }, capture.Events[2] with { Sequence = 2 }, capture.Events[3]] };
        if (defect == "quiesce") run.ExecutionCommands.RemoveAt(1);
        if (defect == "request") run.Capabilities[0] = run.Capabilities[0] with { Result = Json(new { approvalId = "approval", contentHash = capture.Page!.Version }) };
        if (defect == "objective") run.Goal = run.Goal with { Objective = "Another task" };
        if (defect == "source-review") run.NativeProposals[0] = run.NativeProposals[0] with { Status = "not-evaluated" };
        Assert.Equal("INVALID", GradeArtifact(capture).Status);
    }
    [Fact] public void ClaimedArtifactCorrectionRequiresItsNativeDeliveryEvidence()
    {
        var capture = ArtifactCapture(); capture.Run.Repairs = 1;
        var grade = GradeArtifact(capture); Assert.Equal("INVALID", grade.Status); Assert.False(grade.ExactImport);
        Assert.Contains("The native artifact correction did not receive its recorded feedback.", grade.Problems);
    }
    [Fact] public void SingleArtifactPilotCannotClaimAPairedOrRepeatedImprovement()
    {
        var grade = GradeArtifact(ArtifactCapture()) with { CaseId = "workshop" };
        NativeLabItem[] plan = [new("live-artifact-0", "workshop", "artifact", 0)];
        var report = NativeLabScore.ArtifactPilotReport("registration", plan, [grade]);
        Assert.Equal("PASSED", report.ProtocolStatus); Assert.Equal("INCONCLUSIVE", report.Decision);
        Assert.Contains("NOT_EVALUATED", Wire.Pack(report.ModelCapacity)); Assert.Contains("NOT_RUN", Wire.Pack(report.RepeatedControls));
        Assert.Contains("\"promotion\":false", Wire.Pack(report.ProductQuality));
        Assert.Equal("INCOMPLETE_OR_FAILED", NativeLabScore.ArtifactPilotReport("registration", plan, []).ProtocolStatus);
        Assert.Equal("INCOMPLETE_OR_FAILED", NativeLabScore.ArtifactPilotReport("registration", plan, [grade, grade]).ProtocolStatus);
        Assert.Equal("INCOMPLETE_OR_FAILED", NativeLabScore.ArtifactPilotReport("registration", plan, [grade with { Usage = grade.Usage with { Complete = false } }]).ProtocolStatus);
    }

    [Fact] public void IndependentScorerSeparatesAnExactImportFromACorrectAnswer()
    {
        var valid = Grade(Capture()); Assert.Equal("VERIFIED", valid.Status); Assert.True(valid.ExactImport); Assert.True(valid.ContentPassed); Assert.False(valid.FalseSuccess);
        var wrong = Capture(45);
        wrong.Run.Validation = new(true, ["The production check said passed"], []);
        Assert.True(new ProposalEvidenceValidator().Assess(wrong.Run, wrong.Page!.Content,
            [new("notes/source.md", Wire.Hash(Fixture.Note), Fixture.Note)]).Passed);
        var result = Grade(wrong); Assert.Equal("VERIFIED", result.Status); Assert.True(result.ExactImport);
        Assert.False(result.ContentPassed); Assert.True(result.FalseSuccess); Assert.Contains("Duration contradicts the frozen source.", result.ContentProblems);
    }
    [Fact] public void RecordedUserAnswerIsAnIndependentOutputConstraint()
    {
        var capture = Capture(30, "Developers"); capture.Run.Question = capture.Run.Question! with { Answer = Fixture.Answer };
        var grade = Grade(capture); Assert.True(grade.ExactImport); Assert.False(grade.ContentPassed);
        Assert.Contains("Audience differs from the recorded user answer.", grade.ContentProblems);
    }
    [Theory] [InlineData("file")] [InlineData("approval")] [InlineData("event")] [InlineData("readback")]
    public void ARunSuccessFlagCannotSubstituteForIndependentEffectEvidence(string missing)
    {
        var capture = Capture();
        if (missing == "file") capture = capture with { ObservedFileHash = Wire.Hash("different") };
        if (missing == "approval") capture.Run.Approval = capture.Run.Approval! with { Decision = "pending" };
        if (missing == "event") capture = capture with { Events = [] };
        if (missing == "readback") capture.Run.Research = capture.Run.Research! with { Review = null };
        var grade = Grade(capture); Assert.False(grade.ExactImport); Assert.Equal("INVALID", grade.Status);
    }
    [Theory] [InlineData("missing")] [InlineData("hash")] [InlineData("context")] [InlineData("budget")] [InlineData("model")] [InlineData("duplicate-event")] [InlineData("response")] [InlineData("missing-response")]
    public void FrozenProtocolDriftInvalidatesTheComparison(string drift)
    {
        var capture = Capture();
        if (drift == "missing") capture = capture with { Requests = [] };
        if (drift == "hash") capture.Run.ModelDispatches[0] = capture.Run.ModelDispatches[0] with { RequestHash = "wrong" };
        if (drift == "context") capture.Run.PreparedContext = null;
        if (drift == "budget") capture.Run.Goal = capture.Run.Goal with { Limits = Budget with { ModelCalls = 12 } };
        if (drift == "model") capture.Run.Goal = capture.Run.Goal with { Provider = Provider with { Model = "different-model" } };
        if (drift == "duplicate-event") capture = capture with { Events = [capture.Events[0], capture.Events[0]] };
        if (drift == "response") capture = capture with { Responses = [new(1, Json(new { usage = new { prompt_tokens = 0, completion_tokens = 0 } }))] };
        if (drift == "missing-response") capture = capture with { Responses = [] };
        Assert.Equal("INVALID", Grade(capture).Status);
    }
    [Fact] public void UnknownUsageKeepsItsChargeAndNeverBecomesMeasuredZero()
    {
        var capture = Capture(); capture.Run.ModelDispatches[0] = capture.Run.ModelDispatches[0] with { InputTokens = null, OutputTokens = null, Status = "completed-usage-unknown" };
        capture.Run.InputTokens = null; capture.Run.OutputTokens = null; capture.Run.ChargedTokens = Budget.MaxTotalTokens;
        var usage = NativeLabScore.Usage(capture.Run); Assert.False(usage.Complete); Assert.Null(usage.InputTokens); Assert.Null(usage.OutputTokens);
        Assert.Equal(Budget.MaxTotalTokens, usage.ChargedTokens);
        capture.Run.ReservedTokens = 64; Assert.Equal(64, NativeLabScore.Usage(capture.Run).ReservedTokens);
    }
    [Fact] public void MissingCasesOrNegativeControlCanNeverProduceAPassingCampaign()
    {
        var grade = Grade(Capture());
        var report = NativeLabScore.Report("registration", [new("negative-candidate-0", "false-success", "candidate", 0)], [grade]);
        Assert.Equal("INCOMPLETE_OR_FAILED", report.ProtocolStatus); Assert.Equal("INCONCLUSIVE", report.Decision);
        Assert.NotEmpty(report.Problems); Assert.Contains("NOT_EVALUATED", Wire.Pack(report.ModelCapacity));
    }
    [Fact] public void FailedInfrastructureStillReportsCapturedUsageAsAnInvalidCase()
    {
        var capture = Capture() with { InfrastructureFailure = "Fixture worker stopped unexpectedly" };
        var grade = Grade(capture); Assert.Equal("INVALID", grade.Status);
        Assert.Equal(1, grade.Usage.Calls); Assert.Equal(100, grade.Usage.InputTokens); Assert.Equal(30, grade.Usage.OutputTokens);
        Assert.Contains(grade.Problems, problem => problem.StartsWith("Recorded execution/capture failure:", StringComparison.Ordinal));
    }
    [Theory] [InlineData("\n", false)] [InlineData("\n", true)] [InlineData("\r\n", true)]
    public void LiveReportAcceptsOnlyThePredeclaredPlainOrFencedJson(string newline, bool fenced)
    {
        var body = "{\"durationMinutes\":30}";
        var content = "# Workshop brief" + newline + newline + (fenced ? "```json" + newline + body + newline + "```" : body);
        using var document = NativeLabScore.ParseReport(content, allowMarkdownFence: true);
        Assert.Equal(30, document.RootElement.GetProperty("durationMinutes").GetInt32());
        Assert.ThrowsAny<JsonException>(() => NativeLabScore.ParseReport(content + "\nExtra prose", allowMarkdownFence: true));
        if (fenced) Assert.ThrowsAny<JsonException>(() => NativeLabScore.ParseReport(content));
    }
    [Fact] public void LiveUsageRetainsUnknownChargesWithoutSyntheticOrBillingClaims()
    {
        var capture = Capture();
        var known = NativeLabScore.Usage(capture.Run, synthetic: false);
        Assert.True(known.Complete); Assert.Equal(100, known.InputTokens); Assert.Contains("Provider-reported", known.Authority);
        capture.Run.ModelDispatches[0] = capture.Run.ModelDispatches[0] with { InputTokens = null, OutputTokens = null, Status = "unknown" };
        capture.Run.ChargedTokens = 96000;
        var unknown = NativeLabScore.Usage(capture.Run, synthetic: false);
        Assert.False(unknown.Complete); Assert.Null(unknown.InputTokens); Assert.Equal(96000, unknown.ChargedTokens);
    }
    [Fact] public void PairedLivePilotSeparatesFalseSuccessAndCannotClaimReproductionOrPromotion()
    {
        var grade = Grade(Capture(45));
        NativeLabItem[] plan = [new("live-unchanged-0", "workshop", "unchanged", 0), new("live-candidate-0", "workshop", "candidate", 0)];
        var grades = plan.Select(item => grade with { Item = item.Id, CaseId = item.CaseId, Arm = item.Arm }).ToArray();
        var report = NativeLabScore.LivePilotReport("registration", plan, grades);
        Assert.Equal("PASSED", report.ProtocolStatus); Assert.Equal("INCONCLUSIVE", report.Decision);
        Assert.All(report.Results, result => Assert.True(result.FalseSuccess));
        Assert.Contains("\"promotion\":false", Wire.Pack(report.ProductQuality));
        Assert.Contains("NOT_RUN", Wire.Pack(report.RepeatedControls));
        Assert.Contains("NOT_EVALUATED", Wire.Pack(report.ModelCapacity));
        Assert.Equal("INCOMPLETE_OR_FAILED", NativeLabScore.LivePilotReport("registration", plan, [grades[0]]).ProtocolStatus);
        Assert.Equal("INCOMPLETE_OR_FAILED", NativeLabScore.LivePilotReport("registration", plan, [grades[0], grades[0]]).ProtocolStatus);
    }
    [Fact] public void RawResponseBytesSurviveUnicodeReencodingWithoutHidingTamperedContent()
    {
        var capture = Capture();
        const string raw = "{\"message\":\"I’m drafting a café brief.\",\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":30}}";
        using var document = JsonDocument.Parse(raw);
        capture.Run.ModelDispatches[0] = capture.Run.ModelDispatches[0] with { ResponseHash = Wire.Hash(raw) };
        capture = capture with { Responses = [new(1, document.RootElement.Clone(), raw)] };
        var serialized = Wire.Unpack<NativeLabCapture>(Wire.Pack(capture));
        Assert.NotEqual(Wire.Hash(raw), Wire.Hash(serialized.Responses![0].Body.GetRawText()));
        Assert.Equal("VERIFIED", Grade(serialized).Status);
        var altered = serialized with { Responses = [serialized.Responses[0] with { Body = Json(new { message = "Changed", usage = new { prompt_tokens = 100, completion_tokens = 30 } }) }] };
        Assert.Equal("INVALID", Grade(altered).Status);
        var changedOriginal = serialized with { Responses = [serialized.Responses[0] with { RawJson = raw.Replace("café", "book") }] };
        Assert.Equal("INVALID", Grade(changedOriginal).Status);
    }
}
