using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class EmployeeShiftTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "employee-shifts-" + Guid.NewGuid().ToString("N"));
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

    static string Day(int offset) => DateTime.UtcNow.Date.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string SeriesCsv(int days, Func<int, double> signups, Func<int, double> cost)
    {
        var csv = new StringBuilder("date,Signups,Cost per signup\n");
        for (var i = days - 1; i >= 0; i--) csv.Append(CultureInfo.InvariantCulture, $"{Day(-i)},{signups(i)},{cost(i)}\n");
        return csv.ToString();
    }

    [Fact] public void ScorecardReadsWideAndLongCsvAndFlagsOnlyMaterialMoves()
    {
        var (wide, layout) = Scorecard.ParseCsv("Date,Sessions,\"Sign-ups\"\n2026-09-01,\"1,200\",40\n09/02/2026,1100,38\n");
        Assert.Equal("wide", layout); Assert.Equal(4, wide.Count); Assert.Contains(wide, row => row.Metric == "sign_ups" && row.Value == 40);
        var (tall, longLayout) = Scorecard.ParseCsv("date,metric,value\n2026-09-01,MRR,$5000\n2026-09-02,MRR,5100\n");
        Assert.Equal("long", longLayout); Assert.Equal(2, tall.Count);
        Assert.Throws<ArgumentException>(() => Scorecard.ParseCsv("just a header\n"));

        var steady = Enumerable.Range(0, 20).Select(i => new ScoreObservation("signups", Day(-19 + i), 100 + (i % 3), "t", DateTimeOffset.UtcNow)).ToArray();
        var ledger = new ScoreLedger(1, [new ScoreMetric("signups", "Signups", "", "up", true, "t")], steady, [], []);
        Assert.Empty(Scorecard.Anomalies(ledger));
        var dropped = ledger with { Observations = [.. steady[..^1], steady[^1] with { Value = 55 }] };
        var anomaly = Assert.Single(Scorecard.Anomalies(dropped));
        Assert.True(anomaly.ChangePercent < -40); Assert.False(anomaly.Good); Assert.Equal("high", anomaly.Severity);

        var experiment = new ScoreExperiment("e", "Shorter form", "Fewer fields lift signups", "signups", Day(-6), Day(0), new ScoreRule("up", 10), "running", null, null, "o", DateTimeOffset.UtcNow);
        var lifted = ledger with { Observations = steady.Select(item => string.CompareOrdinal(item.Date, Day(-6)) >= 0 ? item with { Value = item.Value * 1.3 } : item).ToArray() };
        var measured = Scorecard.Measure(lifted, experiment);
        Assert.True(measured.ChangePercent > 25);
        Assert.Equal("scale", Scorecard.Rule(experiment, measured).Outcome);
        Assert.Equal("stop", Scorecard.Rule(experiment, Scorecard.Measure(ledger with { Observations = steady.Select(item => string.CompareOrdinal(item.Date, Day(-6)) >= 0 ? item with { Value = 80 } : item).ToArray() }, experiment)).Outcome);
    }

    [Fact] public void CampaignQaBlocksWhatAPersonWouldCatchBeforePosting()
    {
        var bad = CampaignQa.Check("X", "http://x.com/post", "Guaranteed results! See [LINK] http://example.com " + new string('a', 300));
        Assert.Equal("blocked", bad.Status);
        Assert.Equal("fail", bad.Checks.Single(check => check.Id == "destination").Result);
        Assert.Equal("fail", bad.Checks.Single(check => check.Id == "links-secure").Result);
        Assert.Equal("fail", bad.Checks.Single(check => check.Id == "length").Result);
        Assert.Equal("fail", bad.Checks.Single(check => check.Id == "placeholders").Result);
        Assert.Equal("warn", bad.Checks.Single(check => check.Id == "claims").Result);
        var good = CampaignQa.Check("LinkedIn", "https://www.linkedin.com/feed/", "Founders keep telling us follow-through is the hard part. Learn more: https://example.com/?utm_source=linkedin&utm_campaign=q3");
        Assert.Equal("ready", good.Status);
    }

    [Fact] public async Task AShiftRunsTheWholeLoopThroughTheHostWithoutPostingAnything()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        Assert.NotNull(directory);
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host"));
            builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
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
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        string[] Stages(JsonElement shift, int cycle) => shift.GetProperty("cycles")[cycle].GetProperty("stages").EnumerateArray()
            .Select(stage => stage.GetProperty("stage").GetString() + ":" + stage.GetProperty("status").GetString()).ToArray();

        // Signups fell sharply yesterday; cost per signup is steady.
        var imported = await Send(HttpMethod.Post, "/api/scorecard/import", new { requestId = "import-1", csv = SeriesCsv(21, i => i == 0 ? 48 : 100 + i % 4, _ => 12), source = "Test export" });
        Assert.Equal(2, imported.GetProperty("metrics").GetInt32());
        Assert.Contains(imported.GetProperty("scorecard").GetProperty("anomalies").EnumerateArray(), item => item.GetProperty("metric").GetString() == "signups");
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "task-1", title = "Draft a LinkedIn post about onboarding", status = "ready", priority = "normal",
            next_action = "One post, one call to action.", action_state = "agent_ready" });

        var shift = await Send(HttpMethod.Post, "/api/shifts", new { requestId = "shift-1", hours = 8, cycleMinutes = 60, turnBudget = 20 });
        Assert.Equal("running", shift.GetProperty("status").GetString());
        Assert.Equal("scripted", shift.GetProperty("runtime").GetString());
        using (var second = await client.PostAsJsonAsync("/api/shifts", new { requestId = "shift-2", hours = 8 }))
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // Cycle 1: the anomaly becomes an analysis document and the assigned task becomes a draft for approval.
        shift = await Send(HttpMethod.Post, "/api/shifts/shift-1/cycle", new { });
        Assert.Equal(EmployeeShifts.Stages, Stages(shift, 0).Select(item => item.Split(':')[0]));
        Assert.Contains("sense:done", Stages(shift, 0)); Assert.Contains("prioritize:done", Stages(shift, 0)); Assert.Contains("create:done", Stages(shift, 0));
        var created = shift.GetProperty("created").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.Contains(created, key => key.StartsWith("wiki:")); Assert.Contains(created, key => key.StartsWith("draft:"));
        var state = await Send(HttpMethod.Get, "/api/marketing/state");
        var draft = state.GetProperty("drafts").EnumerateArray().Single();
        Assert.Equal("pending", draft.GetProperty("status").GetString());
        var library = await Send(HttpMethod.Get, "/api/workspace-library");
        Assert.Contains(library.GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("folder").GetString() == "Research/Analyses");

        // A second cycle with nothing new spends no model turns.
        var turns = shift.GetProperty("turnsUsed").GetInt32();
        shift = await Send(HttpMethod.Post, "/api/shifts/shift-1/cycle", new { });
        Assert.Equal(turns, shift.GetProperty("turnsUsed").GetInt32());
        Assert.Contains("prioritize:skipped", Stages(shift, 1));

        // The owner approves; the next cycle runs the launch checklist and still posts nothing.
        await Send(HttpMethod.Post, $"/api/marketing/drafts/{draft.GetProperty("id").GetInt32()}/decision", new { requestId = "decide-1", decision = "approved",
            revision = draft.GetProperty("revision").GetInt32(), digest = draft.GetProperty("digest").GetString() });
        // An experiment reaches its review date with a clear lift.
        await Send(HttpMethod.Post, "/api/scorecard/import", new { requestId = "import-2", csv = "date,metric,value\n" + string.Join("\n", Enumerable.Range(0, 14).Select(i => $"{Day(-13 + i)},Trial starts,{(i >= 7 ? 130 : 100)}")) });
        await Send(HttpMethod.Post, "/api/scorecard/experiments", new { requestId = "exp-1", title = "Shorter signup form", hypothesis = "Fewer fields lift trial starts",
            metric = "trial_starts", startDate = Day(-6), reviewDate = Day(0), direction = "up", thresholdPercent = 10 });
        shift = await Send(HttpMethod.Post, "/api/shifts/shift-1/cycle", new { });
        Assert.True(Stages(shift, 2).Contains("launch:done") && Stages(shift, 2).Contains("decide:done"), shift.GetProperty("cycles")[2].GetRawText()); Assert.Contains("measure:done", Stages(shift, 2)); Assert.Contains("decide:done", Stages(shift, 2));
        state = await Send(HttpMethod.Get, "/api/marketing/state");
        Assert.Equal("approved", state.GetProperty("drafts")[0].GetProperty("status").GetString());
        Assert.Contains(state.GetProperty("tasks").EnumerateArray(), task => task.GetProperty("title").GetString() == "Decide: Shorter signup form" && task.GetProperty("status").GetString() == "needs_you");
        var wiki = await Send(HttpMethod.Get, "/api/company-wiki");
        Assert.Contains(wiki.EnumerateArray(), page => page.GetProperty("title").GetString()!.StartsWith("Launch checklist:"));

        // The owner's decisions close the loop on the next cycle: the approved draft's task finishes, the decided experiment's task closes.
        await Send(HttpMethod.Post, "/api/scorecard/experiments/exp-1/decision", new { outcome = "scale" });
        shift = await Send(HttpMethod.Post, "/api/shifts/shift-1/cycle", new { });
        state = await Send(HttpMethod.Get, "/api/marketing/state");
        Assert.Equal("done", state.GetProperty("tasks").EnumerateArray().Single(task => task.GetProperty("title").GetString() == "Draft a LinkedIn post about onboarding").GetProperty("status").GetString());
        Assert.Equal("done", state.GetProperty("tasks").EnumerateArray().Single(task => task.GetProperty("title").GetString() == "Decide: Shorter signup form").GetProperty("status").GetString());

        // Stopping writes the shift report into the Library.
        shift = await Send(HttpMethod.Post, "/api/shifts/shift-1/stop", new { });
        Assert.Equal("stopped", shift.GetProperty("status").GetString());
        var reportId = shift.GetProperty("reportWikiId").GetString();
        wiki = await Send(HttpMethod.Get, "/api/company-wiki");
        var report = wiki.EnumerateArray().Single(page => page.GetProperty("id").GetString() == reportId);
        Assert.Contains("## Cycle log", report.GetProperty("body").GetString());
        library = await Send(HttpMethod.Get, "/api/workspace-library");
        Assert.Contains(library.GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("key").GetString() == "wiki:" + reportId && entry.GetProperty("folder").GetString() == "Shift reports");

        // Only the owner starts shifts.
        var teammateContext = new DefaultHttpContext();
        var teammate = factory.Services.GetRequiredService<Security>().Issue(teammateContext, "Teammate", false, true);
        await Send(HttpMethod.Put, "/api/team/roles/" + teammate.PrincipalId, new { role = "manager" });
        var other = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        other.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        other.DefaultRequestHeaders.Add("Cookie", teammateContext.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        other.DefaultRequestHeaders.Add("X-CSRF", teammate.Csrf);
        using (var denied = await other.PostAsJsonAsync("/api/shifts", new { requestId = "shift-3", hours = 8 })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var read = await other.GetAsync("/api/shifts")) Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }
}
