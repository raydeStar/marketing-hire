using System.Globalization;
using System.Text.RegularExpressions;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record WatchedPage(string Url, string Title, DateTimeOffset CheckedAt, string[] Prices, string[] Words, string Excerpt, string? Error);
public record PageChange(string Url, DateTimeOffset At, string Kind, string Summary);
public record PageWatchLedger(WatchedPage[] Pages, PageChange[] Changes);

/// <summary>Competitors' pricing and plan pages, re-read once a day with the research reader (rendered when the prices are
/// drawn by JavaScript). The first read is the baseline. A changed set of prices becomes a signal for the next shift;
/// a substantial change of wording is noted in Listening only, because pages shift their copy all the time.</summary>
public sealed partial class PageWatch(Store store, CompanyObjectives objectives)
{
    const string Key = "page-watch-v1";
    public static readonly TimeSpan Every = TimeSpan.FromHours(20);
    public Func<string, IReadOnlyCollection<string>, CancellationToken, Task<(string Url, string Title, string Text)>> ReadPage { get; set; } = SiteReader.Read;
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    PageWatchLedger Read() => store.Setting(Key) is { } json ? Wire.Unpack<PageWatchLedger>(json) : new([], []);
    public PageWatchLedger Ledger() { lock (store) return Read(); }
    string[] Pages() => objectives.Current().Content.WatchPages ?? [];

    [GeneratedRegex(@"(?<price>[$€£]\s?\d[\d,]*(?:\.\d{1,2})?)(?<per>\s?(?:/|per|a)\s?(?:mo|month|yr|year|seat|user)\b)?", RegexOptions.IgnoreCase)] private static partial Regex Price();

    /// <summary>The prices a page shows, normalized ("$ 69 / month" → "$69/month"), in page order, duplicates removed.</summary>
    public static string[] Prices(string text) => [.. Price().Matches(text)
        .Select(match => match.Groups["price"].Value.Replace(" ", "") + (match.Groups["per"].Success ? "/" + Regex.Replace(match.Groups["per"].Value, @"^\s*(/|per|a)\s*", "", RegexOptions.IgnoreCase).ToLowerInvariant() : ""))
        .Distinct().Take(40)];

    public static string[] Words(string text) => [.. Regex.Matches(text.ToLowerInvariant(), @"[a-z]{3,}").Select(match => match.Value).Distinct().Take(1500)];

    /// <summary>How much of the wording two reads share (Jaccard over distinct words).</summary>
    public static double Overlap(string[] before, string[] after)
    {
        if (before.Length == 0 && after.Length == 0) return 1;
        var a = before.ToHashSet(); var b = after.ToHashSet();
        return a.Intersect(b).Count() / (double)a.Union(b).Count();
    }

    /// <summary>What changed between two reads of a page, or nothing.</summary>
    public static PageChange[] Compare(WatchedPage before, WatchedPage after)
    {
        var changes = new List<PageChange>();
        if (!before.Prices.SequenceEqual(after.Prices) && (before.Prices.Length > 0 || after.Prices.Length > 0))
        {
            var gone = before.Prices.Except(after.Prices).ToArray(); var added = after.Prices.Except(before.Prices).ToArray();
            var detail = gone.Length == 0 && added.Length == 0 ? "The same prices appear in a different order."
                : string.Join("; ", new[] { gone.Length > 0 ? "no longer shown: " + string.Join(", ", gone) : null, added.Length > 0 ? "now shown: " + string.Join(", ", added) : null }.OfType<string>()) + ".";
            changes.Add(new(after.Url, after.CheckedAt, "prices", $"Prices changed on {Short(after.Url)}: {char.ToUpperInvariant(detail[0])}{detail[1..]} Before: {Join(before.Prices)}. Now: {Join(after.Prices)}."));
        }
        var overlap = Overlap(before.Words, after.Words);
        if (overlap < 0.8)
            changes.Add(new(after.Url, after.CheckedAt, "copy", $"The wording of {Short(after.Url)} changed substantially ({Math.Round((1 - overlap) * 100)}% of its distinct words are new or gone)."));
        return [.. changes];
    }

