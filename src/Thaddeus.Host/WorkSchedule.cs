using System.Globalization;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record ShiftSchedule(bool Enabled, int[] Days, string Start, string End, string TimeZone, int CycleMinutes, int TurnBudget, int? TokenBudget,
    string? LastStartedFor, string UpdatedBy, DateTimeOffset UpdatedAt, int? MonthlyTokens = null);
public record PutToWorkRequest(string TimeZone);
public record ShiftScheduleChange(bool Enabled, int[] Days, string Start, string End, string TimeZone, int? CycleMinutes, int? TurnBudget, int? TokenBudget, int? MonthlyTokens = null);

/// <summary>The employee's working hours: on the chosen days it starts a shift at the start time, in the owner's time zone,
/// and the shift ends at the end time. Once per day: a shift the owner stops is not restarted until the next working day.
/// A monthly token limit, when set, caps what scheduled shifts spend in a calendar month (in the owner's time zone): a day's shift
/// gets at most what's left, and none starts once it's spent.</summary>
public sealed class WorkSchedule(Store store, EmployeeShifts shifts, ILogger<WorkSchedule> logger)
{
    private const string Key = "shift-schedule-v1";
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    public ShiftSchedule? Current() { lock (store) return store.Setting(Key) is { } json ? Wire.Unpack<ShiftSchedule>(json) : null; }

