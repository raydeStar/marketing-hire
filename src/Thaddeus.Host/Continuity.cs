using System.Text.RegularExpressions;

namespace Thaddeus.Host;

public record ContinuityBet(string Id, string Title, string Hypothesis, string Measurement, string Status, string Result, string Uncertainty);
public record ContinuityView(string[] Finished, string[] ChangedMind, int NeedsYou, string[] NeedsYouTop, string Next, ContinuityBet[] Bets, DateTimeOffset? Since);

/// <summary>Coming back to the employee the next day: what it finished, what changed its mind (what it learned, and the owner's
/// notes it acted on), what needs the owner, and what it does next; and each bet it made, its hypothesis beside the result so far.
/// From the records only, at no model cost.</summary>
public sealed class Continuity(EmployeeShifts shifts, CompanyWiki wiki, DecisionLog decisions, TodayBoard today, EmployeeExperience experience, Publishing publishing, WorkSchedule schedule, Lessons lessons)
{
    static string Label(string item) => item.Split(' ', 2) is [_, var title] ? title : item;

    static string[] Section(string? body, string heading) => body == null ? []
        : Regex.Match(body, $@"^## {Regex.Escape(heading)}\s*\n([\s\S]*?)(?=^## |\z)", RegexOptions.Multiline) is { Success: true } found
            ? [.. found.Groups[1].Value.Split('\n').Select(line => line.Trim().TrimStart('-', '*').Trim()).Where(line => line.Length > 0 && !line.StartsWith("None", StringComparison.Ordinal) && !line.StartsWith('_'))]
            : [];

    public async Task<ContinuityView> View()
    {
        var history = shifts.History();
        var last = history.LastOrDefault();
        var report = history.LastOrDefault(shift => shift.ReportWikiId != null) is { } reported ? wiki.List().FirstOrDefault(page => page.Id == reported.ReportWikiId)?.Body : null;
        var finished = (last?.Created ?? []).Select(Label).Distinct().Take(8).ToArray();

        var since = DateTimeOffset.UtcNow.AddDays(-3);
        var acted = decisions.Entries().Where(entry => entry.At > since && entry.Decision is "Sent back for a redraft" or "Changed direction")
            .Take(3).Select(entry => $"After your note on “{entry.What}” ({(entry.Why.Length > 120 ? entry.Why[..119] + "…" : entry.Why)}), I changed course.");
        // What it changed this week and why comes first: the changes it plans by.
        var changed = lessons.Active().Select(card => $"{card.Title}: {card.Why}").Concat(Section(report, "Learnings").Take(5)).Concat(acted).Take(8).ToArray();

        var board = await today.View();
        var needs = (board.Opportunity != null ? 1 : 0) + board.Today.Length + board.Later.Length;
        var top = (board.Opportunity != null ? [board.Opportunity.Headline] : Array.Empty<string>()).Concat(board.Today.Select(item => item.Title)).Take(4).ToArray();

        var focus = Section(report, "Next shift").FirstOrDefault();
        var nextStart = WorkSchedule.NextStart(schedule.Current(), DateTimeOffset.UtcNow);
        // Relative times: the page shows local time, and a clock time from here would be in the host's zone.
        static string In(DateTimeOffset at) => (at - DateTimeOffset.UtcNow).TotalMinutes is var minutes && minutes <= 1 ? "now" : minutes < 90 ? $"in {Math.Ceiling(minutes):0} minutes" : minutes < 36 * 60 ? $"in {Math.Round(minutes / 60):0} hours" : $"in {Math.Round(minutes / 1440):0} days";
        var next = shifts.OnShift && last?.NextCycleAt is { } cycle ? (In(cycle) == "now" ? "Working now." : $"On shift; the next check-in is {In(cycle)}.") + (focus != null ? " " + focus : "")
            : focus != null ? focus + (nextStart is { } start ? $" The next shift starts {In(start)}." : "")
            : nextStart is { } begin ? $"The next shift starts {In(begin)}." : "No shift planned. Start one, or set working hours and it starts by itself.";

        var published = publishing.Ledger().Publications.Where(item => item.Status == "published").ToArray();
        var bets = experience.View().Recommendations.Where(item => item.Hypothesis.Length > 0 || item.Measurement.Length > 0).TakeLast(6).Reverse().Select(item =>
        {
            var posts = published.Where(post => item.Outputs.Contains("draft:" + post.DraftId)).ToArray();
            var result = posts.Length == 0 ? item.Status == "parked" ? "Set aside." : "Not published yet; no result to read."
                : string.Join("; ", posts.Select(post => $"{post.Channel ?? post.Kind}: " + (post.Results is { } seen ? $"{seen.Likes ?? 0} likes, {seen.Replies ?? 0} replies, {seen.Visits ?? 0} visits" : "published, no numbers yet")));
            return new ContinuityBet(item.Id, item.Title, item.Hypothesis, item.Measurement, item.Status, result, item.Uncertainty);
        }).ToArray();
        return new ContinuityView(finished, changed, needs, top, next, bets, last?.StartedAt);
    }
}
