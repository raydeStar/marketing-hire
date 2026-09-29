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
        public List<JsonElement> ReviewPackets { get; } = [];
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
                // "Cut": the first answer stops mid-JSON, as one cut off at the output limit does; asked again, it answers whole.
                if (title == "Cut" && !data.TryGetProperty("retry", out _)) return Task.FromResult(new ShiftTurnResult("{\"deliverable\":\"document\",\"title\":\"Cut\",\"body\":\"An answer that stops at the out", 500));
                reply = JsonSerializer.Serialize(new { deliverable = "document", title, body = "First draft of " + title + (title is "Fits" or "Holds" or "Stuck" ? ", written at length, with far more words than the assignment allows for it." : ", written plainly."), kind = "hypothesis", folder = "Research" });
            }
            else if (request.Stage == "review")
            {
                Reviews++; ReviewPackets.Add(data.Clone());
                var body = data.GetProperty("deliverable").GetProperty("body").GetString()!;
                var title = data.GetProperty("deliverable").GetProperty("title").GetString()!;
                // "Climbs": 3.0, then the revision earns 4.5. "Regresses": 3.5, then the rewrite scores 2.5 and is dropped.
                // "Fits": too long for its assignment; the rewrite that fits scores lower and still stands, because it does what was asked.
                // "Holds": graded high with no fix while it's still too long; asked for the fix, the next pass returns one.
                // "Stuck": graded high, and never brought within its assignment however often it's asked.
                if (title == "Stuck") reply = JsonSerializer.Serialize(new { scores = Scores(5, 4), issues = Array.Empty<string>(), revised = (object?)null });
                else if (title == "Holds")
                    reply = data.TryGetProperty("mustRevise", out var must) && must.ValueKind == JsonValueKind.String
                        ? JsonSerializer.Serialize(new { scores = Scores(5, 4), issues = new[] { "Too long" }, revised = new { title, body = "Holds now, in under ten words." } })
                        : JsonSerializer.Serialize(new { scores = Scores(5, 4), issues = Array.Empty<string>(), revised = (object?)null });
                else reply = title == "Fits"
                    // Its first review wraps the JSON in a sentence, as models sometimes do: the object is still the answer.
                    ? body.StartsWith("First") ? "Here is my review: " + JsonSerializer.Serialize(new { scores = Scores(4, 4), issues = new[] { "Too long" }, revised = new { title, body = "Fits now, in a sentence of ten words or fewer." } }) + " Done."
                      : JsonSerializer.Serialize(new { scores = Scores(3, 3), issues = new[] { "Plainer" }, revised = (object?)null })
                    : title == "Climbs"
                    ? body.StartsWith("First") ? JsonSerializer.Serialize(new { scores = Scores(3, 3), issues = new[] { "No clear next step" }, revised = new { title, body = "Second draft of Climbs, with one clear next step." } })
                      : JsonSerializer.Serialize(new { scores = Scores(5, 1), evidence = new[] { "strategy", "customer", "distinctive", "channel", "brand", "claims", "shareable" }.ToDictionary(key => key, _ => "Second draft of Climbs, with one clear"), issues = Array.Empty<string>(), revised = (object?)null })
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

    [Fact] public async Task ARewriteThatDoesWhatWasAskedBeatsANicerGradeThatDoesnt()
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
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-fits", title = "Fits", status = "ready", priority = "high", next_action = "Write it in under 10 words.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-cut", title = "Cut", status = "ready", priority = "normal", next_action = "Write it.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/shifts", new { requestId = "shift-fits", hours = 8, turnBudget = 20 });
        var shift = await Send(HttpMethod.Post, "/api/shifts/shift-fits/cycle");
        var summary = shift.GetProperty("cycles")[0].GetProperty("stages")[2].GetProperty("summary").GetString()!;
        Assert.True(summary.Contains("Fits: Marketing rubric B → C over 2 passes"), summary);
        var fits = summary[summary.IndexOf("Fits:", StringComparison.Ordinal)..] is var rest && rest.IndexOf("Cut:", StringComparison.Ordinal) is var cut and > 0 ? rest[..cut] : summary[summary.IndexOf("Fits:", StringComparison.Ordinal)..];
        Assert.DoesNotContain("scored lower and was dropped", fits);
        Assert.Contains("under 10 words ✓", summary);
        // A cut-off answer is asked for once more, and the piece is made.
        Assert.Contains("The answer wasn't valid JSON; asked again, shorter.", summary);
        Assert.Contains("Cut: Marketing rubric", summary);
    }

    [Fact] public async Task AHighGradeWithTheAssignmentUnmetIsAskedForItsFix()
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
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-holds", title = "Holds", status = "ready", priority = "high", next_action = "Write it in under 10 words.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/shifts", new { requestId = "shift-holds", hours = 8, turnBudget = 20 });
        var shift = await Send(HttpMethod.Post, "/api/shifts/shift-holds/cycle");
        var summary = shift.GetProperty("cycles")[0].GetProperty("stages")[2].GetProperty("summary").GetString()!;
        // The live week of posts was graded A with its links and a copied line still there, and kept as written.
        Assert.Contains(runtime.ReviewPackets, packet => packet.TryGetProperty("mustRevise", out var must) && must.ValueKind == JsonValueKind.String);
        Assert.Contains("under 10 words ✓", summary);
        Assert.DoesNotContain("kept as written", summary);
        var page = factory.Services.GetRequiredService<CompanyWiki>().List().Single(item => item.Title == "Holds");
        Assert.StartsWith("Holds now, in under ten words.", page.Body);
    }

    [Fact] public async Task UnfinishedWorkGoesBackOnceBeforeTheOwnerSeesIt()
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
        var shifts = factory.Services.GetRequiredService<EmployeeShifts>();
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-stuck", title = "Stuck", status = "ready", priority = "high", next_action = "Write it in under 10 words.", action_state = "agent_ready" });
        var run = (await shifts.StartRequests(CancellationToken.None))!;

        // Made, and short of its assignment: it isn't the owner's yet. Chat and the cockpit say it's being finished, and the run stays open for it.
        await shifts.RunCycle(run.Id, CancellationToken.None);
        var key = "wiki:" + factory.Services.GetRequiredService<CompanyWiki>().List().Single(item => item.Title == "Stuck").Id;
        Assert.True(shifts.Finishing(key));
        Assert.Contains(key, shifts.FinishingKeys());
        Assert.False(shifts.TriedFinishing(key));

        // The next check-in goes back to it, once, in place; then it's the owner's, saying it went back and what's still missing.
        var back = await shifts.RunCycle(run.Id, CancellationToken.None);
        Assert.Contains(back.Cycles[^1].Stages, stage => stage.Stage == "create" && stage.Status == "done");
        Assert.False(shifts.Finishing(key));
        Assert.True(shifts.TriedFinishing(key));
        for (var tick = 0; tick < 4 && shifts.Find(run.Id)!.Status == "running"; tick++) await shifts.Tick(CancellationToken.None);
        Assert.Equal("completed", shifts.Find(run.Id)!.Status);
        var creates = shifts.Find(run.Id)!.Cycles.SelectMany(cycle => cycle.Stages).Count(stage => stage.Stage == "create" && stage.Status == "done");
        Assert.Equal(2, creates);   // made once, gone back to once; not again
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
        await Send(HttpMethod.Put, "/api/company-wiki", new { requestId = "stories", id = (string?)null, version = 0, scope = "company", scopeId = "company", title = "Stories: true stories to tell",
            body = "## Why we started\n\nWe lost a launch because nobody had time to write about it.", kind = "fact", status = "active" });
        await Send(HttpMethod.Post, "/api/shifts", new { requestId = "shift-loop", hours = 8, turnBudget = 120 });
        var shift = await Send(HttpMethod.Post, "/api/shifts/shift-loop/cycle");
        var summary = shift.GetProperty("cycles")[0].GetProperty("stages")[2].GetProperty("summary").GetString()!;
        Assert.Contains("Climbs: Marketing rubric C → A over 2 passes (Strategy A, Audience insight A, Distinctive A, Channel fit A, Brand voice A, Call to action F, Proof A, Shareability A), revised.", summary);
        Assert.Contains("Regresses: Marketing rubric B (Strategy B, Audience insight B, Distinctive B, Channel fit B, Brand voice B, Call to action F, Proof B, Shareability B), revised; a later rewrite scored lower and was dropped: Weak call to action.", summary);
        Assert.Equal(4, runtime.Reviews);
        // Writer and reviewer aim at the same A: the standard for the kind of work, the rubric's levels, and the last pass's issues to check.
        Assert.Equal(QualityStandards.For("document")!.Length, runtime.CreatePackets[0].GetProperty("standard").GetArrayLength());
        // The owner's true stories reach the writer, and the reviewer sees the same voice.
        Assert.Contains("nobody had time to write about it", runtime.CreatePackets[0].GetProperty("voice").GetProperty("stories").GetString());
        Assert.Equal(JsonValueKind.Object, runtime.ReviewPackets[0].GetProperty("voice").ValueKind);
        Assert.Contains("distinctive 5:", runtime.ReviewPackets[0].GetProperty("levels").GetString());
        Assert.Equal(JsonValueKind.Null, runtime.ReviewPackets[0].GetProperty("previousIssues").ValueKind);
        Assert.Contains(runtime.ReviewPackets.Skip(1), packet => packet.GetProperty("previousIssues").ValueKind == JsonValueKind.Array && packet.GetProperty("previousIssues")[0].GetString() == "No clear next step");

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
        // Work short of its assignment goes back first, so v2 may come a check-in later, and a check-in can have two create steps.
        var summaries = new List<string>();
        for (var cycle = 0; cycle < 4 && !summaries.Any(text => text.Contains("It replaces", StringComparison.Ordinal)); cycle++)
        {
            var later = await Send(HttpMethod.Post, "/api/shifts/shift-loop/cycle");
            summaries.AddRange(later.GetProperty("cycles")[later.GetProperty("cycles").GetArrayLength() - 1].GetProperty("stages").EnumerateArray()
                .Where(stage => stage.GetProperty("stage").GetString() == "create").Select(stage => stage.GetProperty("summary").GetString() ?? ""));
        }
        Assert.Contains(summaries, text => text.Contains("It replaces “Competitive battlecard for Jasper and Lindy”, archived.", StringComparison.Ordinal));
        var pages = factory.Services.GetRequiredService<CompanyWiki>().List();
        var old = Assert.Single(pages, page => page.Title == "Competitive battlecard for Jasper and Lindy");
        Assert.Equal("archived", old.Status);
        Assert.StartsWith("_Replaced by a newer version: “Competitive battlecard v2 for Jasper and Lindy with prices”", old.Body);
        Assert.Equal("draft", Assert.Single(pages, page => page.Title == "Competitive battlecard v2 for Jasper and Lindy with prices").Status);
        // An unrelated draft stays.
        Assert.Equal("draft", Assert.Single(pages, page => page.Title == "Climbs").Status);

        // Something the employee filed as "Library / Research / Pricing" moves to Research/Pricing, and the emptied folders go;
        // an owner's empty folder stays.
        var library = factory.Services.GetRequiredService<WorkspaceLibrary>();
        var climbs = Assert.Single(pages, page => page.Title == "Climbs");
        library.SaveEntry("wiki:" + climbs.Id, new LibraryEntryChange(library.View("").Version, "Library/Research/Pricing", []), "Marketing employee (shift)", "employee");
        var withOwner = library.View("");
        library.SaveFolders(new LibraryFoldersChange(withOwner.Version, [.. withOwner.Folders, "Ideas"], []), "Owner", "owner");
        factory.Services.GetRequiredService<EmployeeShifts>().TidyLibrary();
        var tidied = library.View("");
        Assert.Equal("Research/Pricing", Assert.Single(tidied.Entries, entry => entry.Key == "wiki:" + climbs.Id).Folder);
        Assert.DoesNotContain(tidied.Folders, folder => folder == "Library" || folder.StartsWith("Library/", StringComparison.Ordinal));
        Assert.Contains("Ideas", tidied.Folders);

        // A folder the model invents at the top level ("Competitor research", "Marketing / Content Calendars") becomes the Library's own
        // place for that work, and the invented folders go once empty.
        Assert.Equal("Research/Competitive landscape", EmployeeShifts.AreaFor("Competitor research Competitor snapshot: September 2026", null));
        var shifts = factory.Services.GetRequiredService<EmployeeShifts>();
        Assert.Equal("Strategy", shifts.PlaceFolder("Marketing/Strategy", "Positioning", null));
        var snapshot = factory.Services.GetRequiredService<CompanyWiki>().Save(new WikiChange(Guid.NewGuid().ToString("N"), null, 0, "company", "company", "Competitor snapshot: September 2026", "Prices.", "fact", "draft"), "Marketing employee (shift)");
        library.SaveEntry("wiki:" + snapshot.Id, new LibraryEntryChange(library.View("").Version, "Competitor research", []), "Marketing employee (shift)", "employee");
        shifts.TidyLibrary();
        var placed = library.View("");
        Assert.Equal("Research/Competitive landscape", placed.Entries.Single(entry => entry.Key == "wiki:" + snapshot.Id).Folder);
        Assert.DoesNotContain("Competitor research", placed.Folders);

        // The very same title filed in two areas is one document: the older copy is archived.
        var store = factory.Services.GetRequiredService<CompanyWiki>();
        var first = store.Save(new WikiChange(Guid.NewGuid().ToString("N"), null, 0, "company", "company", "Battlecard: us vs them", "Older copy.", "fact", "draft"), "Marketing employee (shift)");
        library.SaveEntry("wiki:" + first.Id, new LibraryEntryChange(library.View("").Version, "Strategy", []), "Marketing employee (shift)", "employee");
        await Task.Delay(20);
        var second = store.Save(new WikiChange(Guid.NewGuid().ToString("N"), null, 0, "company", "company", "Battlecard: us vs them", "Newer copy.", "fact", "draft"), "Marketing employee (shift)");
        library.SaveEntry("wiki:" + second.Id, new LibraryEntryChange(library.View("").Version, "Research/Shift notes", []), "Marketing employee (shift)", "employee");
        factory.Services.GetRequiredService<EmployeeShifts>().TidyLibrary();
        Assert.Equal(("archived", "draft"), (store.List().Single(page => page.Id == first.Id).Status, store.List().Single(page => page.Id == second.Id).Status));
    }
}
