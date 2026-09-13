using System.Text.Json;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

/// <summary>Checks captured quotations and provenance. It does not judge truth, entailment, completeness or research quality.</summary>
public sealed class ProposalEvidenceValidator : IProposalEvidenceValidator
{
    private sealed record Source(string Id, string Version, string Text, string Display, bool Truncated);
    public static readonly string[] Unverified = ["Factual accuracy and whether quotations support conclusions require review", "Claim coverage and completeness are not mechanically established"];
    public ProposalEvidenceAssessment Assess(Run run, string content, EvidenceCitation[] citations)
    {
        var sources = new List<Source>();
        foreach (var evidence in run.Evidence)
        {
            if (!run.Goal.ReadScope.Contains(evidence.Path, StringComparer.Ordinal) || Wire.Hash(evidence.Content) != evidence.Hash)
                throw new InvalidOperationException("Recorded note evidence is not intact and within this task's scope.");
            sources.Add(new(evidence.Path, evidence.Hash, evidence.Content, evidence.Path, false));
        }
        foreach (var entry in run.PreparedContext?.Memories ?? [])
        {
            if (entry.Forgotten || entry.Source == null || !(run.Goal.Memories ?? []).Contains(new(entry.Id, entry.Version)))
                throw new InvalidOperationException("Recorded memory evidence is not within this task's selected context.");
            sources.Add(new("memory:" + entry.Id, entry.Version, entry.Source.Quote, entry.Source.Path, false));
        }
        foreach (var receipt in run.Capabilities.Where(call => call.Name == "thaddeus_fetch_public_page" && !call.IsError))
        {
            if (receipt.Authority != "broker-observed") throw new InvalidOperationException("Public evidence has no broker receipt.");
            var source = receipt.Result.Deserialize<PublicWebResult>(Wire.Json)?.Source ?? throw new InvalidOperationException("Public evidence is missing its captured source.");
            if (run.Goal.Web == null || Wire.Hash(source.Text) != source.TextSha256) throw new InvalidOperationException("Public source text does not match its recorded hash.");
            PublicWebNetwork.Destination(source.Url, run.Goal.Web);
            sources.Add(new(source.Url, source.TextSha256, source.Text, source.Url, source.Truncated));
        }
        var checks = new List<string>(); var problems = new List<string>();
        if (citations.Length == 0) problems.Add("Include at least one exact quotation from evidence already captured for this task.");
        if (citations.Distinct().Count() != citations.Length) problems.Add("Remove duplicate citations.");
        for (var i = 0; i < citations.Length; i++)
        {
            var citation = citations[i];
            if (citation == null || string.IsNullOrWhiteSpace(citation.Source) || string.IsNullOrWhiteSpace(citation.Version) || string.IsNullOrWhiteSpace(citation.Quote))
            { problems.Add($"Citation {i + 1} must contain a source, version and nonempty quotation."); continue; }
            var source = sources.FirstOrDefault(source => source.Id == citation.Source && source.Version == citation.Version);
            if (source == null) { problems.Add($"Citation {i + 1} must identify a captured source and its exact recorded version."); continue; }
            var valid = true;
            if (!source.Text.Contains(citation.Quote, StringComparison.Ordinal)) { valid = false; problems.Add($"Citation {i + 1} quotation is absent from that captured source."); }
            if (!content.Contains(citation.Quote, StringComparison.Ordinal)) { valid = false; problems.Add($"Citation {i + 1} quotation must appear verbatim in the proposed artifact."); }
            if (!content.Contains(source.Display, StringComparison.Ordinal)) { valid = false; problems.Add($"Citation {i + 1} source path or URL must appear in the proposed artifact."); }
            if (valid) checks.Add($"Citation {i + 1}: exact quotation, captured version and visible source reference match" + (source.Truncated ? " (source text was truncated)" : ""));
        }
        return new(problems.Count == 0, checks.ToArray(), problems.ToArray(), Unverified);
    }
}
