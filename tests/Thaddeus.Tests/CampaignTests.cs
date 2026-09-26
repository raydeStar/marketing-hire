using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

/// <summary>Named campaigns hold the work made for them, the shift says which one it serves, and their files live together in the Library.</summary>
public sealed class CampaignTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "campaigns-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++) { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }

    const string Plan = "# Launch-week plan: Sep 26–30\n\n**Goal:** Submit HireZero to the hackathon by Sep 28 and start qualified conversations.\n\n**Operating rule:** I draft.\n\n" +
        "## Sep 26 — Prepare\n\n- **Goal:** Prepare the assets.\n- **Channels:** Agent Index, hirezero.app, LinkedIn, X, Show HN, launch-updates email.\n\n## Sep 27 — Review\n\n- **Channels:** LinkedIn, OpenClaw Discord.\n";

    [Fact] public void APlanDocumentReadsAsACampaign()
    {
        var parsed = Campaigns.ParsePlan("Launch-week plan: Sep 26–30", Plan, new DateOnly(2026, 9, 26));
        Assert.Equal(("Launch week", "2026-09-26", "2026-09-30", "active"), (parsed.Name, parsed.Starts, parsed.Ends, parsed.Status));
        Assert.StartsWith("Submit HireZero to the hackathon", parsed.Goal);
        Assert.Equal(["Agent Index", "hirezero.app", "LinkedIn", "X", "Show HN", "launch-updates email", "OpenClaw Discord"], parsed.Channels!);
        // Without dates in the title, the day headings give them; a later start is planned.
        var later = Campaigns.ParsePlan("Holiday campaign plan", "## Dec 1 — Tease\n\nText.\n\n## Dec 12 — Launch\n\nText.", new DateOnly(2026, 11, 2));
        Assert.Equal(("Holiday", "2026-12-01", "2026-12-12", "planned"), (later.Name, later.Starts, later.Ends, later.Status));
    }

    [Fact] public void FilesMoveIntoTheirCampaignsFolder()
    {
        Assert.Equal("Campaigns/Launch week/Videos", Campaigns.FolderFor("Campaigns/Videos", "Launch week", []));
        Assert.Equal("Campaigns/Launch week/Posts", Campaigns.FolderFor("Campaigns/Drafts", "Launch week", []));
        Assert.Equal("Campaigns/Other/Videos", Campaigns.FolderFor("Campaigns/Launch week/Videos", "Other", ["Launch week", "Other"]));
        Assert.Equal("Campaigns/Drafts", Campaigns.FolderFor("Campaigns/Launch week/Posts", null, ["Launch week"]));
        // Research stays where it is unless it's the campaign's own plan.
        Assert.Null(Campaigns.FolderFor("Research/Shift notes", "Launch week", []));
        Assert.Equal("Campaigns/Launch week/Docs", Campaigns.FolderFor("Research/Shift notes", "Launch week", [], force: true));
    }

    sealed class CampaignRuntime : IShiftRuntime
    {
        public List<JsonElement> Packets { get; } = [];
        public string Name => "scripted";
        public bool Live => false;
        public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation)
        {
            var data = request.Data;
            Packets.Add(data.Clone());
            var reply = request.Stage switch
            {
                "prioritize" => JsonSerializer.Serialize(new
                {
                    priorities = data.GetProperty("queue").EnumerateArray().Select(task => new { title = task.GetProperty("title").GetString(), reason = "Assigned", deliverable = "document", taskId = task.GetProperty("id").GetString(),
                        campaign = data.GetProperty("campaigns").EnumerateArray().Select(item => item.GetProperty("id").GetString()).FirstOrDefault() }),
                    newTasks = new[] { new { title = "Follow up with the entrants", next_action = "Reply to comments.", priority = "normal", campaign = data.GetProperty("campaigns")[0].GetProperty("id").GetString() } }, note = "Launch week first."
                }),
                "create" => JsonSerializer.Serialize(new { deliverable = "document", title = "Why approval comes first", body = "A blog post in the campaign's voice, with a clear next step for founders.", kind = "hypothesis", folder = "Campaigns/Blog" }),
                "review" => JsonSerializer.Serialize(new { scores = new { strategy = 5, customer = 5, distinctive = 5, channel = 5, brand = 5, action = 5, claims = 5, shareable = 5 }, issues = Array.Empty<string>(), revised = (object?)null }),
                _ => JsonSerializer.Serialize(new { learnings = Array.Empty<string>(), nextShiftFocus = "", notebook = new { known = Array.Empty<string>(), decided = Array.Empty<string>(), openQuestions = Array.Empty<string>(), worked = Array.Empty<string>(), didNotWork = Array.Empty<string>(), resolved = Array.Empty<string>() } })
            };
            return Task.FromResult(new ShiftTurnResult(reply, 400));
        }
    }
    sealed class Loopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => { app.Use((context, proceed) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return proceed(); }); next(app); };
    }

    [Fact] public async Task ACampaignHoldsWhatTheShiftMadeForIt()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        var runtime = new CampaignRuntime();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host")); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:FixtureLedger", Path.Combine(root, "ledger"));
            builder.UseSetting("Marketing:FixtureRunwayScript", Path.Combine(directory!.FullName, "business", "agent", "hire", "bin", "runway.py"));
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => { services.AddSingleton<IShiftRuntime>(runtime); services.AddSingleton<IStartupFilter, Loopback>(); });
        });
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var owner = factory.Services.GetRequiredService<Security>().Issue(context, "Owner", true);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        async Task<(HttpStatusCode Status, JsonElement Body)> Call(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body ?? new { }) };
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, text.Length > 0 ? JsonDocument.Parse(text).RootElement.Clone() : default);
        }
        async Task<JsonElement> Send(HttpMethod method, string path, object? body = null)
        {
            var (status, result) = await Call(method, path, body);
            Assert.True((int)status < 300, path + " → " + (int)status + " " + result);
            return result;
        }

        var made = await Send(HttpMethod.Post, "/api/campaigns", new { expectedVersion = 0, name = "Launch week", goal = "Get the entry in and start conversations.", starts = "2026-09-26", ends = "2026-09-30", channels = new[] { "LinkedIn", "X", "LinkedIn" }, status = "active" });
        var campaign = made.GetProperty("campaign");
        var id = campaign.GetProperty("id").GetString()!;
        Assert.Equal(["LinkedIn", "X"], campaign.GetProperty("channels").EnumerateArray().Select(item => item.GetString()));
        var version = made.GetProperty("ledger").GetProperty("version").GetInt32();
        // One name per campaign, never a Library folder's name, and a stale edit is refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await Call(HttpMethod.Post, "/api/campaigns", new { expectedVersion = version, name = "launch WEEK" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Call(HttpMethod.Post, "/api/campaigns", new { expectedVersion = version, name = "Videos" })).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Call(HttpMethod.Post, "/api/campaigns", new { expectedVersion = version - 1, name = "Other" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Call(HttpMethod.Post, "/api/campaigns", new { expectedVersion = version, name = "Backwards", starts = "2026-10-02", ends = "2026-10-01" })).Status);

        // The owner files a task with the campaign; the shift sees it and plans against it.
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-blog", title = "Blog post for launch week", status = "ready", priority = "high", next_action = "Write it.", action_state = "agent_ready" });
        var taskId = (await Send(HttpMethod.Get, "/api/marketing/state")).GetProperty("tasks").EnumerateArray().Single(task => task.GetProperty("title").GetString() == "Blog post for launch week").GetProperty("id").GetString()!;
        await Send(HttpMethod.Post, "/api/campaigns/assign", new { expectedVersion = version, key = "task:" + taskId, campaignId = id });
        Assert.Equal(HttpStatusCode.BadRequest, (await Call(HttpMethod.Post, "/api/campaigns/assign", new { expectedVersion = version + 1, key = "file:../x", campaignId = id })).Status);

        await Send(HttpMethod.Post, "/api/shifts", new { requestId = "shift-c", hours = 8, turnBudget = 20 });
        var cycle = await Send(HttpMethod.Post, "/api/shifts/shift-c/cycle");
        var plan = runtime.Packets.First(packet => packet.TryGetProperty("queue", out _));
        Assert.Equal("Launch week", plan.GetProperty("campaigns")[0].GetProperty("name").GetString());
        Assert.Equal(id, plan.GetProperty("queue")[0].GetProperty("campaign").GetString());
        Assert.Equal("Launch week", runtime.Packets.First(packet => packet.TryGetProperty("sources", out _)).GetProperty("campaign").GetProperty("name").GetString());
        Assert.Contains("Filed with the campaign “Launch week”.", cycle.GetProperty("cycles")[0].GetProperty("stages")[2].GetProperty("summary").GetString());

        var ledger = await Send(HttpMethod.Get, "/api/campaigns");
        var items = ledger.GetProperty("items").EnumerateObject().Where(item => item.Value.GetString() == id).Select(item => item.Name).ToArray();
        Assert.Contains("task:" + taskId, items);
        var post = Assert.Single(items, key => key.StartsWith("wiki:", StringComparison.Ordinal));
        Assert.Equal(2, items.Count(key => key.StartsWith("task:", StringComparison.Ordinal)));   // the new follow-up task joined it too
        var library = factory.Services.GetRequiredService<WorkspaceLibrary>();
        Assert.Equal("Campaigns/Launch week/Blog", library.View("").Entries.Single(entry => entry.Key == post).Folder);

        // Renaming the campaign moves its folder.
        await Send(HttpMethod.Put, "/api/campaigns/" + id, new { expectedVersion = ledger.GetProperty("version").GetInt32(), name = "Hackathon launch", goal = "Get the entry in.", starts = "2026-09-26", ends = "2026-09-30", channels = new[] { "LinkedIn" }, status = "active" });
        Assert.Equal("Campaigns/Hackathon launch/Blog", library.View("").Entries.Single(entry => entry.Key == post).Folder);

        // A plan document becomes a campaign of its own, and the plan is filed with it.
        var page = factory.Services.GetRequiredService<CompanyWiki>().Save(new WikiChange(Guid.NewGuid().ToString("N"), null, 0, "company", "company", "Launch-week plan: Sep 26–30", Plan, "fact", "draft"), "Marketing employee (shift)");
        library.SaveEntry("wiki:" + page.Id, new LibraryEntryChange(library.View("").Version, "Research/Shift notes", []), "Marketing employee (shift)", "employee");
        ledger = await Send(HttpMethod.Get, "/api/campaigns");
        var fromPlan = (await Send(HttpMethod.Post, "/api/campaigns/from-plan", new { expectedVersion = ledger.GetProperty("version").GetInt32(), wikiId = page.Id })).GetProperty("campaign");
        Assert.Equal(("Launch week", page.Id), (fromPlan.GetProperty("name").GetString(), fromPlan.GetProperty("planWikiId").GetString()));
        Assert.Equal("Campaigns/Launch week/Docs", library.View("").Entries.Single(entry => entry.Key == "wiki:" + page.Id).Folder);

        // Taking the post out of its campaign puts it back with the other drafts.
        ledger = await Send(HttpMethod.Get, "/api/campaigns");
        await Send(HttpMethod.Post, "/api/campaigns/assign", new { expectedVersion = ledger.GetProperty("version").GetInt32(), key = post, campaignId = (string?)null });
        Assert.False((await Send(HttpMethod.Get, "/api/campaigns")).GetProperty("items").TryGetProperty(post, out _));
        Assert.Equal("Campaigns/Blog", library.View("").Entries.Single(entry => entry.Key == post).Folder);
    }
}
