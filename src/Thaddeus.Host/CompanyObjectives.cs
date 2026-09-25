using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record NorthStar(string Name, string? Metric, double? Target, string? Unit, string? By, string Why);
public record KeyResult(string Text, string? Metric, double? Target);
public record Objective(string Title, KeyResult[] KeyResults);
public record Positioning(string ForWho, string Problem, string Alternatives, string WhyUs, string[] ProofPoints);
public record Competitor(string Name, string Note);
public record ObjectivesContent(NorthStar? NorthStar, Objective[] Objectives, Positioning? Positioning, Competitor[] Competitors,
    string CurrentFocus, string[] NonGoals, string[]? ResearchSites = null, string[]? WatchTopics = null, string[]? Feeds = null);
public record ObjectivesRevision(int Version, ObjectivesContent Content, string UpdatedBy, DateTimeOffset UpdatedAt);
public record ObjectivesChange(int ExpectedVersion, ObjectivesContent Content);

/// <summary>What the business is trying to achieve: the north star, this quarter's objectives, positioning,
/// competitors, current focus and non-goals. Every shift reads it, so work is ranked against real goals.</summary>
public sealed class CompanyObjectives(Store store)
{
    private const string Key = "company-objectives-v1";
    public static readonly ObjectivesContent Empty = new(null, [], null, [], "", []);
    private ObjectivesRevision[] Read() => store.Setting(Key) is { } json ? Wire.Unpack<ObjectivesRevision[]>(json) : [];
    public ObjectivesRevision Current() { lock (store) return Read().LastOrDefault() ?? new ObjectivesRevision(0, Empty, "", DateTimeOffset.MinValue); }
    public ObjectivesRevision[] History() { lock (store) return [.. Read().Reverse()]; }

    static string Text(string? value, int limit, string field)
    {
        var text = (value ?? "").Trim();
        if (text.Length > limit) throw new ArgumentException($"{field} can be up to {limit:N0} characters.");
        return text;
    }
    static string? Metric(string? key) => string.IsNullOrWhiteSpace(key) ? null : Scorecard.MetricKey(key);
    static double? Number(double? value, string field) => value is { } number && (!double.IsFinite(number) || Math.Abs(number) > 1e12) ? throw new ArgumentException(field + " must be a number.") : value;

    public static ObjectivesContent Validate(ObjectivesContent content)
    {
        NorthStar? north = null;
        if (content.NorthStar is { } star && !string.IsNullOrWhiteSpace(star.Name))
        {
            if (star.By is { Length: > 0 } by && !DateOnly.TryParse(by, out _)) throw new ArgumentException("The north star date must be a date.");
            north = new NorthStar(Text(star.Name, 120, "The north star"), Metric(star.Metric), Number(star.Target, "The target"), Text(star.Unit, 20, "The unit"),
                string.IsNullOrWhiteSpace(star.By) ? null : star.By.Trim(), Text(star.Why, 600, "Why it matters"));
        }
        var objectives = (content.Objectives ?? []).Where(item => !string.IsNullOrWhiteSpace(item.Title)).ToArray();
        if (objectives.Length > 5) throw new ArgumentException("Keep it to five objectives or fewer; three is better.");
        var cleanObjectives = objectives.Select(item =>
        {
            var results = (item.KeyResults ?? []).Where(result => !string.IsNullOrWhiteSpace(result.Text)).ToArray();
            if (results.Length > 5) throw new ArgumentException("Each objective can have up to five key results.");
            return new Objective(Text(item.Title, 200, "An objective"), [.. results.Select(result => new KeyResult(Text(result.Text, 300, "A key result"), Metric(result.Metric), Number(result.Target, "A key result target")))]);
        }).ToArray();
        Positioning? positioning = content.Positioning is { } place && new[] { place.ForWho, place.Problem, place.Alternatives, place.WhyUs }.Any(value => !string.IsNullOrWhiteSpace(value)) || content.Positioning?.ProofPoints?.Length > 0
            ? new Positioning(Text(content.Positioning!.ForWho, 400, "Who it's for"), Text(content.Positioning.Problem, 600, "The problem"), Text(content.Positioning.Alternatives, 600, "The alternatives"),
                Text(content.Positioning.WhyUs, 600, "Why you"), [.. (content.Positioning.ProofPoints ?? []).Where(point => !string.IsNullOrWhiteSpace(point)).Take(10).Select(point => Text(point, 300, "A proof point"))])
            : null;
        var competitors = (content.Competitors ?? []).Where(item => !string.IsNullOrWhiteSpace(item.Name)).ToArray();
        if (competitors.Length > 10) throw new ArgumentException("List up to ten competitors.");
        var nonGoals = (content.NonGoals ?? []).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        if (nonGoals.Length > 12) throw new ArgumentException("List up to twelve non-goals.");
        return new ObjectivesContent(north, cleanObjectives, positioning,
            [.. competitors.Select(item => new Competitor(Text(item.Name, 80, "A competitor"), Text(item.Note, 400, "A competitor note")))],
            Text(content.CurrentFocus, 1000, "The current focus"), [.. nonGoals.Select(item => Text(item, 200, "A non-goal"))], Sites(content.ResearchSites), Topics(content.WatchTopics), FeedList(content.Feeds));
    }

