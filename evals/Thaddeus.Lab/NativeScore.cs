using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Lab;

public record NativeLabCase(string Id, string Split, string Note, int DurationMinutes, string Answer, string Fault);
public record NativeLabItem(string Id, string CaseId, string Arm, int Repeat);
public record NativeLabRequest(int Number, JsonElement Body, string? RawJson = null);
public record NativeLabResponse(int Number, JsonElement Body, string? RawJson = null);
public record NativeLabCapture(NativeLabItem Item, string RegistrationHash, Run Run, RunEvent[] Events,
    Page? Page, string? ObservedFileHash, NativeLabRequest[] Requests, bool GrantRevoked, string WorkerStatus,
    long ElapsedMs, double HostCpuMs, long SharedHostPeakBytes, string? InfrastructureFailure = null, NativeLabResponse[]? Responses = null);
public record NativeLabUsage(int Calls, int? InputTokens, int? OutputTokens, int ChargedTokens, int ReservedTokens,
    bool Complete, string Authority);
public record NativeLabGrade(string Item, string CaseId, string Arm, int Repeat, string Status, bool ExactImport,
    bool ContentPassed, bool FalseSuccess, bool ContextObserved, bool RepairFeedbackObserved,
    string? NativeToolSchemaHash, NativeLabUsage Usage, string[] Problems, string[] ContentProblems, string? OutputHash = null);
public record NativeLabReport(int SchemaVersion, string RegistrationHash, int Planned, int Captured, string ProtocolStatus,
    string Decision, object ModelCapacity, object TaskCapability, object ProductQuality, object RepeatedControls,
    string[] Problems, NativeLabGrade[] Results, string? EvaluatorSha256 = null, DateTimeOffset? Graded = null);

