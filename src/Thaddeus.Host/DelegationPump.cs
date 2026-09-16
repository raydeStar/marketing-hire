using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public sealed class DelegationPump(DelegationScheduler scheduler) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        scheduler.Recover();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do { await scheduler.Tick(stoppingToken); }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
