using System.Text.RegularExpressions;

namespace Thaddeus.Host;

public record QaCheck(string Id, string Label, string Result, string Detail);
public record QaReport(string Status, QaCheck[] Checks, string Summary);

/// <summary>The launch checklist a careful marketer runs before anything goes out, as ordinary code:
/// destination, links and tracking tags, channel length limits, placeholders and risky claims.</summary>
public static partial class CampaignQa
{
    [GeneratedRegex(@"https?://[^\s)\]>""']+", RegexOptions.IgnoreCase)] private static partial Regex Links();
    [GeneratedRegex(@"\[(tbd|todo|link|insert[^\]]*|name|company|x+)\]|\{\{[^}]+\}\}|lorem ipsum|xx+x", RegexOptions.IgnoreCase)] private static partial Regex Placeholders();
    [GeneratedRegex(@"\b(guarantee[sd]?|#1|number one|best in the world|100% (?:free|safe|secure)|risk[- ]free|no risk|cure[sd]?|miracle|instantly rich|world'?s first)\b", RegexOptions.IgnoreCase)] private static partial Regex RiskyClaims();
    // Brief language leaking into public copy: the reader is "you", never "the owner" or "the user".
    [GeneratedRegex(@"\b(the owner'?s?|the user'?s?)\b", RegexOptions.IgnoreCase)] private static partial Regex InternalVoice();
    [GeneratedRegex(@"\b(sign up|start|try|book|join|download|get|learn more|read|register|reply|shop|buy|subscribe|contact|see)\b", RegexOptions.IgnoreCase)] private static partial Regex CallToAction();

    // Practical limits for the post text itself.
    static readonly Dictionary<string, int> ChannelLimits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["x"] = 280, ["twitter"] = 280, ["bluesky"] = 300, ["threads"] = 500, ["linkedin"] = 3000,
        ["instagram"] = 2200, ["facebook"] = 63206, ["reddit"] = 40000, ["hackernews"] = 2000, ["email_subject"] = 78, ["sms"] = 160
    };

    public static QaReport Check(string channel, string destination, string content)
    {
        var checks = new List<QaCheck>();
        void Add(string id, string label, string result, string detail) => checks.Add(new QaCheck(id, label, result, detail));

        var destinationOk = Uri.TryCreate(destination, UriKind.Absolute, out var target) && target.Scheme == "https";
        Add("destination", "Destination is a secure link", destinationOk ? "pass" : "fail",
            destinationOk ? target!.Host : "The destination should be the exact https:// page where this will be posted.");

        var links = Links().Matches(content).Select(match => match.Value.TrimEnd('.', ',', ';', ':')).Distinct().ToArray();
        var insecure = links.Where(link => link.StartsWith("http://", StringComparison.OrdinalIgnoreCase)).ToArray();
        Add("links-secure", "Links use HTTPS", insecure.Length == 0 ? "pass" : "fail",
            insecure.Length == 0 ? (links.Length == 0 ? "No links in the text." : $"{links.Length} link{(links.Length == 1 ? "" : "s")} checked.") : "Not secure: " + string.Join(", ", insecure.Take(3)));
        var untracked = links.Where(link => !link.Contains("utm_source=", StringComparison.OrdinalIgnoreCase) || !link.Contains("utm_campaign=", StringComparison.OrdinalIgnoreCase)).ToArray();
        Add("utm", "Links carry campaign tracking (utm_source, utm_campaign)", links.Length == 0 ? "na" : untracked.Length == 0 ? "pass" : "warn",
            links.Length == 0 ? "No links to tag." : untracked.Length == 0 ? "Every link is tagged." : $"{untracked.Length} link{(untracked.Length == 1 ? "" : "s")} without tags, so results can't be attributed: " + string.Join(", ", untracked.Take(2)));

        var key = channel.Trim().ToLowerInvariant().Replace(" ", "").Replace("(", "").Replace(")", "");
        if (key.StartsWith("r/")) key = "reddit";
        if (ChannelLimits.TryGetValue(key, out var limit))
            Add("length", $"Fits {channel} ({limit:N0} characters)", content.Length <= limit ? "pass" : "fail", $"{content.Length:N0} characters.");
        else Add("length", "Channel length limit", "na", $"No known limit for {channel}. {content.Length:N0} characters.");

        var placeholders = Placeholders().Matches(content).Select(match => match.Value).Distinct().ToArray();
        Add("placeholders", "No placeholders left in", placeholders.Length == 0 ? "pass" : "fail", placeholders.Length == 0 ? "None found." : "Found: " + string.Join(", ", placeholders.Take(4)));
        var internalVoice = InternalVoice().Matches(content).Select(match => match.Value).Distinct().ToArray();
        Add("voice", "Speaks to the reader", internalVoice.Length == 0 ? "pass" : "warn", internalVoice.Length == 0 ? "No brief language." : "Reads like an internal brief (" + string.Join(", ", internalVoice.Take(3)) + "): say “you” to the reader.");
        var risky = RiskyClaims().Matches(content).Select(match => match.Value).Distinct().ToArray();
        Add("claims", "No absolute or risky claims", risky.Length == 0 ? "pass" : "warn", risky.Length == 0 ? "None found." : "Needs support or softer wording: " + string.Join(", ", risky.Take(4)));
        Add("cta", "Clear next step for the reader", CallToAction().IsMatch(content) ? "pass" : "warn", CallToAction().IsMatch(content) ? "A call to action is present." : "No obvious call to action.");
        Add("empty", "Has content", content.Trim().Length >= 20 ? "pass" : "fail", content.Trim().Length >= 20 ? "" : "The text is too short to post.");

        var failed = checks.Count(item => item.Result == "fail"); var warned = checks.Count(item => item.Result == "warn");
        var status = failed > 0 ? "blocked" : warned > 0 ? "ready_with_warnings" : "ready";
        var summary = failed > 0 ? $"{failed} check{(failed == 1 ? "" : "s")} failed. Fix before posting." : warned > 0 ? $"Ready to post, with {warned} warning{(warned == 1 ? "" : "s")} to review." : "Ready for a person to post.";
        return new QaReport(status, [.. checks], summary);
    }

    public static string Markdown(string title, string channel, string destination, string content, QaReport report)
    {
        var mark = new Dictionary<string, string> { ["pass"] = "✅", ["warn"] = "⚠️", ["fail"] = "❌", ["na"] = "–" };
        return $"# Launch checklist: {title}\n\n**Status:** {report.Summary}\n\n- **Channel:** {channel}\n- **Destination:** {destination}\n\n| Check | Result | Detail |\n|---|---|---|\n" +
            string.Join("\n", report.Checks.Select(check => $"| {check.Label} | {mark[check.Result]} | {check.Detail.Replace("|", "\\|")} |")) +
            "\n\n## Approved text\n\n" + content + "\n\n_Nothing was posted. A person posts this and records the live link on the draft._\n";
    }
}
