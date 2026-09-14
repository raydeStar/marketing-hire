using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public sealed class FeedPump(FeedRefresh feeds) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken)) await feeds.Tick(DateTimeOffset.UtcNow, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
