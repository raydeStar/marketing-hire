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
        public static readonly string Long = "# Plan\n\n" + string.Join("\n\n", Enumerable.Range(1, 140).Select(n => $"Paragraph {n} says something plain about the plan."));
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

    /// <summary>A reviewer that inflates: top marks with nothing quoted, then a send-back whose second note it misses at first.</summary>
    sealed class NotesRuntime : IShiftRuntime
    {
        public List<JsonElement> ReviewPackets { get; } = [];
        public string Name => "scripted";
        public bool Live => false;
        static object All(int score) => new { strategy = score, customer = score, distinctive = score, channel = score, brand = score, action = score, claims = score, shareable = score };
        static Dictionary<string, string> Quoted(string passage) => new[] { "strategy", "customer", "distinctive", "channel", "brand", "action", "claims", "shareable" }.ToDictionary(key => key, _ => passage);
        public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation)
        {
            var data = request.Data;
            string reply;
            if (request.Stage == "prioritize")
                reply = JsonSerializer.Serialize(new { priorities = data.GetProperty("queue").EnumerateArray().Select(task => new { title = task.GetProperty("title").GetString(), reason = "Assigned", deliverable = "draft", taskId = task.GetProperty("id").GetString() }), newTasks = Array.Empty<object>(), note = "The post." });
            else if (request.Stage == "create")
                reply = JsonSerializer.Serialize(new { deliverable = "draft", title = "Launch post", channel = "LinkedIn", destination = "https://www.linkedin.com/feed/", rationale = "Launch.",
                    body = data.GetProperty("redraft").ValueKind == JsonValueKind.Object ? "Revised post. Say who it's for: founders with no marketer." : "First post, with no audience named at all." });
            else if (request.Stage == "review")
            {
                ReviewPackets.Add(data.Clone());
                var body = data.GetProperty("deliverable").GetProperty("body").GetString()!;
                var asks = data.GetProperty("ownerAsks").ValueKind == JsonValueKind.Array ? data.GetProperty("ownerAsks").EnumerateArray().Select(item => item.GetString()!).ToArray() : [];
                reply = body.StartsWith("First")
                    ? JsonSerializer.Serialize(new { scores = All(5), issues = Array.Empty<string>(), revised = (object?)null })   // nothing quoted
                    : !body.Contains("beta")
                    ? JsonSerializer.Serialize(new { scores = All(5), evidence = Quoted("Say who it's for: founders with no marketer"), issues = new[] { "No link" },
                        asks = new object[] { new { ask = asks[0], met = true, quote = "Say who it's for: founders with no marketer" }, new { ask = asks[1], met = false, quote = "" } },
                        revised = new { title = "Launch post", body = body + " Sign up for the beta: https://acme.test/beta" } })
                    : JsonSerializer.Serialize(new { scores = All(5), evidence = Quoted("Say who it's for: founders with no marketer"), issues = Array.Empty<string>(),
                        asks = new object[] { new { ask = asks[0], met = true, quote = "Say who it's for: founders with no marketer" }, new { ask = asks[1], met = true, quote = "Sign up for the beta: https://acme.test/beta" } },
                        revised = (object?)null });
            }
            else
                reply = JsonSerializer.Serialize(new { learnings = Array.Empty<string>(), nextShiftFocus = "", notebook = new { known = Array.Empty<string>(), decided = Array.Empty<string>(), openQuestions = Array.Empty<string>(), worked = Array.Empty<string>(), didNotWork = Array.Empty<string>(), resolved = Array.Empty<string>() } });
            return Task.FromResult(new ShiftTurnResult(reply, 500));
        }
    }

    [Fact] public async Task TopMarksNeedTheirPassageAndASendBackIsDoneOnlyWhenEveryNoteIs()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        var runtime = new NotesRuntime();
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
        Assert.Null((await marketing.ShiftHire(JsonSerializer.Serialize(new { request_id = "t-notes", title = "A launch post", status = "ready", priority = "high", next_action = "Write the launch post.", action_state = "agent_ready" }), "task", "create", "--input-json", "-")).Error);
        var shift = shifts.Start(new ShiftStartRequest("shift-notes", 8, 60, 30), "Owner");
        var first = await shifts.RunCycle(shift.Id, CancellationToken.None);
        var made = first.Cycles[0].Stages.Single(stage => stage.Stage == "create").Summary;
        // Straight 5s with nothing quoted are 4s: a B, not an A.
        Assert.Contains("Marketing rubric B", made); Assert.Contains("8 top score(s) lowered for want of a quoted passage.", made);
        // The live feed told it as it happened: the step, the review pass with its grade, and each stage.
        var feed = factory.Services.GetRequiredService<ShiftEvents>().After(shift.Id, 0);
        Assert.Contains(feed, item => item.Kind == "think" && item.Text == "Choosing what matters most today");
        Assert.Contains(feed, item => item.Kind == "think" && item.Text.StartsWith("Writing “A launch post”"));
        Assert.Contains(feed, item => item.Kind == "review" && item.Text.StartsWith("“Launch post”, pass 1: B"));
        Assert.Contains(feed, item => item.Kind == "stage" && item.Text.StartsWith("Sense:"));
        Assert.Equal(feed.Select(item => item.N), feed.Select(item => item.N).Order());
        Assert.Empty(factory.Services.GetRequiredService<ShiftEvents>().After(shift.Id, feed[^1].N));

        await shifts.RequestRedraft(new RedraftAsk("draft:1", "1) Say who it's for. 2) End on the beta link."), "Owner");
        var second = await shifts.RunCycle(shift.Id, CancellationToken.None);
        var redone = second.Cycles[1].Stages.Single(stage => stage.Stage == "create").Summary;
        // The first pass missed the second note, so the loop kept going until both were done, each with its passage.
        Assert.Equal(["Say who it's for.", "End on the beta link."], runtime.ReviewPackets[1].GetProperty("ownerAsks").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("Your notes: all 2 done ✓.", redone);
        Assert.Contains("Marketing rubric A", redone);
        var draft = (await marketing.ShiftHire(null, "snapshot")).Value!.Value.GetProperty("drafts").EnumerateArray().First(item => item.GetProperty("content").GetString()!.StartsWith("Revised"));
        Assert.EndsWith("Sign up for the beta: https://acme.test/beta", draft.GetProperty("content").GetString());
        // Coming back to it: what it finished, the note it acted on, what needs the owner, and what it does next.
        var back = await factory.Services.GetRequiredService<Continuity>().View();
        Assert.Contains(back.Finished, item => item.StartsWith("LinkedIn draft #"));
        Assert.Contains(back.ChangedMind, item => item.StartsWith("After your note on “LinkedIn draft #1”"));
        Assert.True(back.NeedsYou >= 1);
        Assert.Matches(@"^(Working now\.|On shift; the next cycle is in \d+ (minutes|hours)\.)", back.Next);   // relative, never a host-zone clock time
    }

    /// <summary>A series where the reviewer shows one ask done in the LinkedIn post only, and another in both.</summary>
    sealed class SeriesAsksRuntime : IShiftRuntime
    {
        public string Name => "scripted";
        public bool Live => false;
        static Dictionary<string, string> Quoted(string passage) => new[] { "strategy", "customer", "distinctive", "channel", "brand", "action", "claims", "shareable" }.ToDictionary(key => key, _ => passage);
        public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation)
        {
            var data = request.Data;
            string reply;
            if (request.Stage == "prioritize")
                reply = JsonSerializer.Serialize(new { priorities = data.GetProperty("queue").EnumerateArray().Select(task => new { title = task.GetProperty("title").GetString(), reason = "Assigned", deliverable = "draft", taskId = task.GetProperty("id").GetString() }), newTasks = Array.Empty<object>(), note = "The posts." });
            else if (request.Stage == "create")
                reply = JsonSerializer.Serialize(new { deliverable = "draft", title = "Open source posts", channel = "LinkedIn", destination = "https://www.linkedin.com/feed/", rationale = "Launch.", body = "ignored",
                    drafts = new[] { new { channel = "LinkedIn", destination = "https://www.linkedin.com/feed/", body = "Open source, and it runs on your own computer today. Sign up for the beta: https://acme.test/beta", rationale = "One." },
                                     new { channel = "X", destination = "https://x.com/home", body = "It asks before anything goes out, every time. Sign up for the beta: https://acme.test/beta", rationale = "Two." } } });
            else if (request.Stage == "review")
            {
                var asks = data.GetProperty("ownerAsks").EnumerateArray().Select(item => item.GetString()!).ToArray();
                reply = JsonSerializer.Serialize(new { scores = new { strategy = 5, customer = 5, distinctive = 5, channel = 5, brand = 5, action = 5, claims = 5, shareable = 5 }, evidence = Quoted("Sign up for the beta: https://acme.test/beta"),
                    issues = Array.Empty<string>(), revised = (object?)null,
                    asks = new object[] { new { ask = asks[0], met = true, quote = "Open source, and it runs on your own computer today" },   // only the LinkedIn post says it
                                          new { ask = asks[1], met = true, quotes = new[] { "Sign up for the beta: https://acme.test/beta" } } } });
            }
            else
                reply = JsonSerializer.Serialize(new { learnings = Array.Empty<string>(), nextShiftFocus = "", notebook = new { known = Array.Empty<string>(), decided = Array.Empty<string>(), openQuestions = Array.Empty<string>(), worked = Array.Empty<string>(), didNotWork = Array.Empty<string>(), resolved = Array.Empty<string>() } });
            return Task.FromResult(new ShiftTurnResult(reply, 500));
        }
    }

    [Fact] public async Task AnAssignmentIsCheckedAskByAskAndInASeriesEveryPostMustDoIt()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host")); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:FixtureLedger", Path.Combine(root, "ledger"));
            builder.UseSetting("Marketing:FixtureRunwayScript", Path.Combine(directory!.FullName, "business", "agent", "hire", "bin", "runway.py"));
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => { services.AddSingleton<IShiftRuntime>(new SeriesAsksRuntime()); services.AddSingleton<IStartupFilter, Loopback>(); });
        });
        var shifts = factory.Services.GetRequiredService<EmployeeShifts>();
        var marketing = factory.Services.GetRequiredService<MarketingBackend>();
        Assert.Null((await marketing.ShiftHire(JsonSerializer.Serialize(new { request_id = "t-os", title = "Open source posts", status = "ready", priority = "high",
            next_action = "Announce that HireZero is open source and runs on your own computer today. Each post ends on the sign-up link.", action_state = "agent_ready" }), "task", "create", "--input-json", "-")).Error);
        var shift = shifts.Start(new ShiftStartRequest("shift-asks", 8, 60, 30), "Owner");
        var done = await shifts.RunCycle(shift.Id, CancellationToken.None);
        var made = done.Cycles[0].Stages.Single(stage => stage.Stage == "create").Summary;
        // The X post never says it, so the first ask is still to do, whatever the reviewer claimed; the second is shown in both posts.
        Assert.Contains("The assignment: 1 of 2 done ✗ (still to do: Announce that HireZero is open source and runs on your own computer today.)", made);
        Assert.Contains(factory.Services.GetRequiredService<EmployeeMemory>().Quality(), entry => entry.Unmet?.Any(item => item.StartsWith("asked: Announce that HireZero is open source")) == true);
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
