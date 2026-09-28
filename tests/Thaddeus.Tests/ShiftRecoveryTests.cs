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

/// <summary>The live beta's failures reproduced without a provider call. Even fictional accountants keep their receipts.</summary>
public sealed class ShiftRecoveryTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "shift-recovery-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    HttpClient client = null!;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        client?.Dispose();
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++)
            try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); }
    }
    sealed class Loopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => { app.Use((context, proceed) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return proceed(); }); next(app); };
    }
    sealed class Runtime(string failure, int promptByteLimit = EmployeeShifts.PromptBytes) : IShiftRuntime
    {
        public List<ShiftTurnRequest> Calls { get; } = [];
        public string Name => "scripted";
        public bool Live => false;
        public int PromptByteLimit => promptByteLimit;
        public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation)
        {
            Calls.Add(request);
            var data = request.Data;
            var body = request.Stage == "review" ? data.GetProperty("deliverable").GetProperty("body").GetString()! : "";
            var reviews = Calls.Count(call => call.Stage == "review");
            if (request.Stage == "review" && (failure == "always" || failure == "retry" && reviews == 1 || failure == "after-edit" && reviews > 1))
                return Task.FromResult(new ShiftTurnResult("{\"scores\":{\"strategy\":4", 700));
            var fixedWork = body.StartsWith("Improved", StringComparison.Ordinal);
            var reply = request.Stage switch
            {
                "prioritize" => JsonSerializer.Serialize(new { priorities = data.GetProperty("queue").EnumerateArray().Select(task => new { title = task.GetProperty("title").GetString(), reason = "Assigned", deliverable = "document", taskId = task.GetProperty("id").GetString() }), newTasks = Array.Empty<object>() }),
                "create" => JsonSerializer.Serialize(new { deliverable = "document", title = "Campaign launch plan", body = "Original campaign plan for owners. Goal: qualified conversations. Channels: LinkedIn. Decide whether to test this angle.", kind = "hypothesis", folder = "Campaigns/Docs" }),
                "review" => JsonSerializer.Serialize(new { scores = new[] { "strategy", "customer", "distinctive", "channel", "brand", "action", "claims", "shareable" }.ToDictionary(key => key, _ => fixedWork ? 5 : 3),
                    evidence = new[] { "strategy", "customer", "distinctive", "channel", "brand", "action", "claims", "shareable" }.ToDictionary(key => key, _ => body),
                    asks = Array.Empty<object>(), issues = fixedWork ? Array.Empty<string>() : new[] { "Make the owner decision explicit" } }),
                "revise" => JsonSerializer.Serialize(new { revised = new { body = "Improved campaign plan for owners. Goal: qualified conversations. Channels: LinkedIn. Decide whether to test this angle." } }),
                _ => JsonSerializer.Serialize(new { learnings = Array.Empty<string>(), nextShiftFocus = "", notebook = new { known = Array.Empty<string>(), decided = Array.Empty<string>(), openQuestions = Array.Empty<string>(), worked = Array.Empty<string>(), didNotWork = Array.Empty<string>(), resolved = Array.Empty<string>() } })
            };
            return Task.FromResult(new ShiftTurnResult(reply, 700));
        }
    }
    void Open(Runtime runtime)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host")); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:FixtureLedger", Path.Combine(root, "ledger"));
            builder.UseSetting("Marketing:FixtureRunwayScript", Path.Combine(directory!.FullName, "business", "agent", "hire", "bin", "runway.py"));
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => { services.AddSingleton<IShiftRuntime>(runtime); services.AddSingleton<IStartupFilter, Loopback>(); });
        });
        client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var owner = factory.Services.GetRequiredService<Security>().Issue(context, "Owner", true);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
    }
    async Task<JsonElement> Send(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body ?? new { }) };
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, path + " → " + (int)response.StatusCode + " " + text);
        return text.Length > 0 ? JsonDocument.Parse(text).RootElement.Clone() : default;
    }

    [Fact] public async Task FullLengthFeedbackReachesTheTaskAndTheRedraftUncut()
    {
        var runtime = new Runtime("none"); Open(runtime);
        var wiki = factory!.Services.GetRequiredService<CompanyWiki>();
        var page = wiki.Save(new WikiChange(Guid.NewGuid().ToString("N"), null, 0, "company", "company", new string('T', 150), "Existing original.", "hypothesis", "draft"), "Owner");
        var feedback = "Keep the source. " + new string('x', 1000 - "Keep the source. ".Length);
        var saved = await Send(HttpMethod.Post, "/api/redrafts", new { key = "wiki:" + page.Id, feedback });
        var task = (await Send(HttpMethod.Get, "/api/marketing/state")).GetProperty("tasks").EnumerateArray().Single();
        Assert.Equal(feedback, task.GetProperty("next_action").GetString());
        Assert.Equal(feedback, Assert.Single(factory.Services.GetRequiredService<Redrafts>().All()).Feedback);
        Assert.True(saved.GetProperty("queued").GetBoolean()); Assert.Empty(runtime.Calls);
        using var rejected = await client.PostAsJsonAsync("/api/redrafts", new { key = "wiki:" + page.Id, feedback = feedback + "x" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Theory]
    [InlineData(EmployeeShifts.PromptBytes)]
    [InlineData(EmployeeShifts.PlowPromptBytes)]
    public async Task ACampaignPlanKeepsTwoOwnerSourcesWithinTheRealCreatePromptBudget(int limit)
    {
        var runtime = new Runtime("none", limit); Open(runtime);
        var shifts = factory!.Services.GetRequiredService<EmployeeShifts>();
        var evidence = "A directory listing is not a partnership. " + new string('e', 1400);
        shifts.ReadSite = (url, _, _) => url.Contains("blocked.example", StringComparison.Ordinal)
            ? throw new HttpRequestException("It could not be read (403).") : Task.FromResult((url, "Public source", evidence));
        shifts.Research = (_, _) => throw new InvalidOperationException("No news needed.");
        var initial = await Send(HttpMethod.Get, "/api/marketing/state");
        var brief = string.Concat(Enumerable.Repeat("A useful marketing employee prepares work for review. ", 9));
        await Send(HttpMethod.Put, "/api/marketing/profile", new { requestId = "brief", version = initial.GetProperty("profile").GetProperty("version").GetInt32(),
            display_name = "Chip", product_summary = brief, audience = brief, goals = brief, voice = "Plain and specific", channels = "LinkedIn", guardrails = brief, claims = brief, examples = brief });
        await Send(HttpMethod.Put, "/api/objectives", new { expectedVersion = 0, content = new { objectives = Array.Empty<object>(), competitors = Array.Empty<object>(), nonGoals = Array.Empty<string>(), currentFocus = brief, researchSites = new[] { "directory.example", "university.example", "blocked.example" } } });
        var assignment = "Read https://directory.example/members and https://university.example/program and https://blocked.example/. Prepare a campaign plan. ".PadRight(1000, 'a');
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "task", title = "Campaign launch plan", status = "ready", priority = "high", next_action = assignment, action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/shifts", new { requestId = "fit", hours = 1, turnBudget = 12 });
        await Send(HttpMethod.Post, "/api/shifts/fit/cycle");
        var create = Assert.Single(runtime.Calls, call => call.Stage == "create");
        Assert.Equal(assignment, create.Data.GetProperty("task").GetProperty("next_action").GetString());
        Assert.Equal(2, create.Data.GetProperty("sources").GetArrayLength());
        Assert.All(create.Data.GetProperty("sources").EnumerateArray(), source => Assert.Equal(evidence[..1400], source.GetProperty("evidenceText").GetString()));
        Assert.Contains("blocked.example", create.Data.GetProperty("sourceGaps")[0].GetString());
        Assert.Contains("403", create.Data.GetProperty("sourceGaps")[0].GetString());
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(create.Prompt)) <= limit);
        Assert.DoesNotContain("A video deliverable's body", create.Prompt);
        if (limit == EmployeeShifts.PlowPromptBytes)
        {
            Assert.Equal(brief.Trim(), create.Data.GetProperty("brief").GetProperty("guardrails").GetString());
            Assert.Equal(brief.Trim(), create.Data.GetProperty("brief").GetProperty("product_summary").GetString());
        }
    }

    [Fact] public void PlowContextCanKeepAFullDocumentAndUnicodeEvidenceBeyondTheOldCeiling()
    {
        var body = string.Concat(Enumerable.Repeat("Day 1: test the offer; keep the owner in charge. ", 160));
        var evidence = string.Concat(Enumerable.Repeat("Quoted evidence: “Résumé — café”. ", 180));
        var packet = JsonSerializer.SerializeToElement(new { deliverable = new { body }, sources = new[] { new { evidenceText = evidence } }, feedback = "Shorten the introduction; preserve every citation." });
        var fitted = EmployeeShifts.Fit(packet, "Review the full deliverable. ", ["body", "evidenceText", "feedback"], EmployeeShifts.PlowPromptBytes);
        var bytes = System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize("Review the full deliverable. " + fitted.GetRawText()));
        Assert.InRange(bytes, EmployeeShifts.PromptBytes + 1, EmployeeShifts.PlowPromptBytes);
        Assert.Equal(body, fitted.GetProperty("deliverable").GetProperty("body").GetString());
        Assert.Equal(evidence, fitted.GetProperty("sources")[0].GetProperty("evidenceText").GetString());
    }

    [Theory]
    [InlineData("retry", "Improved")]
    [InlineData("always", "Original")]
    [InlineData("after-edit", "Original")]
    public async Task ReviewsAreBoundedAccountedForAndAttachedToTheVersionActuallyAssessed(string failure, string expected)
    {
        var runtime = new Runtime(failure); Open(runtime);
        var services = factory!.Services;
        var shifts = services.GetRequiredService<EmployeeShifts>();
        var reads = new List<string>();
        shifts.ReadSite = (url, _, _) => { reads.Add(url); return Task.FromResult((url, "Local directory", "Named local business listed at https://business.example/ with a publicly listed website.")); };
        shifts.Research = (_, _) => throw new InvalidOperationException("This assignment needs no news lookup.");
        await Send(HttpMethod.Put, "/api/objectives", new { expectedVersion = 0, content = new { objectives = Array.Empty<object>(), competitors = Array.Empty<object>(), nonGoals = Array.Empty<string>(), currentFocus = "", researchSites = new[] { "directory.example" } } });
        var task = await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "task", title = "Campaign launch plan", status = "ready", priority = "high", next_action = "https://directory.example/members", action_state = "agent_ready" });
        var campaigns = services.GetRequiredService<Campaigns>();
        var campaign = campaigns.Save(null, new CampaignChange(0, "Existing beta campaign", "Qualified conversations", "2026-09-28", "2026-10-05", ["LinkedIn"], "active", null), "Owner");
        campaigns.Assign("task:" + task.GetProperty("id").GetString(), campaign.Id, "Owner");
        await Send(HttpMethod.Post, "/api/shifts", new { requestId = "recovery", hours = 4, turnBudget = 30 });
        var result = await Send(HttpMethod.Post, "/api/shifts/recovery/cycle");
        var page = Assert.Single(services.GetRequiredService<CompanyWiki>().List(), page => page.Title == "Campaign launch plan");
        Assert.StartsWith(expected, page.Body);
        Assert.Equal(runtime.Calls.Count * 700, result.GetProperty("tokensUsed").GetInt32());
        Assert.Equal(runtime.Calls.Count, result.GetProperty("turnsUsed").GetInt32());
        Assert.Equal(runtime.Calls.Count(call => call.Stage is "create" or "review" or "revise") * 700,
            result.GetProperty("cycles")[0].GetProperty("stages").EnumerateArray().Single(stage => stage.GetProperty("stage").GetString() == "create").GetProperty("tokens").GetInt32());
        Assert.Equal(["https://directory.example/members"], reads);
        var create = Assert.Single(runtime.Calls, call => call.Stage == "create");
        Assert.Contains("Named local business", create.Data.GetProperty("sources")[0].GetProperty("evidenceText").GetString());
        Assert.Contains("internal work dates", create.Prompt);
        Assert.Single(campaigns.View().Campaigns); Assert.Equal(page.Id, campaigns.Find(campaign.Id)!.PlanWikiId);
        Assert.Equal(page.Id, campaigns.FromPlan(new CampaignFromPlan(campaigns.View().Version, page.Id), "Owner").PlanWikiId);
        var other = services.GetRequiredService<CompanyWiki>().Save(new WikiChange(Guid.NewGuid().ToString("N"), null, 0, "company", "company", "Different plan", "Keep the chosen plan.", "hypothesis", "draft"), "Owner");
        Assert.False(campaigns.AttachPlan(campaign.Id, other.Id, "Owner"));
        if (failure == "always")
        {
            Assert.Contains("Self-review unavailable", page.Body);
            var piece = Assert.Single((await services.GetRequiredService<CampaignPieces>().View(campaign.Id)).Pieces, piece => piece.Key == "wiki:" + page.Id);
            Assert.Null(piece.Grade); Assert.Contains("Self-review unavailable; owner review required", piece.Blockers);
            Assert.DoesNotContain(runtime.Calls, call => call.Stage == "revise");
        }
        else Assert.Contains(runtime.Calls, call => call.Stage == "revise");
        Assert.Contains(runtime.Calls, call => call.Stage == "review" && call.Data.GetProperty("retry").ValueKind == JsonValueKind.String);
    }
}
