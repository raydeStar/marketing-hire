using System.Text.Json;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Runtime
{
    private static readonly CapabilityDefinition EvidenceProposalTool = new("thaddeus_propose_import",
        "Offer an existing text artifact for source quotation checks and exact user approval. Include captured source versions and exact quotations visible in the artifact. A repair-requested error permits a bounded correction using a new operation ID; otherwise stop. This does not verify factual accuracy or write host files.", Schema("""
        {"type":"object","properties":{"path":{"type":"string","maxLength":120,"description":"Destination Markdown path in the granted plans/ scope."},"content":{"type":"string","minLength":1,"maxLength":100000,"description":"Exact UTF-8 text already written into the worker artifact."},"artifact":{"type":"string","maxLength":100,"pattern":"^[a-z0-9][a-z0-9-]{0,90}\\.(md|txt|json)$","description":"Existing filename inside the worker artifact directory, for example summary.md."},"citations":{"type":"array","maxItems":24,"items":{"type":"object","properties":{"source":{"type":"string","minLength":1,"maxLength":2048,"description":"Captured note path, memory:<id>, or retrieved final public URL."},"version":{"type":"string","minLength":1,"maxLength":100,"description":"Note hash, selected memory version, or public source textSha256."},"quote":{"type":"string","minLength":1,"maxLength":1000,"description":"Exact quotation in the captured source AND artifact. Also include the source note path or final URL visibly in the artifact."}},"required":["source","version","quote"],"additionalProperties":false}}},"required":["path","content","artifact","citations"],"additionalProperties":false}
        """));

    private static EvidenceCitation[] ParseCitations(JsonElement args, bool artifactReference = false)
    {
        if (artifactReference) Fields(args, "path", "artifact", "citations");
        else Fields(args, "path", "content", "artifact", "citations");
        var values = args.GetProperty("citations");
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 24) throw new ArgumentException("Provide at most 24 citations in an array.");
        return values.EnumerateArray().Select(value =>
        {
            if (value.ValueKind != JsonValueKind.Object) throw new ArgumentException("Each citation must contain source, version and quote.");
            Fields(value, "source", "version", "quote");
            return new EvidenceCitation(Text(value, "source", 2048), Text(value, "version", 100), Text(value, "quote", 1000));
        }).ToArray();
    }
    private static string ProposalHash(string path, string artifact, string content, EvidenceCitation[] citations) =>
        Wire.Hash(Wire.Pack(new { path, artifact, content, citations = citations.OrderBy(c => c.Source, StringComparer.Ordinal)
            .ThenBy(c => c.Version, StringComparer.Ordinal).ThenBy(c => c.Quote, StringComparer.Ordinal).ToArray() }));

    private object ReviewNativeProposal(Run run, string path, string artifact, string content, JsonElement args)
    {
        var profile = run.Profile!; profile.Validate();
        EvidenceCitation[] citations = []; ProposalEvidenceAssessment? assessment = null;
        string? stopped = null;
        try { citations = ParseCitations(args); }
        catch (ArgumentException error) { assessment = new(false, [], [error.Message], ProposalEvidenceValidator.Unverified); }
        var hash = assessment == null ? ProposalHash(path, artifact, content, citations) : Wire.Hash(args.GetRawText());
        try
        {
            store.AssertMemoriesCurrent(run);
            if (run.Evidence.Any(evidence => store.Version(evidence.Path) != evidence.Hash))
                throw new InvalidOperationException("Source notes changed.");
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or ArgumentException)
        {
            stopped = "source-changed";
            assessment = new(false, [], ["Captured context changed or is unavailable. Start a new task with reviewed sources."], ProposalEvidenceValidator.Unverified);
        }
        if (assessment == null && profile.ValidateEvidence)
        {
            try
            {
                assessment = proposalValidator.Assess(run, content, citations);
                if (assessment == null || assessment.Checks == null || assessment.Problems == null || assessment.Unverified == null ||
                    assessment.Passed != (assessment.Problems.Length == 0))
                    throw new InvalidOperationException("The validator returned an inconsistent assessment.");
            }
            catch (Exception)
            {
                // An absent verdict is not a passing grade. Even the butler must show his receipts.
                stopped = "validation-unavailable";
                assessment = new(false, [], ["Source quotation checks could not be completed. No approval was created."], ProposalEvidenceValidator.Unverified);
            }
        }
        var uncheckedControl = assessment == null && !profile.ValidateEvidence;
        assessment ??= new(false, [], [], ["Source quotation checks disabled in this explicit experiment control", .. ProposalEvidenceValidator.Unverified]);
        var limit = Math.Min(profile.RepairLimit, run.Goal.Limits.Repairs);
        var status = uncheckedControl ? "not-evaluated" : assessment.Passed ? "passed" : stopped ?? "repair-exhausted";
        if (!uncheckedControl && !assessment.Passed && stopped == null)
        {
            if (run.NativeProposals.Any(review => review.ProposalHash == hash && !review.Assessment.Passed)) status = "repeated-failure";
            else if (run.Repairs < limit && run.ModelCalls < run.Goal.Limits.ModelCalls && run.ReservedTokens == 0 &&
                run.ChargedTokens < run.Goal.Limits.MaxTotalTokens && run.Goal.Limits.ToolCalls - run.ToolCalls >= 2 && RemainingExecutionTime(run) > TimeSpan.Zero)
            { run.Repairs++; status = "repair-requested"; }
        }
        var review = new NativeProposalReview(Guid.NewGuid().ToString("N"), path, artifact, content, Wire.Hash(content), hash,
            status, citations, assessment, DateTimeOffset.UtcNow);
        run.NativeProposals.Add(review);
        if (assessment.Passed || uncheckedControl) return review;
        if (status != "repair-requested")
        {
            run.State = RunState.NeedsAttention; PauseExecutionClock(run);
            run.Summary = "Proposal stopped · source quotation checks did not pass; no import approval created";
        }
        else run.Summary = "Source quotation checks requested a correction within the existing task allowance";
        return new ProposalRepairFeedback(status, review.Id, hash, assessment.Problems, run.Repairs, limit,
            status == "repair-requested" ? "Correct the artifact and its citations, then propose once more with a new operation ID. Existing model, tool, token and time limits still apply."
            : "Stop this turn. No import approval was created. Inspect the recorded feedback before starting a new task.");
    }

    private static void AssertProposalReview(Run run, Approval approval, bool reconciling = false)
    {
        if (run.Profile?.ProposalEvidenceVersion is not (1 or 2)) return;
        run.Profile.Validate();
        var review = run.NativeProposals.LastOrDefault();
        if (review == null || review.ApprovalId != approval.Id || review.Path != approval.Action.Path || review.Content != approval.Action.Content ||
            review.ContentHash != Wire.Hash(review.Content) || review.ProposalHash != ProposalHash(review.Path, review.Artifact, review.Content, review.Citations) ||
            (run.Profile.ValidateEvidence ? review.Status != "passed" || !review.Assessment.Passed || review.Assessment.Problems.Length != 0
                : review.Status != "not-evaluated" || review.Assessment.Passed))
            throw new InvalidOperationException("The exact proposal has no matching source quotation review. Nothing was written.");
        if (run.Profile.ProposalEvidenceVersion == 2 &&
            (run.ArtifactImports.LastOrDefault() is not { Status: "ready-for-approval" } import || import.ApprovalId != approval.Id ||
             import.Sha256 != review.ContentHash || import.Path != review.Path || import.Artifact != review.Artifact ||
             import.ResourceVersion != approval.ResourceVersion || !import.Citations.SequenceEqual(review.Citations) ||
             run.Research is not { Review: not null } research ||
             (reconciling ? research.Phase != "attention" || approval.Decision != "approved" : research.Phase != "awaiting-approval") ||
             research.Review.ApprovalId != approval.Id || research.Review.Sha256 != review.ContentHash))
            throw new InvalidOperationException("The captured artifact has no matching stopped-worker review. Nothing was written.");
    }
}
