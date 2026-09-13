using System.Text.Json;
using Thaddeus.Core;

namespace Thaddeus.Lab;

public static partial class NativeLabScore
{
    private static bool CapturedArtifactMatches(NativeLabCapture capture)
    {
        var run = capture.Run; var approval = run.Approval!; var page = capture.Page!;
        var import = run.ArtifactImports.LastOrDefault(); var proposal = run.NativeProposals.LastOrDefault();
        if (import is not { Status: "ready-for-approval", CapturedAt: not null } || import.ApprovalId != approval.Id ||
            import.Path != page.Path || import.Artifact != run.Research!.Review!.Artifact || import.Sha256 != page.Version ||
            import.ResourceVersion != approval.ResourceVersion || proposal == null || proposal.ApprovalId != approval.Id ||
            proposal.Content != page.Content || proposal.ContentHash != page.Version || proposal.Artifact != import.Artifact ||
            proposal.Path != import.Path || proposal.Status != "passed" || !proposal.Assessment.Passed || proposal.Assessment.Problems.Length != 0 ||
            !proposal.Citations.SequenceEqual(import.Citations)) return false;
        var requests = run.Capabilities.Where(call => call.Name == "thaddeus_propose_import" && !call.IsError).ToArray();
        if (!requests.Any(call => call.OperationId == import.OperationId && call.Result.TryGetProperty("importId", out var id) && id.GetString() == import.Id &&
            call.Result.TryGetProperty("status", out var status) && status.GetString() == "awaiting-artifact-review" &&
            call.Result.TryGetProperty("artifact", out var artifact) && artifact.GetString() == import.Artifact)) return false;
        if (run.ExecutionCommands.Count(command => command.Kind == "start") != 1 || run.ExecutionCommands.Count(command => command.Kind == "resume") != 1 ||
            run.ExecutionCommands.Count(command => command.Kind == "artifact-repair") != run.Repairs ||
            run.ExecutionCommands.Count(command => command.Kind == "quiesce") < 2 + run.Repairs) return false;
        var repairs = run.ArtifactImports.Where(item => item.Repair?.Status == "repair-requested").ToArray();
        if (run.ArtifactImports.Count != run.Repairs + 1 || run.NativeProposals.Count != run.ArtifactImports.Count ||
            repairs.Length != run.Repairs || repairs.Any(item => item.Status != "repair-dispatched" ||
            !run.NativeProposals.Any(review => review.Id == item.Repair!.ReviewId && review.ProposalHash == item.Repair.ProposalHash &&
                review.Status == "repair-requested" && !review.Assessment.Passed && review.Assessment.Problems.Length > 0 &&
                item.Sha256 == Wire.Hash(review.Content) && review.Artifact == item.Artifact && review.Path == item.Path && review.Citations.SequenceEqual(item.Citations)) ||
            !run.ExecutionCommands.Any(command => command.Id == "artifact-repair-" + item.Id && command.Kind == "artifact-repair" && command.Status == "acknowledged"))) return false;
        try
        {
            var captured = capture.Events.SingleOrDefault(record => record.Type == "research.artifact.captured" &&
                record.Data.GetProperty("import").GetProperty("id").GetString() == import.Id);
            if (captured == null || captured.Data.GetProperty("authority").GetString() != "host-readback" ||
                Wire.Pack(captured.Data.GetProperty("import").Deserialize<ArtifactImport>(Wire.Json)) != Wire.Pack(import)) return false;
            var ready = capture.Events.SingleOrDefault(record => record.Type == "research.awaiting-approval" &&
                record.Data.GetProperty("research").GetProperty("review").GetProperty("approvalId").GetString() == approval.Id);
            var approved = capture.Events.SingleOrDefault(record => record.Type == "approval.approved" && record.Data.GetProperty("id").GetString() == approval.Id);
            return ready != null && approved != null && captured.Sequence < ready.Sequence && ready.Sequence < approved.Sequence &&
                capture.Events.Any(record => record.Type == "tool.result" && record.Sequence > approved.Sequence);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException) { return false; }
    }

    public static NativeLabReport ArtifactPilotReport(string registrationHash, NativeLabItem[] plan, NativeLabGrade[] grades)
    {
        var problems = new List<string>();
        if (plan is not [{ Id: "live-artifact-0", CaseId: "workshop", Arm: "artifact", Repeat: 0 }] ||
            grades.Length != 1 || grades.Any(grade => !plan.Any(item => item.Id == grade.Item && item.CaseId == grade.CaseId && item.Arm == grade.Arm && item.Repeat == grade.Repeat)))
            problems.Add("The single registered artifact workflow was not captured exactly once.");
        if (grades.Any(grade => grade.Status != "VERIFIED" || !grade.Usage.Complete || grade.NativeToolSchemaHash == null))
            problems.Add("The artifact workflow protocol or reported usage is incomplete or invalid.");
        return new(1, registrationHash, plan.Length, grades.Length, problems.Count == 0 ? "PASSED" : "INCOMPLETE_OR_FAILED", "INCONCLUSIVE",
            new { status = "NOT_EVALUATED", reason = "One native workflow does not measure model capacity; weights, sampling and CLI internal calls remain uncertified." },
            new { status = problems.Count == 0 ? "LIVE_ARTIFACT_PROTOCOL_VERIFIED" : "UNVERIFIED", exactImports = grades.Count(grade => grade.ExactImport),
                calls = grades.Sum(grade => grade.Usage.Calls), chargedTokens = grades.Sum(grade => grade.Usage.ChargedTokens),
                reportedUsageComplete = grades.Length == 1 && grades.All(grade => grade.Usage.Complete) },
            new { status = "BOUNDED_FIXTURE_PREDICATES_ONLY", contentPasses = grades.Count(grade => grade.ContentPassed), falseSuccesses = grades.Count(grade => grade.FalseSuccess),
                promotion = false, reason = "Document correctness is scored separately. One task cannot establish comparative reliability or improvement." },
            new { status = "NOT_RUN", reason = "Single smoke check; existing repeated v1 controls and failures remain unchanged. No automatic repeat or holdout." },
            problems.ToArray(), grades);
    }
}
