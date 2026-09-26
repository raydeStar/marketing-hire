using System.Globalization;
using System.Text.RegularExpressions;

namespace Thaddeus.Host;

public record SpecResult(string Requirement, bool Met, string Detail);

/// <summary>What the assignment asked for that can be measured, measured on the work: how many questions, posts, ideas or
/// emails; a word limit or range; a Subject line; citations. The self-review gets what's unmet as must-fix, and the result is
/// part of the review's summary, so "eight questions" can't quietly become eleven.</summary>
public static partial class SpecCheck
{
    static readonly Dictionary<string, int> Numbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12
    };
    static int? Count(string word) => int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : Numbers.TryGetValue(word, out var named) ? named : null;

    [GeneratedRegex(@"\b(\d{1,2}|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\s+(?:[\w-]+\s+){0,2}?(questions|posts|emails|ideas|tips|steps|headlines|subject lines|reasons|objections|drafts)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Counted();
    [GeneratedRegex(@"\b(?:under|at most|no more than|up to|maximum of)\s+(\d{1,2},\d{3}|\d{2,5})\s+words\b", RegexOptions.IgnoreCase)]
    private static partial Regex WordLimit();
    [GeneratedRegex(@"\b(\d{1,2},\d{3}|\d{2,5})\s*(?:–|-|to)\s*(\d{1,2},\d{3}|\d{2,5})\s+words\b", RegexOptions.IgnoreCase)]
    private static partial Regex WordRange();

    static int Whole(string text) => int.Parse(text.Replace(",", ""), CultureInfo.InvariantCulture);
    public static int Words(string text) => Regex.Matches(Regex.Replace(text, @"\[(\d{1,2})\]|[#*_`>|]", " "), @"[\p{L}\p{N}][\p{L}\p{N}'’-]*").Count;

    /// <summary>How many of a thing the work holds: questions as headings or lines ending in "?", posts or emails as series parts,
    /// ideas, tips and steps as list items or numbered headings.</summary>
    static int Found(string thing, string body, int parts)
    {
        var lines = body.Replace("\r", "").Split('\n').Select(line => line.Trim()).ToArray();
        switch (thing.ToLowerInvariant())
        {
            case "questions":
            case "objections":
                var headed = lines.Count(line => line.StartsWith('#') && line.TrimEnd().EndsWith('?'));
                // Questions as headings; else bold questions ("**Is it safe?** Yes.") or list lines that end in "?".
                return headed > 0 ? headed : lines.Count(line => Regex.IsMatch(line, @"^([-*]\s+|\d+[.)]\s+)?\*\*[^*]+\?\*\*") || Regex.IsMatch(line, @"^([-*]|\d+[.)])\s+.*\?$"));
            case "posts":
            case "emails":
            case "drafts":
                return parts;
            default:
                var headings = lines.Count(line => Regex.IsMatch(line, @"^#{2,4}\s+\S"));
                var items = lines.Count(line => Regex.IsMatch(line, @"^([-*]|\d+[.)])\s+\S"));
                return Math.Max(items, headings);
        }
    }

    /// <summary>Every measurable requirement in the assignment, checked on the work. <paramref name="parts"/> is how many posts or
    /// emails a series holds (1 for a single piece).</summary>
    public static SpecResult[] Check(string assignment, string body, int parts = 1, int sources = 0)
    {
        var results = new List<SpecResult>();
        foreach (Match match in Counted().Matches(assignment))
        {
            if (Count(match.Groups[1].Value) is not { } wanted || wanted < 2) continue;
            var thing = match.Groups[2].Value.ToLowerInvariant();
            if (results.Any(result => result.Requirement.EndsWith(" " + thing, StringComparison.Ordinal))) continue;
            // A series is judged by its parts only when it is one; a single long piece asked for "three posts" is judged on items.
            var found = thing is "posts" or "emails" or "drafts" && parts < 2 ? Found("ideas", body, parts) : Found(thing, body, parts);
            results.Add(new($"{wanted} {thing}", found == wanted, found == wanted ? $"{found}" : $"found {found}"));
        }
        var words = Words(body);
        if (WordLimit().Match(assignment) is { Success: true } limit && Whole(limit.Groups[1].Value) is var most)
            results.Add(new($"under {most} words", words <= most, $"{words} words"));
        if (WordRange().Match(assignment) is { Success: true } range && Whole(range.Groups[1].Value) is var low && Whole(range.Groups[2].Value) is var high && high > low)
            results.Add(new($"{low}–{high} words", words >= low * 0.9 && words <= high * 1.1, $"{words} words"));
        if (Regex.IsMatch(assignment, @"\bsubject:? line\b", RegexOptions.IgnoreCase))
            results.Add(new("a Subject: line", Regex.IsMatch(body.TrimStart(), @"^\**subject\**\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline), Regex.IsMatch(body, @"^\**subject\**\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline) ? "present" : "missing"));
        if (Regex.IsMatch(assignment, @"\bcite\b|\bcitations?\b|\bsources?\b", RegexOptions.IgnoreCase) && sources > 0)
            results.Add(new("sources cited", Regex.IsMatch(body, @"\[\d{1,2}\]"), Regex.Matches(body, @"\[\d{1,2}\]").Count + " citation(s)"));
        return [.. results];
    }

    /// <summary>"8 questions ✓, under 150 words ✗ (163 words)".</summary>
    public static string Line(SpecResult[] results) =>
        string.Join(", ", results.Select(result => $"{result.Requirement} {(result.Met ? "✓" : "✗")}{(result.Met ? "" : $" ({result.Detail})")}"));
}
