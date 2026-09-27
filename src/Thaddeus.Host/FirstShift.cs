namespace Thaddeus.Host;

public record FirstShiftPiece(string Key, string Title, string? Grade, string[] Unmet, PreparedSource[] Sources);
public record FirstShiftView(string ShiftId, string Status, DateTimeOffset EndsAt, string? Positioning, FirstShiftPiece[] Prepared, AuditIssue[] Fixes,
    string? Site, string? SiteNote, bool CallToActionSet);

/// <summary>Hired in minutes: the first shift's results in one place: who it's for and why us (the positioning it works from),
/// what it prepared with its grade and the sources behind it, and the three fixes that matter most on the owner's site (a site
/// check in code, no model turn, started once when the first shift begins).</summary>
public sealed class FirstShift(EmployeeShifts shifts, EmployeeExperience experience, EmployeeMemory memory, MarketingRubric rubric, SiteAudit audit, CompanyObjectives objectives, ILogger<FirstShift> logger)
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
            .Concat(recommendations.SelectMany(item => item.Outputs)).Distinct().Take(6).ToArray();
        var prepared = keys.Select(key =>
        {
            var graded = quality.LastOrDefault(entry => entry.Keys?.Contains(key) == true);
            var title = shift.Created.FirstOrDefault(item => item.StartsWith(key + " ", StringComparison.Ordinal))?[(key.Length + 1)..] ?? graded?.Title ?? key;
            var sources = recommendations.FirstOrDefault(item => item.Outputs.Contains(key))?.Sources ?? [];
            return new FirstShiftPiece(key, title, graded is { Scores.Count: > 0 } ? MarketingRubric.Grade(rubric.Overall(graded.Scores)) : null, graded?.Unmet ?? [], [.. sources.Take(4)]);
        }).ToArray();

        // The site: its latest check, or one started now (code only) while the first shift works.
        var site = content.OwnSite is { } own ? SiteReader.NormalizeSite(own) : null;
        string? note = null; AuditIssue[] fixes = [];
        if (site == null) note = "Add your website in Objectives to get the three fixes that matter most on it.";
        else if (audit.Latest(site) is { } latest && latest.At > DateTimeOffset.UtcNow.AddDays(-7))
            fixes = [.. latest.Issues.Where(issue => issue.Severity != "notice").OrderBy(issue => Rank(issue.Severity)).DistinctBy(issue => issue.Check).Take(Fixes)];
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
        return new FirstShiftView(shift.Id, shift.Status, shift.EndsAt, positioning, prepared, fixes, site, note, content.CallToAction != null);
    }
}
