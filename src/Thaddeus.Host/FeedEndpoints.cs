using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public static class FeedEndpoints
{
    public static void Map(WebApplication app)
    {
        // The same authenticated, CSRF-protected personal collection is available on paired devices.
        app.MapPost("/api/feeds/preview", async (FeedAddress request, FeedRefresh feeds, HttpContext context) =>
            await feeds.Preview(request.Url, context.RequestAborted));
        app.MapPost("/api/feeds", (FeedAddress request, Store store) => store.Subscribe(request.Url, DateTimeOffset.UtcNow));
        app.MapPut("/api/feeds/{id}", (string id, FeedPause request, Store store) => store.ChangeSubscription(id, request.Version, request.Paused, DateTimeOffset.UtcNow));
        app.MapPost("/api/feeds/{id}/remove", (string id, FeedVersion request, Store store) => { store.RemoveSubscription(id, request.Version); return Results.Ok(); });
        app.MapPost("/api/feeds/{id}/refresh", (string id, FeedVersion request, Store store) => { store.QueueFeedRefresh(id, request.Version, DateTimeOffset.UtcNow); return Results.Ok(); });
        app.MapPut("/api/feed-entries/{id}", (string id, FeedRead request, Store store) => store.ReadFeedEntry(id, request.Version, request.Read));
        app.MapPost("/api/feed-entries/{id}/save", (string id, FeedVersion request, Store store) => store.SaveFeedEntry(id, request.Version));
    }
}
public record FeedAddress(string Url);
public record FeedVersion(string Version);
public record FeedPause(string Version, bool Paused);
public record FeedRead(string Version, bool Read);
