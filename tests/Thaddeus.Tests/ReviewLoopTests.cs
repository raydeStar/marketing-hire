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

/// <summary>The self-review works toward the bar over several passes, keeps the best version, and remembers where the employee is weakest.</summary>
public sealed class ReviewLoopTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "review-loop-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++) { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }

    static object Scores(int all, int action) => new { strategy = all, customer = all, distinctive = all, channel = all, brand = all, action, claims = all, shareable = all };

    sealed class LoopRuntime : IShiftRuntime
    {
        public List<JsonElement> CreatePackets { get; } = [];
        public int Reviews { get; private set; }
        public string Name => "scripted";
        public bool Live => false;
        public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation)
        {
            var data = request.Data;
            string reply;
            if (request.Stage == "prioritize")
                reply = JsonSerializer.Serialize(new { priorities = data.GetProperty("queue").EnumerateArray().Select(task => new { title = task.GetProperty("title").GetString(), reason = "Assigned", deliverable = "document", taskId = task.GetProperty("id").GetString() }), newTasks = Array.Empty<object>(), note = "Two pieces." });
            else if (request.Stage == "create")
            {
                CreatePackets.Add(data.Clone());
                var title = data.GetProperty("task").GetProperty("title").GetString()!;
                reply = JsonSerializer.Serialize(new { deliverable = "document", title, body = "First draft of " + title + ", written plainly.", kind = "hypothesis", folder = "Research" });
            }
            else if (request.Stage == "review")
            {
                Reviews++;
                var body = data.GetProperty("deliverable").GetProperty("body").GetString()!;
                var title = data.GetProperty("deliverable").GetProperty("title").GetString()!;
                // "Climbs": 3.0, then the revision earns 4.5. "Regresses": 3.5, then the rewrite scores 2.5 and is dropped.
                reply = title == "Climbs"
                    ? body.StartsWith("First") ? JsonSerializer.Serialize(new { scores = Scores(3, 3), issues = new[] { "No clear next step" }, revised = new { title, body = "Second draft of Climbs, with one clear next step." } })
                      : JsonSerializer.Serialize(new { scores = Scores(5, 1), issues = Array.Empty<string>(), revised = (object?)null })
                    : body.StartsWith("First") ? JsonSerializer.Serialize(new { scores = Scores(4, 1), issues = new[] { "Weak call to action" }, revised = new { title, body = "Second draft of Regresses, rewritten loudly." } })
                      : JsonSerializer.Serialize(new { scores = Scores(3, 1), issues = new[] { "Worse" }, revised = new { title, body = "Third draft of Regresses." } });
            }
            else
                reply = JsonSerializer.Serialize(new { learnings = Array.Empty<string>(), nextShiftFocus = "", notebook = new { known = Array.Empty<string>(), decided = Array.Empty<string>(), openQuestions = Array.Empty<string>(), worked = Array.Empty<string>(), didNotWork = Array.Empty<string>(), resolved = Array.Empty<string>() } });
            return Task.FromResult(new ShiftTurnResult(reply, 500));
        }
    }
    sealed class Loopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => { app.Use((context, proceed) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return proceed(); }); next(app); };
    }

    [Fact] public async Task TheReviewClimbsTowardTheBarKeepsTheBestVersionAndRemembersWeakSpots()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        var runtime = new LoopRuntime();
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
        async Task<JsonElement> Send(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body ?? new { }) };
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, path + " → " + (int)response.StatusCode + " " + text);
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-climb", title = "Climbs", status = "ready", priority = "high", next_action = "Write it.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-regress", title = "Regresses", status = "ready", priority = "normal", next_action = "Write it.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/shifts", new { requestId = "shift-loop", hours = 8, turnBudget = 20 });
        var shift = await Send(HttpMethod.Post, "/api/shifts/shift-loop/cycle");
        var summary = shift.GetProperty("cycles")[0].GetProperty("stages")[2].GetProperty("summary").GetString()!;
        Assert.Contains("Climbs: Self-review 3.0 → 4.5/5 over 2 passes, revised.", summary);
        Assert.Contains("Regresses: Self-review 3.6/5, revised; a later rewrite scored lower and was dropped: Weak call to action.", summary);
        Assert.Equal(4, runtime.Reviews);

        var wiki = factory.Services.GetRequiredService<CompanyWiki>().List();
        Assert.StartsWith("Second draft of Climbs", Assert.Single(wiki, page => page.Title == "Climbs").Body);
        Assert.StartsWith("First draft of Regresses", Assert.Single(wiki, page => page.Title == "Regresses").Body);   // the lower-scoring rewrite went

        // The quality record: both pieces, their passes, and the weakest item (action) named for the next work.
        var memory = factory.Services.GetRequiredService<EmployeeMemory>();
        Assert.Equal([("Climbs", 2, 3.0), ("Regresses", 1, 3.62)], memory.Quality().Select(entry => (entry.Title, entry.Passes, entry.First)).OrderBy(entry => entry.Title));   // the two tasks may run in either order
        var quality = JsonSerializer.SerializeToElement(memory.QualitySummary());
        Assert.Equal("action", quality.GetProperty("weakest")[0].GetProperty("item").GetString());
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-next", title = "Next piece", status = "ready", priority = "normal", next_action = "Write it.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/shifts/shift-loop/cycle");
        Assert.Equal("action", runtime.CreatePackets[^1].GetProperty("memory").GetProperty("quality").GetProperty("weakest")[0].GetProperty("item").GetString());
        var usage = await Send(HttpMethod.Get, "/api/employee/usage");
        Assert.True(usage.GetProperty("quality").GetProperty("entries").GetArrayLength() >= 2);

        // The Library tidies itself: a newer version of the employee's own draft archives the older one, pointing at it.
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-card-1", title = "Competitive battlecard for Jasper and Lindy", status = "ready", priority = "normal", next_action = "Write it.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/shifts/shift-loop/cycle");
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-card-2", title = "Competitive battlecard v2 for Jasper and Lindy with prices", status = "ready", priority = "normal", next_action = "Write it.", action_state = "agent_ready" });
        var later = await Send(HttpMethod.Post, "/api/shifts/shift-loop/cycle");
        Assert.Contains("It replaces “Competitive battlecard for Jasper and Lindy”, archived.", later.GetProperty("cycles")[later.GetProperty("cycles").GetArrayLength() - 1].GetProperty("stages")[2].GetProperty("summary").GetString());
        var pages = factory.Services.GetRequiredService<CompanyWiki>().List();
        var old = Assert.Single(pages, page => page.Title == "Competitive battlecard for Jasper and Lindy");
        Assert.Equal("archived", old.Status);
        Assert.StartsWith("_Replaced by a newer version: “Competitive battlecard v2 for Jasper and Lindy with prices”", old.Body);
        Assert.Equal("draft", Assert.Single(pages, page => page.Title == "Competitive battlecard v2 for Jasper and Lindy with prices").Status);
        // An unrelated draft stays.
        Assert.Equal("draft", Assert.Single(pages, page => page.Title == "Climbs").Status);
    }
}
