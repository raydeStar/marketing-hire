using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public sealed class HostDelegationDispatcher(
    WindowsDelegationDispatcher windows,
    ConnectedEmailDelegationDispatcher email) : IDelegationDispatcher
{
    public Task<DelegationDispatchResult> Dispatch(DelegationJob job, DelegationOccurrence occurrence, CancellationToken cancellation) =>
        job.Kind switch
        {
            "reminder" => windows.Dispatch(job, occurrence, cancellation),
            "email" => email.Dispatch(job, occurrence, cancellation),
            _ => throw new InvalidOperationException("This host has no dispatcher for the delegated work kind.")
        };
}
