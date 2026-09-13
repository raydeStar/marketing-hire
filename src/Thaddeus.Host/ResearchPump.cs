using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

/// <summary>The product drives lifecycle transitions; OpenClaw alone drives agent work.</summary>
public sealed class ResearchPump(ResearchCoordinator coordinator) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken)) await coordinator.Tick(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
