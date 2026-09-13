namespace Thaddeus.Core;

public record EvidenceCitation(string Source, string Version, string Quote);
public record ProposalEvidenceAssessment(bool Passed, string[] Checks, string[] Problems, string[] Unverified);
public record NativeProposalReview(string Id, string Path, string Artifact, string Content, string ContentHash, string ProposalHash,
    string Status, EvidenceCitation[] Citations, ProposalEvidenceAssessment Assessment, DateTimeOffset Recorded, string? ApprovalId = null);
public record ProposalRepairFeedback(string Status, string ReviewId, string ProposalHash, string[] Problems,
    int RepairsUsed, int RepairLimit, string Instruction);
public interface IProposalEvidenceValidator
{
    ProposalEvidenceAssessment Assess(Run run, string content, EvidenceCitation[] citations);
}
