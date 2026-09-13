using System.Text.Json;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

internal record ResearchCheckpoint(string? Kind, string Summary, ArtifactReview? Artifact = null);

public sealed partial class Runtime
{
    internal ResearchCheckpoint AssessResearchRecovery(Run run)
    {
        ResearchCheckpoint Block(string reason) => new(null, reason);
        if (run.Research is not { Phase: "attention", WorkerRetained: true } || run.State != RunState.NeedsAttention ||
            run.Execution is not { Backend: "openclaw", RuntimeRunId: not null } identity || identity.SessionKey != "agent:thaddeus:" + run.Id ||
            run.PreparedContext is not { } context || context.ContentHash != Wire.Hash(context.Text) || context.ProfileDigest != run.Profile?.Digest)
            return Block("No confirmed saved checkpoint is available for this task. Keep its receipts and cancel unfinished work when appropriate.");
        if (run.Approval?.Decision == "approved" || store.WriteOperation(run.Id) != null)
            return Block("An approved write requires import reconciliation. Restoring a worker checkpoint cannot resolve that effect.");
        if (run.ReservedTokens != 0 || run.ModelDispatches.Any(call => call.Status is "dispatched-outcome-unknown" or "outcome-unknown") ||
            run.ExecutionCommands.Any(command => command.Status != "acknowledged"))
            return Block("A command or model call has an unknown outcome. It cannot be replayed or cleared by checkpoint recovery.");
        if (run.ExecutionCommands.LastOrDefault() is not { Kind: "quiesce", Status: "acknowledged", Observation: { } stop } ||
            stop.Report.ValueKind != JsonValueKind.Object || !stop.Report.TryGetProperty("thaddeusFilesystemCheckpoint", out var checkpoint) ||
            checkpoint.ValueKind != JsonValueKind.String || checkpoint.GetString() != "syncfs")
            return Block("The ledger has no final acknowledged filesystem checkpoint. Recovery will not invent one.");
        if (run.ArtifactImports.LastOrDefault() is { Status: "repair-requested", Repair: { Status: "repair-requested" } })
            return new("saved-repair", "The captured file and correction request were saved. Restore them to a paused task; continuing remains a separate action.");
        if (run.Approval is { Decision: "pending" } approval)
        {
            if (approval.Expires <= DateTimeOffset.UtcNow) return Block("The saved approval expired. No import can be approved through checkpoint recovery.");
            var hash = Wire.Hash(approval.Action.Content!);
            var captured = run.ArtifactImports.LastOrDefault();
            if (captured is { Status: "ready-for-approval", CapturedAt: not null } && captured.ApprovalId == approval.Id && captured.Sha256 == hash)
                return new("captured-artifact", "The exact artifact and its pending approval were captured before interruption. Restoring review does not approve or import it.",
                    new(approval.Id, captured.Artifact, hash, captured.CapturedAt.Value));
            var checkedFile = run.ArtifactChecks.LastOrDefault();
            if (checkedFile is { Status: "matched" } && checkedFile.ApprovalId == approval.Id && checkedFile.ExpectedSha256 == hash && checkedFile.ObservedSha256 == hash)
                return new("captured-artifact", "The exact artifact readback is recorded. Restore the pending review, then decide whether to approve it.",
                    new(approval.Id, checkedFile.Artifact, hash, checkedFile.CheckedAt));
            return Block("The pending proposal has no matching captured artifact. Recovery cannot supply missing write evidence.");
        }
        if (run.Question is { Answer: null }) return new("saved-question", "The question and filesystem checkpoint were saved. Restore the question, then answer when ready.");
        if (run.Question is { Answer: not null }) return new("saved-answer", "Your answer is already saved. Restore a paused task; continuing it remains a separate action.");
        return Block("No saved question, answer, captured artifact or correction matches this checkpoint. No work will be replayed.");
    }

    internal async Task<ResearchRecoveryReview> RecordResearchRecovery(string id, int expectedVersion, bool stopped, string? failure)
    {
        await Gate(id).WaitAsync();
        try
        {
            var run = store.Get(id) ?? throw new ArgumentException("Task not found.");
            if (run.Version != expectedVersion || run.Research?.Phase != "attention") throw new InvalidOperationException("The task changed during inspection. Review its current receipts.");
            var assessment = AssessResearchRecovery(run); var now = DateTimeOffset.UtcNow;
            var summary = failure ?? assessment.Summary;
            var version = run.Version + 1;
            var digest = Wire.Hash(Wire.Pack(new { id, version, assessment.Kind, stopped, summary, now }));
            var review = new ResearchRecoveryReview(digest, version, assessment.Kind, stopped && assessment.Kind != null && failure == null,
                summary, now, now.AddMinutes(10), stopped);
            run.Research = run.Research with { Recovery = review };
            store.Save(run, "research.recovery.reviewed", new { review, modelCallsStarted = 0, workerBooted = false, commandsReplayed = false });
            return review;
        }
        finally { Gate(id).Release(); }
    }

    internal async Task<Run> RestoreResearchCheckpoint(string id, string digest)
    {
        await Gate(id).WaitAsync();
        try
        {
            var run = store.Get(id) ?? throw new ArgumentException("Task not found.");
            if (run.Research?.Recovery is not { CanRestore: true, WorkerStopped: true } review || review.Digest != digest ||
                run.Version != review.Version || review.Expires <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException("The recovery review changed or expired. Inspect the saved worker again.");
            var checkpoint = AssessResearchRecovery(run);
            if (checkpoint.Kind == null || checkpoint.Kind != review.Checkpoint) throw new InvalidOperationException(checkpoint.Summary);
            // This restores a stopping point. The butler does not answer his own question or sign your approval.
            var (state, phase) = checkpoint.Kind switch
            {
                "saved-question" => (RunState.AwaitingInput, "awaiting-input"),
                "captured-artifact" => (RunState.AwaitingApproval, "awaiting-approval"),
                _ => (RunState.Paused, "paused")
            };
            run.State = state; run.Summary = checkpoint.Summary;
            run.Research = run.Research with { Phase = phase, Message = checkpoint.Summary, Review = checkpoint.Artifact ?? run.Research.Review, FailureCode = null };
            store.Save(run, "research.recovery.restored", new { review.Digest, checkpoint = checkpoint.Kind, modelCallsStarted = 0, workerBooted = false, commandsReplayed = false });
            return run;
        }
        finally { Gate(id).Release(); }
    }
}