    static string Join(string[] prices) => prices.Length == 0 ? "none" : string.Join(", ", prices.Take(12));
    public static string Short(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.Replace("www.", "") + uri.AbsolutePath.TrimEnd('/') : url;

    /// <summary>Reads each watched page not read in the last 20 hours. Called from the hourly listening pass.</summary>
    public async Task<int> Check(CancellationToken cancellation)
    {
        var pages = Pages();
        var sites = objectives.Current().Content.ResearchSites ?? [];
        var now = Clock();
        var ledger = Ledger();
        var due = pages.Where(url => ledger.Pages.FirstOrDefault(item => item.Url == url) is not { } seen || seen.CheckedAt <= now - Every).ToArray();
        var fresh = new List<WatchedPage>(); var changes = new List<PageChange>();
        foreach (var url in due)
        {
            var previous = ledger.Pages.FirstOrDefault(item => item.Url == url);
            try
            {
                var (_, title, text) = await ReadPage(url, sites, cancellation);
                var read = new WatchedPage(url, title, now, Prices(text), Words(text), text.Length > 600 ? text[..600] : text, null);
                // The last good read is the baseline, even if a later read failed.
                if (previous is { } baseline && (baseline.Words.Length > 0 || baseline.Prices.Length > 0)) changes.AddRange(Compare(baseline, read));
                fresh.Add(read);
            }
            catch (Exception error) when (error is IOException or HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                // A failed read keeps the last good baseline; it is retried on the next pass.
                fresh.Add(previous is { } kept ? kept with { CheckedAt = now - Every + TimeSpan.FromHours(1), Error = error.Message } : new(url, Short(url), now - Every + TimeSpan.FromHours(1), [], [], "", error.Message));
            }
        }
        if (due.Length == 0) return 0;
        lock (store)
        {
            var current = Read();
            var kept = current.Pages.Where(item => pages.Contains(item.Url) && fresh.All(read => read.Url != item.Url));
            store.Setting(Key, Wire.Pack(new PageWatchLedger([.. kept.Concat(fresh)], [.. current.Changes.Concat(changes).Where(item => item.At > now.AddDays(-60)).TakeLast(60)])));
        }
        return changes.Count;
    }

    /// <summary>Price changes from the last three days, as signals for a shift.</summary>
    public IEnumerable<ShiftSignal> Signals()
    {
        var now = Clock();
        foreach (var change in Ledger().Changes.Where(item => item.Kind == "prices" && item.At > now.AddDays(-3)))
            yield return new ShiftSignal("competitor_change", "high", $"Price change on {Short(change.Url)}", change.Summary,
                $"watch:{change.Url}:{change.At.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}", change.Url);
    }

    /// <summary>The page as a source a deliverable can cite: what changed, and what it says now.</summary>
    public ResearchSource? SourceFor(string url)
    {
        var ledger = Ledger();
        if (ledger.Pages.FirstOrDefault(item => item.Url == url) is not { } page) return null;
        var change = ledger.Changes.LastOrDefault(item => item.Url == url);
        return new ResearchSource(url, page.Title, (change != null ? change.Summary + " " : "") + "Current page: " + page.Excerpt, null, page.CheckedAt, "Watched page");
    }

    public object View()
    {
        var ledger = Ledger();
        return Pages().Select(url =>
        {
            var page = ledger.Pages.FirstOrDefault(item => item.Url == url);
            var last = ledger.Changes.LastOrDefault(item => item.Url == url);
            return new { url, title = page?.Title, checkedAt = page?.Error == null ? page?.CheckedAt : null, prices = page?.Prices ?? [], error = page?.Error,
                lastChange = last == null ? null : new { at = last.At, kind = last.Kind, summary = last.Summary } };
        });
    }
}
