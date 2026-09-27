using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

/// <summary>Sounds like you: past posts from a public profile (or pasted) and three true stories become the Voice and Stories
/// pages, and each piece of work gets the two closest posts and the one closest story.</summary>
public sealed class VoiceStudioTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "voice-studio-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++) { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }
    sealed class Loopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => { app.Use((context, proceed) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return proceed(); }); next(app); };
    }
    /// <summary>Bluesky's public AppView and a Mastodon server's public API, as far as reading a profile's posts needs them.</summary>
    sealed class FakeProfiles : HttpMessageHandler
    {
        static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        static object Post(string text, bool repost = false) => repost ? new { post = new { record = new { text } }, reason = new { by = "someone" } } : new { post = new { record = new { text } } };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(request.RequestUri!.AbsoluteUri switch
        {
            "https://public.api.bsky.app/xrpc/app.bsky.feed.getAuthorFeed?actor=mark.bsky.social&limit=50&filter=posts_no_replies" => Json(new { feed = new[] {
                Post("We gave our AI marketing employee a four-hour shift. Here's the receipt: nothing posted without us."),
                Post("Someone else's launch, reposted, which isn't how we sound.", repost: true),
                Post("Too short."),
                Post("Receipts make mistakes cheap. Every problem took minutes to find, because every action was on the record.") } }),
            "https://mastodon.example/api/v1/accounts/lookup?acct=mark" => Json(new { id = "42" }),
            "https://mastodon.example/api/v1/accounts/42/statuses?exclude_replies=true&exclude_reblogs=true&limit=40" => Json(new[] {
                new { content = "<p>An employee that knows when not to add to your pile is worth more than one that fills folders.</p>" } }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
    }

    [Fact] public async Task PastPostsAndTrueStoriesBecomeTheVoiceTheWriterMatches()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, Loopback>());
        });
        factory.Services.GetRequiredService<VoiceStudio>().Handler = () => new FakeProfiles();
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var owner = factory.Services.GetRequiredService<Security>().Issue(context, "Owner", true);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        async Task<(HttpStatusCode Status, JsonElement Body)> Post(string path, object body)
        {
            using var response = await client.PostAsJsonAsync(path, body);
            var text = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, text.Length > 0 ? JsonDocument.Parse(text).RootElement.Clone() : default);
        }

        // Bluesky: the owner's own posts only, no reposts, nothing too short to show a voice.
        var bluesky = await Post("/api/voice/import", new { handle = "@mark.bsky.social" });
        Assert.Equal(HttpStatusCode.OK, bluesky.Status);
        Assert.Equal(2, bluesky.Body.GetProperty("posts").GetArrayLength());
        Assert.DoesNotContain("reposted", bluesky.Body.GetRawText());
        // Mastodon: the server's public API, HTML turned into the words.
        var mastodon = await Post("/api/voice/import", new { handle = "@mark@mastodon.example" });
        Assert.Equal("An employee that knows when not to add to your pile is worth more than one that fills folders.", mastodon.Body.GetProperty("posts")[0].GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Post("/api/voice/import", new { handle = "nobody.bsky.social" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("/api/voice/import", new { handle = "not a handle" })).Status);

        var posts = bluesky.Body.GetProperty("posts").EnumerateArray().Select(item => item.GetString()!).Append(mastodon.Body.GetProperty("posts")[0].GetString()!).ToArray();
        var saved = await Post("/api/voice", new { posts, started = "We lost a launch because nobody had time to write about it.", customer = "A founder approved three drafts from her phone between meetings.", opinion = "An AI employee should ask first. Autopilot is the wrong goal for anything that speaks in your name." });
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        Assert.Equal((3, 3), (saved.Body.GetProperty("posts").GetInt32(), saved.Body.GetProperty("stories").GetInt32()));
        var wiki = factory.Services.GetRequiredService<CompanyWiki>();
        var voice = wiki.List().Single(page => page.Title == VoiceStudio.VoiceTitle);
        Assert.Contains(VoiceStudio.PostsHeading, voice.Body);

        // Saving again replaces the examples and the same story, and keeps the rest of each page.
        await Post("/api/voice", new { posts = new[] { "Hire a marketing employee. Keep the final say." }, started = "We started because our own launch went unmarketed." });
        voice = wiki.List().Single(page => page.Title == VoiceStudio.VoiceTitle);
        var stories = wiki.List().Single(page => page.Title == VoiceStudio.StoriesTitle).Body;
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(voice.Body, "## Posts that sound like us"));
        Assert.DoesNotContain("four-hour shift", voice.Body);
        Assert.Contains("our own launch went unmarketed", stories);
        Assert.DoesNotContain("nobody had time", stories);
        Assert.Contains("Autopilot is the wrong goal", stories);

        // The writer gets the posts and the story closest to the piece.
        var (guide, closest, story) = VoiceStudio.Closest("Short sentences.\n\n## Posts that sound like us\n\n" + string.Join("\n\n---\n\n", posts), stories, "LinkedIn post: receipts make every mistake cheap to find");
        Assert.Equal("Short sentences.", guide);
        Assert.StartsWith("Receipts make mistakes cheap.", closest[0]);
        Assert.Equal(2, closest.Length);
        Assert.StartsWith("## Something we believe", VoiceStudio.Closest(null, stories, "Should an AI employee ask first, or run on autopilot?").Story);
    }
}
