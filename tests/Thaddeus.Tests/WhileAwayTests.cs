using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

/// <summary>While you were away: a public question with its reply, a competitor's price change with its response, and what the
/// latest post brought in — at most three, each with one action, at the top of the morning brief.</summary>
public sealed class WhileAwayTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "while-away-" + Guid.NewGuid().ToString("N"));
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

    [Fact] public async Task WhatItNoticedComesWithWhatItMadeAndOneTapForTheRest()
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

        var now = DateTimeOffset.UtcNow;
        var store = factory.Services.GetRequiredService<Store>();
        const string asked = "https://bsky.app/profile/sam.bsky.social/post/3kq";
        store.Setting("listening-v1", Wire.Pack(new ListeningLedger([
            new("m1", "AI marketing employee", "Bluesky", "", "Anyone using an AI marketing employee? How do you stop it posting without approval?", asked, now.AddHours(-5), now.AddHours(-4), "neutral"),
            new("m2", "AI marketing employee", "Google News", "AI employees are coming?", "A news story.", "https://news.example/story", now.AddHours(-3), now.AddHours(-2), "neutral"),
            new("m3", "AI marketing employee", "Bluesky", "", "We shipped our AI marketing employee today. Proud of the team.", "https://bsky.app/profile/x/post/9", now.AddHours(-2), now.AddHours(-1), "positive")],
            [], now, [])));
        const string pricing = "https://jasper.example/pricing";
        store.Setting("page-watch-v1", Wire.Pack(new PageWatchLedger([], [new(pricing, now.AddHours(-6), "prices", "Pro went from $49 to $69 a seat.")])));
        store.Setting("publishing-v1", Wire.Pack(new PublishingLedger("scope", [], [new("p1", "r1", 7, "d", "c1", "bluesky", "published", null, now.AddHours(-30), now.AddHours(-20),
            "https://bsky.app/profile/hirezero/post/1", null, "Owner", "at://x", "Four hours, eight cycles, nothing posted without us.", "Bluesky", new(12, 3, 2, null, null, 40, now, null))])));
        // The overnight shift already answered the price change with a note.
        factory.Services.GetRequiredService<EmployeeShifts>().RecordAnswer($"watch:{pricing}:{now.AddHours(-6):yyyy-MM-dd}", ["wiki:pricing-note"]);

        // The public question is a signal the shifts act on, answered on its own network under that post; news and announcements aren't questions.
        var signal = Assert.Single(factory.Services.GetRequiredService<MarketListening>().Signals(), item => item.Kind == "public_question");
        Assert.Equal((asked, "listen:question:m1"), (signal.MetricName, signal.Ref));
        Assert.Contains("destination " + asked, signal.Detail);

        var items = await Send(HttpMethod.Get, "/api/away");
        Assert.Equal(["question", "competitor", "results"], items.EnumerateArray().Select(item => item.GetProperty("kind").GetString()));
        Assert.Equal(("Draft a reply", "assign"), (items[0].GetProperty("action").GetProperty("label").GetString(), items[0].GetProperty("action").GetProperty("kind").GetString()));
        Assert.Equal(("Review the response", "wiki:pricing-note"), (items[1].GetProperty("action").GetProperty("label").GetString(), items[1].GetProperty("action").GetProperty("key").GetString()));
        Assert.Equal("Your Bluesky post brought in 12 likes, 3 reposts, 2 replies, 40 visits", items[2].GetProperty("title").GetString());

        // One tap: the reply is the next shift's task, and the question leaves the list.
        var after = await Send(HttpMethod.Post, "/api/away/" + Uri.EscapeDataString("listen:question:m1"), new { action = "assign" });
        Assert.Equal(["competitor", "results"], after.EnumerateArray().Select(item => item.GetProperty("kind").GetString()));
        var state = await Send(HttpMethod.Get, "/api/marketing/state");
        var task = Assert.Single(state.GetProperty("tasks").EnumerateArray(), item => item.GetProperty("title").GetString()!.StartsWith("Reply: A question on Bluesky", StringComparison.Ordinal));
        Assert.Contains(asked, task.GetProperty("next_action").GetString());

        // The morning brief opens with what it noticed.
        var brief = await factory.Services.GetRequiredService<WeeklyRhythm>().Write("brief", default);
        var body = factory.Services.GetRequiredService<CompanyWiki>().List().Single(page => page.Id == brief.WikiId).Body;
        Assert.True(body.IndexOf("## While you were away", StringComparison.Ordinal) is > 0 and var at && at < body.IndexOf("## The call", StringComparison.Ordinal), body);
        Assert.Contains("**Price change on jasper.example/pricing.**", body);
    }
}
