using System.Globalization;
using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

/// <summary>One change the employee made to how it works, and why: "more LinkedIn, less X", from the numbers that justify it.</summary>
public record LessonCard(string Id, string Week, string Channel, string Direction, double Weight, string Title, string Why, DateTimeOffset At, DateTimeOffset Until);

/// <summary>Learns out loud: once a week, from the last two weeks of post results and the owner's verdicts, the employee adopts at
/// most three changes to where it puts its effort, says each one plainly with its numbers, and plans by them for the next two
/// weeks. A change needs enough evidence: three or more posts with results on each channel compared (and one doing at least
/// twice as well), or four or more decisions on a channel's drafts. Code, not a model.</summary>
public sealed class Lessons(Store store, Publishing publishing, MarketingBackend marketing)
{
    const string Key = "lessons-v1";
    public const int Most = 3, MinPosts = 3, MinDecisions = 4;
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    LessonCard[] Read() { lock (store) return store.Setting(Key) is { } json ? Wire.Unpack<LessonCard[]>(json) : []; }
    static string Week(DateTimeOffset at) => $"{ISOWeek.GetYear(at.UtcDateTime)}-W{ISOWeek.GetWeekOfYear(at.UtcDateTime):00}";
    static string Str(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    static long Epoch(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? (long)value.GetDouble() : 0;
    static int Score(PostResults results) => (results.Likes ?? 0) + 2 * (results.Reposts ?? 0) + 3 * (results.Replies ?? 0) + (results.Visits ?? 0);

    /// <summary>The changes in force now, newest first.</summary>
    public LessonCard[] Active() { var now = Clock(); return [.. Read().Where(card => card.Until > now).OrderByDescending(card => card.At)]; }
    public LessonCard[] All() => [.. Read().OrderByDescending(card => card.At)];

    /// <summary>What each channel's weight is now: above 1 for more, below 1 for less; channels without a change are 1.</summary>
    public Dictionary<string, double> Weights() => Active().GroupBy(card => card.Channel, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First().Weight, StringComparer.OrdinalIgnoreCase);

    /// <summary>This week's changes: adopted once a week (the plan and the update both ask; the first one decides).</summary>
    public async Task<LessonCard[]> Adopt(CancellationToken cancellation)
    {
        var now = Clock(); var week = Week(now);
        if (Read().Any(card => card.Week == week)) return [.. Read().Where(card => card.Week == week)];
        var cards = new List<LessonCard>();
        var since = now.AddDays(-14);
        // Where the posts did best: the channel with the most engagement per post against the one with the least.
        var byChannel = publishing.Ledger().Publications.Where(item => item.Status == "published" && item.PublishedAt >= since && item.Results != null)
            .GroupBy(item => item.Channel ?? item.Kind, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() >= MinPosts)
            .Select(group => (Channel: group.Key, Posts: group.Count(), Average: group.Average(item => Score(item.Results!)))).OrderByDescending(item => item.Average).ToArray();
        if (byChannel.Length >= 2 && byChannel[0].Average >= 3 && byChannel[0].Average >= 2 * Math.Max(byChannel[^1].Average, 0.5))
        {
            var (best, worst) = (byChannel[0], byChannel[^1]);
            var times = worst.Average > 0 ? $"{best.Average / worst.Average:0.#}×" : "far more than";
            cards.Add(Card(week, best.Channel, "more", 1.5, $"More {best.Channel}",
                $"{best.Channel} posts earned {times} the engagement of {worst.Channel} posts over two weeks ({best.Average:0.#} against {worst.Average:0.#} a post, {best.Posts} and {worst.Posts} posts). I'm putting more of the social work there.", now));
            cards.Add(Card(week, worst.Channel, "less", 0.6, $"Less {worst.Channel}",
                $"{worst.Channel} brought the least per post ({worst.Average:0.#}, over {worst.Posts} posts), so I'm writing fewer {worst.Channel} posts unless you assign them.", now));
        }
        // What the owner keeps turning down: a channel whose drafts they mostly reject.
        var work = (await marketing.ShiftHire(null, "snapshot")).Value;
        var decided = work is { } w && w.TryGetProperty("drafts", out var drafts) ? drafts.EnumerateArray().Where(draft => Epoch(draft, "decided_at") >= since.ToUnixTimeSeconds() && Str(draft, "status") is "approved" or "posted" or "rejected").ToArray() : [];
        foreach (var group in decided.GroupBy(draft => Str(draft, "channel"), StringComparer.OrdinalIgnoreCase).Where(group => group.Key.Length > 0 && group.Count() >= MinDecisions))
        {
            var approved = group.Count(draft => Str(draft, "status") is "approved" or "posted");
            if (approved * 4 > group.Count() || cards.Any(card => string.Equals(card.Channel, group.Key, StringComparison.OrdinalIgnoreCase))) continue;
            cards.Add(Card(week, group.Key, "less", 0.6, $"Fewer {group.Key} drafts",
                $"You approved {approved} of the last {group.Count()} {group.Key} drafts, so I'm writing fewer until I learn what you want there. Your notes on the ones you sent back are what I'll change first.", now));
        }
        var adopted = cards.Take(Most).ToArray();
        lock (store) store.Setting(Key, Wire.Pack(Read().Concat(adopted).TakeLast(60).ToArray()));
        return adopted;
    }

    static LessonCard Card(string week, string channel, string direction, double weight, string title, string why, DateTimeOffset now) =>
        new(Guid.NewGuid().ToString("N")[..12], week, channel, direction, weight, title, why, now, now.AddDays(14));

    /// <summary>The weekly update's section: what changed and why.</summary>
    public static string Section(LessonCard[] cards) => cards.Length == 0 ? "" :
        "## What I changed and why\n\n" + string.Join("\n", cards.Select(card => $"- **{card.Title}.** {card.Why}")) + "\n\n";
}
