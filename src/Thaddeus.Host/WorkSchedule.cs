using System.Globalization;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record ShiftSchedule(bool Enabled, int[] Days, string Start, string End, string TimeZone, int CycleMinutes, int TurnBudget, int? TokenBudget,
    string? LastStartedFor, string UpdatedBy, DateTimeOffset UpdatedAt);
public record ShiftScheduleChange(bool Enabled, int[] Days, string Start, string End, string TimeZone, int? CycleMinutes, int? TurnBudget, int? TokenBudget);

/// <summary>The employee's working hours: on the chosen days it starts a shift at the start time, in the owner's time zone,
/// and the shift ends at the end time. Once per day: a shift the owner stops is not restarted until the next working day.</summary>
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
        lock (store)
        {
            var previous = Current();
            var next = new ShiftSchedule(change.Enabled, days, change.Start, change.End, change.TimeZone, cycle, turns, change.TokenBudget, previous?.LastStartedFor, author, Clock());
            store.Setting(Key, Wire.Pack(next));
            return next;
        }
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
        lock (store) store.Setting(Key, Wire.Pack(schedule with { LastStartedFor = dayKey }));
        var minutes = (int)Math.Floor((end - local.TimeOfDay).TotalMinutes);
        try
        {
            var shift = shifts.Start(new ShiftStartRequest("schedule-" + dayKey, Math.Max(1, (int)Math.Ceiling(minutes / 60.0)), schedule.CycleMinutes, schedule.TurnBudget, minutes, schedule.TokenBudget),
                $"Work schedule (set by {schedule.UpdatedBy})");
            logger.LogInformation("Scheduled shift {Shift} started for {Minutes} minutes", shift.Id, minutes);
            await Task.CompletedTask;
            return shift;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException) { logger.LogWarning("The scheduled shift did not start: {Error}", error.Message); return null; }
    }

    public object View() { var schedule = Current(); return new { schedule, nextStart = NextStart(schedule, Clock()) }; }
}
