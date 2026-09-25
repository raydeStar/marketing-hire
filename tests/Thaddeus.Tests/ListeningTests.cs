using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class ListeningTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "listening-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++)
        {
            try { Directory.Delete(root, true); }
            catch (IOException) when (attempt < 10) { await Task.Delay(200); }
        }
    }

    [Fact] public void FeedsParseRssAndAtomAndRefuseDtds()
    {
        var rss = MarketListening.ParseFeed("""
            <?xml version="1.0"?><rss version="2.0"><channel><title>Rival blog</title>
            <item><title>Launching &lt;b&gt;AI agents&lt;/b&gt;</title><link>https://blog.rival.example/agents</link><description>&lt;p&gt;Our new agents.&lt;/p&gt;</description><pubDate>Tue, 22 Sep 2026 10:00:00 GMT</pubDate></item>
            <item><title>No link</title></item></channel></rss>
            """);
        var item = Assert.Single(rss);
        Assert.Equal("Launching AI agents", item.Title); Assert.Equal("Our new agents.", item.Summary); Assert.Equal(new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero), item.PublishedAt);
        var atom = MarketListening.ParseFeed("""
            <feed xmlns="http://www.w3.org/2005/Atom"><entry><title>Pricing change</title><link href="https://rival.example/pricing"/><summary>New tiers</summary><updated>2026-09-24T08:00:00Z</updated></entry></feed>
            """);
        Assert.Equal("https://rival.example/pricing", Assert.Single(atom).Url);
        Assert.Throws<XmlException>(() => MarketListening.ParseFeed("""<?xml version="1.0"?><!DOCTYPE x [<!ENTITY e SYSTEM "file:///etc/passwd">]><rss><channel><item><title>&e;</title></item></channel></rss>"""));
    }

    [Fact] public void WordListSentimentReadsNegationAndStaysNeutralWithoutCues()
    {
        Assert.Equal("positive", SentimentLexicon.Of("I love how reliable this is"));
        Assert.Equal("negative", SentimentLexicon.Of("The update is broken and support is useless"));
        Assert.Equal("negative", SentimentLexicon.Of("honestly not good"));
        Assert.Equal("neutral", SentimentLexicon.Of("Released version 2.4 today"));
    }

    [Fact] public async Task ListeningFlagsSpikesAndNegativeTurnsAndAShiftAnswersThemFromTheMentions()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host"));
            builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:FixtureLedger", Path.Combine(root, "ledger"));
            builder.UseSetting("Marketing:FixtureRunwayScript", Path.Combine(directory!.FullName, "business", "agent", "hire", "bin", "runway.py"));
            builder.UseSetting("Marketing:ShiftPump", "off");
        });
        var now = DateTimeOffset.UtcNow;
        var listening = factory.Services.GetRequiredService<MarketListening>();
        // A steady week of one mention a day, then a burst of complaints; a brand-new topic arrives all at once.
        listening.Pulse = (topic, _) => Task.FromResult<ResearchSource[]?>(topic == "First Employee"
            ? [.. Enumerable.Range(2, 7).Select(day => new ResearchSource($"https://bsky.app/profile/a/post/{day}", $"Tried First Employee, day {day}", "Released a new build today.", null, now.AddDays(-day).AddHours(6), "Bluesky")),
               .. Enumerable.Range(0, 8).Select(hour => new ResearchSource($"https://news.ycombinator.com/item?id={hour}", $"First Employee update is broken #{hour}", "Terrible: the shift failed and support is useless.", null, now.AddHours(-hour - 1), "Hacker News"))]
            : [.. Enumerable.Range(0, 10).Select(hour => new ResearchSource($"https://bsky.app/profile/b/post/{hour}", $"Brand new thing is broken {hour}", "Awful and broken.", null, now.AddHours(-hour - 1), "Bluesky"))]);
        listening.FetchFeed = (url, _) => Task.FromResult($"""
            <rss version="2.0"><channel><item><title>Rival launches AI marketing agents</title><link>https://blog.rival.example/agents</link><description>Agents for every team.</description><pubDate>{now.AddHours(-3):R}</pubDate></item></channel></rss>
            """);

        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var owner = factory.Services.GetRequiredService<Security>().Issue(context, "Owner", true);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        async Task<JsonElement> Send(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path) { Content = body == null ? null : JsonContent.Create(body) };
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, path + " → " + (int)response.StatusCode + " " + text);
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        object Goals(string[] topics, string[] feeds) => new { expectedVersion = 0, content = new { objectives = Array.Empty<object>(), competitors = Array.Empty<object>(), currentFocus = "", nonGoals = Array.Empty<string>(), watchTopics = topics, feeds } };
        using (var bad = await client.PutAsJsonAsync("/api/objectives", Goals(["First Employee"], ["http://blog.rival.example/feed"])))
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        await Send(HttpMethod.Put, "/api/objectives", Goals(["First Employee", "Brand new thing"], ["https://blog.rival.example/feed.xml"]));

        var scanned = await Send(HttpMethod.Post, "/api/listening/scan", new { });
        Assert.Equal(15 + 10 + 1, scanned.GetProperty("scan").GetProperty("new").GetInt32()); // both topics, plus the feed post
        var view = scanned.GetProperty("view");
        var stats = view.GetProperty("stats").EnumerateArray().ToDictionary(item => item.GetProperty("topic").GetString()!);
        Assert.Equal(8, stats["First Employee"].GetProperty("last24h").GetInt32());
        Assert.Equal(1, stats["First Employee"].GetProperty("perDayPriorWeek").GetDouble());
        Assert.Equal(14, stats["First Employee"].GetProperty("daily").GetArrayLength());
        Assert.Equal("mention_spike", stats["First Employee"].GetProperty("flag").GetString());
        // No history yet: a first backfill is never a spike.
        Assert.Equal(JsonValueKind.Null, stats["Brand new thing"].GetProperty("flag").ValueKind);
        Assert.Contains(view.GetProperty("mentions").EnumerateArray(), item => item.GetProperty("source").GetString() == "blog.rival.example");
        var signals = listening.Signals();
        Assert.Contains(signals, signal => signal.Kind == "mention_spike" && signal.Severity == "high" && signal.MetricName == "First Employee");
        Assert.Contains(signals, signal => signal.Kind == "sentiment_drop" && signal.MetricName == "First Employee");
        Assert.DoesNotContain(signals, signal => signal.MetricName == "Brand new thing");
        // Scanning again adds nothing new.
        Assert.Equal(0, (await Send(HttpMethod.Post, "/api/listening/scan", new { })).GetProperty("scan").GetProperty("new").GetInt32());

        // A shift hears it in Sense and answers from what people said.
        await Send(HttpMethod.Post, "/api/shifts", new { requestId = "shift-l", hours = 8, turnBudget = 20 });
        var shift = await Send(HttpMethod.Post, "/api/shifts/shift-l/cycle", new { });
        var stages = shift.GetProperty("cycles")[0].GetProperty("stages");
        Assert.Contains("Listened to 2 topic(s) and 1 feed(s)", stages[0].GetProperty("summary").GetString());
        Assert.Contains("What people are saying about First Employee", stages[1].GetRawText());
        var wiki = await Send(HttpMethod.Get, "/api/company-wiki");
        var page = wiki.EnumerateArray().First(item => item.GetProperty("title").GetString() == "What people are saying about First Employee").GetProperty("body").GetString()!;
        Assert.Contains("## Sources", page); Assert.Contains("https://news.ycombinator.com/item?id=0", page); Assert.Contains("· Hacker News ·", page);
        // Handled signals don't come back the next cycle.
        shift = await Send(HttpMethod.Post, "/api/shifts/shift-l/cycle", new { });
        Assert.Contains("prioritize:skipped", shift.GetProperty("cycles")[1].GetProperty("stages").EnumerateArray().Select(stage => stage.GetProperty("stage").GetString() + ":" + stage.GetProperty("status").GetString()));
        Assert.Equal(JsonValueKind.Object, (await Send(HttpMethod.Get, "/api/export")).GetProperty("listening").ValueKind);
    }
}
