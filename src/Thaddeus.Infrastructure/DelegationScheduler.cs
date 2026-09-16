using System.Text.Json;
using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed record DelegationDispatchResult(string DispatchState, string Summary, bool ActionSucceeded,
    string? ProviderId = null, JsonElement? ProviderEvidence = null, string NotificationStatus = "not-attempted",
    string? NotificationError = null);

public interface IDelegationDispatcher
{
    Task<DelegationDispatchResult> Dispatch(DelegationJob job, DelegationOccurrence occurrence, CancellationToken cancellation);
}

public sealed class DelegationOutcomeUnknownException(string message) : IOException(message);

/// <summary>One durable host loop owns occurrence claims; dispatchers never choose or widen authority.</summary>
public sealed class DelegationScheduler(Store store, IDelegationDispatcher dispatcher, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public int Recover() => store.RecoverInterruptedDelegations(clock.GetUtcNow());

    public (DelegationJob Job, DelegationGrant Grant) CreateReminder(string title, string message, DateTimeOffset due,
        string timeZone, TimeSpan? maxLateness = null, DateTimeOffset? requestedAt = null, string? sourceRunId = null)
    {
        var now = clock.GetUtcNow(); var requested = requestedAt ?? now;
        title = title?.Trim() ?? ""; message = message?.Trim() ?? "";
        if (title.Length is < 1 or > 200 || message.Length is < 1 or > 2000) throw new ArgumentException("Use a reminder title up to 200 characters and message up to 2,000 characters.");
        if (due.ToUniversalTime() <= now) throw new ArgumentException("Choose a future reminder time.");
        var schedule = new DelegationSchedule("once", due.ToUniversalTime(), timeZone);
        schedule.Validate();
        var action = new DelegatedAction("reminder", "owner:windows+in-app",
            JsonSerializer.SerializeToElement(new { message }, Wire.Json));
        var jobId = Guid.NewGuid().ToString("N"); var grantId = Guid.NewGuid().ToString("N");
        var scheduleVersion = Wire.Hash(Wire.Pack(new { schedule, action, revision = 1 }));
        var lateness = maxLateness ?? TimeSpan.FromHours(6);
        var firstDue = schedule.FirstDue(requested);
        var job = new DelegationJob(jobId, 0, "local-owner", "reminder", title, schedule, scheduleVersion, action,
            grantId, "scheduled", requested, now, now, firstDue, 1, lateness, SourceRunId: sourceRunId);
        var grant = new DelegationGrant(grantId, 0, jobId, job.OwnerId, "windows-owner", "reminder", action.Target,
            Store.ActionHash(action), scheduleVersion, 1, 0, firstDue + lateness + TimeSpan.FromDays(1), 1, 0, false, now, now);
        return store.CreateDelegation(job, grant);
    }

    public (DelegationJob Job, DelegationGrant Grant) CreateEmail(EmailDelegationProposal proposal,
        TimeSpan? maxLateness = null, DateTimeOffset? requestedAt = null, string? sourceRunId = null)
    {
        var now = clock.GetUtcNow(); var requested = requestedAt ?? now;
        if (proposal.DueUtc.ToUniversalTime() <= now) throw new ArgumentException("Choose a future email send time.");
        var schedule = new DelegationSchedule("once", proposal.DueUtc.ToUniversalTime(), proposal.TimeZone); schedule.Validate();
        var action = new DelegatedAction("email", proposal.Email.Recipient, JsonSerializer.SerializeToElement(proposal.Email, Wire.Json));
        var jobId = Guid.NewGuid().ToString("N"); var grantId = Guid.NewGuid().ToString("N");
        var scheduleVersion = Wire.Hash(Wire.Pack(new { schedule, action, toolVersion = DelegationEmailConversation.ToolVersion(proposal.Email.Tool), revision = 1 }));
        var lateness = maxLateness ?? TimeSpan.FromMinutes(15);
        var firstDue = schedule.FirstDue(requested);
        var rawTitle = string.IsNullOrWhiteSpace(proposal.Email.Subject) ? "Email " + proposal.Email.Recipient : proposal.Email.Subject!;
        var title = rawTitle.Length <= 200 ? rawTitle : rawTitle[..199] + "…";
        var job = new DelegationJob(jobId, 0, "local-owner", "email", title, schedule, scheduleVersion, action,
            grantId, "scheduled", requested, now, now, firstDue, 1, lateness, SourceRunId: sourceRunId);
        var grant = new DelegationGrant(grantId, 0, jobId, job.OwnerId, proposal.Email.SenderConnection, "email", proposal.Email.Recipient,
            Store.ActionHash(action), scheduleVersion, 1, 0, firstDue + lateness + TimeSpan.FromDays(1), 1, 0, false, now, now);
        return store.CreateDelegation(job, grant);
    }

    public (DelegationJob Job, DelegationGrant Grant) CreateBrief(BriefDelegationProposal proposal,
        TimeSpan? maxLateness = null, DateTimeOffset? requestedAt = null, string? sourceRunId = null)
    {
        var now = clock.GetUtcNow(); var requested = requestedAt ?? now;
        var brief = proposal.Brief;
        var schedule = new DelegationSchedule("weekdays", null, brief.TimeZone, brief.LocalTime); schedule.Validate();
        var action = new DelegatedAction("brief", brief.Destination, JsonSerializer.SerializeToElement(brief, Wire.Json), true);
        var jobId = Guid.NewGuid().ToString("N"); var grantId = Guid.NewGuid().ToString("N");
        var scheduleVersion = Wire.Hash(Wire.Pack(new { schedule, action,
            emailTool = DelegationEmailConversation.ToolVersion(brief.Email.Tool),
            calendarTool = DelegationEmailConversation.ToolVersion(brief.Calendar.Tool), revision = 1 }));
        var lateness = maxLateness ?? TimeSpan.FromHours(6); var firstDue = schedule.FirstDue(requested);
        var job = new DelegationJob(jobId, 0, "local-owner", "brief", "Weekday morning brief", schedule, scheduleVersion, action,
            grantId, "scheduled", requested, now, now, firstDue, 1, lateness, SourceRunId: sourceRunId);
        var account = $"email:{brief.Email.Tool.ConnectorId};calendar:{brief.Calendar.Tool.ConnectorId}";
        var grant = new DelegationGrant(grantId, 0, jobId, job.OwnerId, account, "brief", action.Target,
            Store.ActionHash(action), scheduleVersion, 260, 0, firstDue + TimeSpan.FromDays(370), 520, 260, false, now, now);
        return store.CreateDelegation(job, grant);
    }

    public async Task<int> Tick(CancellationToken cancellation = default)
    {
        var settled = 0;
        for (var count = 0; count < 32; count++)
        {
            cancellation.ThrowIfCancellationRequested();
            var now = clock.GetUtcNow();
            var claim = store.ClaimDueDelegation(now);
            if (claim == null) break;
            if (!store.AuthorizeDelegationClaim(claim.Occurrence.Id, claim.Token, clock.GetUtcNow()))
            {
                store.CompleteDelegation(claim.Occurrence.Id, claim.Token,
                    new("cancelled", "not-dispatched", "Authorization was revoked or changed before dispatch; no effect was attempted.", false), clock.GetUtcNow());
                settled++; continue;
            }
            try
            {
                var result = await dispatcher.Dispatch(claim.Job, claim.Occurrence, cancellation);
                store.CompleteDelegation(claim.Occurrence.Id, claim.Token,
                    new(result.ActionSucceeded ? "succeeded" : "failed", result.DispatchState, result.Summary,
                        result.ActionSucceeded, result.ProviderId, result.ProviderEvidence, result.NotificationStatus,
                        result.NotificationError), clock.GetUtcNow());
            }
            catch (DelegationOutcomeUnknownException error)
            {
                store.CompleteDelegation(claim.Occurrence.Id, claim.Token,
                    new("unknown", "unknown", "The provider outcome is unknown. No automatic retry was started.", false,
                        ProviderEvidence: JsonSerializer.SerializeToElement(new { classification = error.GetType().Name }, Wire.Json)), clock.GetUtcNow());
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            {
                var reason = Regex.Replace(error.Message ?? "", "\\s+", " ").Trim();
                if (reason.Length is < 1 or > 1000) reason = "The reviewed setup is no longer valid.";
                store.CompleteDelegation(claim.Occurrence.Id, claim.Token,
                    new("failed", "failed", $"Setup review required: {reason} Open Settings → Connections if a connector or credential changed, then create a newly reviewed schedule; this occurrence will not retry automatically.", false,
                        ProviderEvidence: JsonSerializer.SerializeToElement(new { classification = error.GetType().Name, setupReview = true }, Wire.Json)), clock.GetUtcNow());
            }
            catch (Exception error) when (error is HttpRequestException or IOException or JsonException)
            {
                store.CompleteDelegation(claim.Occurrence.Id, claim.Token,
                    new("failed", "failed", "The delegated action failed before a verified outcome. Review its setup before retrying.", false,
                        ProviderEvidence: JsonSerializer.SerializeToElement(new { classification = error.GetType().Name }, Wire.Json)), clock.GetUtcNow());
            }
            settled++;
        }
        return settled;
    }
}