/// <summary>Independent fixture scoring. Production validation results are evidence inputs, never the answer key.</summary>
public static partial class NativeLabScore
{
    public static NativeLabGrade Grade(NativeLabCase fixture, NativeLabCapture capture, ProviderSnapshot provider, Budget budget,
        string expectedContextHash, string expectedPolicyDigest, bool synthetic = true, int importContract = 1, string? expectedObjective = null)
    {
        var run = capture.Run; var problems = new List<string>(); var contentProblems = new List<string>();
        if (importContract is not (1 or 2) || run.Profile?.ProposalEvidenceVersion != importContract ||
            importContract == 2 && expectedPolicyDigest != PolicyProfile.ArtifactEvidence.Digest)
            problems.Add("The capture differs from the registered import contract.");
        if (expectedObjective != null && run.Goal.Objective != expectedObjective) problems.Add("The task objective differs from registration.");
        // Preserve the old serialized field while avoiding a diagnosis the generic runner exception cannot establish.
        if (capture.InfrastructureFailure != null) problems.Add("Recorded execution/capture failure: " + capture.InfrastructureFailure);
        if (run.Goal.Provider != provider || run.Goal.Limits != budget || run.Profile?.Digest != expectedPolicyDigest ||
            run.Execution?.Backend != "openclaw" || run.Execution.RuntimeVersion != OpenClawBackend.PinnedVersion)
            problems.Add("Frozen provider, budget, policy or runtime differs.");
        if (run.Goal.ReadScope.Length != 1 || run.Goal.ReadScope[0] != "notes/source.md" || run.Goal.Web != null ||
            run.Goal.Memories is { Length: > 0 } || run.Goal.WriteScope != "plans/" ||
            run.Evidence.Count != 1 || run.Evidence[0] != new EvidenceRef("notes/source.md", Wire.Hash(fixture.Note), fixture.Note))
            problems.Add("Captured source or granted scope differs from the frozen input.");
        if (run.PreparedContext?.ContentHash != expectedContextHash || Wire.Hash(run.PreparedContext?.Text ?? "") != expectedContextHash)
            problems.Add("Frozen source context differs.");
        if (capture.Requests.Length != run.ModelCalls || run.ModelDispatches.Count != run.ModelCalls ||
            !capture.Requests.Select(request => request.Number).Order().SequenceEqual(Enumerable.Range(1, run.ModelCalls)))
            problems.Add("Model requests or dispatch receipts are missing or duplicated.");
        if (capture.Responses?.Length != run.ModelCalls || !(capture.Responses ?? []).Select(response => response.Number).Order().SequenceEqual(Enumerable.Range(1, run.ModelCalls)))
            problems.Add("Model responses are missing or duplicated.");
        foreach (var response in capture.Responses ?? [])
        {
            var dispatch = run.ModelDispatches.ElementAtOrDefault(response.Number - 1);
            if (dispatch == null || !MatchesReceipt(response.Body, response.RawJson, dispatch.ResponseHash)) problems.Add("A native response differs from its broker receipt.");
            if (dispatch is { InputTokens: not null, OutputTokens: not null })
            {
                try
                {
                    var responseUsage = response.Body.GetProperty("usage");
                    if (responseUsage.GetProperty("prompt_tokens").GetInt32() != dispatch.InputTokens || responseUsage.GetProperty("completion_tokens").GetInt32() != dispatch.OutputTokens)
                        problems.Add("Response usage differs from the recorded dispatch.");
                }
                catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
                { problems.Add("Reported model usage has no matching response evidence."); }
            }
        }
        if (capture.Events.Length == 0 || capture.Events.Any(record => record.RunId != run.Id) ||
            !capture.Events.Select(record => record.Sequence).Order().SequenceEqual(Enumerable.Range(1, capture.Events.Length).Select(value => (long)value)))
            problems.Add("Run events are missing, duplicated or belong to another task.");
        var contextObserved = run.ModelCalls > 0 && run.PreparedContext != null;
        var repairObserved = false; var schemas = new List<string>();
        foreach (var request in capture.Requests)
        {
            var body = request.Body;
            try
            {
                var dispatch = run.ModelDispatches.ElementAtOrDefault(request.Number - 1);
                if (dispatch == null || !MatchesReceipt(body, request.RawJson, dispatch.RequestHash) || dispatch.ContextObserved != true ||
                    dispatch.ContextHash != expectedContextHash) problems.Add("A native request does not match its broker dispatch receipt.");
                if (body.GetProperty("model").GetString() != provider.Model || body.GetProperty("reasoning_effort").GetString() != provider.Reasoning ||
                    body.GetProperty("max_completion_tokens").GetInt32() > budget.MaxOutputTokens || body.GetProperty("max_completion_tokens").GetInt32() < 1)
                    problems.Add("A model request differs from its frozen model or output allowance.");
                var messages = body.GetProperty("messages").EnumerateArray().ToArray();
                contextObserved &= run.PreparedContext != null && messages.Any(message => message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String &&
                    content.GetString()!.Contains(run.PreparedContext!.Text, StringComparison.Ordinal));
                foreach (var review in run.NativeProposals.Where(review => review.Status == "repair-requested"))
                    repairObserved |= messages.Any(message => message.GetProperty("role").GetString() == (importContract == 2 ? "user" : "tool") && message.TryGetProperty("content", out var content) &&
                        content.ValueKind == JsonValueKind.String && content.GetString()!.Contains(review.ProposalHash, StringComparison.Ordinal) &&
                        content.GetString()!.Contains("repair-requested", StringComparison.Ordinal) && review.Assessment.Problems.All(problem => content.GetString()!.Contains(problem, StringComparison.Ordinal)));
                schemas.Add(Wire.Hash(body.GetProperty("tools").GetRawText()));
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentOutOfRangeException)
            { problems.Add("A captured native request is malformed."); contextObserved = false; }
        }
        if (!contextObserved) problems.Add("Prepared context was not observed in every native request.");
        if (schemas.Count == 0 || schemas.Distinct().Count() != 1) problems.Add("The observed native tool catalog changed or is missing.");
        if (run.ExecutionCommands.Count < 2 || run.ExecutionCommands.Any(command => command.Status != "acknowledged") ||
            run.Question?.Answer != fixture.Answer || run.Question.Answered == null)
            problems.Add("The question and acknowledged native continuation are not verified.");
        if (run.ModelCalls > budget.ModelCalls || run.ToolCalls > budget.ToolCalls || run.Repairs > budget.Repairs ||
            run.ChargedTokens + run.ReservedTokens > budget.MaxTotalTokens || run.ExecutionActiveSeconds > budget.Seconds)
            problems.Add("A frozen task allowance was exceeded.");
        if (!capture.GrantRevoked || capture.WorkerStatus != "purged" || run.Research is not { Phase: "finished", WorkerRetained: false })
            problems.Add("Worker cleanup and grant revocation are not verified.");
        if (importContract == 2 && run.Repairs > 0 && !repairObserved) problems.Add("The native artifact correction did not receive its recorded feedback.");
        var exact = ExactImport(capture, importContract);
        if (!exact) problems.Add("Independent artifact/import evidence does not establish an exact approved write.");
        if (capture.Page != null)
        {
            try
            {
                using var document = ParseReport(capture.Page.Content, allowMarkdownFence: !synthetic);
                var output = document.RootElement;
                var properties = output.EnumerateObject().Select(property => property.Name).ToArray();
                if (!properties.Order().SequenceEqual(new[] { "durationMinutes", "audience", "sourcePath", "sourceQuote" }.Order()))
                    contentProblems.Add("The requested report fields are missing, duplicated or unexpected.");
                if (output.GetProperty("durationMinutes").GetInt32() != fixture.DurationMinutes) contentProblems.Add("Duration contradicts the frozen source.");
                if (output.GetProperty("audience").GetString() != fixture.Answer) contentProblems.Add("Audience differs from the recorded user answer.");
                if (output.GetProperty("sourcePath").GetString() != "notes/source.md") contentProblems.Add("Source reference differs.");
                var quote = output.GetProperty("sourceQuote").GetString();
                if (string.IsNullOrWhiteSpace(quote) || !fixture.Note.Contains(quote, StringComparison.Ordinal)) contentProblems.Add("Report quotation is absent from the frozen source.");
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            { contentProblems.Add("The requested report is not a valid typed document."); }
        }
        else contentProblems.Add("No imported document exists.");
        var usage = Usage(run, synthetic);
        if (usage.Complete && (run.InputTokens != usage.InputTokens || run.OutputTokens != usage.OutputTokens ||
            run.ChargedTokens < usage.InputTokens + usage.OutputTokens)) problems.Add("Aggregate usage does not match dispatch receipts.");
        return new(capture.Item.Id, fixture.Id, capture.Item.Arm, capture.Item.Repeat, problems.Count == 0 ? "VERIFIED" : "INVALID",
            exact, contentProblems.Count == 0, run.State == RunState.Succeeded && contentProblems.Count != 0,
            contextObserved, repairObserved, schemas.Count > 0 ? schemas[0] : null, usage, problems.Distinct().ToArray(), contentProblems.ToArray(), capture.ObservedFileHash);
    }

    public static JsonDocument ParseReport(string content, bool allowMarkdownFence = false)
    {
        // The live pilot declares Markdown fencing before inference; field and fact checks remain strict.
        if (allowMarkdownFence) content = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string heading = "# Workshop brief\n\n";
        if (!content.StartsWith(heading, StringComparison.Ordinal)) throw new JsonException("Missing requested report heading.");
        var body = content[heading.Length..].Trim();
        if (allowMarkdownFence && body.StartsWith("```json\n", StringComparison.Ordinal) && body.EndsWith("\n```", StringComparison.Ordinal))
            body = body[8..^4];
        return JsonDocument.Parse(body);
    }

    private static bool MatchesReceipt(JsonElement body, string? raw, string? expectedHash)
    {
        if (raw == null) return Wire.Hash(body.GetRawText()) == expectedHash;
        try
        {
            using var original = JsonDocument.Parse(raw);
            // JSON encoders may escape Unicode differently. Verify the original bytes and the parsed capture separately.
            return Wire.Hash(raw) == expectedHash && JsonElement.DeepEquals(body, original.RootElement);
        }
        catch (JsonException) { return false; }
    }

    public static NativeLabUsage Usage(Run run, bool synthetic = true)
    {
        var complete = run.ModelDispatches.Count == run.ModelCalls && run.ReservedTokens == 0 &&
            run.ModelDispatches.All(dispatch => dispatch.Status == "completed" && dispatch.InputTokens is >= 0 && dispatch.OutputTokens is >= 0);
        return new(run.ModelCalls, complete ? run.ModelDispatches.Sum(dispatch => dispatch.InputTokens!.Value) : null,
            complete ? run.ModelDispatches.Sum(dispatch => dispatch.OutputTokens!.Value) : null, run.ChargedTokens, run.ReservedTokens,
            complete, synthetic ? "Synthetic protocol counts; no measured model capacity or billing"
                : "Provider-reported Luna CLI counts; conservative charges retained. Not a billing total or certified remote ceiling; CLI internal request count unavailable.");
    }

    private static bool ExactImport(NativeLabCapture capture, int importContract)
    {
        var run = capture.Run; var approval = run.Approval; var page = capture.Page; var review = run.Research?.Review;
        if (run.State != RunState.Succeeded || approval?.Decision != "approved" || page == null || review?.ApprovalId != approval.Id ||
            approval.Action.Name != "knowledge.write" || approval.Action.Path != page.Path || run.OutputPath != page.Path ||
            approval.Action.Content != page.Content || page.Version != Wire.Hash(page.Content) || capture.ObservedFileHash != page.Version || review.Sha256 != page.Version ||
            approval.Digest != Runtime.ApprovalDigest(run.Id, approval.Id, approval.Action, approval.ResourceVersion, approval.Expires)) return false;
        return (importContract == 2 ? CapturedArtifactMatches(capture) : run.Capabilities.Any(call => call.Name == "thaddeus_propose_import" && !call.IsError &&
            call.Result.TryGetProperty("approvalId", out var id) && id.GetString() == approval.Id &&
            call.Result.TryGetProperty("contentHash", out var hash) && hash.GetString() == page.Version)) &&
            capture.Events.Any(record => record.Type == "tool.result" && record.RunId == run.Id && record.Data.TryGetProperty("evidence", out var evidence) &&
                evidence.TryGetProperty("hash", out var hash) && hash.GetString() == page.Version);
    }

    public static NativeLabReport Report(string registrationHash, NativeLabItem[] plan, NativeLabGrade[] grades)
    {
        var problems = new List<string>();
        if (grades.Length != plan.Length || !grades.Select(grade => grade.Item).Order().SequenceEqual(plan.Select(item => item.Id).Order()))
            problems.Add("The full registered schedule was not captured exactly once.");
        if (grades.Any(grade => grade.Status != "VERIFIED")) problems.Add("One or more native protocol observations are invalid.");
        if (grades.Select(grade => grade.NativeToolSchemaHash).Distinct().Count() != 1) problems.Add("Native tool catalogs differ across frozen arms.");
        var repeated = grades.Where(grade => grade.CaseId == "repair").GroupBy(grade => grade.Arm).Select(group => new
        {
            arm = group.Key, count = group.Count(), agree = group.Select(grade => Wire.Pack(new { grade.ExactImport, grade.ContentPassed,
                grade.FalseSuccess, grade.ContextObserved, grade.RepairFeedbackObserved, grade.NativeToolSchemaHash, grade.OutputHash, grade.Usage })).Distinct().Count() == 1
        }).ToArray();
        if (repeated.Length != 2 || repeated.Any(pair => pair.count != 2 || !pair.agree)) problems.Add("Unchanged repeated controls disagree or are missing.");
        var repaired = grades.Where(grade => grade.CaseId == "repair" && grade.Arm == "candidate").ToArray();
        if (repaired.Length != 2 || repaired.Any(grade => !grade.ContentPassed || !grade.RepairFeedbackObserved)) problems.Add("Candidate repair activation or fixture output did not reproduce.");
        var uncheckedArm = grades.Where(grade => grade.CaseId == "repair" && grade.Arm == "unchanged").ToArray();
        if (uncheckedArm.Length != 2 || uncheckedArm.Any(grade => !grade.FalseSuccess || grade.RepairFeedbackObserved)) problems.Add("The unchecked injected-defect control was not observed.");
        var negatives = grades.Where(grade => grade.CaseId == "false-success").ToArray();
        if (negatives.Length != 2 || negatives.Any(grade => !grade.ExactImport || !grade.FalseSuccess || grade.ContentPassed))
            problems.Add("The valid-quotation false-success negative was not detected in both arms.");
        return new(1, registrationHash, plan.Length, grades.Length, problems.Count == 0 ? "PASSED" : "INCOMPLETE_OR_FAILED", "INCONCLUSIVE",
            new { status = "NOT_EVALUATED", reason = "Scripted responses cannot measure raw model capacity; no live model or holdout was consumed." },
            new { status = problems.Count == 0 ? "SCRIPTED_PROTOCOL_VERIFIED" : "UNVERIFIED", exactImports = grades.Count(grade => grade.ExactImport),
                contextDeliveries = grades.Count(grade => grade.ContextObserved), repairDeliveries = grades.Count(grade => grade.RepairFeedbackObserved) },
            new { status = "BOUNDED_FIXTURE_PREDICATES_ONLY", contentPasses = grades.Count(grade => grade.ContentPassed), falseSuccesses = grades.Count(grade => grade.FalseSuccess),
                reason = "Fictional typed facts and approvals only; no general research-quality or model-improvement claim.", promotion = false },
            repeated, problems.ToArray(), grades);
    }

    public static NativeLabReport LivePilotReport(string registrationHash, NativeLabItem[] plan, NativeLabGrade[] grades)
    {
        var problems = new List<string>();
        if (plan.Length != 2 || plan.Select(item => item.Arm).Order().SequenceEqual(new[] { "candidate", "unchanged" }) == false ||
            plan.Any(item => item.Repeat != 0) || plan.Select(item => item.CaseId).Distinct().Count() != 1 ||
            plan.Select(item => item.Id).Distinct().Count() != 2)
            problems.Add("The live pilot requires exactly one matched task in each arm.");
        if (grades.Length != plan.Length || !grades.Select(grade => grade.Item).Order().SequenceEqual(plan.Select(item => item.Id).Order()) ||
            grades.Any(grade => !plan.Any(item => item.Id == grade.Item && item.CaseId == grade.CaseId && item.Arm == grade.Arm && item.Repeat == grade.Repeat)))
            problems.Add("The full live schedule was not captured exactly once.");
        if (grades.Any(grade => grade.Status != "VERIFIED" || !grade.Usage.Complete))
            problems.Add("One or more live protocol or usage observations are incomplete or invalid.");
        if (grades.Length == 0 || grades.Select(grade => grade.NativeToolSchemaHash).Distinct().Count() != 1 || grades.Any(grade => grade.NativeToolSchemaHash == null))
            problems.Add("Native tool catalogs differ across frozen arms or are absent.");
        return new(1, registrationHash, plan.Length, grades.Length, problems.Count == 0 ? "PASSED" : "INCOMPLETE_OR_FAILED", "INCONCLUSIVE",
            new { status = "NOT_EVALUATED", reason = "Native workflow pilot only. Remote weights, sampling and CLI internal model requests are not independently controlled; no closed-book test or holdout." },
            new { status = problems.Count == 0 ? "LIVE_PILOT_PROTOCOL_VERIFIED" : "UNVERIFIED", exactImports = grades.Count(grade => grade.ExactImport),
                contextDeliveries = grades.Count(grade => grade.ContextObserved), repairDeliveries = grades.Count(grade => grade.RepairFeedbackObserved),
                calls = grades.Sum(grade => grade.Usage.Calls), chargedTokens = grades.Sum(grade => grade.Usage.ChargedTokens),
                reservedTokens = grades.Sum(grade => grade.Usage.ReservedTokens), reportedUsageComplete = grades.Length == plan.Length && grades.All(grade => grade.Usage.Complete) },
            new { status = "BOUNDED_FIXTURE_PREDICATES_ONLY", contentPasses = grades.Count(grade => grade.ContentPassed),
                falseSuccesses = grades.Count(grade => grade.FalseSuccess), promotion = false,
                reason = "Two tasks do not establish improvement. Review this pilot before any repeated or disjoint evaluation." },
            new { status = "NOT_RUN", reason = "No repeat or false-success injection in this live pilot; the separate scripted protocol is retained." },
            problems.ToArray(), grades);
    }
}
