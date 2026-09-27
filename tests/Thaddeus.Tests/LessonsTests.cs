using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

/// <summary>Learns out loud: from two weeks of post results and the owner's verdicts, the week's changes are written up with their
/// numbers, shown in continuity, and applied to the work the employee chooses itself.</summary>
public sealed class LessonsTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "lessons-" + Guid.NewGuid().ToString("N"));
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

    [Fact] public async Task ResultsAndVerdictsBecomeChangesItSaysAndPlansBy()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host")); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:FixtureLedger", Path.Combine(root, "ledger"));
            builder.UseSetting("Marketing:FixtureRunwayScript", Path.Combine(directory!.FullName, "business", "agent", "hire", "bin", "runway.py"));
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, Loopback>());
        });
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
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        // Two weeks of posts: LinkedIn drew replies, X barely anything.
        var now = DateTimeOffset.UtcNow;
        Publication Post(int n, string channel, int likes, int replies) => new("p" + n, "r" + n, n, "d", "c", channel.ToLowerInvariant(), "published", null, now.AddDays(-n), now.AddDays(-n), "https://example.org/" + n, null, "Owner",
            null, "A post.", channel, new(likes, 0, replies, null, null, null, now, null));
        factory.Services.GetRequiredService<Store>().Setting("publishing-v1", Wire.Pack(new PublishingLedger("scope", [],
            [Post(1, "LinkedIn", 10, 3), Post(2, "LinkedIn", 8, 2), Post(3, "LinkedIn", 12, 4), Post(4, "X", 1, 0), Post(5, "X", 0, 0), Post(6, "X", 2, 0)])));
        // And the owner turned down four Bluesky drafts out of four.
        var marketing = factory.Services.GetRequiredService<MarketingBackend>();
        for (var n = 1; n <= 4; n++)
            Assert.Null((await marketing.ShiftHire(null, "draft", "add", "--channel", "Bluesky", "--destination", "https://bsky.app/", "--content", $"Bluesky post number {n} about receipts.", "--rationale", "Test.", "--rules-url", "UNVERIFIED")).Error);
        foreach (var draft in (await Send(HttpMethod.Get, "/api/marketing/state")).GetProperty("drafts").EnumerateArray())
            await Send(HttpMethod.Post, $"/api/marketing/drafts/{draft.GetProperty("id").GetInt32()}/decision",
                new { requestId = Guid.NewGuid().ToString("N"), decision = "rejected", revision = draft.GetProperty("revision").GetInt32(), digest = draft.GetProperty("digest").GetString() });

        // The weekly update says what changed and why, with the numbers.
        var weekly = factory.Services.GetRequiredService<WeeklyRhythm>();
        var update = await weekly.Write("update", default);
        var body = factory.Services.GetRequiredService<CompanyWiki>().List().Single(page => page.Id == update.WikiId).Body;
        Assert.Contains("## What I changed and why", body);
        Assert.Contains("**More LinkedIn.** LinkedIn posts earned 19× the engagement of X posts over two weeks (19 against 1 a post, 3 and 3 posts).", body);
        Assert.Contains("**Less X.**", body);
        Assert.Contains("**Fewer Bluesky drafts.** You approved 0 of the last 4 Bluesky drafts", body);
        // Once a week: the plan asking again changes nothing.
        var lessons = factory.Services.GetRequiredService<Lessons>();
        Assert.Equal(3, (await lessons.Adopt(default)).Length);
        Assert.Equal(3, lessons.All().Length);
        Assert.Equal((1.5, 0.6, 0.6), (lessons.Weights()["LinkedIn"], lessons.Weights()["X"], lessons.Weights()["Bluesky"]));

        // Continuity leads with the changes it plans by.
        Assert.StartsWith("More LinkedIn: ", (await Send(HttpMethod.Get, "/api/continuity")).GetProperty("changedMind")[0].GetString());

        // The next plan applies them to the work it chose itself; the owner's task keeps its channel.
        JsonElement Task(string id, string title) => JsonSerializer.SerializeToElement(new { id, title, priority = "normal", next_action = "Write it." });
        var plan = JsonSerializer.SerializeToElement(new { priorities = new object[] {
            new { title = "Three X posts on receipts", reason = "Idea", deliverable = "draft" },
            new { title = "An X thread for the launch", reason = "Assigned", deliverable = "draft", taskId = "t1" },
            new { title = "A LinkedIn post on receipts", reason = "Idea", deliverable = "draft" } }, newTasks = Array.Empty<object>(), note = "Social." });
        var (priorities, _, note) = EmployeeShifts.ValidatePriorities(plan, [Task("t1", "An X thread for the launch")], lessons.Weights());
        Assert.Equal(["A LinkedIn post on receipts", "An X thread for the launch"], priorities.Select(item => item.GetProperty("title").GetString()));
        Assert.Contains("Held back “Three X posts on receipts”", note);
    }
}