    static string[] Sites(string[]? sites)
    {
        var list = (sites ?? []).Where(site => !string.IsNullOrWhiteSpace(site)).ToArray();
        if (list.Length > 10) throw new ArgumentException("List up to ten research sites.");
        return [.. list.Select(site => SiteReader.NormalizeSite(site) ?? throw new ArgumentException($"“{site}” isn't a website address.")).Distinct()];
    }

    static string[] Topics(string[]? topics)
    {
        var list = (topics ?? []).Select(item => System.Text.RegularExpressions.Regex.Replace(item ?? "", @"\s+", " ").Trim()).Where(item => item.Length > 0).ToArray();
        if (list.Length > 10) throw new ArgumentException("Watch up to ten topics.");
        if (list.FirstOrDefault(item => item.Length is < 2 or > 60) is { } bad) throw new ArgumentException($"“{bad}”: a watch topic is 2 to 60 characters.");
        return [.. list.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    static string[] FeedList(string[]? feeds)
    {
        var list = (feeds ?? []).Select(item => (item ?? "").Trim()).Where(item => item.Length > 0).ToArray();
        if (list.Length > 20) throw new ArgumentException("Follow up to twenty feeds.");
        foreach (var feed in list)
            if (feed.Length > 500 || !Uri.TryCreate(feed, UriKind.Absolute, out var url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length > 0 || SiteReader.NormalizeSite(url.Host) == null)
                throw new ArgumentException($"“{(feed.Length > 80 ? feed[..80] : feed)}” isn't an https feed address.");
        return [.. list.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public ObjectivesRevision Save(ObjectivesChange change, string author)
    {
        var content = Validate(change.Content ?? throw new ArgumentException("Nothing to save."));
        lock (store)
        {
            var history = Read();
            var current = history.LastOrDefault()?.Version ?? 0;
            if (change.ExpectedVersion != current) throw new InvalidOperationException("The objectives changed since you opened them. Reload and try again.");
            var revision = new ObjectivesRevision(current + 1, content, author, DateTimeOffset.UtcNow);
            store.Setting(Key, Wire.Pack(history.TakeLast(29).Append(revision).ToArray()));
            return revision;
        }
    }

    /// <summary>The north star's progress from the scorecard, when it is tied to a metric with a target.</summary>
    public static object? Progress(ObjectivesContent content, ScoreLedger scorecard)
    {
        if (content.NorthStar is not { Metric: { } metric, Target: { } target } star) return null;
        var latest = scorecard.Observations.Where(item => item.Metric == metric).OrderBy(item => item.Date, StringComparer.Ordinal).LastOrDefault();
        if (latest == null) return new { metric, target, latest = (double?)null, date = (string?)null, percent = (double?)null };
        // Monthly-style targets compare against the sum of the last 30 days; others against the latest value.
        var monthly = star.Unit is { } unit && unit.Contains("month", StringComparison.OrdinalIgnoreCase);
        var cutoff = DateOnly.Parse(latest.Date).AddDays(-29).ToString("yyyy-MM-dd");
        var value = monthly ? scorecard.Observations.Where(item => item.Metric == metric && string.CompareOrdinal(item.Date, cutoff) >= 0).Sum(item => item.Value) : latest.Value;
        return new { metric, target, latest = Math.Round(value, 2), date = latest.Date, window = monthly ? "last 30 days" : "latest", percent = target == 0 ? (double?)null : Math.Round(value / target * 100, 1) };
    }
}