    static TimeSpan Time(string value, string field) =>
        TimeSpan.TryParseExact(value ?? "", @"hh\:mm", CultureInfo.InvariantCulture, out var time) && time < TimeSpan.FromDays(1) ? time : throw new ArgumentException($"{field} must be a time like 08:00.");
    static TimeZoneInfo Zone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { throw new ArgumentException("That time zone isn't known."); }
    }

    public ShiftSchedule Save(ShiftScheduleChange change, string author)
    {
        var days = (change.Days ?? []).Distinct().Order().ToArray();
        if (days.Any(day => day is < 0 or > 6)) throw new ArgumentException("Days run from 0 (Sunday) to 6 (Saturday).");
        if (change.Enabled && days.Length == 0) throw new ArgumentException("Choose at least one working day.");
        var start = Time(change.Start, "The start"); var end = Time(change.End, "The end");
        if (end - start < TimeSpan.FromMinutes(30)) throw new ArgumentException("The working day must be at least 30 minutes, ending after it starts.");
        Zone(change.TimeZone);
        var cycle = change.CycleMinutes ?? 60;
        if (cycle is < 5 or > 240) throw new ArgumentException("Check in every 5 to 240 minutes.");
        var turns = change.TurnBudget ?? 200;
        if (turns is < 1 or > 2000) throw new ArgumentException("Set a model-turn budget of 1 to 2,000 per day.");
        if (change.TokenBudget is < 8000 or > 20_000_000) throw new ArgumentException("Set a daily token limit of 8,000 to 20,000,000, or leave it empty.");
        if (change.MonthlyTokens is < 8000 or > 500_000_000) throw new ArgumentException("Set a monthly token limit of 8,000 to 500,000,000, or leave it empty.");
        if (change.MonthlyTokens is { } month && change.TokenBudget is { } day && day > month) throw new ArgumentException("The daily token limit can't be more than the monthly one.");
        lock (store)
        {
            var previous = Current();
            var next = new ShiftSchedule(change.Enabled, days, change.Start, change.End, change.TimeZone, cycle, turns, change.TokenBudget, previous?.LastStartedFor, author, Clock(), change.MonthlyTokens);
            store.Setting(Key, Wire.Pack(next));
            return next;
        }
    }

    /// <summary>Daily and monthly token limits "Put it to work" sets on the live model: a working day of hourly cycles uses well
    /// under the daily one, and the monthly one stops a runaway month. The owner can change both under Working hours.</summary>
    public const int DefaultDailyTokens = 3_000_000, DefaultMonthlyTokens = 60_000_000;

    /// <summary>One click to put the employee to work: the working hours already set, turned on, or weekdays 9 to 5 with an hourly
    /// check-in; limits on the live model. The weekly rhythm (plan, update, morning brief) is turned on beside it by the caller.</summary>
    public ShiftSchedule PutToWork(string timeZone, bool live, string author)
    {
        var current = Current();
        var daily = current?.TokenBudget ?? (live ? DefaultDailyTokens : null);
        var monthly = current?.MonthlyTokens ?? (live ? DefaultMonthlyTokens : null);
        return current is { Days.Length: > 0 }
            ? Save(new(true, current.Days, current.Start, current.End, current.TimeZone, current.CycleMinutes, current.TurnBudget, daily, monthly), author)
            : Save(new(true, [1, 2, 3, 4, 5], "09:00", "17:00", timeZone, 60, 200, daily, monthly), author);
    }

    /// <summary>When the next scheduled shift starts, if the schedule is on.</summary>
    public static DateTimeOffset? NextStart(ShiftSchedule? schedule, DateTimeOffset now)
    {
        if (schedule is not { Enabled: true }) return null;
        var zone = Zone(schedule.TimeZone);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var start = Time(schedule.Start, "start"); var end = Time(schedule.End, "end");
        for (var offset = 0; offset < 8; offset++)
        {
            var day = local.Date.AddDays(offset);
            if (!schedule.Days.Contains((int)day.DayOfWeek)) continue;
            var dayKey = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (schedule.LastStartedFor == dayKey) continue;
            if (offset == 0 && local.TimeOfDay >= end - TimeSpan.FromMinutes(15)) continue;
            var begin = offset == 0 && local.TimeOfDay > start ? local.DateTime : day + start;
            return new DateTimeOffset(begin, zone.GetUtcOffset(begin));
        }
        return null;
    }

    /// <summary>Start today's shift when its time has come. Called by the pump.</summary>
    public async Task<EmployeeShift?> Tick(CancellationToken cancellation)
    {
        var schedule = Current();
        if (schedule is not { Enabled: true } || shifts.OnShift) return null;
        var now = Clock();
        var zone = Zone(schedule.TimeZone);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var dayKey = local.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var start = Time(schedule.Start, "start"); var end = Time(schedule.End, "end");
        if (!schedule.Days.Contains((int)local.DayOfWeek) || schedule.LastStartedFor == dayKey || local.TimeOfDay < start || local.TimeOfDay >= end - TimeSpan.FromMinutes(15)) return null;
        var budget = schedule.TokenBudget;
        if (schedule.MonthlyTokens is { } month)
        {
            var left = month - MonthUsed(schedule, now);
            if (left < 8000) { logger.LogInformation("This month's token limit is spent; no scheduled shift today"); return null; }
            budget = Math.Min(budget ?? left, left);
        }
        // A run for what was asked finishes first; the day's shift starts at the next tick after it.
        if (shifts.RequestsRunning) return null;
        lock (store) store.Setting(Key, Wire.Pack(schedule with { LastStartedFor = dayKey }));
        var minutes = (int)Math.Floor((end - local.TimeOfDay).TotalMinutes);
        try
        {
            var shift = shifts.Start(new ShiftStartRequest("schedule-" + dayKey, Math.Max(1, (int)Math.Ceiling(minutes / 60.0)), schedule.CycleMinutes, schedule.TurnBudget, minutes, budget),
                $"Work schedule (set by {schedule.UpdatedBy})");
            logger.LogInformation("Scheduled shift {Shift} started for {Minutes} minutes", shift.Id, minutes);
            await Task.CompletedTask;
            return shift;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException) { logger.LogWarning("The scheduled shift did not start: {Error}", error.Message); return null; }
    }

    /// <summary>Tokens every shift started this calendar month (in the schedule's time zone, or UTC) has used, scheduled or not.</summary>
    public int MonthUsed(ShiftSchedule? schedule, DateTimeOffset now)
    {
        var zone = schedule is null ? TimeZoneInfo.Utc : Zone(schedule.TimeZone);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        return shifts.History().Where(shift => TimeZoneInfo.ConvertTime(shift.StartedAt, zone) is var at && at.Year == local.Year && at.Month == local.Month).Sum(shift => shift.TokensUsed);
    }

    /// <summary>A month is spent when too little is left for a scheduled shift to start.</summary>
    public bool MonthSpent() => Current() is { MonthlyTokens: { } month } schedule && month - MonthUsed(schedule, Clock()) < 8000;

    public object View()
    {
        var schedule = Current(); var now = Clock();
        return new { schedule, nextStart = MonthSpent() ? null : NextStart(schedule, now), monthUsed = MonthUsed(schedule, now), monthSpent = MonthSpent() };
    }
}
