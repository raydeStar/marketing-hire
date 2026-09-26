using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

/// <summary>Today leads with the one opportunity the employee prepared, then at most three decisions, and the rest under Later.</summary>
public sealed class TodayBoardTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "today-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++) { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }

    [Fact] public async Task TodayLeadsWithThePreparedOpportunityThenThreeDecisionsAndTheRestLater()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host")); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:FixtureLedger", Path.Combine(root, "ledger"));
            builder.UseSetting("Marketing:FixtureRunwayScript", Path.Combine(directory!.FullName, "business", "agent", "hire", "bin", "runway.py"));
            builder.UseSetting("Marketing:ShiftPump", "off");
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
        var marketing = factory.Services.GetRequiredService<MarketingBackend>();
        async Task<string> Draft(int n)
        {
            var added = await marketing.ShiftHire(null, "draft", "add", "--channel", "LinkedIn", "--destination", "https://www.linkedin.com/feed/", "--content", $"Post number {n} about approval first.", "--rationale", "Test.", "--rules-url", "UNVERIFIED");
            Assert.Null(added.Error);
            return added.Value!.Value.GetProperty("draft").GetRawText();
        }
        var lead = await Draft(1);
        foreach (var n in Enumerable.Range(2, 5)) await Draft(n);
        var initial = await Send(HttpMethod.Get, "/api/marketing/state");
        await Send(HttpMethod.Put, "/api/marketing/profile", new { requestId = "today-brief", version = initial.GetProperty("profile").GetProperty("version").GetInt32(),
            display_name = "Marketing", product_summary = "An AI marketing employee.", audience = "Founders", goals = "Beta sign-ups.", voice = "Plain", channels = "LinkedIn", guardrails = "Draft only", claims = "Approval first", examples = "" });

        // Nothing prepared yet: no opportunity, three drafts today, the rest later.
        var plain = await Send(HttpMethod.Get, "/api/today");
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("opportunity").ValueKind);
        Assert.Equal(3, plain.GetProperty("today").GetArrayLength());
        Assert.Equal(3, plain.GetProperty("later").GetArrayLength());

        // A recommendation built on draft #1: it leads, and draft #1 isn't listed again.
        var reply = JsonSerializer.SerializeToElement(new { title = "Lead with approval first", rationale = "It's the one proof rivals can't match.",
            recommendation = new { whyNow = "The beta campaign starts Monday.", choice = "Open the week with the approval post.", nextStep = "Review the post.", uncertainty = "No results yet." } });
        var prepared = factory.Services.GetRequiredService<EmployeeExperience>().Capture("shift-1", true, reply, default, [$"draft:{lead} LinkedIn draft #{lead}"], [], null)!;
        var today = await Send(HttpMethod.Get, "/api/today");
        var opportunity = today.GetProperty("opportunity");
        Assert.Equal(("Lead with approval first", prepared.Id), (opportunity.GetProperty("headline").GetString(), opportunity.GetProperty("id").GetString()));
        Assert.StartsWith("The beta campaign starts Monday.", opportunity.GetProperty("why").GetString());
        Assert.Equal(("draft:" + lead, "draft", $"LinkedIn draft #{lead}"), (opportunity.GetProperty("prepared")[0].GetProperty("key").GetString(), opportunity.GetProperty("prepared")[0].GetProperty("kind").GetString(), opportunity.GetProperty("prepared")[0].GetProperty("title").GetString()));
        Assert.Equal(["review", "change", "park"], opportunity.GetProperty("decisions").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        Assert.True(opportunity.GetProperty("decisions")[0].GetProperty("primary").GetBoolean());
        var listed = today.GetProperty("today").EnumerateArray().Concat(today.GetProperty("later").EnumerateArray()).Select(item => item.GetProperty("id").GetString()).ToArray();
        Assert.DoesNotContain("draft:" + lead, listed);
        Assert.Equal(5, listed.Length);

        // Changing direction needs a note, queues the employee's next task, and sets the recommendation aside.
        using (var bare = await client.PostAsJsonAsync($"/api/today/{prepared.Id}/decision", new { decision = "change" })) Assert.Equal(HttpStatusCode.BadRequest, bare.StatusCode);
        var changed = await Send(HttpMethod.Post, $"/api/today/{prepared.Id}/decision", new { decision = "change", note = "Lead with the receipts instead." });
        Assert.Equal(JsonValueKind.Null, changed.GetProperty("opportunity").ValueKind);
        var task = (await Send(HttpMethod.Get, "/api/marketing/state")).GetProperty("tasks").EnumerateArray().Single(item => item.GetProperty("title").GetString() == "Change direction: Lead with approval first");
        Assert.Equal(("ready", "agent_ready"), (task.GetProperty("status").GetString(), task.GetProperty("action_state").GetString()));
        Assert.Contains("Lead with the receipts instead.", task.GetProperty("next_action").GetString());
        Assert.Contains("draft:" + lead, changed.GetProperty("today").EnumerateArray().Concat(changed.GetProperty("later").EnumerateArray()).Select(item => item.GetProperty("id").GetString()));

        // A campaign's pieces: each with the week it serves, its channel, its claims and their sources, and the campaign's angle.
        var campaigns = factory.Services.GetRequiredService<Campaigns>();
        var launch = campaigns.Save(null, new CampaignChange(campaigns.View().Version, "Beta sign-ups", "Sign-ups", "2026-09-28", "2026-10-23", ["LinkedIn"], "planned", null), "Owner");
        campaigns.Assign("draft:" + lead, launch.Id, "Owner");
        campaigns.Assign("pagecopy:0123456789abcdef", launch.Id, "Owner");   // page copy can be part of a campaign
        factory.Services.GetRequiredService<CampaignPieces>().Record(["draft:" + lead], JsonSerializer.SerializeToElement(new { channel = "LinkedIn",
            piece = new { week = "2026-10-01", claims = new[] { "Every draft waits for approval [1]", "Runs on your own computer" } } }),
            [new ResearchSource("https://hirezero.app/", "HireZero", "", null, DateTimeOffset.UtcNow, "Site")]);
        var package = await Send(HttpMethod.Get, $"/api/campaigns/{launch.Id}/pieces");
        var piece = package.GetProperty("pieces").EnumerateArray().Single(item => item.GetProperty("key").GetString() == "draft:" + lead);
        Assert.Equal(("2026-09-28", "LinkedIn"), (piece.GetProperty("week").GetString(), piece.GetProperty("channel").GetString()));   // the Monday of its week
        Assert.Equal(("Every draft waits for approval", "https://hirezero.app/"), (piece.GetProperty("claims")[0].GetProperty("text").GetString(), piece.GetProperty("claims")[0].GetProperty("url").GetString()));
        Assert.Equal(JsonValueKind.Null, piece.GetProperty("claims")[1].GetProperty("url").ValueKind);
        Assert.Equal("Page", package.GetProperty("pieces").EnumerateArray().Single(item => item.GetProperty("key").GetString() == "pagecopy:0123456789abcdef").GetProperty("channel").GetString());
    }
}
