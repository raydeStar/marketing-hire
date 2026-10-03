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

public sealed class EmployeeExperienceTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "employee-experience-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++)
        { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }

    static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    static readonly JsonElement Reply = Json(new { title = "A concrete launch example", rationale = "Lead with the actual demonstration.",
        recommendation = new { whyNow = "The assigned launch needs an example.", choice = "Show the result before explaining the machinery.", nextStep = "Review the draft.", hypothesis = "A concrete example may make the offer clearer.", measurement = "Measurement not set", uncertainty = "No conversion evidence yet." } });

    [Fact] public void EmptyOrInvalidSaveReceiptsCannotBecomeRecommendationsAndDecisionsPersist()
    {
        Directory.CreateDirectory(root);
        string id;
        using (var store = new Store(root))
        {
            var experience = new EmployeeExperience(store);
            Assert.Null(experience.Capture("shift", true, Reply, default, [], [], null));
            Assert.Null(experience.Capture("shift", true, Reply, default, ["wiki:../secret", "promise:posted"], [], null));
            var item = experience.Capture("shift", true, Reply, default, ["wiki:saved Saved document"],
                [new ResearchSource("https://example.org/read", "Source", "Read excerpt", null, DateTimeOffset.UtcNow), new ResearchSource("javascript:alert(1)", "Unsafe", "", null, DateTimeOffset.UtcNow)], null)!;
            id = item.Id;
            Assert.True(item.Simulated); Assert.Single(item.Outputs); Assert.Single(item.Sources);
            var parked = experience.Decide(id, new(1, "parked", "Wait for the launch date."));
            Assert.Equal(2, parked.Version);
            Assert.Throws<InvalidOperationException>(() => experience.Decide(id, new(1, "ready", null)));
            Assert.Throws<ArgumentException>(() => experience.Decide(id, new(2, "approved", null)));
            // Replaying the host save does not erase a person's parking decision.
            Assert.Equal("parked", experience.Capture("shift", true, Reply, default, ["wiki:saved Saved document"], [], null)!.Status);
            Assert.Single(experience.View().Recommendations);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var restored = new Store(root);
        Assert.Equal("Wait for the launch date.", new EmployeeExperience(restored).View().Recommendations.Single(item => item.Id == id).DecisionReason);
    }

    void Host(IShiftRuntime? runtime = null)
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
            builder.UseSetting("Marketing:BackgroundEnabled", "false");
            builder.ConfigureServices(services => { services.AddSingleton<IStartupFilter, Loopback>(); if (runtime != null) services.AddSingleton<IShiftRuntime>(runtime); });
        });
    }

    HttpClient Client(bool owner, string? role = null)
    {
        var client = factory!.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var session = factory.Services.GetRequiredService<Security>().Issue(context, owner ? "Owner" : "Teammate", owner);
        if (role != null) factory.Services.GetRequiredService<MemberRoles>().Set(session.PrincipalId, role, "Owner");
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", session.Csrf);
        return client;
    }
    sealed class Loopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        { app.Use((context, proceed) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return proceed(); }); next(app); };
    }

    [Fact] public async Task RecommendationDecisionsAreOwnerOnlyVersionedAndDoNotApproveOrRunWork()
    {
        Host(); using var owner = Client(true);
        var wiki = factory!.Services.GetRequiredService<CompanyWiki>();
        var page = wiki.Save(new WikiChange("example", null, 0, "company", "company", "Prepared example", "A saved draft example for owner inspection.", "hypothesis", "draft"), "Employee");
        var experience = factory.Services.GetRequiredService<EmployeeExperience>();
        var item = experience.Capture("fictional-shift", true, Reply, default, ["wiki:" + page.Id], [], null)!;
        using var viewer = Client(false, "viewer"); using var contributor = Client(false, "contributor");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/experience")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await contributor.GetAsync("/api/experience")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await contributor.PostAsJsonAsync("/api/experience/" + item.Id + "/decision", new RecommendationDecision(1, "parked", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await contributor.PostAsJsonAsync("/api/experience/first-win", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/experience/" + item.Id + "/decision", new RecommendationDecision(1, "parked", "Wait."))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/experience/" + item.Id + "/decision", new RecommendationDecision(1, "ready", null))).StatusCode);
        Assert.Equal("draft", wiki.List().Single(item => item.Id == page.Id).Status);
        Assert.False(factory.Services.GetRequiredService<EmployeeShifts>().OnShift);
        Assert.Equal("parked", experience.View().Recommendations.Single().Status);
    }

    // "What's your priority right now?": the first win always, and what the owner ticked (at most two) instead of the usual pieces.
    [Fact] public async Task TheFirstShiftMakesWhatTheOwnerPicked()
    {
        Host(); using var owner = Client(true);
        var initial = await owner.GetFromJsonAsync<JsonElement>("/api/marketing/state");
        var profile = await owner.PutAsJsonAsync("/api/marketing/profile", new { requestId = "picks-brief", version = initial.GetProperty("profile").GetProperty("version").GetInt32(),
            display_name = "Marketing", product_summary = "A neighborhood bakery.", audience = "Families nearby", goals = "More cake orders", voice = "Warm", channels = "Instagram", guardrails = "Draft only", claims = "", examples = "" });
        Assert.True(profile.IsSuccessStatusCode, await profile.Content.ReadAsStringAsync());
        var offered = await owner.GetFromJsonAsync<JsonElement>("/api/experience/first-shift-choices");
        var titles = offered.GetProperty("choices").EnumerateArray().Select(item => item.GetProperty("title").GetString()!).ToArray();
        Assert.Contains(Playbooks.ResearchTitle, titles);
        Assert.Equal(2, offered.GetProperty("most").GetInt32());
        Assert.Contains(offered.GetProperty("choices").EnumerateArray(), item => item.GetProperty("suggested").GetBoolean());
        // Three picks, one unknown: the two known ones are made, and nothing else beside the first win.
        var made = await owner.PostAsJsonAsync("/api/experience/first-win", new { picks = new[] { Playbooks.ResearchTitle, "Not offered", titles.Last() } });
        Assert.True(made.IsSuccessStatusCode, await made.Content.ReadAsStringAsync());
        var tasks = (await owner.GetFromJsonAsync<JsonElement>("/api/marketing/state")).GetProperty("tasks").EnumerateArray().Select(task => task.GetProperty("title").GetString()).ToArray();
        Assert.Equal(new[] { EmployeeShifts.FirstWinTitle, Playbooks.ResearchTitle, titles.Last() }.OrderBy(title => title), tasks.OrderBy(title => title));
        Assert.False(factory!.Services.GetRequiredService<EmployeeShifts>().OnShift);
    }

    // A pottery studio with no website: "Book a class" is its next step with no link, it isn't offered work on a site it doesn't have,
    // and its first win (the Google profile) isn't a site fix, so chat doesn't ask it to connect one.
    [Fact] public async Task AnOwnerWithNoWebsiteIsOfferedNoWorkOnOne()
    {
        Host(); using var owner = Client(true);
        Assert.True((await owner.PutAsJsonAsync("/api/playbook", new PlaybookChoice("local"))).IsSuccessStatusCode);
        var objectives = factory!.Services.GetRequiredService<CompanyObjectives>();
        var current = objectives.Current();
        objectives.Save(new ObjectivesChange(current.Version, current.Content with { CallToAction = new("Book a class", "") }), "Owner");
        Assert.Equal(new CallToAction("Book a class", ""), objectives.Current().Content.CallToAction);
        current = objectives.Current();
        Assert.Throws<ArgumentException>(() => objectives.Save(new ObjectivesChange(current.Version, current.Content with { CallToAction = new("Book a class", "http://kettleandkiln.example/book") }), "Owner"));
        async Task<(string[] Titles, bool OnSite)> Offered()
        {
            var view = await owner.GetFromJsonAsync<JsonElement>("/api/experience/first-shift-choices");
            return ([.. view.GetProperty("choices").EnumerateArray().Select(item => item.GetProperty("title").GetString()!)], view.GetProperty("firstWinOnSite").GetBoolean());
        }
        var (titles, onSite) = await Offered();
        Assert.DoesNotContain("Local search fixes for the site", titles);
        Assert.False(onSite);
        // With a website it's offered, and a local business's first win is still its Google profile.
        current = objectives.Current();
        objectives.Save(new ObjectivesChange(current.Version, current.Content with { OwnSite = "kettleandkiln.example" }), "Owner");
        (titles, onSite) = await Offered();
        Assert.Contains("Local search fixes for the site", titles);
        Assert.False(onSite);
        // A product's first win, with a website, is the biggest fix on it.
        Assert.True((await owner.PutAsJsonAsync("/api/playbook", new PlaybookChoice("product"))).IsSuccessStatusCode);
        Assert.True((await Offered()).OnSite);
        // Each starter held back without a site is one a playbook offers: a renamed one would quietly be offered again.
        Assert.All(Playbooks.NeedsSite, title => Assert.Contains(Playbooks.All.SelectMany(playbook => playbook.Starters), starter => starter.Title == title));
    }

    [Fact] public async Task AFirstWinIsOneDurableAssignmentAndDoesNotStartInference()
    {
        Host(); using var owner = Client(true);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/experience/first-win", new { })).StatusCode);
        var initial = await owner.GetFromJsonAsync<JsonElement>("/api/marketing/state");
        var profile = await owner.PutAsJsonAsync("/api/marketing/profile", new { requestId = "first-win-brief", version = initial.GetProperty("profile").GetProperty("version").GetInt32(),
            display_name = "Marketing", product_summary = "A configurable marketing employee.", audience = "Solo founders", goals = "Explain the offer clearly", voice = "Concrete and direct", channels = "LinkedIn", guardrails = "Draft only", claims = "Owner reviews every draft", examples = "Lead with a working example" });
        Assert.True(profile.IsSuccessStatusCode, await profile.Content.ReadAsStringAsync());
        var first = await owner.PostAsJsonAsync("/api/experience/first-win", new { });
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        var receipt = await first.Content.ReadFromJsonAsync<JsonElement>();
        var second = await owner.PostAsJsonAsync("/api/experience/first-win", new { });
        Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());
        var repeated = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(receipt.GetProperty("taskId").GetString(), repeated.GetProperty("taskId").GetString());
        Assert.False(repeated.GetProperty("queued").GetBoolean());
        var state = await owner.GetFromJsonAsync<JsonElement>("/api/marketing/state");
        Assert.Single(state.GetProperty("tasks").EnumerateArray(), task => task.GetProperty("id").GetString() == receipt.GetProperty("taskId").GetString());
        Assert.False(factory!.Services.GetRequiredService<EmployeeShifts>().OnShift);
        Assert.Empty(factory.Services.GetRequiredService<EmployeeExperience>().View().Recommendations);
        var shifts = factory.Services.GetRequiredService<EmployeeShifts>();
        shifts.Research = (_, _) => Task.FromResult<ResearchSource[]>([]);
        var started = await owner.PostAsJsonAsync("/api/shifts", new { requestId = "first-win-shift", hours = 1, turnBudget = 30 });
        Assert.True(started.IsSuccessStatusCode, await started.Content.ReadAsStringAsync());
        var cycle = await owner.PostAsJsonAsync("/api/shifts/first-win-shift/cycle", new { });
        Assert.True(cycle.IsSuccessStatusCode, await cycle.Content.ReadAsStringAsync());
        // The first shift works the site's fix and the playbook's two pieces (a week of posts, a competitor snapshot).
        var recommendations = factory.Services.GetRequiredService<EmployeeExperience>().View().Recommendations;
        Assert.Equal(3, recommendations.Length);
        var prepared = Assert.Single(recommendations, item => item.Title.Contains("first useful win", StringComparison.OrdinalIgnoreCase));
        Assert.True(prepared.Simulated);
        var output = Assert.Single(prepared.Outputs);
        Assert.StartsWith("wiki:", output);
        Assert.Contains(factory.Services.GetRequiredService<CompanyWiki>().List(), page => "wiki:" + page.Id == output);
        var before = factory.Services.GetRequiredService<CompanyWiki>().List().Single(page => "wiki:" + page.Id == output).Body;
        var redraft = await owner.PostAsJsonAsync("/api/redrafts", new RedraftAsk(output, "Lead with the example and cut the generic introduction."));
        Assert.True(redraft.IsSuccessStatusCode, await redraft.Content.ReadAsStringAsync());
        Assert.Equal(before, factory.Services.GetRequiredService<Redrafts>().All().Single().Original);
        Assert.DoesNotContain("approved", prepared.Status);
    }

    [Fact] public void EveryPlaybookTaskFitsTheTaskStore()
    {
        // hire.py keeps a task's next action to 1,000 characters; a longer one is refused and the owner's click does nothing.
        var tasks = Playbooks.All.SelectMany(playbook => playbook.Starters.Concat(playbook.FirstShift).Select(task => (playbook.Id, task)))
            .Append((Id: "product", task: Playbooks.Competitor("a rival (rival.example): read their own pages there")))
            .Concat(Playbooks.All.Select(playbook => (Id: playbook.Id, task: new PlaybookTask(EmployeeShifts.FirstWinTitle, EmployeeShifts.FirstWinNext(Playbooks.FirstWinPage(playbook.Id))))));
        Assert.All(tasks, item => Assert.True(item.task.Next.Length <= 1000 && item.task.Title.Length <= 160, $"{item.Id}: {item.task.Title} is {item.task.Next.Length} characters"));
    }

    [Fact] public async Task APlaybookShapesTheGuidanceTheNorthStarAndTheFirstShift()
    {
        Host(); using var owner = Client(true); using var contributor = Client(false, "contributor");
        Assert.Equal(HttpStatusCode.Forbidden, (await contributor.PutAsJsonAsync("/api/playbook", new PlaybookChoice("practice"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/playbook", new PlaybookChoice("hobby"))).StatusCode);
        Assert.True((await owner.PutAsJsonAsync("/api/playbook", new PlaybookChoice("practice"))).IsSuccessStatusCode);
        var view = await owner.GetFromJsonAsync<JsonElement>("/api/playbook");
        Assert.Equal("practice", view.GetProperty("current").GetString());
        Assert.Equal(4, view.GetProperty("all").GetArrayLength());
        // No north star was set, so the practice's usual number is suggested; the guidance every turn reads carries its guardrails.
        Assert.Equal("Consults or seminar sign-ups", factory!.Services.GetRequiredService<CompanyObjectives>().Current().Content.NorthStar?.Name);
        var guidance = factory.Services.GetRequiredService<WorkspaceRole>().Guidance();
        Assert.Contains("Kind of business: a practice or service", guidance);
        Assert.Contains("No promised outcomes or cures", guidance);

        var initial = await owner.GetFromJsonAsync<JsonElement>("/api/marketing/state");
        var profile = await owner.PutAsJsonAsync("/api/marketing/profile", new { requestId = "playbook-brief", version = initial.GetProperty("profile").GetProperty("version").GetInt32(),
            display_name = "Marketing", product_summary = "Seminars on managing stress at work.", audience = "Working professionals", goals = "Fill the next seminar", voice = "Warm and plain", channels = "Facebook, email", guardrails = "No client stories", claims = "Licensed counselor", examples = "" });
        Assert.True(profile.IsSuccessStatusCode, await profile.Content.ReadAsStringAsync());
        Assert.True((await owner.PostAsJsonAsync("/api/experience/first-win", new { })).IsSuccessStatusCode);
        Assert.True((await owner.PostAsJsonAsync("/api/experience/first-win", new { })).IsSuccessStatusCode);   // a repeat queues nothing more
        var titles = (await owner.GetFromJsonAsync<JsonElement>("/api/marketing/state")).GetProperty("tasks").EnumerateArray().Select(task => task.GetProperty("title").GetString()).ToArray();
        // No competitor's site is allowed, so the practice's first step (a seminar kit) is made instead of a snapshot it couldn't research.
        Assert.Equal(["Prepare my first useful win", "Seminar promotion kit for the next event", "Your first week of posts"], titles.Order());
        var shifts = factory.Services.GetRequiredService<EmployeeShifts>();
        var objectives = factory.Services.GetRequiredService<CompanyObjectives>();
        var current = objectives.Current();
        objectives.Save(new ObjectivesChange(current.Version, current.Content with { Competitors = [new("Calm Minds Workshops", "calmminds.example")], ResearchSites = ["https://calmminds.example/"] }), "Owner");
        var snapshot = Assert.Single(shifts.FirstShiftPieces(Playbooks.Find("practice")!), piece => piece.Title == Playbooks.SnapshotTitle);
        Assert.Contains("Calm Minds Workshops (calmminds.example)", snapshot.Next);
        Assert.Contains("No client stories, no promised outcomes.", (await owner.GetFromJsonAsync<JsonElement>("/api/marketing/state")).GetProperty("tasks").EnumerateArray().Single(task => task.GetProperty("title").GetString() == "Your first week of posts").GetProperty("next_action").GetString());
    }

    [Fact] public void TimeSavingsStayUnreportedUntilSomeoneSuppliesAnEstimate()
    {
        Directory.CreateDirectory(root);
        using var store = new Store(root);
        var wiki = new CompanyWiki(store, new OrganizationDirectory(store));
        var library = new WorkspaceLibrary(store);
        var memory = new EmployeeMemory(store, wiki, library);
        Assert.Null(memory.Record(new("wiki:one", "Useful work", "useful", "Clearer copy"), "Owner").MinutesSaved);
        Assert.Equal(25, memory.Record(new("wiki:one", "Useful work", "useful", "Clearer copy", 25), "Owner").MinutesSaved);
        Assert.Null(memory.Record(new("wiki:two", "Wrong direction", "not_useful", "Try another angle", 25), "Owner").MinutesSaved);
        Assert.Throws<ArgumentException>(() => memory.Record(new("wiki:one", "Useful work", "useful", "", -1), "Owner"));
    }

    sealed class LaunchRuntime : IShiftRuntime
    {
        readonly ScriptedShiftRuntime scripted = new();
        public string Name => "scripted-launch";
        public bool Live => false;
        public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation) => request.Stage == "create"
            ? Task.FromResult(new ShiftTurnResult(JsonSerializer.Serialize(new { deliverable = "document", title = "Launch week plan", kind = "hypothesis", folder = "Campaigns/Plans",
                body = "# Launch week plan\n\nGoal: Make the offer clear.\nChannels: LinkedIn, email.\n\nPrepare one demonstration, then the launch post. Review the copy before posting. Measurement not set.",
                recommendation = new { packageName = "Launch week", whyNow = "The owner requested a launch.", choice = "Lead with a demonstration.", uncertainty = "No results yet." } }), 0))
            : scripted.Turn(request, cancellation);
    }

    [Fact] public async Task ARequestedLaunchBecomesOnePlannedPackageWithItsSavedWork()
    {
        Host(new LaunchRuntime()); using var owner = Client(true);
        var shifts = factory!.Services.GetRequiredService<EmployeeShifts>();
        shifts.Research = (_, _) => Task.FromResult<ResearchSource[]>([]);
        var assigned = await owner.PostAsJsonAsync("/api/marketing/tasks", new { requestId = "launch-package-task", title = "Launch campaign", status = "ready", priority = "high", next_action = "Prepare the launch campaign next week", action_state = "agent_ready" });
        Assert.True(assigned.IsSuccessStatusCode, await assigned.Content.ReadAsStringAsync());
        Assert.True((await owner.PostAsJsonAsync("/api/shifts", new { requestId = "launch-package-shift", hours = 1, turnBudget = 8 })).IsSuccessStatusCode);
        var cycled = await owner.PostAsJsonAsync("/api/shifts/launch-package-shift/cycle", new { });
        Assert.True(cycled.IsSuccessStatusCode, await cycled.Content.ReadAsStringAsync());
        var campaign = Assert.Single(factory.Services.GetRequiredService<Campaigns>().View().Campaigns);
        Assert.Equal("planned", campaign.Status);
        Assert.NotNull(campaign.PlanWikiId);
        var recommendation = Assert.Single(factory.Services.GetRequiredService<EmployeeExperience>().View().Recommendations);
        Assert.Equal(campaign.Id, recommendation.CampaignId);
        Assert.Contains("wiki:" + campaign.PlanWikiId, recommendation.Outputs);
        Assert.Equal(campaign.Id, factory.Services.GetRequiredService<Campaigns>().Of("wiki:" + campaign.PlanWikiId));
    }
}
