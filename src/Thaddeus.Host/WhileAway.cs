using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

/// <summary>One tap on a noticed item: open what the employee already made for it, open the post, or ask for the work.</summary>
public record AwayAction(string Label, string Kind, string? Key = null, string? Url = null);
/// <summary>Something the employee noticed while the owner was away, with the one action that moves it.</summary>
public record AwayItem(string Id, string Kind, string Title, string Detail, string? Url, AwayAction Action);
public record AwayAct(string Action);

/// <summary>While you were away: at most three things from the records since the owner last looked (a day; the weekend on a
/// Monday), each with one action: a public question on a watch topic, with the reply the employee drafted for it; a
/// competitor's price change, with the post it drafted; and what the latest posts brought in. Code, not a model: the same
/// records, the same list.</summary>
public sealed class WhileAway(Store store, MarketListening listening, Publishing publishing, EmployeeShifts shifts, MarketingBackend marketing)
{
    const string Key = "while-away-done-v1";
    public const int Most = 3;
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    static string Str(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    static string Id(JsonElement item) => item.TryGetProperty("id", out var value) ? value.ValueKind == JsonValueKind.Number ? value.GetRawText() : value.GetString() ?? "" : "";
    static string Flat(string text, int length) { var flat = Regex.Replace(text, @"\s+", " ").Trim(); return flat.Length > length ? flat[..(length - 1)].TrimEnd() + "…" : flat; }
    HashSet<string> Done() { lock (store) return store.Setting(Key) is { } json ? [.. Wire.Unpack<string[]>(json)] : []; }
    void MarkDone(string id) { lock (store) store.Setting(Key, Wire.Pack(Done().Append(id).TakeLast(400).ToArray())); }

    /// <summary>Since the owner last looked: a day, or since Friday evening on a Monday.</summary>
    public DateTimeOffset Since(DateTimeOffset now) => now.AddHours(now.ToLocalTime().DayOfWeek == DayOfWeek.Monday ? -72 : -24);

    public async Task<AwayItem[]> Items()
    {
        var now = Clock(); var since = Since(now); var done = Done();
        var work = (await marketing.ShiftHire(null, "snapshot")).Value;
        var drafts = work is { } w && w.TryGetProperty("drafts", out var list) ? list.EnumerateArray().ToArray() : [];
        // What the employee made for a signal and the owner hasn't decided yet: the reply, the post.
        string? Waiting(string signalRef) => shifts.Answered(signalRef).FirstOrDefault(key => key.StartsWith("draft:", StringComparison.Ordinal)
            ? drafts.FirstOrDefault(draft => "draft:" + Id(draft) == key) is { ValueKind: JsonValueKind.Object } made && Str(made, "status") == "pending"
            : key.StartsWith("wiki:", StringComparison.Ordinal));
        var items = new List<AwayItem>();

        // A question someone asked in public about a watch topic.
        foreach (var asked in listening.Ledger().Mentions.Where(item => item.PublishedAt >= since && MarketListening.Asks(item)).OrderByDescending(item => item.PublishedAt).Take(3))
        {
            var id = "listen:question:" + asked.Id;
            if (done.Contains(id)) continue;
            var reply = Waiting(id);
            items.Add(new(id, "question", $"A question on {asked.Source}: {Flat(asked.Title.Length > 0 ? asked.Title : asked.Snippet, 90)}",
                reply != null ? $"The employee drafted a reply for you to post under it. It asked about “{asked.Topic}”." : $"Someone asked about “{asked.Topic}”. A helpful reply puts you in the conversation.",
                asked.Url, reply != null ? new("Review the reply", "open", reply) : new("Draft a reply", "assign")));
            break;
        }
        // A competitor changed a price on a page the owner watches.
        foreach (var change in listening.Watched().Changes.Where(item => item.Kind == "prices" && item.At >= since).OrderByDescending(item => item.At).Take(3))
        {
            var id = $"watch:{change.Url}:{change.At.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
            if (done.Contains(id)) continue;
            var made = Waiting(id);
            items.Add(new(id, "competitor", $"Price change on {PageWatch.Short(change.Url)}", Flat(change.Summary, 200) + (made != null ? " The employee drafted a response." : ""),
                change.Url, made != null ? new("Review the response", "open", made) : new("Draft a response", "assign")));
            break;
        }
        // What the latest posts brought in.
        var posts = publishing.Ledger().Publications.Where(item => item.Status == "published" && item.PublishedAt >= since.AddHours(-24) && item.Results is { } results
            && (results.Likes ?? 0) + (results.Reposts ?? 0) + (results.Replies ?? 0) + (results.Visits ?? 0) > 0).ToArray();
        if (posts.Length > 0)
        {
            static int Score(PostResults results) => (results.Likes ?? 0) + 2 * (results.Reposts ?? 0) + 3 * (results.Replies ?? 0) + (results.Visits ?? 0);
            var best = posts.OrderByDescending(item => Score(item.Results!)).First();
            var id = "post:" + best.Id;
            if (!done.Contains(id))
            {
                var r = best.Results!;
                var counts = string.Join(", ", new[] { (r.Likes, "likes"), (r.Reposts, "reposts"), (r.Replies, "replies"), (r.Visits, "visits") }.Where(pair => pair.Item1 is > 0).Select(pair => $"{pair.Item1} {pair.Item2}"));
                items.Add(new(id, "results", posts.Length == 1 ? $"Your {best.Channel ?? best.Kind} post brought in {counts}" : $"{posts.Length} posts went out; the {best.Channel ?? best.Kind} one did best: {counts}",
                    Flat(best.Excerpt ?? "", 140), best.Url, new("Write a follow-up", "assign")));
            }
        }
        return [.. items.Take(Most)];
    }

    /// <summary>The one action: an "assign" becomes the employee's next task (a reply to post under the question, a response to
    /// the price change, a follow-up to the post that worked); any action takes the item off the list.</summary>
    public async Task<AwayItem[]> Act(string id, AwayAct act)
    {
        var item = (await Items()).FirstOrDefault(entry => entry.Id == id) ?? throw new KeyNotFoundException("That item is no longer on the list.");
        if (act.Action == "assign" && item.Action.Kind == "assign")
        {
            var (title, next) = item.Kind switch
            {
                "question" => ($"Reply: {Flat(item.Title, 120)}", $"Draft a helpful reply to post under {item.Url} (channel: the network it's on, destination: that post). Answer the question plainly from the facts page; mention HireZero only where it truly helps."),
                "competitor" => ($"Respond: {Flat(item.Title, 120)}", $"{item.Detail} Decide whether it changes our positioning, and draft one post or a short note on what to do, citing the page {item.Url}."),
                _ => ($"Follow-up: {Flat(item.Title, 120)}", $"Our post did well ({item.Title}; {item.Detail}). Draft one follow-up post for the same channel that builds on what worked, with a different opening. The post: {item.Url}")
            };
            await shifts.AssignFromAway(title.Length > 160 ? title[..160] : title, next);
        }
        MarkDone(id);
        return await Items();
    }

    /// <summary>The morning brief's opening section, in the same order as the list.</summary>
    public static string Section(AwayItem[] items) => items.Length == 0 ? "" :
        "## While you were away\n\n" + string.Join("\n", items.Select(item => $"- **{item.Title}.** {item.Detail}" + (item.Url != null ? $" ([open]({item.Url}))" : "") + $" _{item.Action.Label}._")) + "\n\n";
}
