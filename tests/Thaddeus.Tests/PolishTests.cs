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

/// <summary>With nothing new to make, a cycle brings a draft waiting for the owner up to an A in place: the better version replaces
/// the waiting one, its task points to it, and nothing new lands on the owner's list.</summary>
public sealed class PolishTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "polish-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++) { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }

    static object Scores(int all) => new { strategy = all, customer = all, distinctive = all, channel = all, brand = all, action = all, claims = all, shareable = all };

    sealed class PolishRuntime : IShiftRuntime
    {
        public int Reviews { get; private set; }
        public string Name => "scripted";
        public bool Live => false;
        public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation)
        {
            var data = request.Data;
            string reply;
            if (request.Stage == "prioritize")
                reply = JsonSerializer.Serialize(new { priorities = data.GetProperty("queue").EnumerateArray().Select(task => new { title = task.GetProperty("title").GetString(), reason = "Assigned", deliverable = "draft", taskId = task.GetProperty("id").GetString() }), newTasks = Array.Empty<object>(), note = "One post." });
            else if (request.Stage == "create")
                reply = JsonSerializer.Serialize(new { deliverable = "draft", title = "Launch post", channel = "LinkedIn", destination = "https://www.linkedin.com/feed/", body = "First version of the launch post, which ends without an ask.", rationale = "Launch." });
            else if (request.Stage == "review")
            {
                Reviews++;
                var body = data.GetProperty("deliverable").GetProperty("body").GetString()!;
                // The shift's own review leaves it at a B; the polish pass edits it, and the edit reviews at an A.
                reply = Reviews == 1 ? JsonSerializer.Serialize(new { scores = Scores(4), issues = new[] { "Ends without the call to action" }, revised = (object?)null })
                    : body.StartsWith("First") ? JsonSerializer.Serialize(new { scores = Scores(4), issues = new[] { "Ends without the call to action" }, revised = new { title = "Launch post", body = "Better version of the launch post. Try the free starter brief: https://acme.test/start" } })
                    : JsonSerializer.Serialize(new { scores = Scores(5), issues = Array.Empty<string>(), revised = (object?)null });
            }
            else
                reply = JsonSerializer.Serialize(new { learnings = Array.Empty<string>(), nextShiftFocus = "", notebook = new { known = Array.Empty<string>(), decided = Array.Empty<string>(), openQuestions = Array.Empty<string>(), worked = Array.Empty<string>(), didNotWork = Array.Empty<string>(), resolved = Array.Empty<string>() } });
            return Task.FromResult(new ShiftTurnResult(reply, 500));
        }
    }
    /// <summary>A long document, reviewed by edits to exact passages: one that matches is applied, one that doesn't is skipped.</summary>
    sealed class EditsRuntime : IShiftRuntime
    {
        public List<JsonElement> ReviewPackets { get; } = [];
        public string Name => "scripted";
        public bool Live => false;
        public static readonly string Long = "# Plan\n\n" + string.Join("\n\n", Enumerable.Range(1, 90).Select(n => $"Paragraph {n} says something plain about the plan."));
        public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation)
        {
            var data = request.Data;
            string reply;
            if (request.Stage == "prioritize")
                reply = JsonSerializer.Serialize(new { priorities = data.GetProperty("queue").EnumerateArray().Select(task => new { title = task.GetProperty("title").GetString(), reason = "Assigned", deliverable = "document", taskId = task.GetProperty("id").GetString() }), newTasks = Array.Empty<object>(), note = "The plan." });
            else if (request.Stage == "create")
                reply = JsonSerializer.Serialize(new { deliverable = "document", title = "Long plan", body = Long, kind = "hypothesis", folder = "Strategy" });
            else if (request.Stage == "review")
            {
                ReviewPackets.Add(data.Clone());
                var body = data.GetProperty("deliverable").GetProperty("body").GetString()!;
                reply = body.Contains("sharper")
                    ? JsonSerializer.Serialize(new { scores = Scores(5), issues = Array.Empty<string>(), revised = (object?)null })
                    : JsonSerializer.Serialize(new { scores = Scores(4), issues = new[] { "Paragraph 3 is vague" }, revised = (object?)null,
                        edits = new[] { new { find = "Paragraph 3 says something plain about the plan.", replace = "Paragraph 3 says something sharper, with a number." }, new { find = "Not in the text at all.", replace = "Ignored." } } });
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

    [Fact] public async Task LongWorkIsRevisedByEditsToExactPassages()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        var runtime = new EditsRuntime();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host")); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:FixtureLedger", Path.Combine(root, "ledger"));
            builder.UseSetting("Marketing:FixtureRunwayScript", Path.Combine(directory!.FullName, "business", "agent", "hire", "bin", "runway.py"));
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => { services.AddSingleton<IShiftRuntime>(runtime); services.AddSingleton<IStartupFilter, Loopback>(); });
        });
        var shifts = factory.Services.GetRequiredService<EmployeeShifts>();
        var marketing = factory.Services.GetRequiredService<MarketingBackend>();
        Assert.Null((await marketing.ShiftHire(JsonSerializer.Serialize(new { request_id = "t-long", title = "A long plan", status = "ready", priority = "high", next_action = "Write the plan.", action_state = "agent_ready" }), "task", "create", "--input-json", "-")).Error);
        var shift = shifts.Start(new ShiftStartRequest("shift-edits", 8, 60, 20), "Owner");
        await shifts.RunCycle(shift.Id, CancellationToken.None);
        Assert.Equal("edits", runtime.ReviewPackets[0].GetProperty("edit").GetString());
        var page = factory.Services.GetRequiredService<CompanyWiki>().List().Single(item => item.Title == "Long plan");
        Assert.Contains("Paragraph 3 says something sharper, with a number.", page.Body);
        Assert.DoesNotContain("Paragraph 3 says something plain", page.Body);
        Assert.Contains("Paragraph 89 says something plain about the plan.", page.Body);   // the rest is untouched
        Assert.Equal(2, runtime.ReviewPackets.Count);   // the edited version was reviewed, and met the bar
    }

    [Fact] public async Task AnIdleCycleBringsAWaitingDraftUpToAnAInPlace()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        var runtime = new PolishRuntime();
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

        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-post", title = "A launch post", status = "ready", priority = "high", next_action = "Write the launch post.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/shifts", new { requestId = "shift-polish", hours = 8, turnBudget = 20 });
        await Send(HttpMethod.Post, "/api/shifts/shift-polish/cycle");
        var first = (await Send(HttpMethod.Get, "/api/marketing/state")).GetProperty("drafts").EnumerateArray().Single();
        Assert.Equal("pending", first.GetProperty("status").GetString());

        // Nothing queued: the cycle polishes the waiting draft instead of idling.
        var shift = await Send(HttpMethod.Post, "/api/shifts/shift-polish/cycle");
        var create = shift.GetProperty("cycles")[1].GetProperty("stages").EnumerateArray().Single(stage => stage.GetProperty("stage").GetString() == "create");
        Assert.Equal("done", create.GetProperty("status").GetString());
        var id = first.GetProperty("id").GetInt32();
        Assert.Contains($"Draft #{id + 1} replaces #{id}.", create.GetProperty("summary").GetString());
        Assert.Contains("While it waits for you", create.GetProperty("summary").GetString());

        var state = await Send(HttpMethod.Get, "/api/marketing/state");
        var drafts = state.GetProperty("drafts").EnumerateArray().ToDictionary(item => item.GetProperty("id").GetInt32());
        Assert.Equal("withdrawn", drafts[id].GetProperty("status").GetString());
        Assert.Equal("pending", drafts[id + 1].GetProperty("status").GetString());
        Assert.StartsWith("Better version", drafts[id + 1].GetProperty("content").GetString());
        // Still one thing for the owner to decide, and its task points to the better version.
        Assert.Single(state.GetProperty("drafts").EnumerateArray(), item => item.GetProperty("status").GetString() == "pending");
        var task = state.GetProperty("tasks").EnumerateArray().Single(item => item.GetProperty("title").GetString() == "A launch post");
        Assert.Equal("needs_you", task.GetProperty("status").GetString());
        Assert.Contains($"draft #{id + 1}", task.GetProperty("next_action").GetString());

        // The next cycle closes nothing (the withdrawn draft isn't a rejection) and doesn't polish it again.
        var reviews = runtime.Reviews;
        shift = await Send(HttpMethod.Post, "/api/shifts/shift-polish/cycle");
        Assert.Equal(reviews, runtime.Reviews);
        Assert.Equal("needs_you", (await Send(HttpMethod.Get, "/api/marketing/state")).GetProperty("tasks").EnumerateArray().Single(item => item.GetProperty("title").GetString() == "A launch post").GetProperty("status").GetString());
    }
}
