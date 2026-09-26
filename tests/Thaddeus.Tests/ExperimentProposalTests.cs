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

/// <summary>The employee proposes experiments; nothing runs until the owner starts one.</summary>
public sealed class ExperimentProposalTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "experiment-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++) { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }

    sealed class ExperimentRuntime : IShiftRuntime
    {
        public List<JsonElement> Scorecards { get; } = [];
        public List<string> Plans { get; } = [];
        public string Name => "scripted";
        public bool Live => false;
        public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation)
        {
            var data = request.Data;
            var reply = request.Stage switch
            {
                "prioritize" => Plan(data) ?? JsonSerializer.Serialize(new { priorities = data.GetProperty("queue").EnumerateArray().Select(task => new { title = task.GetProperty("title").GetString(), reason = "Assigned", deliverable = "experiment", taskId = task.GetProperty("id").GetString() }), newTasks = Array.Empty<object>(), note = "Tests." }),
                "create" => Create(data),
                _ => JsonSerializer.Serialize(new { learnings = Array.Empty<string>(), nextShiftFocus = "", notebook = new { known = Array.Empty<string>(), decided = Array.Empty<string>(), openQuestions = Array.Empty<string>(), worked = Array.Empty<string>(), didNotWork = Array.Empty<string>(), resolved = Array.Empty<string>() } }),
            };
            return Task.FromResult(new ShiftTurnResult(reply, 500));
        }
        string? Plan(JsonElement data) { Plans.Add(JsonSerializer.Serialize(data, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })); return null; }
        string Create(JsonElement data)
        {
            Scorecards.Add(data.GetProperty("scorecard").Clone());
            var title = data.GetProperty("task").GetProperty("title").GetString()!;
            var metric = title.Contains("unknown") ? "Revenue" : "Signups";
            return JsonSerializer.Serialize(new { deliverable = "experiment", title = title.Contains("unknown") ? "Price test" : "Starter brief above the fold",
                body = JsonSerializer.Serialize(new { hypothesis = "If the starter brief is the first thing on the page, then signups will rise, because it is the only thing visitors can use today.", metric, days = 14, direction = "up", thresholdPercent = 15, change = "Move the starter section to the top of the home page.", ice = new { impact = 7, confidence = 5, ease = 9 } }) });
        }
    }
    sealed class Loopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => { app.Use((context, proceed) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return proceed(); }); next(app); };
    }

    [Fact] public async Task AProposedExperimentWaitsForTheOwnerToStartIt()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        var runtime = new ExperimentRuntime();
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

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var csv = "date,Signups\n" + string.Join("\n", Enumerable.Range(1, 10).Select(day => $"{today.AddDays(-day):yyyy-MM-dd},{10 + day % 3}"));
        await Send(HttpMethod.Post, "/api/scorecard/import", new { requestId = "import-1", csv, source = "Test export" });
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-exp", title = "Propose a test to lift signups", status = "ready", priority = "high", next_action = "One experiment.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-bad", title = "Propose a test on an unknown metric", status = "ready", priority = "normal", next_action = "One experiment.", action_state = "agent_ready" });
        // Search Console queries within reach of page one arrive as a signal the plan can act on.
        factory.Services.GetRequiredService<Store>().Setting("search-queries-v1", Thaddeus.Core.Wire.Pack(new SearchQueries(DateTimeOffset.UtcNow, "sc-domain:acme.test", "2026-08-01", "2026-08-28",
            [new SearchQuery("ai marketing employee", "https://acme.test/", 3, 800, 0.4, 9.6)])));
        await Send(HttpMethod.Post, "/api/shifts", new { requestId = "shift-exp", hours = 8, turnBudget = 10 });
        var shift = await Send(HttpMethod.Post, "/api/shifts/shift-exp/cycle");
        var summary = shift.GetProperty("cycles")[0].GetProperty("stages")[2].GetProperty("summary").GetString()!;
        Assert.Contains("Proposed an experiment on Signups (+15% in 14 days) for the owner to start.", summary);
        Assert.Contains("isn't one", summary);   // a metric that isn't on the scorecard is refused, not invented
        Assert.Equal("signups", runtime.Scorecards[0][0].GetProperty("key").GetString());
        Assert.Contains("1 search queries within reach of page one", runtime.Plans[0]);
        Assert.Contains("ai marketing employee\u201D at 9.6, 800 impressions, 0.4% CTR, on https://acme.test/", runtime.Plans[0]);

        // Proposed, not running: the measure stage leaves it alone and the task tells the owner where to decide.
        var scorecard = factory.Services.GetRequiredService<Scorecard>();
        var proposal = Assert.Single(scorecard.Ledger().Experiments);
        Assert.Equal(("proposed", "signups", "up", 15.0), (proposal.Status, proposal.Metric, proposal.Rule.Direction, proposal.Rule.ThresholdPercent));
        Assert.Contains("Change: Move the starter section to the top of the home page. (ICE: impact 7, confidence 5, ease 9)", proposal.Hypothesis);
        Assert.StartsWith("task:", Assert.Single(shift.GetProperty("decisions").EnumerateArray()).GetString());
        var state = await Send(HttpMethod.Get, "/api/marketing/state");
        Assert.StartsWith("Start or decline the proposed experiment", state.GetProperty("tasks").EnumerateArray().Single(item => item.GetProperty("title").GetString() == "Propose a test to lift signups").GetProperty("next_action").GetString());

        var started = await Send(HttpMethod.Post, $"/api/scorecard/experiments/{proposal.Id}/start");
        Assert.Equal(("running", today.ToString("yyyy-MM-dd"), today.AddDays(14).ToString("yyyy-MM-dd")), (started.GetProperty("status").GetString(), started.GetProperty("startDate").GetString(), started.GetProperty("reviewDate").GetString()));
        using (var late = await client.PostAsJsonAsync($"/api/scorecard/experiments/{proposal.Id}/decline", new { note = "x" })) Assert.NotEqual(HttpStatusCode.OK, late.StatusCode);

        var second = scorecard.AddExperiment(new ScoreExperimentRequest("p-2", "Another idea", "If we..., then...", "signups", today.ToString("yyyy-MM-dd"), today.AddDays(7).ToString("yyyy-MM-dd"), "up", 10), "Marketing employee (shift)", proposed: true);
        var declined = await Send(HttpMethod.Post, $"/api/scorecard/experiments/{second.Id}/decline", new { note = "Too early; we have no traffic yet." });
        Assert.Equal(("declined", "Declined by the owner: Too early; we have no traffic yet."), (declined.GetProperty("status").GetString(), declined.GetProperty("outcomeNote").GetString()));

        // Every decision lands in one log, with its reason: Company → Decision log.
        await Send(HttpMethod.Post, "/api/feedback", new { key = "draft:7", title = "LinkedIn draft #7", verdict = "approved", note = "Strong hook." });
        var log = factory.Services.GetRequiredService<DecisionLog>().Entries();
        Assert.Equal([("LinkedIn draft #7", "Approved", "Strong hook."), ("Experiment: Another idea", "Declined", "Too early; we have no traffic yet."), ("Experiment: Starter brief above the fold", "Started", $"Runs to {today.AddDays(14):yyyy-MM-dd}.")],
            log.Select(entry => (entry.What, entry.Decision, entry.Why)));
        var page = Assert.Single(factory.Services.GetRequiredService<CompanyWiki>().List(), item => item.Title == "Decision log");
        Assert.Contains("- **Declined**: Experiment: Another idea — “Too early; we have no traffic yet.”", page.Body);
        Assert.Contains(factory.Services.GetRequiredService<WorkspaceLibrary>().View("").Entries, entry => entry.Key == "wiki:" + page.Id && entry.Folder == "Company");

        // The log says who, not which browser session; a page written in an older layout is written again at start.
        var decisions = factory.Services.GetRequiredService<DecisionLog>();
        decisions.Record("Owner 5e58d289c69846eaacfd8781a3471cc9", "Blog draft #10", "Approved", "Test approval.");
        var wiki = factory.Services.GetRequiredService<CompanyWiki>();
        page = Assert.Single(wiki.List(), item => item.Title == "Decision log");
        Assert.DoesNotContain("5e58d289", page.Body); Assert.Contains("· Owner\n", page.Body);
        wiki.Save(new WikiChange(Guid.NewGuid().ToString("N"), page.Id, page.Version, page.Scope, page.ScopeId, page.Title, "| When | By |\n|---|---|\n| Sep 25 | Owner 5e58d289c69846eaacfd8781a3471cc9 |", "fact", "active"), "Decision log");
        Assert.True(decisions.Tidy());
        Assert.Contains("- **Approved**: Blog draft #10", Assert.Single(wiki.List(), item => item.Title == "Decision log").Body);
        Assert.False(decisions.Tidy());
    }
}
