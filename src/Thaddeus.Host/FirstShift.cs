namespace Thaddeus.Host;

public record FirstShiftPiece(string Key, string Title, string? Grade, string[] Unmet, PreparedSource[] Sources);
public record FirstShiftView(string ShiftId, string Status, DateTimeOffset EndsAt, string? Positioning, FirstShiftPiece[] Prepared, AuditIssue[] Fixes,
    string? Site, string? SiteNote, bool CallToActionSet, int PagesChecked = 0, bool OnlySuggestions = false, string? CompetitorNote = null);

/// <summary>Hired in minutes: the first shift's results in one place: who it's for and why us (the positioning it works from),
/// what it prepared with its grade and the sources behind it, and the three fixes that matter most on the owner's site (a site
/// check in code, no model turn, started once when the first shift begins).</summary>
public sealed class FirstShift(EmployeeShifts shifts, EmployeeExperience experience, EmployeeMemory memory, MarketingRubric rubric, SiteAudit audit, CompanyObjectives objectives, Playbooks playbooks, ILogger<FirstShift> logger)
{
    public const int Fixes = 3;
    Task? checking;
    static int Rank(string severity) => severity switch { "error" => 0, "warning" => 1, _ => 2 };

    public FirstShiftView View(string shiftId)
    {
        var shift = shifts.History().FirstOrDefault(item => item.Id == shiftId) ?? throw new KeyNotFoundException("That shift doesn't exist.");
        var content = objectives.Current().Content;
        var positioning = content.Positioning is { } position && (position.ForWho.Trim().Length > 0 || position.WhyUs.Trim().Length > 0)
            ? string.Join(" ", new[] { position.ForWho.Trim().Length > 0 ? $"For {position.ForWho.Trim().TrimEnd('.')}." : "", position.WhyUs.Trim() }.Where(part => part.Length > 0)) : null;

        // What it prepared: each piece it made, graded, with the sources the recommendation cites.
        var recommendations = experience.View().Recommendations.Where(item => item.ShiftId == shiftId).ToArray();
        var quality = memory.Quality();
        var keys = shift.Created.Select(item => item.Split(' ')[0]).Where(key => key.StartsWith("draft:", StringComparison.Ordinal) || key.StartsWith("wiki:", StringComparison.Ordinal) || key.StartsWith("pagecopy:", StringComparison.Ordinal) || key.StartsWith("media:", StringComparison.Ordinal))
            .Concat(recommendations.SelectMany(item => item.Outputs)).Distinct().Take(10).ToArray();   // a week of posts, the site's fix and a snapshot
        var prepared = keys.Select(key =>
        {
            var graded = quality.LastOrDefault(entry => entry.Keys?.Contains(key) == true);
            var title = shift.Created.FirstOrDefault(item => item.StartsWith(key + " ", StringComparison.Ordinal))?[(key.Length + 1)..] ?? graded?.Title ?? key;
            var sources = recommendations.FirstOrDefault(item => item.Outputs.Contains(key))?.Sources ?? [];
            return new FirstShiftPiece(key, title, graded is { Scores.Count: > 0 } ? MarketingRubric.Grade(rubric.Overall(graded.Scores)) : null, [.. (graded?.Unmet ?? []).Select(SpecCheck.Plain).Distinct()], [.. sources.Take(4)]);
        }).ToArray();

        // The site: its latest check, or one started now (code only) while the first shift works.
        var site = content.OwnSite is { } own ? SiteReader.NormalizeSite(own) : null;
        string? note = null; AuditIssue[] fixes = []; var pages = 0; var suggestions = false;
        if (site == null) note = "No website needed: the first fix works on the page people find you by. Add a website in Objectives any time for a full site check.";
        else if (audit.Latest(site) is { } latest && latest.At > DateTimeOffset.UtcNow.AddDays(-7))
        {
            pages = latest.Pages;
            fixes = [.. latest.Issues.Where(issue => issue.Severity != "notice").OrderBy(issue => Rank(issue.Severity)).DistinctBy(issue => issue.Check).Take(Fixes)];
            // Nothing broken: the smaller things it noticed are suggestions, said as such.
            if (fixes.Length == 0 && latest.Issues.Length > 0) { fixes = [.. latest.Issues.DistinctBy(issue => issue.Check).Take(Fixes)]; suggestions = true; }
        }
        else if (!audit.Sites().Contains(site)) note = $"Add {site} to the research sites (Objectives) and the site check runs with the next shift.";
        else
        {
            note = $"Checking {site} now: its pages, titles, descriptions, links and images.";
            lock (this)
                if (checking is not { IsCompleted: false })
                    checking = Task.Run(async () =>
                    {
                        try { await audit.Run(site, "Marketing employee (first shift)", CancellationToken.None); }
                        catch (Exception error) when (error is InvalidOperationException or ArgumentException or HttpRequestException or IOException) { logger.LogWarning("The first shift's site check didn't run: {Error}", error.Message); }
                    });
        }
        var snapshot = shifts.FirstShiftPieces(playbooks.Current() ?? Playbooks.Find("product")!).Any(piece => piece.Title == Playbooks.SnapshotTitle);
        return new FirstShiftView(shift.Id, shift.Status, shift.EndsAt, positioning, prepared, fixes, site, note, content.CallToAction != null, pages, suggestions,
            snapshot ? null : "Want a competitor snapshot next time? Add a competitor and their website in Objectives.");
    }
}
