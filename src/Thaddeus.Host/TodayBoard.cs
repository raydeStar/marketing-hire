using System.Text.Json;
using System.Text.RegularExpressions;

namespace Thaddeus.Host;

public record TodayPrepared(string Key, string Kind, string Title);
public record TodayEvidence(string Title, string? Url = null, string? Key = null);
public record TodayDecisionOption(string Id, string Label, bool Primary = false);
public record TodayOpportunity(string Id, string Headline, string Why, string Recommendation, TodayPrepared[] Prepared, TodayEvidence[] Evidence, TodayDecisionOption[] Decisions);
public record TodayView(TodayOpportunity? Opportunity, AttentionItem[] Today, AttentionItem[] Later);
public record TodayDecision(string Decision, string? Note);

/// <summary>What the owner sees first: the one opportunity the employee prepared (its recommendation, with the work already done
/// and the decision that moves it), then at most three other things worth a decision today, and everything else under Later.
/// A task that only points at a waiting draft, document or page proposal is shown once, as that item.</summary>
public sealed class TodayBoard(MarketingBackend marketing, OwnerAttention attention, EmployeeExperience experience, EmployeeShifts shifts,
    EmployeeMemory memory, Campaigns campaigns, CompanyWiki wiki, PageProposals pages, WorkspaceLibrary library, Scorecard scorecard)
{
    public const int TodayCount = 3;
    static string Str(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    static string Id(JsonElement item) => item.TryGetProperty("id", out var value) ? value.ValueKind == JsonValueKind.Number ? value.GetRawText() : value.GetString() ?? "" : "";
    static string Clip(string text, int length = 140) { text = Regex.Replace(text, @"[#*`>_]", "").ReplaceLineEndings(" ").Trim(); return text.Length > length ? text[..(length - 1)].TrimEnd() + "…" : text; }

    public async Task<TodayView> View()
    {
        var snapshot = await marketing.ShiftHire(null, "snapshot");
        var work = snapshot.Value ?? JsonSerializer.SerializeToElement(new { drafts = Array.Empty<object>(), tasks = Array.Empty<object>() });
        JsonElement[] List(string name) => work.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array ? [.. list.EnumerateArray()] : [];
        var drafts = List("drafts").Where(item => Str(item, "status") == "pending").ToArray();
        var waitingKeys = new HashSet<string>(drafts.Select(item => "draft:" + Id(item)));
        foreach (var page in pages.List().Where(item => item.Status == "pending")) waitingKeys.Add("pagecopy:" + page.Id);
        foreach (var page in wiki.List().Where(item => item.Status == "draft")) waitingKeys.Add("wiki:" + page.Id);

        // Tasks a shift linked to the item they asked for: the item is the decision, so the task isn't shown beside it.
        var linked = shifts.History().TakeLast(10).SelectMany(shift => shift.Handled).Where(item => item.StartsWith("link:", StringComparison.Ordinal))
            .Select(item => item.Split(':')).Where(parts => parts.Length == 4 && waitingKeys.Contains((parts[1] == "expstart" ? "exp" : parts[1]) + ":" + parts[2]))
            .Select(parts => parts[3]).ToHashSet();

        var items = new List<AttentionItem>();
        if (work.TryGetProperty("profile", out var profile) && (Str(profile, "product_summary").Trim().Length == 0 || Str(profile, "goals").Trim().Length == 0))
            items.Add(new("brief", "brief", "Finish your business brief", "Marketing needs your offer and goals before it can plan useful work.", "brief:profile"));
        items.AddRange(attention.Items());
        items.AddRange(drafts.Select(item => new AttentionItem("draft:" + Id(item), "draft", $"Draft for {Str(item, "channel")}", Clip(Str(item, "content")), "draft:" + Id(item))));
        // What's still short of its assignment or the owner's notes leads the item, so it's seen before deciding.
        var quality = memory.Quality();
        items = [.. items.Select(item => quality.LastOrDefault(entry => entry.Keys?.Contains(item.Target ?? item.Id) == true) is { Unmet: { Length: > 0 } unmet }
            ? item with { Detail = Clip("Doesn't meet: " + string.Join("; ", unmet) + ". " + item.Detail, 220) } : item)];
        items.AddRange(List("tasks").Where(task => Str(task, "status") == "needs_you" && !linked.Contains(Id(task)))
            .Select(task => new AttentionItem("task:" + Id(task), "task", Str(task, "title"), Clip(Str(task, "blocker") is { Length: > 0 } blocker ? blocker : Str(task, "next_action")) is { Length: > 0 } detail ? detail : "Needs your decision.", "task:" + Id(task))));

        var opportunity = Opportunity(waitingKeys, drafts);
        var shown = opportunity?.Prepared.Select(item => item.Key).ToHashSet() ?? [];
        var ranked = items.Where(item => !shown.Contains(item.Target ?? item.Id) && !shown.Contains(item.Id)).DistinctBy(item => item.Id)
            .OrderByDescending(Score).ToArray();
        return new TodayView(opportunity, [.. ranked.Take(TodayCount)], [.. ranked.Skip(TodayCount)]);
    }

    /// <summary>How much a decision on this moves things now: a stalled employee and an unfinished brief first, then work for an
    /// open campaign, then the work furthest along (graded higher), page copy and drafts before documents.</summary>
    double Score(AttentionItem item)
    {
        var key = item.Target ?? item.Id;
        var score = item.Kind switch { "shift" => 100, "brief" => 90, "page" => 12, "draft" => 10, "experiment" => 9, "document" => 8, "task" => 6, _ => 5 };
        if (campaigns.Find(campaigns.Of(key)) is { Status: "active" or "planned" }) score += 30;
        if (memory.Quality().LastOrDefault(entry => entry.Keys?.Contains(key) == true) is { } graded)
        {
            var overall = graded.Scores.Count > 0 ? graded.Scores.Values.Average() : 0;
            score += overall >= 4.5 ? 10 : overall >= 3.5 ? 5 : 0;
        }
        return score;
    }

    /// <summary>The newest recommendation that's ready and still has work waiting on the owner.</summary>
    TodayOpportunity? Opportunity(HashSet<string> waitingKeys, JsonElement[] drafts)
    {
        var ready = experience.View().Recommendations.Where(item => item.Status == "ready" && item.Outputs.Any(key => waitingKeys.Contains(key) || key.StartsWith("media:", StringComparison.Ordinal)))
            .OrderByDescending(item => item.UpdatedAt).FirstOrDefault();
        if (ready == null) return null;
        var why = ready.WhyNow + (ready.Uncertainty is { Length: > 0 } limits ? " " + limits : "");
        var recommendation = ready.Recommendation + (ready.NextStep is { Length: > 0 } next ? " " + next : "");
        return new TodayOpportunity(ready.Id, ready.Title, why.Trim(), recommendation.Trim(),
            [.. ready.Outputs.Select(key => new TodayPrepared(key, key.Split(':')[0], Title(key, drafts)))],
            [.. ready.Sources.Select(source => new TodayEvidence(source.Title + (source.Coverage.Length > 0 ? $" ({source.Coverage})" : ""), source.Url))],
            [new("review", "Review the package", true), new("change", "Change direction"), new("park", "Park it")]);
    }

    string Title(string key, JsonElement[] drafts)
    {
        var id = key[(key.IndexOf(':') + 1)..];
        return key.Split(':')[0] switch
        {
            "draft" => drafts.FirstOrDefault(item => Id(item) == id) is { ValueKind: JsonValueKind.Object } draft ? $"{Str(draft, "channel")} draft #{id}" : $"Draft #{id}",
            "wiki" => wiki.List().FirstOrDefault(page => page.Id == id)?.Title ?? "Document",
            "pagecopy" => pages.Find(id) is { } proposal ? "New copy for " + PageWatch.Short(proposal.Url) : "Page copy",
            "exp" => scorecard.Ledger().Experiments.FirstOrDefault(item => item.Id == id)?.Title ?? "Experiment",
            "media" => library.View("").Entries.FirstOrDefault(entry => entry.Key == key) is { } entry ? Path.GetFileName(entry.Key) : "Media",
            _ => key
        };
    }

    /// <summary>The owner's call on the opportunity: review it (it stays), change direction (the employee gets the note as its next
    /// task, and this one is set aside), or park it.</summary>
    public async Task<TodayView> Decide(string id, TodayDecision decision, string actor)
    {
        var item = experience.View().Recommendations.FirstOrDefault(entry => entry.Id == id) ?? throw new KeyNotFoundException("That recommendation is no longer available.");
        var note = (decision.Note ?? "").Trim();
        switch (decision.Decision)
        {
            case "review": break;
            case "park": experience.Decide(id, new RecommendationDecision(item.Version, "parked", note.Length > 0 ? note : "Parked by the owner.")); break;
            case "change":
                if (note.Length < 3) throw new ArgumentException("Say which direction to take instead.");
                await shifts.ChangeDirection(item, note, actor);
                experience.Decide(id, new RecommendationDecision(item.Version, "parked", "Direction changed: " + note));
                break;
            default: throw new ArgumentException("Choose review, change or park.");
        }
        return await View();
    }
}
