using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

internal static class GatewayReadiness
{
    internal static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    internal const int MaxAttempts = 30;

    internal static async Task<bool> Wait(Func<int, CancellationToken, Task<SandboxCommandResult>> probe,
        TimeProvider clock, CancellationToken cancellation)
    {
        var started = clock.GetTimestamp();
        using var deadline = new CancellationTokenSource(Window, clock);
        using var admission = CancellationTokenSource.CreateLinkedTokenSource(cancellation, deadline.Token);
        try
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (clock.GetElapsedTime(started) >= Window || deadline.IsCancellationRequested) return false;
                var health = await probe(attempt, admission.Token);
                cancellation.ThrowIfCancellationRequested();
                // A late butler still missed his appointment, even if he arrives bearing good news.
                var remaining = Window - clock.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero || deadline.IsCancellationRequested) return false;
                if (health.ExitCode == 0) return true;
                await Task.Delay(remaining < Interval ? remaining : Interval, clock, admission.Token);
            }
            return false;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            return false;
        }
    }
}
