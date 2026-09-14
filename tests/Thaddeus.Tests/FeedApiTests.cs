using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class FeedApiTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-feed-api-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> factory;
    private readonly Reader reader = new();
    private Store Store => factory.Services.GetRequiredService<Store>();
    private sealed class Reader : IPublicFeedReader
    {
        public int Calls;
        public Task<FeedFetch> Read(string url, bool discover, string? validatorUrl, string? etag, DateTimeOffset? modified, CancellationToken cancellation)
        {
            Interlocked.Increment(ref Calls);
            var feed = FeedParser.Parse(Encoding.UTF8.GetBytes("<rss version='2.0'><channel><title>Fixture Gazette</title><item><guid>fixture-one</guid><title>A careful test</title><description>Untrusted &lt;b&gt;excerpt&lt;/b&gt;</description><link>https://news.example.org/one</link></item></channel></rss>"), new(url));
            return Task.FromResult(new FeedFetch(url, feed));
        }
    }
    public FeedApiTests()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.ConfigureServices(services => services.AddSingleton<IPublicFeedReader>(reader));
        });
    }
    private HttpClient Client(bool? owner = true, bool csrf = true)
    {
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        if (owner != null)
        {
            var context = new DefaultHttpContext(); var session = factory.Services.GetRequiredService<Security>().Issue(context, "Feed fixture", owner.Value);
            client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
            if (csrf) client.DefaultRequestHeaders.Add("X-CSRF", session.Csrf);
        }
        return client;
    }
    [Theory]
    [InlineData(null, true, 401)]
    [InlineData(true, false, 403)]
    public async Task UnauthenticatedOrMissingCsrfRequestsCannotFetchOrMutate(bool? owner, bool csrf, int code)
    {
        using var client = Client(owner, csrf);
        foreach (var route in new[] { "/api/feeds/preview", "/api/feeds", "/api/feeds/missing/remove", "/api/feeds/missing/refresh", "/api/feed-entries/missing/save" })
            Assert.Equal((HttpStatusCode)code, (await client.PostAsJsonAsync(route, new { url = "https://news.example.org/rss", version = "absent" })).StatusCode);
        Assert.Equal(0, reader.Calls); Assert.Empty(Store.Feeds().Subscriptions);
    }
    [Fact] public async Task PairedDeviceCanSubscribeReadSaveAndRemoveWithoutModelsAndExportIncludesFeeds()
    {
        using var paired = Client(false);
        var response = await paired.PostAsJsonAsync("/api/feeds", new { url = "https://news.example.org/rss" }); response.EnsureSuccessStatusCode();
        await factory.Services.GetRequiredService<FeedRefresh>().Tick(DateTimeOffset.UtcNow, default);
        var state = await paired.GetFromJsonAsync<JsonElement>("/api/state");
        Assert.Single(state.GetProperty("feeds").GetProperty("subscriptions").EnumerateArray());
        var entry = Store.Feeds().Entries.Single();
        (await paired.PutAsJsonAsync("/api/feed-entries/" + entry.Id, new { version = entry.Version, read = true })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await paired.PutAsJsonAsync("/api/feed-entries/" + entry.Id, new { version = entry.Version, read = false })).StatusCode);
        entry = Store.Feeds().Entries.Single();
        (await paired.PostAsJsonAsync("/api/feed-entries/" + entry.Id + "/save", new { version = entry.Version })).EnsureSuccessStatusCode();
        using var owner = Client(); var exported = await owner.GetFromJsonAsync<JsonElement>("/api/export");
        Assert.Equal(5, exported.GetProperty("schemaVersion").GetInt32()); Assert.Single(exported.GetProperty("feeds").GetProperty("entries").EnumerateArray());
        Assert.Single(exported.GetProperty("library").EnumerateArray()); Assert.Empty(exported.GetProperty("runs").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await paired.GetAsync("/api/export")).StatusCode);
        var subscription = Store.Feeds().Subscriptions.Single();
        (await paired.PostAsJsonAsync("/api/feeds/" + subscription.Id + "/remove", new { version = subscription.Version })).EnsureSuccessStatusCode();
        Assert.Empty(Store.Feeds().Subscriptions); Assert.Empty(Store.Feeds().Entries); Assert.Single(Store.Library()); Assert.Empty(Store.AllEvents());
    }
    [Fact] public async Task PreviewIsEphemeralAndPersonalDeletionClearsSubscriptions()
    {
        using var client = Client();
        (await client.PostAsJsonAsync("/api/feeds/preview", new { url = "https://news.example.org/rss" })).EnsureSuccessStatusCode();
        Assert.Empty(Store.Feeds().Subscriptions); Assert.Empty(Store.Feeds().Entries);
        (await client.PostAsJsonAsync("/api/feeds", new { url = "https://news.example.org/rss" })).EnsureSuccessStatusCode();
        var revision = Store.FeedRevision();
        (await client.PostAsJsonAsync("/api/data/delete", new { confirmation = "DELETE MY DATA" })).EnsureSuccessStatusCode();
        Assert.Empty(Store.Feeds().Subscriptions); Assert.NotEqual(revision, Store.FeedRevision()); Assert.Empty(Store.List());
    }
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        await factory.DisposeAsync(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
