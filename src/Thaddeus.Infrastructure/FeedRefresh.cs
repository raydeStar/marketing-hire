namespace Thaddeus.Infrastructure;

public sealed class FeedRefresh(Store store, IPublicFeedReader reader) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1);
    public async Task<FeedFetch> Preview(string url, CancellationToken cancellation)
    {
        if (!await gate.WaitAsync(0, cancellation)) throw new InvalidOperationException("A feed request is already running. Try again shortly.");
        try { return await reader.Read(url, true, null, null, null, cancellation); }
        finally { gate.Release(); }
    }
    public async Task Tick(DateTimeOffset now, CancellationToken cancellation)
    {
        if (!await gate.WaitAsync(0, cancellation)) return;
        try
        {
            if (store.ClaimFeedRefresh(now) is not { } item) return;
            var result = await reader.Read(item.Url, false, item.ValidatorUrl, item.ETag, item.LastModified, cancellation);
            cancellation.ThrowIfCancellationRequested();
            store.FinishFeedRefresh(item, result, DateTimeOffset.UtcNow);
        }
        finally { gate.Release(); }
    }
    public void Dispose() => gate.Dispose();
}
