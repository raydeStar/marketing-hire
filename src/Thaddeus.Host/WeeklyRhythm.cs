using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record WeeklyDoc(string Kind, string Week, string WikiId, string Title, DateTimeOffset At, string? EmailUrl);
public record WeeklySettings(bool Enabled, string TimeZone, int PlanDay, string PlanTime, int UpdateDay, string UpdateTime, bool EmailDraft,
    string? LastPlanWeek, string? LastUpdateWeek, WeeklyDoc[] Docs, string? LastMonth = null);
public record WeeklySettingsChange(bool Enabled, string TimeZone, int? PlanDay, string? PlanTime, int? UpdateDay, string? UpdateTime, bool? EmailDraft);

/// <summary>The weekly rhythm from the research: a plan at the start of the week and an update at the end, written from the
/// host's own records (numbers, posts and how they did, work done, decisions, listening, learnings) at no model cost.
/// Each is filed in the Library under Reports/Weekly and, if the owner wants, saved as a Gmail draft to forward. In the first week of
/// each month the same records give a monthly report on the month just ended, filed under Reports/Monthly.</summary>
public sealed class WeeklyRhythm(Store store, CompanyObjectives objectives, Scorecard scorecard, Publishing publishing, CompanyWiki wiki,
    WorkspaceLibrary library, EmployeeMemory memory, EmployeeShifts shifts, MarketListening listening, MarketingBackend marketing, DataConnections data, ILogger<WeeklyRhythm> logger)
{
    private const string Key = "weekly-rhythm-v1";
    const string Author = "Marketing employee (weekly)";
    private readonly SemaphoreSlim gate = new(1, 1);
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    public WeeklySettings Settings() { lock (store) return store.Setting(Key) is { } json ? Wire.Unpack<WeeklySettings>(json) : new(false, "UTC", 1, "08:00", 5, "16:00", false, null, null, []); }
    void Write(WeeklySettings settings) { lock (store) store.Setting(Key, Wire.Pack(settings)); }

    static TimeSpan Time(string value, string field) =>
        TimeSpan.TryParseExact(value ?? "", @"hh\:mm", CultureInfo.InvariantCulture, out var time) && time < TimeSpan.FromDays(1) ? time : throw new ArgumentException($"{field} must be a time like 08:00.");
    static TimeZoneInfo Zone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { throw new ArgumentException("That time zone isn't known."); }
    }

    public WeeklySettings Save(WeeklySettingsChange change)
    {
        var current = Settings();
        Zone(change.TimeZone);
        var planDay = change.PlanDay ?? current.PlanDay; var updateDay = change.UpdateDay ?? current.UpdateDay;
        if (planDay is < 0 or > 6 || updateDay is < 0 or > 6) throw new ArgumentException("Days run from 0 (Sunday) to 6 (Saturday).");
        var planTime = change.PlanTime ?? current.PlanTime; var updateTime = change.UpdateTime ?? current.UpdateTime;
        Time(planTime, "The plan time"); Time(updateTime, "The update time");
        var next = current with { Enabled = change.Enabled, TimeZone = change.TimeZone, PlanDay = planDay, PlanTime = planTime, UpdateDay = updateDay, UpdateTime = updateTime, EmailDraft = change.EmailDraft ?? current.EmailDraft };
        Write(next);
        return next;
    }

    public object View() => new { settings = Settings(), latest = Settings().Docs.OrderByDescending(item => item.At).Take(6) };

    // Weeks run Monday to Sunday in the owner's time zone.
    static DateTime WeekStart(DateTime local) => local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7));
    static string WeekKey(DateTime local) => $"{ISOWeek.GetYear(local)}-W{ISOWeek.GetWeekOfYear(local):00}";
    static DateTime Moment(DateTime weekStart, int day, string time) => weekStart.AddDays((day + 6) % 7) + Time(time, "time");
    static string MonthKey(DateTime local) => local.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Writes the plan or the update once its moment has passed this week. Called by the pump.</summary>
    public async Task<WeeklyDoc?> Tick(CancellationToken cancellation)
    {
        var settings = Settings();
        if (!settings.Enabled) return null;
        var local = TimeZoneInfo.ConvertTime(Clock(), Zone(settings.TimeZone)).DateTime;
        var start = WeekStart(local); var week = WeekKey(local);
        var plan = Moment(start, settings.PlanDay, settings.PlanTime); var update = Moment(start, settings.UpdateDay, settings.UpdateTime);
        if (local >= update && settings.LastUpdateWeek != week) return await Write("update", cancellation);
        // A plan written after the update's moment would be about a week that's over.
        if (local >= plan && local < update && settings.LastPlanWeek != week) return await Write("plan", cancellation);
        // The monthly report: once, in the first week of the month, after the plan's time of day.
        if (local.Day <= 7 && local.TimeOfDay >= Time(settings.PlanTime, "time") && settings.LastMonth != MonthKey(local.AddMonths(-1))) return await Write("month", cancellation);
        return null;
    }

    public async Task<WeeklyDoc> Write(string kind, CancellationToken cancellation)
    {
        if (kind is not ("plan" or "update" or "month")) throw new ArgumentException("Write the plan, the update or the monthly report.");
        await gate.WaitAsync(cancellation);
        try
        {
            var settings = Settings();
            var zone = Zone(settings.TimeZone);
            var now = Clock();
            var local = TimeZoneInfo.ConvertTime(now, zone).DateTime;
            var start = WeekStart(local); var week = WeekKey(local);
            var startUtc = new DateTimeOffset(start, zone.GetUtcOffset(start)).ToUniversalTime();
            // The month just ended, in the owner's time zone.
            var monthStart = new DateTime(local.Year, local.Month, 1).AddMonths(-1);
            var body = kind == "plan" ? await Plan(start, startUtc, now) : kind == "update" ? await Update(start, startUtc, now) : await Month(monthStart, zone, now);
            var title = kind == "plan" ? $"Weekly plan: week of {start:MMM d}" : kind == "update" ? $"Weekly update: week of {start:MMM d}" : $"Monthly report: {monthStart.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}";
            var page = wiki.Save(new WikiChange(Guid.NewGuid().ToString("N"), null, 0, "company", "company", title, body, "fact", "active"), Author);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try { library.SaveEntry("wiki:" + page.Id, new LibraryEntryChange(library.View("").Version, kind == "month" ? "Reports/Monthly" : "Reports/Weekly", [kind == "month" ? "monthly" : "weekly", kind]), Author, "employee"); break; }
                catch (InvalidOperationException) when (attempt < 2) { }
            }
            string? email = null;
            if (kind is "update" or "month" && settings.EmailDraft)
                try { email = await publishing.EmailDraft(title, Regex.Replace(body, @"[#*_`]", "").Trim(), cancellation); }
                catch (Exception error) when (error is InvalidOperationException or HttpRequestException or TaskCanceledException) { logger.LogWarning("The weekly update's Gmail draft failed: {Error}", error.Message); }
            var doc = new WeeklyDoc(kind, kind == "month" ? MonthKey(monthStart) : week, page.Id, title, now, email);
            lock (store)
            {
                var current = Settings();
                Write(current with { LastPlanWeek = kind == "plan" ? week : current.LastPlanWeek, LastUpdateWeek = kind == "update" ? week : current.LastUpdateWeek, LastMonth = kind == "month" ? MonthKey(monthStart) : current.LastMonth,
                    Docs = [.. current.Docs.TakeLast(51), doc] });
            }
            return doc;
        }
        finally { gate.Release(); }
    }

    // ---------- What goes in ----------
    static string Str(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    static long Epoch(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? (long)value.GetDouble() : 0;
    static string Bullets(IEnumerable<string> items, string empty) { var list = items.ToList(); return list.Count == 0 ? $"- {empty}\n" : string.Join("\n", list.Select(item => "- " + item)) + "\n"; }
    static string Flat(string text, int length) { var flat = Regex.Replace(text, @"\s+", " ").Trim(); return flat.Length > length ? flat[..length] + "…" : flat; }

    string NorthStar()
    {
        var content = objectives.Current().Content;
        if (content.NorthStar is not { } star) return "_No north star set yet._\n";
        var progress = JsonSerializer.SerializeToElement(CompanyObjectives.Progress(content, scorecard.Ledger()));
        var line = $"**{star.Name}**" + (star.Target is { } target ? $": target {target.ToString("0.##", CultureInfo.InvariantCulture)} {star.Unit}" + (star.By != null ? $" by {star.By}" : "") : "");
        if (progress.ValueKind == JsonValueKind.Object && progress.TryGetProperty("latest", out var latest) && latest.ValueKind == JsonValueKind.Number)
            line += $". Now {latest.GetDouble().ToString("0.##", CultureInfo.InvariantCulture)} ({(progress.TryGetProperty("percent", out var percent) && percent.ValueKind == JsonValueKind.Number ? percent.GetDouble().ToString("0.#", CultureInfo.InvariantCulture) + "% of target" : "no target")}).";
        return line + "\n";
    }

    /// <summary>Each metric's last seven complete days against the seven before, as a daily average.</summary>
    string Numbers(DateTime localToday)
    {
        var ledger = scorecard.Ledger();
        var lines = new List<string>();
        var end = DateOnly.FromDateTime(localToday).AddDays(-1);
        string D(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var metric in ledger.Metrics.OrderByDescending(item => item.Primary).Take(8))
        {
            double[] Window(DateOnly from, DateOnly to) => [.. ledger.Observations.Where(item => item.Metric == metric.Key && string.CompareOrdinal(item.Date, D(from)) >= 0 && string.CompareOrdinal(item.Date, D(to)) <= 0).Select(item => item.Value)];
            var recent = Window(end.AddDays(-6), end); var before = Window(end.AddDays(-13), end.AddDays(-7));
            if (recent.Length == 0) continue;
            var now = recent.Average(); var unit = metric.Unit == "%" ? "%" : "";
            var change = before.Length > 0 && before.Average() != 0 ? (now - before.Average()) / Math.Abs(before.Average()) * 100 : (double?)null;
            var good = change is { } c && (c >= 0) == (metric.Good == "up");
            lines.Add($"**{metric.Name}**: {now.ToString("0.##", CultureInfo.InvariantCulture)}{unit} a day" + (change is { } delta ? $" ({delta.ToString("+0.#;-0.#", CultureInfo.InvariantCulture)}% vs. the week before{(Math.Abs(delta) >= 10 ? good ? ", good" : ", worth a look" : "")})" : ""));
        }
        return Bullets(lines, "No scorecard data this week. Connect analytics or import a CSV in Work → Scorecard.");
    }

    static string Result(PostResults? results)
    {
        if (results == null) return "no results yet";
        var parts = new List<string>();
        if (results.Likes is { } likes) parts.Add($"{likes} likes");
        if (results.Reposts is { } reposts) parts.Add($"{reposts} reposts");
        if (results.Replies is { } replies) parts.Add($"{replies} replies");
        if (results.Visits is { } visits) parts.Add($"{visits} visits");
        return parts.Count > 0 ? string.Join(", ", parts) : results.Note ?? "no counts available";
    }
    static int Score(PostResults? results) => results == null ? 0 : (results.Likes ?? 0) + 2 * (results.Reposts ?? 0) + 2 * (results.Replies ?? 0) + (results.Visits ?? 0);

    IEnumerable<string> Learnings(DateTimeOffset since) => shifts.History().Where(shift => shift.EndedAt >= since && shift.ReportWikiId != null)
        .Select(shift => wiki.List().FirstOrDefault(page => page.Id == shift.ReportWikiId)?.Body ?? "")
        .SelectMany(body => Regex.Match(body, @"## Learnings\s*\n(.*?)(\n## |$)", RegexOptions.Singleline) is { Success: true } found
            ? found.Groups[1].Value.Split('\n').Select(line => line.Trim().TrimStart('-', ' ')).Where(line => line.Length > 3 && !line.StartsWith("None", StringComparison.Ordinal)) : [])
        .Distinct().Take(6);

    string? NextFocus() => shifts.History().Where(shift => shift.ReportWikiId != null).OrderByDescending(shift => shift.EndedAt).Select(shift => wiki.List().FirstOrDefault(page => page.Id == shift.ReportWikiId)?.Body ?? "")
        .Select(body => Regex.Match(body, @"## Next shift\s*\n(.*?)(\n## |$)", RegexOptions.Singleline)).FirstOrDefault(found => found.Success)?.Groups[1].Value.Trim();

    async Task<JsonElement?> Work() => (await marketing.ShiftHire(null, "snapshot")).Value;

    async Task<string> Plan(DateTime weekStart, DateTimeOffset startUtc, DateTimeOffset now)
    {
        var content = objectives.Current().Content;
        var work = await Work();
        var tasks = work is { } w && w.TryGetProperty("tasks", out var list) ? list.EnumerateArray().ToArray() : [];
        var drafts = work is { } d && d.TryGetProperty("drafts", out var draftList) ? draftList.EnumerateArray().ToArray() : [];
        var rank = new Dictionary<string, int> { ["high"] = 0, ["normal"] = 1, ["low"] = 2 };
        var queued = tasks.Where(task => Str(task, "status") == "ready").OrderBy(task => rank.GetValueOrDefault(Str(task, "priority"), 1)).Take(6)
            .Select(task => $"{Str(task, "title")}" + (Str(task, "priority") == "high" ? " (high priority)" : ""));
        var waiting = drafts.Where(draft => Str(draft, "status") == "pending").Select(draft => $"Review the {Str(draft, "channel")} draft #{draft.GetProperty("id").GetRawText()}")
            .Concat(drafts.Where(draft => Str(draft, "status") == "approved").Select(draft => $"Publish or schedule the approved {Str(draft, "channel")} draft #{draft.GetProperty("id").GetRawText()}"))
            .Concat(tasks.Where(task => Str(task, "status") == "needs_you").Select(task => Str(task, "title"))).Take(8);
        var scheduled = publishing.Ledger().Publications.Where(item => item.Status == "scheduled" && item.ScheduledFor < now.AddDays(7))
            .OrderBy(item => item.ScheduledFor).Select(item => $"{item.ScheduledFor!.Value.ToLocalTime():ddd MMM d, h:mm tt}: {item.Channel ?? item.Kind}, {Flat(item.Excerpt ?? "", 90)}");
        var today = DateOnly.FromDateTime(now.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var week = DateOnly.FromDateTime(now.UtcDateTime).AddDays(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var experiments = scorecard.Ledger().Experiments.Where(item => item.Status == "running" && string.CompareOrdinal(item.ReviewDate, week) <= 0)
            .Select(item => $"{item.Title}: review {item.ReviewDate}{(string.CompareOrdinal(item.ReviewDate, today) <= 0 ? " (due now)" : "")}");
        var focus = new[] { content.CurrentFocus, NextFocus() ?? "" }.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct().ToArray();
        var text = new StringBuilder($"# Weekly plan: week of {weekStart:MMMM d}\n\n_Written {now.ToLocalTime():ddd MMM d, h:mm tt} from the workspace's records. Change anything; the employee reads it._\n\n");
        text.Append("## North star\n\n").Append(NorthStar()).Append('\n');
        text.Append("## Focus this week\n\n").Append(Bullets(focus, "No focus set. Add one in Objectives → Current focus.")).Append('\n');
        text.Append("## The employee's queue\n\n").Append(Bullets(queued, "Nothing assigned. Add tasks with a first step in Work → Board.")).Append('\n');
        text.Append("## Waiting on you\n\n").Append(Bullets(waiting, "Nothing. Good.")).Append('\n');
        text.Append("## Going out this week\n\n").Append(Bullets(scheduled, "Nothing scheduled yet.")).Append('\n');
        text.Append("## Experiments to decide\n\n").Append(Bullets(experiments, "None due.")).Append('\n');
        text.Append("## Open questions\n\n").Append(Bullets(memory.Notebook().OpenQuestions.Take(5), "None in the notebook."));
        text.Append("\n## Where we are\n\n").Append(Numbers(TimeZoneInfo.ConvertTime(now, Zone(Settings().TimeZone)).DateTime));
        return text.ToString();
    }

    /// <summary>The month just ended against the month before, from the same records as the weekly update.</summary>
    async Task<string> Month(DateTime monthStart, TimeZoneInfo zone, DateTimeOffset now)
    {
        DateTimeOffset Utc(DateTime local) => new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
        var from = Utc(monthStart); var to = Utc(monthStart.AddMonths(1)); var before = Utc(monthStart.AddMonths(-1));
        var name = monthStart.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        var work = await Work();
        var tasks = work is { } w && w.TryGetProperty("tasks", out var list) ? list.EnumerateArray().ToArray() : [];
        var drafts = work is { } d && d.TryGetProperty("drafts", out var draftList) ? draftList.EnumerateArray().ToArray() : [];
        long Since(DateTimeOffset at) => at.ToUnixTimeSeconds();
        var posts = publishing.Ledger().Publications.Where(item => item.Status == "published" && item.PublishedAt >= from && item.PublishedAt < to).ToArray();
        var priorPosts = publishing.Ledger().Publications.Count(item => item.Status == "published" && item.PublishedAt >= before && item.PublishedAt < from);
        var done = tasks.Where(task => Str(task, "status") == "done" && Epoch(task, "updated_at") >= Since(from) && Epoch(task, "updated_at") < Since(to)).Select(task => Str(task, "title")).ToArray();
        var written = wiki.List().Where(page => page.Author.StartsWith("Marketing employee", StringComparison.Ordinal) && page.UpdatedAt >= from && page.UpdatedAt < to
            && !page.Title.StartsWith("Shift report", StringComparison.Ordinal) && !page.Title.StartsWith("Weekly", StringComparison.Ordinal) && !page.Title.StartsWith("Monthly", StringComparison.Ordinal)).Select(page => page.Title).ToArray();
        var decided = drafts.Where(draft => Epoch(draft, "decided_at") >= Since(from) && Epoch(draft, "decided_at") < Since(to)).ToArray();
        var approved = decided.Count(draft => Str(draft, "status") is "approved" or "posted"); var rejected = decided.Count(draft => Str(draft, "status") == "rejected");
        var monthShifts = shifts.History().Where(shift => shift.StartedAt >= from && shift.StartedAt < to).ToArray();
        var experiments = scorecard.Ledger().Experiments;
        var decidedExperiments = experiments.Where(item => item.Status == "decided" && string.CompareOrdinal(item.ReviewDate, monthStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) >= 0
            && string.CompareOrdinal(item.ReviewDate, monthStart.AddMonths(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) < 0).Select(item => $"{item.Title}: {item.Outcome}{(item.OutcomeNote is { Length: > 0 } note ? " — " + Flat(note, 120) : "")}");
        var running = experiments.Where(item => item.Status is "running" or "proposed").Select(item => $"{item.Title} ({(item.Status == "proposed" ? "proposed, not started" : "review " + item.ReviewDate)})");
        var ledger = listening.Ledger();
        var heard = (objectives.Current().Content.WatchTopics ?? []).Select(topic => (topic, count: ledger.Mentions.Count(item => string.Equals(item.Topic, topic, StringComparison.OrdinalIgnoreCase) && item.PublishedAt >= from && item.PublishedAt < to)))
            .Where(item => item.count > 0).Select(item => $"“{item.topic}”: {item.count} mention(s)");
        var byChannel = posts.GroupBy(item => item.Channel ?? item.Kind).OrderByDescending(group => group.Count()).Select(group => $"{group.Key}: {group.Count()}");
        var best = posts.Where(item => Score(item.Results) > 0).OrderByDescending(item => Score(item.Results)).Take(3)
            .Select(item => $"{item.PublishedAt!.Value.ToLocalTime():MMM d}, {item.Channel ?? item.Kind}: {Flat(item.Excerpt ?? "", 90)} — {Result(item.Results)}" + (item.Url != null ? $" ([link]({item.Url}))" : ""));
        var notebook = memory.Notebook();
        var content = objectives.Current().Content;
        var text = new StringBuilder($"# Monthly report: {name}\n\n_Written {now.ToLocalTime():ddd MMM d, h:mm tt} from the workspace's records: {name} against the month before._\n\n");
        text.Append("## Headline\n\n").Append($"{posts.Length} post(s) went out ({(priorPosts == 0 ? "none" : priorPosts.ToString(CultureInfo.InvariantCulture))} the month before), {done.Length} task(s) finished, {written.Length} document(s) written, {monthShifts.Length} shift(s) worked. You approved {approved} draft(s) and rejected {rejected}.\n\n");
        text.Append("## North star\n\n").Append(NorthStar()).Append('\n');
        text.Append("## Numbers (daily average, this month vs. the month before)\n\n").Append(MonthNumbers(monthStart)).Append('\n');
        text.Append("## What went out\n\n").Append(Bullets(byChannel, "Nothing was published this month.")).Append('\n');
        text.Append("## Best posts\n\n").Append(Bullets(best, "No post results yet.")).Append('\n');
        text.Append("## Where visits came from (last four weeks)\n\n").Append(Bullets(DataConnections.TrafficLines(data.Traffic(), 8), "Connect Google Analytics in Work → Scorecard to see channels and landing pages.")).Append('\n');
        text.Append("## Experiments\n\n").Append(Bullets(decidedExperiments.Select(item => "Decided: " + item).Concat(running.Select(item => "Open: " + item)), "None this month. The employee can propose one on a scorecard metric.")).Append('\n');
        text.Append("## Work done\n\n").Append(Bullets(done.Take(10).Concat(written.Take(10).Select(title => "Wrote: " + title)), "Nothing finished this month.")).Append('\n');
        text.Append("## What people said\n\n").Append(Bullets(heard, "Nothing on the watch topics this month.")).Append('\n');
        text.Append("## What we learned\n\n").Append(Bullets(Learnings(from).Concat(notebook.Worked.TakeLast(3).Select(item => "Worked: " + item)).Concat(notebook.DidNotWork.TakeLast(3).Select(item => "Didn't: " + item)), "Nothing recorded this month.")).Append('\n');
        text.Append("## Next month\n\n").Append(Bullets(new[] { content.CurrentFocus }.Where(item => !string.IsNullOrWhiteSpace(item)).Concat(content.Objectives.Select(item => "Objective: " + item.Title)).Concat(notebook.OpenQuestions.Take(3).Select(item => "Open question: " + item)), "Set objectives and a focus in the brief.")).Append('\n');
        text.Append($"_Model spend this month: {monthShifts.Sum(shift => shift.TokensUsed):N0} tokens over {monthShifts.Sum(shift => shift.TurnsUsed)} turns._\n");
        return text.ToString();
    }

    /// <summary>Each metric's daily average over a calendar month against the month before.</summary>
    string MonthNumbers(DateTime monthStart)
    {
        var ledger = scorecard.Ledger();
        string D(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var lines = new List<string>();
        foreach (var metric in ledger.Metrics.OrderByDescending(item => item.Primary).Take(8))
        {
            double[] Window(DateTime from, DateTime to) => [.. ledger.Observations.Where(item => item.Metric == metric.Key && string.CompareOrdinal(item.Date, D(from)) >= 0 && string.CompareOrdinal(item.Date, D(to)) < 0).Select(item => item.Value)];
            var now = Window(monthStart, monthStart.AddMonths(1)); var prior = Window(monthStart.AddMonths(-1), monthStart);
            if (now.Length == 0) continue;
            var average = now.Average(); var unit = metric.Unit == "%" ? "%" : "";
            var change = prior.Length > 0 && prior.Average() != 0 ? (average - prior.Average()) / Math.Abs(prior.Average()) * 100 : (double?)null;
            var good = change is { } c && (c >= 0) == (metric.Good == "up");
            lines.Add($"**{metric.Name}**: {average.ToString("0.##", CultureInfo.InvariantCulture)}{unit} a day over {now.Length} day(s)" + (change is { } delta ? $" ({delta.ToString("+0.#;-0.#", CultureInfo.InvariantCulture)}% vs. the month before{(Math.Abs(delta) >= 10 ? good ? ", good" : ", worth a look" : "")})" : ", no earlier month to compare"));
        }
        return Bullets(lines, "No scorecard data this month. Connect analytics or import a CSV in Work → Scorecard.");
    }

    async Task<string> Update(DateTime weekStart, DateTimeOffset startUtc, DateTimeOffset now)
    {
        var work = await Work();
        var tasks = work is { } w && w.TryGetProperty("tasks", out var list) ? list.EnumerateArray().ToArray() : [];
        var drafts = work is { } d && d.TryGetProperty("drafts", out var draftList) ? draftList.EnumerateArray().ToArray() : [];
        var since = startUtc.ToUnixTimeSeconds();
        var posts = publishing.Ledger().Publications.Where(item => item.Status == "published" && item.PublishedAt >= startUtc).OrderBy(item => item.PublishedAt).ToArray();
        var top = posts.Where(item => Score(item.Results) > 0).OrderByDescending(item => Score(item.Results)).FirstOrDefault();
        var done = tasks.Where(task => Str(task, "status") == "done" && Epoch(task, "updated_at") >= since).Select(task => Str(task, "title")).Take(10);
        var written = wiki.List().Where(page => page.Author.StartsWith("Marketing employee", StringComparison.Ordinal) && page.UpdatedAt >= startUtc && !page.Title.StartsWith("Shift report", StringComparison.Ordinal) && !page.Title.StartsWith("Weekly", StringComparison.Ordinal))
            .Select(page => page.Title).Take(8);
        var decided = drafts.Where(draft => Str(draft, "status") is "approved" or "rejected" or "posted" && Epoch(draft, "decided_at") >= since)
            .Select(draft => $"{Str(draft, "channel")} draft #{draft.GetProperty("id").GetRawText()}: {(Str(draft, "status") == "rejected" ? "rejected" : "approved")}");
        var waiting = drafts.Where(draft => Str(draft, "status") == "pending").Select(draft => $"{Str(draft, "channel")} draft #{draft.GetProperty("id").GetRawText()} to review")
            .Concat(tasks.Where(task => Str(task, "status") == "needs_you").Select(task => Str(task, "title"))).Take(8);
        var weekShifts = shifts.History().Where(shift => shift.StartedAt >= startUtc).ToArray();
        var ledger = listening.Ledger();
        var heard = (objectives.Current().Content.WatchTopics ?? []).Select(topic => (topic, count: ledger.Mentions.Count(item => string.Equals(item.Topic, topic, StringComparison.OrdinalIgnoreCase) && item.PublishedAt >= startUtc),
            negative: ledger.Mentions.Count(item => string.Equals(item.Topic, topic, StringComparison.OrdinalIgnoreCase) && item.PublishedAt >= startUtc && item.Sentiment == "negative")))
            .Where(item => item.count > 0).Select(item => $"“{item.topic}”: {item.count} mention(s){(item.negative > 0 ? $", {item.negative} negative" : "")}");
        var notebook = memory.Notebook();
        var text = new StringBuilder($"# Weekly update: week of {weekStart:MMMM d}\n\n_Written {now.ToLocalTime():ddd MMM d, h:mm tt} from the workspace's records._\n\n");
        text.Append("## Headline\n\n").Append($"{posts.Length} post(s) went out, {done.Count()} task(s) finished, {weekShifts.Length} shift(s) worked, {waiting.Count()} item(s) waiting on you.").Append(top != null ? $" Best post: {top.Channel ?? top.Kind} on {top.PublishedAt:ddd} ({Result(top.Results)})." : "").Append("\n\n");
        text.Append("## North star\n\n").Append(NorthStar()).Append('\n');
        text.Append("## Numbers (last 7 days vs. the 7 before)\n\n").Append(Numbers(TimeZoneInfo.ConvertTime(now, Zone(Settings().TimeZone)).DateTime)).Append('\n');
        text.Append("## What went out\n\n").Append(Bullets(posts.Select(item => $"{item.PublishedAt!.Value.ToLocalTime():ddd h:mm tt}, {item.Channel ?? item.Kind}: {Flat(item.Excerpt ?? "", 100)} — {Result(item.Results)}" + (item.Url != null ? $" ([link]({item.Url}))" : "")), "Nothing was published this week.")).Append('\n');
        text.Append("## Where visits came from (last four weeks)\n\n").Append(Bullets(DataConnections.TrafficLines(data.Traffic()), "Connect Google Analytics in Work → Scorecard to see channels and landing pages.")).Append('\n');
        text.Append("## Work done\n\n").Append(Bullets(done.Concat(written.Select(title => "Wrote: " + title)), "Nothing finished this week.")).Append('\n');
        text.Append("## Your decisions\n\n").Append(Bullets(decided, "None this week.")).Append('\n');
        text.Append("## Waiting on you\n\n").Append(Bullets(waiting, "Nothing.")).Append('\n');
        text.Append("## What people said\n\n").Append(Bullets(heard, "Nothing on the watch topics this week.")).Append('\n');
        text.Append("## What we learned\n\n").Append(Bullets(Learnings(startUtc).Concat(notebook.Worked.TakeLast(2).Select(item => "Worked: " + item)).Concat(notebook.DidNotWork.TakeLast(2).Select(item => "Didn't: " + item)), "Nothing recorded this week.")).Append('\n');
        text.Append("## Next week\n\n").Append(NextFocus() is { Length: > 0 } next ? next + "\n" : "_No focus recorded; the Monday plan picks up from the queue._\n");
        text.Append($"\n_Model spend this week: {weekShifts.Sum(shift => shift.TokensUsed):N0} tokens over {weekShifts.Sum(shift => shift.TurnsUsed)} turns._\n");
        return text.ToString();
    }
}
