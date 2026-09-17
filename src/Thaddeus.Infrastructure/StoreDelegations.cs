using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed record DelegationSchedule(string Kind, DateTimeOffset? AtUtc, string TimeZone, string? LocalTime = null,
    int? IntervalMinutes = null)
{
    public DateTimeOffset FirstDue(DateTimeOffset requestedAt) => Kind switch
    {
        "once" when AtUtc is { } due => due.ToUniversalTime(),
        "weekdays" => NextWeekday(requestedAt),
        "interval" when IntervalMinutes is { } minutes => requestedAt.ToUniversalTime().AddMinutes(minutes),
        _ => throw new ArgumentException("Choose a one-time, weekday, or fixed-interval schedule.")
    };

    public DateTimeOffset? NextAfter(DateTimeOffset due) => Kind switch
    {
        "weekdays" => NextWeekday(due),
        "interval" when IntervalMinutes is { } minutes => due.ToUniversalTime().AddMinutes(minutes),
        _ => null
    };

    public void Validate()
    {
        if (Kind == "once")
        {
            if (AtUtc == null || LocalTime != null || IntervalMinutes != null) throw new ArgumentException("A one-time schedule needs one absolute due time.");
            _ = Zone();
            return;
        }
        if (Kind == "interval")
        {
            if (AtUtc != null || LocalTime != null || IntervalMinutes is < 1 or > 1440)
                throw new ArgumentException("An interval schedule needs 1 through 1,440 minutes.");
            _ = Zone();
            return;
        }
        if (Kind != "weekdays" || AtUtc != null || !TimeOnly.TryParseExact(LocalTime, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
            throw new ArgumentException("A weekday schedule needs a local time in HH:mm form.");
        _ = Zone();
    }

    private DateTimeOffset NextWeekday(DateTimeOffset after)
    {
        Validate();
        var zone = Zone();
        var localAfter = TimeZoneInfo.ConvertTime(after, zone);
        var time = TimeOnly.ParseExact(LocalTime!, "HH:mm", CultureInfo.InvariantCulture);
        for (var offset = 0; offset < 9; offset++)
        {
            var date = DateOnly.FromDateTime(localAfter.Date).AddDays(offset);
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
            while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
            var zoneOffset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
            var candidate = new DateTimeOffset(local, zoneOffset).ToUniversalTime();
            if (candidate > after.ToUniversalTime()) return candidate;
        }
        throw new InvalidOperationException("A weekday occurrence could not be resolved in the selected timezone.");
    }

    private TimeZoneInfo Zone()
    {
        if (string.IsNullOrWhiteSpace(TimeZone) || TimeZone.Length > 100) throw new ArgumentException("Choose a valid timezone.");
        try { return TimeZoneInfo.FindSystemTimeZoneById(TimeZone); }
        catch (TimeZoneNotFoundException) { throw new ArgumentException("Choose a timezone installed on this host."); }
        catch (InvalidTimeZoneException) { throw new ArgumentException("The selected timezone is invalid on this host."); }
    }
}

public sealed record DelegatedAction(string Kind, string Target, JsonElement Payload, bool RequiresModel = false);
public sealed record DelegationGrant(string Id, int Version, string JobId, string OwnerId, string AccountId, string Operation,
    string Target, string PayloadHash, string ScheduleVersion, int MaxOccurrences, int UsedOccurrences, DateTimeOffset Expires,
    int MaxExternalCalls, int MaxModelCalls, bool Revoked, DateTimeOffset Created, DateTimeOffset Updated);
public sealed record DelegationJob(string Id, int Version, string OwnerId, string Kind, string Title, DelegationSchedule Schedule,
    string ScheduleVersion, DelegatedAction Action, string GrantId, string State, DateTimeOffset RequestedAt,
    DateTimeOffset Created, DateTimeOffset Updated, DateTimeOffset? NextRunUtc, int NextSequence, TimeSpan MaxLateness,
    bool CancellationRequested = false, string? LastSummary = null, string? SourceRunId = null);
public sealed record DelegationOccurrence(string Id, int Version, string JobId, string ScheduleVersion, int Sequence,
    DateTimeOffset DueUtc, string State, string OperationId, string DispatchState, string? ClaimToken,
    DateTimeOffset? ClaimedAt, DateTimeOffset? CompletedAt, string? Summary = null, string? ProviderId = null,
    JsonElement? ProviderEvidence = null, bool ActionSucceeded = false, string NotificationStatus = "not-attempted",
    string? NotificationError = null, DateTimeOffset? ReadAt = null);
public sealed record DelegationClaim(DelegationJob Job, DelegationGrant Grant, DelegationOccurrence Occurrence, string Token);
public sealed record DelegationCompletion(string State, string DispatchState, string Summary, bool ActionSucceeded,
    string? ProviderId = null, JsonElement? ProviderEvidence = null, string NotificationStatus = "not-attempted",
    string? NotificationError = null, bool Quiet = false, bool PauseSchedule = false, InboxWatchState? InboxState = null);

public sealed record InboxWatchState(string JobId, int Version, DateTimeOffset ActivatedAtUtc,
    DateTimeOffset? LastSuccessfulCheckUtc, string? LastError, string[] ProcessedMessageIds,
    string[] AlertedMessageIds, DateTimeOffset Updated);

public sealed partial class Store
{
    public DelegationJob[] DelegationJobs()
    {
        lock (gate) return Query("SELECT body FROM delegation_jobs ORDER BY rowid DESC").Select(Wire.Unpack<DelegationJob>).ToArray();
    }

    public DelegationOccurrence[] DelegationOccurrences(string? jobId = null)
    {
        lock (gate) return Query("SELECT body FROM delegation_occurrences WHERE $job IS NULL OR jobId=$job ORDER BY due,rowid",
            ("$job", (object?)jobId ?? DBNull.Value)).Select(Wire.Unpack<DelegationOccurrence>).ToArray();
    }

    public DelegationGrant? DelegationGrant(string id)
    {
        lock (gate) return Query("SELECT body FROM delegation_grants WHERE id=$id", ("$id", id)).Select(Wire.Unpack<DelegationGrant>).SingleOrDefault();
    }

    public InboxWatchState? InboxWatchState(string jobId)
    {
        lock (gate) return Query("SELECT body FROM inbox_watch_states WHERE jobId=$id", ("$id", jobId))
            .Select(Wire.Unpack<InboxWatchState>).SingleOrDefault();
    }

    public (DelegationJob Job, DelegationGrant Grant) CreateDelegation(DelegationJob job, DelegationGrant grant)
    {
        lock (gate)
        {
            ValidateId(job.Id, "job"); ValidateId(grant.Id, "grant");
            job.Schedule.Validate();
            if (job.Version != 0 || grant.Version != 0 || job.OwnerId != "local-owner" || grant.OwnerId != job.OwnerId ||
                grant.JobId != job.Id || job.GrantId != grant.Id || job.ScheduleVersion != grant.ScheduleVersion ||
                job.State != "scheduled" || job.NextSequence != 1 || job.CancellationRequested)
                throw new ArgumentException("The delegation job and grant are not a fresh, matching authorization.");
            if (job.Kind is not ("reminder" or "email" or "brief" or "inbox-watch") || job.Action.Kind != job.Kind ||
                string.IsNullOrWhiteSpace(job.Title) || job.Title.Length > 200 || job.Action.Target.Length is < 1 or > 500 ||
                job.Action.Payload.GetRawText().Length > 40_000)
                throw new ArgumentException("The delegated action is invalid or exceeds its review limits.");
            if (job.MaxLateness <= TimeSpan.Zero || job.MaxLateness > TimeSpan.FromDays(7) ||
                grant.MaxOccurrences is < 1 or > 10_000 || grant.UsedOccurrences != 0 || grant.MaxExternalCalls is < 0 or > 20_000 ||
                grant.MaxModelCalls is < 0 or > 10_000 || grant.Revoked || grant.Expires <= job.RequestedAt ||
                grant.Operation != job.Kind || grant.Target != job.Action.Target || grant.PayloadHash != ActionHash(job.Action))
                throw new ArgumentException("The delegation grant does not match the bounded action.");
            var due = job.Schedule.FirstDue(job.RequestedAt);
            if (job.NextRunUtc != due || job.Created != grant.Created || job.Created != job.Updated || grant.Created != grant.Updated)
                throw new ArgumentException("The persisted schedule must be resolved from the original request time.");
            if (Query("SELECT CAST(COUNT(*) AS TEXT) FROM delegation_jobs").Single() is var count && int.Parse(count) >= 1000)
                throw new InvalidOperationException("This study has reached 1,000 delegated jobs. Export or remove old work before adding more.");
            using var transaction = db.BeginTransaction();
            Exec("INSERT INTO delegation_jobs VALUES($id,$version,$next,$body)", ("$id", job.Id), ("$version", job.Version),
                ("$next", due.ToString("O")), ("$body", Wire.Pack(job)));
            Exec("INSERT INTO delegation_grants VALUES($id,$version,$body)", ("$id", grant.Id), ("$version", grant.Version), ("$body", Wire.Pack(grant)));
            if (job.Kind == "inbox-watch")
            {
                var state = new InboxWatchState(job.Id, 0, job.Created.ToUniversalTime(), null, null, [], [], job.Created);
                Exec("INSERT INTO inbox_watch_states VALUES($id,$version,$body)", ("$id", job.Id), ("$version", state.Version), ("$body", Wire.Pack(state)));
            }
            transaction.Commit();
            return (job, grant);
        }
    }

    public DelegationClaim? ClaimDueDelegation(DateTimeOffset now)
    {
        lock (gate)
        {
            while (true)
            {
                var job = Query("SELECT body FROM delegation_jobs WHERE nextRun IS NOT NULL AND nextRun<=$now ORDER BY nextRun LIMIT 1",
                    ("$now", now.ToUniversalTime().ToString("O"))).Select(Wire.Unpack<DelegationJob>).SingleOrDefault();
                if (job == null) return null;
                var due = job.NextRunUtc!.Value.ToUniversalTime();
                if (job.State != "scheduled") { SaveDelegationJob(job with { NextRunUtc = null, Version = job.Version + 1, Updated = now }); continue; }
                var occurrenceId = OccurrenceId(job, due);
                if (Query("SELECT body FROM delegation_occurrences WHERE id=$id", ("$id", occurrenceId)).Count != 0)
                {
                    SaveDelegationJob(AdvanceWithoutDispatch(job, due, now, "Duplicate occurrence suppressed."));
                    continue;
                }
                if (now - due > job.MaxLateness)
                {
                    var missed = NewOccurrence(job, due, "missed", "not-dispatched", null, now,
                        "This occurrence was missed while the host was unavailable; it was not replayed.");
                    using var missedTransaction = db.BeginTransaction();
                    SaveDelegationOccurrence(missed);
                    SaveDelegationJob(AdvanceWithoutDispatch(job, due, now, missed.Summary!));
                    missedTransaction.Commit();
                    RecordOccurrence(job, missed, now);
                    continue;
                }
                var grant = DelegationGrant(job.GrantId) ?? throw new InvalidOperationException("The delegation grant is missing.");
                var reason = grant.UsedOccurrences >= grant.MaxOccurrences
                    ? "The delegation occurrence allowance is exhausted."
                    : GrantFailure(job, grant, now);
                if (reason != null)
                {
                    var blocked = NewOccurrence(job, due, "needs-approval", "not-dispatched", null, now, reason);
                    using var blockedTransaction = db.BeginTransaction();
                    SaveDelegationOccurrence(blocked);
                    SaveDelegationJob(job with { State = "needs-approval", NextRunUtc = null, Version = job.Version + 1, Updated = now, LastSummary = reason });
                    blockedTransaction.Commit();
                    RecordOccurrence(job, blocked, now);
                    continue;
                }
                var token = Guid.NewGuid().ToString("N");
                var occurrence = NewOccurrence(job, due, "working", "intent-recorded", token, now, "Dispatch intent recorded before the external effect.");
                var next = job.Schedule.NextAfter(due);
                var updatedJob = job with
                {
                    State = next == null ? "working" : "scheduled", NextRunUtc = next, NextSequence = job.NextSequence + 1,
                    Version = job.Version + 1, Updated = now, LastSummary = occurrence.Summary
                };
                var updatedGrant = grant with { UsedOccurrences = grant.UsedOccurrences + 1, Version = grant.Version + 1, Updated = now };
                using var transaction = db.BeginTransaction();
                SaveDelegationOccurrence(occurrence);
                SaveDelegationJob(updatedJob);
                SaveDelegationGrant(updatedGrant);
                transaction.Commit();
                testFault?.Invoke("after-delegation-claim");
                return new(updatedJob, updatedGrant, occurrence, token);
            }
        }
    }

    public bool AuthorizeDelegationClaim(string occurrenceId, string token, DateTimeOffset now)
    {
        lock (gate)
        {
            var occurrence = Occurrence(occurrenceId);
            if (occurrence is not { State: "working" } || occurrence.ClaimToken != token) return false;
            var job = DelegationJobs().Single(item => item.Id == occurrence.JobId);
            var grant = DelegationGrant(job.GrantId);
            return grant != null && !job.CancellationRequested && GrantFailure(job, grant, now) == null &&
                grant.UsedOccurrences <= grant.MaxOccurrences;
        }
    }

    public DelegationOccurrence CompleteDelegation(string occurrenceId, string token, DelegationCompletion completion, DateTimeOffset now)
    {
        lock (gate)
        {
            if (completion.State is not ("succeeded" or "failed" or "unknown" or "cancelled") ||
                completion.DispatchState is not ("not-dispatched" or "accepted" or "failed" or "unknown") ||
                completion.Summary.Length is < 1 or > 2000 || completion.ProviderId?.Length > 500 ||
                completion.ProviderEvidence?.GetRawText().Length > 40_000 || completion.NotificationError?.Length > 2000)
                throw new ArgumentException("The delegation completion receipt is invalid.");
            var occurrence = Occurrence(occurrenceId) ?? throw new ArgumentException("Delegation occurrence not found.");
            if (occurrence.State != "working" || occurrence.ClaimToken != token) throw new InvalidOperationException("The dispatch claim changed or was already settled.");
            var job = DelegationJobs().Single(item => item.Id == occurrence.JobId);
            var settled = occurrence with
            {
                Version = occurrence.Version + 1, State = completion.State, DispatchState = completion.DispatchState,
                ClaimToken = null, CompletedAt = now, Summary = completion.Summary, ProviderId = completion.ProviderId,
                ProviderEvidence = completion.ProviderEvidence, ActionSucceeded = completion.ActionSucceeded,
                NotificationStatus = completion.NotificationStatus, NotificationError = completion.NotificationError,
                ReadAt = completion.Quiet ? now : occurrence.ReadAt
            };
            var jobState = job.Schedule.Kind == "once" ? completion.State : completion.PauseSchedule ? "paused" : job.State;
            var grant = DelegationGrant(job.GrantId)!;
            if (job.Schedule.Kind != "once" && grant.UsedOccurrences >= grant.MaxOccurrences) jobState = "completed";
            var updatedJob = job with { State = jobState, Version = job.Version + 1, Updated = now, LastSummary = completion.Summary,
                NextRunUtc = jobState is "completed" or "paused" ? null : job.NextRunUtc };
            using var transaction = db.BeginTransaction();
            SaveDelegationOccurrence(settled); SaveDelegationJob(updatedJob);
            if (completion.InboxState != null) SaveInboxWatchState(job, completion.InboxState, now);
            transaction.Commit();
            RecordOccurrence(job, settled, now);
            return settled;
        }
    }

    public DelegationJob CancelDelegation(string id, int version, DateTimeOffset now)
    {
        lock (gate)
        {
            var job = DelegationJobs().SingleOrDefault(item => item.Id == id) ?? throw new ArgumentException("Delegated job not found.");
            if (job.Version != version) throw new InvalidOperationException("This delegated job changed. Review it before cancelling.");
            if (job.State is "cancelled" or "completed" or "succeeded" or "failed" or "unknown" or "missed") return job;
            var working = DelegationOccurrences(id).Any(item => item.State == "working");
            var updated = job with { State = working ? job.State : "cancelled", CancellationRequested = true,
                NextRunUtc = null, Version = job.Version + 1, Updated = now,
                LastSummary = working ? "Cancellation requested after dispatch intent; the final receipt will report what happened." : "Cancelled before dispatch." };
            var currentGrant = DelegationGrant(job.GrantId) ?? throw new InvalidOperationException("The delegation grant is missing.");
            var grant = currentGrant with { Revoked = true, Version = currentGrant.Version + 1, Updated = now };
            using var transaction = db.BeginTransaction(); SaveDelegationJob(updated); SaveDelegationGrant(grant); transaction.Commit();
            if (job.SourceRunId != null) AppendRunEvent(job.SourceRunId, "delegation.job.cancelled", new { job = updated, grantRevoked = true }, now);
            return updated;
        }
    }

    public DelegationJob RescheduleReminder(string id, int version, DateTimeOffset due, string timeZone, DateTimeOffset now)
    {
        lock (gate)
        {
            var job = DelegationJobs().SingleOrDefault(item => item.Id == id) ?? throw new ArgumentException("Delegated job not found.");
            if (job.Version != version) throw new InvalidOperationException("This delegated job changed. Review it before rescheduling.");
            if (job.Kind != "reminder" || job.Schedule.Kind != "once" || job.State != "scheduled" || job.CancellationRequested ||
                DelegationOccurrences(id).Any(item => item.State == "working"))
                throw new InvalidOperationException("Only a still-scheduled reminder can be rescheduled.");
            due = due.ToUniversalTime();
            if (due <= now.ToUniversalTime()) throw new ArgumentException("Choose a future reminder time.");
            var schedule = new DelegationSchedule("once", due, timeZone); schedule.Validate();
            var scheduleVersion = Wire.Hash(Wire.Pack(new { schedule, job.Action, previous = job.ScheduleVersion, revision = job.Version + 1 }));
            var currentGrant = DelegationGrant(job.GrantId) ?? throw new InvalidOperationException("The delegation grant is missing.");
            if (currentGrant.Revoked || currentGrant.UsedOccurrences != 0)
                throw new InvalidOperationException("This reminder's authority has already changed. Review it before rescheduling.");
            var updatedJob = job with { Version = job.Version + 1, Schedule = schedule, ScheduleVersion = scheduleVersion,
                NextRunUtc = due, Updated = now, LastSummary = "Rescheduled before dispatch." };
            var grant = currentGrant with { Version = currentGrant.Version + 1, ScheduleVersion = scheduleVersion,
                Expires = due + job.MaxLateness + TimeSpan.FromDays(1), Updated = now };
            using var transaction = db.BeginTransaction(); SaveDelegationJob(updatedJob); SaveDelegationGrant(grant); transaction.Commit();
            if (job.SourceRunId != null) AppendRunEvent(job.SourceRunId, "delegation.job.rescheduled", new { before = job, job = updatedJob, grantVersion = grant.Version }, now);
            return updatedJob;
        }
    }

    public DelegationJob EditScheduledEmail(string id, int version, ScheduledEmailPayload payload, DateTimeOffset now)
    {
        lock (gate)
        {
            var job = DelegationJobs().SingleOrDefault(item => item.Id == id) ?? throw new ArgumentException("Delegated job not found.");
            if (job.Version != version) throw new InvalidOperationException("This delegated job changed. Review it before editing.");
            if (job.Kind != "email" || job.Schedule.Kind != "once" || job.State != "scheduled" || job.CancellationRequested ||
                DelegationOccurrences(id).Any(item => item.State == "working"))
                throw new InvalidOperationException("Only a still-scheduled email can be edited.");
            ScheduledEmailPayload current;
            try { current = job.Action.Payload.Deserialize<ScheduledEmailPayload>(Wire.Json) ?? throw new JsonException(); }
            catch (JsonException) { throw new InvalidOperationException("The scheduled email payload is unreadable."); }
            if (DelegationEmailConversation.ToolVersion(current.Tool) != DelegationEmailConversation.ToolVersion(payload.Tool) ||
                current.SenderConnection != payload.SenderConnection)
                throw new InvalidOperationException("Editing cannot change the reviewed sender connection or provider tool.");
            var currentGrant = DelegationGrant(job.GrantId) ?? throw new InvalidOperationException("The delegation grant is missing.");
            if (currentGrant.Revoked || currentGrant.UsedOccurrences != 0)
                throw new InvalidOperationException("This email's authority has already changed. Review its current result.");
            var action = new DelegatedAction("email", payload.Recipient, JsonSerializer.SerializeToElement(payload, Wire.Json));
            var scheduleVersion = Wire.Hash(Wire.Pack(new { job.Schedule, action, previous = job.ScheduleVersion, revision = job.Version + 1 }));
            var rawTitle = string.IsNullOrWhiteSpace(payload.Subject) ? "Email " + payload.Recipient : payload.Subject;
            var title = rawTitle.Length <= 200 ? rawTitle : rawTitle[..199] + "…";
            var updatedJob = job with { Version = job.Version + 1, Title = title, Action = action,
                ScheduleVersion = scheduleVersion, Updated = now, LastSummary = "Email content replaced before dispatch." };
            var grant = currentGrant with { Version = currentGrant.Version + 1, Target = payload.Recipient,
                PayloadHash = ActionHash(action), ScheduleVersion = scheduleVersion, Updated = now };
            using var transaction = db.BeginTransaction(); SaveDelegationJob(updatedJob); SaveDelegationGrant(grant); transaction.Commit();
            if (job.SourceRunId != null) AppendRunEvent(job.SourceRunId, "delegation.email.edited", new
            {
                before = new { job.Id, job.Version, target = job.Action.Target },
                job = updatedJob,
                grantVersion = grant.Version
            }, now);
            return updatedJob;
        }
    }

    public DelegationJob PauseBrief(string id, int version, DateTimeOffset now)
    {
        lock (gate)
        {
            var job = DelegationJobs().SingleOrDefault(item => item.Id == id) ?? throw new ArgumentException("Delegated job not found.");
            if (job.Version != version) throw new InvalidOperationException("This recurring work changed. Review it before pausing.");
            if (job.Kind is not ("brief" or "inbox-watch") || job.Schedule.Kind is not ("weekdays" or "interval") || job.State != "scheduled" || job.CancellationRequested ||
                DelegationOccurrences(id).Any(item => item.State == "working"))
                throw new InvalidOperationException("Only scheduled recurring work with no active occurrence can be paused.");
            var currentGrant = DelegationGrant(job.GrantId) ?? throw new InvalidOperationException("The delegation grant is missing.");
            if (currentGrant.Revoked) throw new InvalidOperationException("This recurring work's authority was revoked.");
            var scheduleVersion = Wire.Hash(Wire.Pack(new { job.Schedule, job.Action, previous = job.ScheduleVersion, state = "paused", revision = job.Version + 1 }));
            var updatedJob = job with { Version = job.Version + 1, State = "paused", ScheduleVersion = scheduleVersion,
                NextRunUtc = null, Updated = now, LastSummary = "Paused before the next occurrence." };
            var grant = currentGrant with { Version = currentGrant.Version + 1, ScheduleVersion = scheduleVersion, Updated = now };
            using var transaction = db.BeginTransaction(); SaveDelegationJob(updatedJob); SaveDelegationGrant(grant); transaction.Commit();
            if (job.SourceRunId != null) AppendRunEvent(job.SourceRunId, job.Kind == "brief" ? "delegation.brief.paused" : "delegation.inbox-watch.paused", new { job = updatedJob, grantVersion = grant.Version }, now);
            return updatedJob;
        }
    }

    public DelegationJob ResumeBrief(string id, int version, DateTimeOffset now)
    {
        lock (gate)
        {
            var job = DelegationJobs().SingleOrDefault(item => item.Id == id) ?? throw new ArgumentException("Delegated job not found.");
            if (job.Version != version) throw new InvalidOperationException("This recurring work changed. Review it before resuming.");
            if (job.Kind is not ("brief" or "inbox-watch") || job.Schedule.Kind is not ("weekdays" or "interval") || job.State != "paused" || job.CancellationRequested ||
                DelegationOccurrences(id).Any(item => item.State == "working"))
                throw new InvalidOperationException("Only paused recurring work with no active occurrence can be resumed.");
            var currentGrant = DelegationGrant(job.GrantId) ?? throw new InvalidOperationException("The delegation grant is missing.");
            if (currentGrant.Revoked || currentGrant.Expires <= now)
                throw new InvalidOperationException("This recurring work needs a new review because its authority is unavailable or expired.");
            var next = job.Schedule.FirstDue(now);
            var scheduleVersion = Wire.Hash(Wire.Pack(new { job.Schedule, job.Action, previous = job.ScheduleVersion, state = "scheduled", revision = job.Version + 1 }));
            var updatedJob = job with { Version = job.Version + 1, State = "scheduled", ScheduleVersion = scheduleVersion,
                NextRunUtc = next, Updated = now, LastSummary = "Resumed with a newly resolved next occurrence." };
            var grant = currentGrant with { Version = currentGrant.Version + 1, ScheduleVersion = scheduleVersion, Updated = now };
            using var transaction = db.BeginTransaction(); SaveDelegationJob(updatedJob); SaveDelegationGrant(grant); transaction.Commit();
            if (job.SourceRunId != null) AppendRunEvent(job.SourceRunId, job.Kind == "brief" ? "delegation.brief.resumed" : "delegation.inbox-watch.resumed", new { job = updatedJob, grantVersion = grant.Version }, now);
            return updatedJob;
        }
    }

    public DelegationJob EditBrief(string id, int version, ScheduledBriefPayload payload, DateTimeOffset now)
    {
        lock (gate)
        {
            var job = DelegationJobs().SingleOrDefault(item => item.Id == id) ?? throw new ArgumentException("Delegated job not found.");
            if (job.Version != version) throw new InvalidOperationException("This recurring brief changed. Review it before editing.");
            if (job.Kind != "brief" || job.Schedule.Kind != "weekdays" || job.State is not ("scheduled" or "paused") || job.CancellationRequested ||
                DelegationOccurrences(id).Any(item => item.State == "working"))
                throw new InvalidOperationException("Only a scheduled or paused recurring brief with no active occurrence can be edited.");
            ScheduledBriefPayload current;
            try { current = job.Action.Payload.Deserialize<ScheduledBriefPayload>(Wire.Json) ?? throw new JsonException(); }
            catch (JsonException) { throw new InvalidOperationException("The recurring brief payload is unreadable."); }
            if (DelegationEmailConversation.ToolVersion(current.Email.Tool) != DelegationEmailConversation.ToolVersion(payload.Email.Tool) ||
                DelegationEmailConversation.ToolVersion(current.Calendar.Tool) != DelegationEmailConversation.ToolVersion(payload.Calendar.Tool) ||
                current.Destination != payload.Destination || current.Provider != payload.Provider)
                throw new InvalidOperationException("Editing cannot change the recurring brief's reviewed accounts, tools, destination, or model provider.");
            var schedule = new DelegationSchedule("weekdays", null, payload.TimeZone, payload.LocalTime); schedule.Validate();
            var action = new DelegatedAction("brief", payload.Destination, JsonSerializer.SerializeToElement(payload, Wire.Json), true);
            if (action.Payload.GetRawText().Length > 40_000) throw new ArgumentException("The recurring brief exceeds its review limits.");
            var currentGrant = DelegationGrant(job.GrantId) ?? throw new InvalidOperationException("The delegation grant is missing.");
            if (currentGrant.Revoked || currentGrant.Expires <= now)
                throw new InvalidOperationException("This recurring brief needs a new review because its authority is unavailable or expired.");
            var scheduleVersion = Wire.Hash(Wire.Pack(new { schedule, action, previous = job.ScheduleVersion, revision = job.Version + 1 }));
            DateTimeOffset? next = job.State == "paused" ? null : schedule.FirstDue(now);
            var updatedJob = job with { Version = job.Version + 1, Schedule = schedule, ScheduleVersion = scheduleVersion,
                Action = action, NextRunUtc = next, Updated = now, LastSummary = "Recurring brief schedule and read scope replaced before the next occurrence." };
            var grant = currentGrant with { Version = currentGrant.Version + 1, PayloadHash = ActionHash(action),
                ScheduleVersion = scheduleVersion, Expires = now + TimeSpan.FromDays(370), Updated = now };
            using var transaction = db.BeginTransaction(); SaveDelegationJob(updatedJob); SaveDelegationGrant(grant); transaction.Commit();
            if (job.SourceRunId != null) AppendRunEvent(job.SourceRunId, "delegation.brief.edited", new { before = job, job = updatedJob, grantVersion = grant.Version }, now);
            return updatedJob;
        }
    }

    public DelegationOccurrence ReadDelegationOccurrence(string id, int version, DateTimeOffset now)
    {
        lock (gate)
        {
            var occurrence = Occurrence(id) ?? throw new ArgumentException("Delegation occurrence not found.");
            if (occurrence.Version != version) throw new InvalidOperationException("This result changed. Refresh it before marking it read.");
            if (occurrence.State is "working") throw new InvalidOperationException("This delegated action has no final result yet.");
            if (occurrence.ReadAt != null) return occurrence;
            var updated = occurrence with { Version = occurrence.Version + 1, ReadAt = now };
            SaveDelegationOccurrence(updated);
            return updated;
        }
    }

    public DelegationJob EditInboxWatchInstruction(string id, int version, string instruction, DateTimeOffset now)
    {
        lock (gate)
        {
            instruction = instruction?.Trim() ?? "";
            if (instruction.Length is < 1 or > 500) throw new ArgumentException("Use an importance instruction from 1 through 500 characters.");
            var job = DelegationJobs().SingleOrDefault(item => item.Id == id) ?? throw new ArgumentException("Delegated job not found.");
            if (job.Version != version) throw new InvalidOperationException("This inbox watch changed. Refresh it before editing.");
            if (job.Kind != "inbox-watch" || job.Schedule.Kind != "interval" || job.State is not ("scheduled" or "paused") || job.CancellationRequested ||
                DelegationOccurrences(id).Any(item => item.State == "working"))
                throw new InvalidOperationException("Only a scheduled or paused inbox watch with no active check can be edited.");
            ScheduledInboxWatchPayload payload;
            try { payload = job.Action.Payload.Deserialize<ScheduledInboxWatchPayload>(Wire.Json) ?? throw new JsonException(); }
            catch (JsonException) { throw new InvalidOperationException("The inbox-watch payload is unreadable."); }
            var changedPayload = payload with { ImportanceInstruction = instruction };
            var action = new DelegatedAction("inbox-watch", payload.Destination, JsonSerializer.SerializeToElement(changedPayload, Wire.Json), true);
            var currentGrant = DelegationGrant(job.GrantId) ?? throw new InvalidOperationException("The delegation grant is missing.");
            if (currentGrant.Revoked || currentGrant.Expires <= now) throw new InvalidOperationException("This inbox watch needs a new authorization.");
            var scheduleVersion = Wire.Hash(Wire.Pack(new { job.Schedule, action, previous = job.ScheduleVersion, revision = job.Version + 1 }));
            var updatedJob = job with { Version = job.Version + 1, Action = action, ScheduleVersion = scheduleVersion,
                Updated = now, LastSummary = "Importance instruction updated before the next inbox check." };
            var grant = currentGrant with { Version = currentGrant.Version + 1, PayloadHash = ActionHash(action),
                ScheduleVersion = scheduleVersion, Updated = now };
            using var transaction = db.BeginTransaction(); SaveDelegationJob(updatedJob); SaveDelegationGrant(grant); transaction.Commit();
            return updatedJob;
        }
    }

    public int RecoverInterruptedDelegations(DateTimeOffset now)
    {
        lock (gate)
        {
            var interrupted = DelegationOccurrences().Where(item => item.State == "working").ToArray();
            foreach (var occurrence in interrupted)
            {
                var job = DelegationJobs().Single(item => item.Id == occurrence.JobId);
                var settled = occurrence with { Version = occurrence.Version + 1, State = "unknown", DispatchState = "unknown",
                    ClaimToken = null, CompletedAt = now, Summary = "The host stopped after dispatch intent. The effect will not be replayed without reconciliation." };
                var updatedJob = job.Schedule.Kind == "once" ? job with { State = "unknown", NextRunUtc = null, Version = job.Version + 1,
                    Updated = now, LastSummary = settled.Summary } : job with { Version = job.Version + 1, Updated = now, LastSummary = settled.Summary };
                using var transaction = db.BeginTransaction(); SaveDelegationOccurrence(settled); SaveDelegationJob(updatedJob); transaction.Commit();
                RecordOccurrence(job, settled, now);
            }
            return interrupted.Length;
        }
    }

    public int DisarmDelegationsAfterRestore(DateTimeOffset now)
    {
        lock (gate)
        {
            var active = DelegationJobs().Where(job => job.State is not ("cancelled" or "completed" or "succeeded" or "failed" or "unknown" or "missed")).ToArray();
            foreach (var job in active)
            {
                var currentGrant = DelegationGrant(job.GrantId) ?? throw new InvalidOperationException("A restored delegation grant is missing.");
                var summary = "Restored from backup with outbound authority disabled. Review and schedule a new delegation before any effect.";
                var updatedJob = job with { Version = job.Version + 1, State = "needs-approval", NextRunUtc = null,
                    CancellationRequested = false, Updated = now, LastSummary = summary };
                var grant = currentGrant with { Version = currentGrant.Version + 1, Revoked = true, Updated = now };
                using var transaction = db.BeginTransaction(); SaveDelegationJob(updatedJob); SaveDelegationGrant(grant); transaction.Commit();
                if (job.SourceRunId != null) AppendRunEvent(job.SourceRunId, "delegation.restore.disarmed", new
                {
                    job = updatedJob,
                    grantRevoked = true,
                    automaticRearm = false
                }, now);
            }
            return active.Length;
        }
    }

    public static string ActionHash(DelegatedAction action) => Wire.Hash(Wire.Pack(action));

    private DelegationOccurrence? Occurrence(string id) => Query("SELECT body FROM delegation_occurrences WHERE id=$id", ("$id", id))
        .Select(Wire.Unpack<DelegationOccurrence>).SingleOrDefault();
    private static void ValidateId(string id, string label)
    { if (!Regex.IsMatch(id ?? "", "\\A[a-f0-9]{32}\\z")) throw new ArgumentException($"Invalid delegation {label} ID."); }
    private static string OccurrenceId(DelegationJob job, DateTimeOffset due) => Wire.Hash(Wire.Pack(new { job.Id, job.ScheduleVersion, job.NextSequence, due = due.ToUniversalTime() }));
    private static string? GrantFailure(DelegationJob job, DelegationGrant grant, DateTimeOffset now)
    {
        if (grant.Revoked) return "The delegation grant was revoked; review is required before any future dispatch.";
        if (grant.Expires <= now) return "The delegation grant expired; review is required before any future dispatch.";
        if (grant.JobId != job.Id || grant.OwnerId != job.OwnerId || grant.Operation != job.Kind || grant.Target != job.Action.Target ||
            grant.PayloadHash != ActionHash(job.Action) || grant.ScheduleVersion != job.ScheduleVersion)
            return "The delegated action or schedule no longer matches its reviewed grant.";
        if (job.Kind == "brief" && (grant.MaxExternalCalls < 2 || grant.MaxModelCalls < 1)) return "The brief grant lacks its required bounded read and model allowance.";
        if (job.Kind == "inbox-watch" && (grant.MaxExternalCalls < 1 || grant.MaxModelCalls < 1)) return "The inbox-watch grant lacks its required bounded read and model allowance.";
        if (job.Kind is "email" or "reminder" && grant.MaxExternalCalls < 1) return "The delegation grant lacks an external delivery allowance.";
        return null;
    }
    private static DelegationOccurrence NewOccurrence(DelegationJob job, DateTimeOffset due, string state, string dispatch,
        string? token, DateTimeOffset now, string summary) => new(OccurrenceId(job, due), 0, job.Id, job.ScheduleVersion,
        job.NextSequence, due, state, Guid.NewGuid().ToString("N"), dispatch, token, token == null ? null : now,
        token == null ? now : null, summary);
    private static DelegationJob AdvanceWithoutDispatch(DelegationJob job, DateTimeOffset due, DateTimeOffset now, string summary)
    {
        var next = job.Schedule.NextAfter(due);
        return job with { State = next == null ? "missed" : "scheduled", NextRunUtc = next, NextSequence = job.NextSequence + 1,
            Version = job.Version + 1, Updated = now, LastSummary = summary };
    }
    private void SaveDelegationJob(DelegationJob job) => Exec("UPDATE delegation_jobs SET version=$version,nextRun=$next,body=$body WHERE id=$id",
        ("$version", job.Version), ("$next", (object?)job.NextRunUtc?.ToUniversalTime().ToString("O") ?? DBNull.Value), ("$body", Wire.Pack(job)), ("$id", job.Id));
    private void SaveDelegationGrant(DelegationGrant grant) => Exec("UPDATE delegation_grants SET version=$version,body=$body WHERE id=$id",
        ("$version", grant.Version), ("$body", Wire.Pack(grant)), ("$id", grant.Id));
    private void SaveDelegationOccurrence(DelegationOccurrence occurrence) => Exec("INSERT INTO delegation_occurrences VALUES($id,$job,$due,$body) ON CONFLICT(id) DO UPDATE SET body=$body",
        ("$id", occurrence.Id), ("$job", occurrence.JobId), ("$due", occurrence.DueUtc.ToUniversalTime().ToString("O")), ("$body", Wire.Pack(occurrence)));
    private void SaveInboxWatchState(DelegationJob job, InboxWatchState state, DateTimeOffset now)
    {
        var current = InboxWatchState(job.Id) ?? throw new InvalidOperationException("The inbox-watch progress record is missing.");
        if (state.JobId != job.Id || state.Version != current.Version + 1 || state.ActivatedAtUtc != current.ActivatedAtUtc ||
            state.ProcessedMessageIds.Length > 2000 || state.AlertedMessageIds.Length > 2000 || state.LastError?.Length > 1000)
            throw new InvalidOperationException("The inbox-watch progress update is invalid or stale.");
        state = state with { Updated = now };
        Exec("UPDATE inbox_watch_states SET version=$version,body=$body WHERE jobId=$id",
            ("$version", state.Version), ("$body", Wire.Pack(state)), ("$id", job.Id));
    }
    private void RecordOccurrence(DelegationJob job, DelegationOccurrence occurrence, DateTimeOffset now)
    {
        if (job.SourceRunId != null) AppendRunEvent(job.SourceRunId, "delegation.occurrence." + occurrence.State,
            new { jobId = job.Id, job.Title, occurrence, target = job.Action.Target }, now);
    }
}
