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

/// <summary>What's asked for is worked on right away, shift or not: a short run that does only the queue and closes itself.</summary>
public sealed class RequestsRunTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "requests-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++) { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }

    [Fact] public async Task AQueuedTaskStartsARunThatDoesOnlyTheQueueAndClosesItself()
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
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var owner = factory.Services.GetRequiredService<Security>().Issue(context, "Owner", true);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        var shifts = factory.Services.GetRequiredService<EmployeeShifts>();

        // Nothing queued: nothing starts, and no model turn is spent looking.
        Assert.Null(await shifts.StartRequests(CancellationToken.None));

        // A task lands in the queue: a run starts on it, off shift.
        using (var made = await client.PostAsJsonAsync("/api/marketing/tasks", new { requestId = "t-ask", title = "Homepage headline options", status = "ready", priority = "high", next_action = "Write three headline options.", action_state = "agent_ready" }))
            Assert.True(made.IsSuccessStatusCode, await made.Content.ReadAsStringAsync());
        var run = await shifts.StartRequests(CancellationToken.None);
        Assert.NotNull(run);
        Assert.True(run!.Requests);
        Assert.Equal(EmployeeShifts.RequestsAuthor, run.StartedBy);
        Assert.Equal(EmployeeShifts.RequestsTokens, run.TokenBudget);
        // A shift can't start over it; the owner is told why.
        using (var over = await client.PostAsJsonAsync("/api/shifts", new { requestId = "shift-over", hours = 1 }))
        {
            Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
            Assert.Contains("working on what you asked", await over.Content.ReadAsStringAsync());
        }

        // It works the task, then finds the queue empty and closes itself, without planning its own work or writing a report.
        var worked = await shifts.RunCycle(run.Id, CancellationToken.None);
        Assert.Contains(worked.Cycles[0].Stages, stage => stage.Stage == "create" && stage.Status == "done");
        Assert.DoesNotContain(worked.Handled, item => item.StartsWith("selfplan:", StringComparison.Ordinal));
        for (var tick = 0; tick < 4 && shifts.Find(run.Id)!.Status == "running"; tick++) await shifts.Tick(CancellationToken.None);
        var closed = shifts.Find(run.Id)!;
        Assert.Equal(("completed", EmployeeShifts.RequestsDone), (closed.Status, closed.StopReason));
        Assert.Null(closed.ReportWikiId);
        Assert.DoesNotContain(closed.Cycles.SelectMany(cycle => cycle.Stages), stage => stage.Stage == "prioritize" && stage.Summary.Contains("chose", StringComparison.OrdinalIgnoreCase) && closed.Handled.Any(item => item.StartsWith("selfplan:", StringComparison.Ordinal)));

        // Nothing left: nothing new starts.
        Assert.Null(await shifts.StartRequests(CancellationToken.None));
    }

    /// <summary>A model route whose input allowance is too small for the work: the packet never fits.</summary>
    sealed class TinyRuntime : IShiftRuntime
    {
        public int Turns { get; private set; }
        public string Name => "scripted";
        public bool Live => false;
        public int PromptByteLimit => 3000;
        public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation)
        {
            Turns++;
            var data = request.Data;
            var reply = request.Stage == "prioritize"
                ? JsonSerializer.Serialize(new { priorities = data.GetProperty("queue").EnumerateArray().Select(task => new { title = task.GetProperty("title").GetString(), reason = "Assigned", deliverable = "document", taskId = task.GetProperty("id").GetString() }), newTasks = Array.Empty<object>(), note = "One piece." })
                : "{}";
            return Task.FromResult(new ShiftTurnResult(reply, 100));
        }
    }

    [Fact] public async Task WorkThatCantBeDoneGoesToTheOwnerInsteadOfBeingTriedForever()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        var runtime = new TinyRuntime();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host"));
            builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:FixtureLedger", Path.Combine(root, "ledger"));
            builder.UseSetting("Marketing:FixtureRunwayScript", Path.Combine(directory!.FullName, "business", "agent", "hire", "bin", "runway.py"));
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => services.AddSingleton<IShiftRuntime>(runtime));
        });
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var owner = factory.Services.GetRequiredService<Security>().Issue(context, "Owner", true);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        var shifts = factory.Services.GetRequiredService<EmployeeShifts>();
        using (var made = await client.PostAsJsonAsync("/api/marketing/tasks", new { requestId = "t-big", title = "Rewrite the whole site", status = "ready", priority = "high", next_action = "Rewrite every page of the site.", action_state = "agent_ready" }))
            Assert.True(made.IsSuccessStatusCode, await made.Content.ReadAsStringAsync());
        var run = (await shifts.StartRequests(CancellationToken.None))!;

        // The work doesn't fit: no model request is sent for it, it goes back to the owner saying why, and the run closes.
        await shifts.RunCycle(run.Id, CancellationToken.None);
        for (var tick = 0; tick < 4 && shifts.Find(run.Id)!.Status == "running"; tick++) await shifts.Tick(CancellationToken.None);
        var after = shifts.Find(run.Id)!;
        Assert.True(after.Status == "completed" && after.StopReason == EmployeeShifts.RequestsDone, string.Join(" | ", after.Cycles.SelectMany(cycle => cycle.Stages).Select(stage => $"{stage.Stage}:{stage.Status}:{stage.Summary}")));
        var state = JsonDocument.Parse(await client.GetStringAsync("/api/marketing/state")).RootElement;
        var task = state.GetProperty("tasks").EnumerateArray().Single(item => item.GetProperty("title").GetString() == "Rewrite the whole site");
        Assert.Equal("needs_you", task.GetProperty("status").GetString());
        Assert.StartsWith("Too much to take in one step", task.GetProperty("blocker").GetString());
        Assert.Equal(0, runtime.Turns);   // nothing to plan for what was asked, and the work itself was never sent
        // Nothing is ready, so no new run starts on it.
        Assert.Null(await shifts.StartRequests(CancellationToken.None));
    }
}
