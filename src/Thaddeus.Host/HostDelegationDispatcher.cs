using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public sealed class HostDelegationDispatcher(
    WindowsDelegationDispatcher windows,
    ConnectedEmailDelegationDispatcher email,
    ConnectedBriefDelegationDispatcher brief,
    ConnectedInboxWatchDispatcher inboxWatch) : IDelegationDispatcher
{
    public async Task<DelegationDispatchResult> Dispatch(DelegationJob job, DelegationOccurrence occurrence, CancellationToken cancellation)
    {
        var result = job.Kind switch
        {
            "reminder" => await windows.Dispatch(job, occurrence, cancellation),
            "email" => await email.Dispatch(job, occurrence, cancellation),
            "brief" => await brief.Dispatch(job, occurrence, cancellation),
            "inbox-watch" => await inboxWatch.Dispatch(job, occurrence, cancellation),
            _ => throw new InvalidOperationException("This host has no dispatcher for the delegated work kind.")
        };

        return job.Kind switch
        {
            "brief" => windows.NotifyResult(job, result, "Your morning brief is ready in Thaddeus."),
            "inbox-watch" when !result.Quiet => windows.NotifyResult(job, result,
                "Important mail needs your attention. Open Thaddeus to review it."),
            _ => result
        };
    }
}
