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

    [Fact] public void ObjectivesValidateAndMeasureTheNorthStarFromTheScorecard()
    {
        var content = CompanyObjectives.Validate(new ObjectivesContent(new NorthStar(" Trial starts ", "Trial starts", 1500, "per month", Day(90), "Leading indicator of revenue"),
            [new Objective("Fix the signup funnel", [new KeyResult("Signup conversion back above 5%", null, 5), new KeyResult("", null, null)]), new Objective("", [])],
            new Positioning("Founders", "No time for marketing", "Agencies, doing it yourself", "An employee that asks before acting", ["Every draft is approved", ""]),
            [new Competitor("Agency", "Slow and expensive")], " Signup funnel first ", ["Paid ads", " "]));
        Assert.Equal("trial_starts", content.NorthStar!.Metric);
        Assert.Single(content.Objectives); Assert.Single(content.Objectives[0].KeyResults);
        Assert.Single(content.Positioning!.ProofPoints); Assert.Equal("Signup funnel first", content.CurrentFocus); Assert.Single(content.NonGoals);
        Assert.Throws<ArgumentException>(() => CompanyObjectives.Validate(content with { Objectives = [.. Enumerable.Repeat(content.Objectives[0], 6)] }));
        var observations = Enumerable.Range(0, 40).Select(i => new ScoreObservation("trial_starts", Day(-39 + i), 50, "t", DateTimeOffset.UtcNow)).ToArray();
        var ledger = new ScoreLedger(1, [new ScoreMetric("trial_starts", "Trial starts", "", "up", true, "t")], observations, [], []);
        var progress = JsonSerializer.SerializeToElement(CompanyObjectives.Progress(content, ledger));
        Assert.Equal(1500, progress.GetProperty("latest").GetDouble()); // 30 days x 50, a monthly target
        Assert.Equal(100, progress.GetProperty("percent").GetDouble());
    }

    /// <summary>A stand-in model with fixed answers, to drive specific paths through the host.</summary>
    sealed class CannedRuntime : IShiftRuntime
    {
        public string Name => "scripted";
        public bool Live => false;
        public List<(string Stage, JsonElement Data)> Packets { get; } = [];
        public int TokensPerTurn { get; set; }
        public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation)
        {
            var data = request.Data;
            Packets.Add((request.Stage, data.Clone()));
            string reply;
            if (request.Stage == "prioritize" && !data.GetProperty("queue").EnumerateArray().Any(task => task.GetProperty("title").GetString() == "Compare buyer segments"))
                reply = JsonSerializer.Serialize(new { priorities = data.GetProperty("queue").EnumerateArray().Select(task => new { title = task.GetProperty("title").GetString(), reason = "Queued", deliverable = "document",
                    taskId = task.GetProperty("id").GetString() }), newTasks = Array.Empty<object>(), note = "The queue." });
            else if (request.Stage == "prioritize")
            {
                var queue = data.GetProperty("queue").EnumerateArray().ToDictionary(task => task.GetProperty("title").GetString()!, task => task.GetProperty("id").GetString()!);
                reply = JsonSerializer.Serialize(new { priorities = new object[] {
                    new { title = "Compare buyer segments", reason = "Needed for the decision", deliverable = "document", taskId = queue["Compare buyer segments"], signalRef = (string?)null, research = "founders marketing time",
                        read = new[] { "https://rival.example/pricing", "https://elsewhere.test/page" }, market = new { industries = new[] { "5418" }, companies = new[] { "HUBS" } } },
                    new { title = "Hackathon description", reason = "Due soon", deliverable = "draft", taskId = queue["Hackathon description"], signalRef = (string?)null, research = (string?)null, audit = "rival.example" },
                    new { title = "LinkedIn launch post", reason = "Brand", deliverable = "draft", taskId = queue["LinkedIn launch post"], signalRef = (string?)null, research = "founders marketing time" } }, newTasks = Array.Empty<object>(), note = "Three items." });
            }
            else if (request.Stage == "create")
                reply = data.GetProperty("priority").GetProperty("title").GetString() == "Compare buyer segments"
                    ? JsonSerializer.Serialize(new { deliverable = "document", title = "Segments", body = "Founders say they lack time for marketing [1].", kind = "hypothesis", folder = "Research" })
                    : data.GetProperty("priority").GetProperty("title").GetString() == "LinkedIn launch post"
                    ? JsonSerializer.Serialize(new { deliverable = "draft", title = "LinkedIn launch post", channel = "LinkedIn", destination = "https://www.linkedin.com/feed/", body = "Founders keep telling us marketing is the first thing they drop [1]. Follow the build.", rationale = "Leads with a real complaint." })
                    : JsonSerializer.Serialize(new { deliverable = "draft", title = "Hackathon description", channel = "Hackathon submission", destination = "", body = "First Employee is an AI marketing employee that works shifts and asks before acting." });
            else if (request.Stage == "review")
                reply = data.GetProperty("deliverable").GetProperty("title").GetString()!.StartsWith("Segments")
                    ? JsonSerializer.Serialize(new { scores = new { strategy = 4, customer = 3, distinctive = 2, channel = 4, brand = 4, action = 3, claims = 5, shareable = 3 }, issues = new[] { "Generic: name the segment" },
                        revised = new { title = "Segments: solo founders first", body = "Solo founders say they lack time for marketing [2]; the rival charges a monthly fee [1].\n\n## Sources\n\n[1] https://rival.example/pricing\n[2] a discussion" } })
                    : data.GetProperty("deliverable").GetProperty("title").GetString() == "LinkedIn launch post"
                    ? JsonSerializer.Serialize(new { scores = new { strategy = 4, customer = 4, distinctive = 4, channel = 4, brand = 4, action = 4, claims = 4, shareable = 4 }, issues = Array.Empty<string>(), revised = (object?)null })
                    : JsonSerializer.Serialize(new { scores = new { strategy = 3 }, issues = new[] { "Cites nothing" }, revised = new { title = "Hackathon description", body = "An invented statistic [5] makes this stronger." } });
            else reply = JsonSerializer.Serialize(new { learnings = new[] { "Research filled the evidence gaps." }, nextShiftFocus = "Decide the segment.",
                notebook = new { known = new[] { "Solo founders describe marketing as the task they drop first.", "Seven drafts were created during the cycle.", "The owner approved LinkedIn draft #40." }, decided = Array.Empty<string>(), openQuestions = new[] { "Which segment do we lead with?" },
                    worked = Array.Empty<string>(), didNotWork = Array.Empty<string>(), resolved = Array.Empty<string>() } });
            return Task.FromResult(new ShiftTurnResult(reply, TokensPerTurn));
        }
    }

    [Fact] public void ARevisionThatDropsTheRequestedCodeBlockIsNotUsed()
    {
        var storyboard = "Scenes below.\n```json\n{\"scenes\":[]}\n```\nWhy each scene.";
        Assert.False(EmployeeShifts.KeepsFormat(storyboard, "Scene 1 - Intro. Caption: Meet your employee."));
        Assert.True(EmployeeShifts.KeepsFormat(storyboard, "Tighter intro.\n```json\n{\"scenes\":[1]}\n```"));
        Assert.True(EmployeeShifts.KeepsFormat("Plain text.", "Plain, tighter text."));
    }

    [Fact] public async Task ResearchIsReviewedCitedAndLearnedFrom()
    {
        var canned = new CannedRuntime();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host"));
            builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:FixtureLedger", Path.Combine(root, "ledger"));
            builder.UseSetting("Marketing:FixtureRunwayScript", Path.Combine(directory!.FullName, "business", "agent", "hire", "bin", "runway.py"));
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => services.AddSingleton<IShiftRuntime>(canned));
        });
        var shifts = factory.Services.GetRequiredService<EmployeeShifts>();
        shifts.Research = (query, _) => Task.FromResult<ResearchSource[]>(
            [new ResearchSource("https://news.ycombinator.com/item?id=123", "Ask HN: How do solo founders do marketing?", "I never have time for marketing.", 88, DateTimeOffset.UtcNow.AddDays(-3))]);
        // Official figures for sizing: BLS answers with no setup; SEC waits for the owner's contact.
        factory.Services.GetRequiredService<MarketData>().Fetch = (url, _, _, _) => Task.FromResult(url.Contains("industry_titles")
            ? "\"industry_code\",\"industry_title\"\n\"5418\",\"NAICS 5418 Advertising and PR\"\n"
            : "\"area_fips\",\"own_code\",\"industry_code\",\"year\",\"disclosure_code\",\"annual_avg_estabs\",\"annual_avg_emplvl\",\"total_annual_wages\",\"avg_annual_pay\"\n\"US000\",\"5\",\"5418\",\"2024\",\"\",80121,489153,52486950808,107302\n");
        var siteCheck = factory.Services.GetRequiredService<SiteAudit>();
        siteCheck.Pause = TimeSpan.Zero;
        siteCheck.Probe = (url, _, _) => Task.FromResult(url.EndsWith("/robots.txt") || url.EndsWith("/sitemap.xml") ? (404, new Uri(url), "text/html", "", 0)
            : (200, new Uri(url), "text/html", "<html><head><title>Rival</title></head><body><h1>Rival</h1><p>Plans for teams.</p></body></html>", 0));
        var reads = new List<string>();
        shifts.ReadSite = (url, sites, _) => { reads.Add(url); return Task.FromResult((url, "Rival pricing", "Rival plans start at a monthly fee for teams of five.")); };
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
        foreach (var title in new[] { "Compare buyer segments", "Hackathon description", "LinkedIn launch post" })
            await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-" + title, title, status = "ready", priority = "normal", next_action = "Do it.", action_state = "agent_ready" });
        // The owner allows one competitor's site and has told the employee what they thought of earlier work.
        await Send(HttpMethod.Put, "/api/objectives", new { expectedVersion = 0, content = new { objectives = Array.Empty<object>(), competitors = Array.Empty<object>(), currentFocus = "", nonGoals = Array.Empty<string>(),
            researchSites = new[] { "https://www.rival.example/" } } });
        await Send(HttpMethod.Post, "/api/feedback", new { key = "wiki:earlier", title = "Earlier plan", verdict = "not_useful", note = "Too generic; name the segment." });
        await Send(HttpMethod.Post, "/api/shifts", new { requestId = "shift-r", hours = 8, turnBudget = 10 });
        var shift = await Send(HttpMethod.Post, "/api/shifts/shift-r/cycle", new { });
        var summary = shift.GetProperty("cycles")[0].GetProperty("stages")[2].GetProperty("summary").GetString()!;
        Assert.Contains("Read 1 public source", summary);
        Assert.Contains("Read rival.example/pricing", summary);
        Assert.Matches(@"Read 1 public figure: BLS QCEW \d{4}: NAICS 5418 Advertising and PR\.", summary);
        Assert.Contains("Checked 1 pages of rival.example", summary);
        Assert.Contains("Settings → Research data", summary);
        Assert.Contains("80,121 establishments", canned.Packets.First(packet => packet.Stage == "create").Data.GetProperty("sources").GetRawText());
        Assert.Contains("isn't on the research allowlist", summary);
        Assert.Equal(["https://rival.example/pricing"], reads);
        Assert.Contains("revision discarded", summary);
        // The owner's verdicts reach every planning and writing turn.
        Assert.Contains("Too generic", canned.Packets.First(packet => packet.Stage == "prioritize").Data.GetProperty("memory").GetRawText());
        Assert.Contains("Too generic", canned.Packets.First(packet => packet.Stage == "create").Data.GetProperty("memory").GetRawText());
        Assert.Equal("rival.example", canned.Packets.First(packet => packet.Stage == "prioritize").Data.GetProperty("researchSites")[0].GetString());
        // The review judges the work against what the owner asked for.
        Assert.Equal("Do it.", canned.Packets.First(packet => packet.Stage == "review").Data.GetProperty("assignment").GetProperty("next_action").GetString());
        var wiki = await Send(HttpMethod.Get, "/api/company-wiki");
        // The review revised the weak document; the revision that cited a source that doesn't exist was thrown away.
        var segments = wiki.EnumerateArray().Single(page => page.GetProperty("title").GetString() == "Segments: solo founders first").GetProperty("body").GetString()!;
        Assert.StartsWith("Solo founders say", segments); Assert.Contains("Self-review 3.5 → 3.5/5 over 2 passes, revised: Generic: name the segment.", segments);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(segments, "## Sources"));   // the host's linked list, not the model's too
        Assert.DoesNotContain("[2] a discussion", segments);
        Assert.Contains("## Sources", segments); Assert.Contains("https://news.ycombinator.com/item?id=123", segments); Assert.Contains("[Rival pricing](https://rival.example/pricing) · rival.example", segments);
        var hackathon = wiki.EnumerateArray().Single(page => page.GetProperty("title").GetString() == "Hackathon description");
        Assert.Contains("no posting destination", hackathon.GetProperty("body").GetString());
        Assert.DoesNotContain("invented statistic", hackathon.GetProperty("body").GetString());
        var library = await Send(HttpMethod.Get, "/api/workspace-library");
        Assert.Contains(library.GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("key").GetString() == "wiki:" + hackathon.GetProperty("id").GetString() && entry.GetProperty("folder").GetString() == "Campaigns/Drafts");
        var state = await Send(HttpMethod.Get, "/api/marketing/state");
        Assert.Contains(state.GetProperty("evidence").EnumerateArray(), item => item.GetProperty("url").GetString() == "https://news.ycombinator.com/item?id=123");
        // Evidence says what it supports: the sentence that cites it, then what the page says.
        var pricing = state.GetProperty("evidence").EnumerateArray().First(item => item.GetProperty("url").GetString() == "https://rival.example/pricing");
        Assert.StartsWith("Cited for: “Solo founders say they lack time for marketing; the rival charges a monthly fee.”", pricing.GetProperty("note").GetString());
        Assert.Contains("What it says: ", pricing.GetProperty("note").GetString());
        Assert.Equal("needs_you", state.GetProperty("tasks").EnumerateArray().Single(task => task.GetProperty("title").GetString() == "Hackathon description").GetProperty("status").GetString());
        // The public post carries no citation markers; what it relied on is in the rationale.
        var post = Assert.Single(state.GetProperty("drafts").EnumerateArray());
        Assert.Equal("Founders keep telling us marketing is the first thing they drop. Follow the build.", post.GetProperty("content").GetString());
        Assert.Contains("Based on: Ask HN: How do solo founders do marketing? (Hacker News).", post.GetProperty("rationale").GetString());
        Assert.Contains(shift.GetProperty("decisions").EnumerateArray(), item => item.GetString() == $"draft:{post.GetProperty("id").GetInt32()} LinkedIn draft #{post.GetProperty("id").GetInt32()}");

        // At the end of the shift the employee writes what it established into the Marketing notebook.
        await Send(HttpMethod.Post, "/api/shifts/shift-r/stop", new { });
        wiki = await Send(HttpMethod.Get, "/api/company-wiki");
        var notebook = wiki.EnumerateArray().Single(page => page.GetProperty("title").GetString() == "Marketing notebook").GetProperty("body").GetString()!;
        Assert.Contains("- Which segment do we lead with?", notebook);
        Assert.DoesNotContain("during the cycle", notebook); Assert.DoesNotContain("draft #40", notebook);   // a log of the shift isn't knowledge
        // A notebook written before that rule loses its shift trivia at start, while only the employee has edited it.
        var pages = factory!.Services.GetRequiredService<CompanyWiki>();
        var kept = pages.List().Single(page => page.Title == "Marketing notebook");
        pages.Save(new WikiChange(Guid.NewGuid().ToString("N"), kept.Id, kept.Version, kept.Scope, kept.ScopeId, kept.Title,
            kept.Body.Replace("## What we know\n\n", "## What we know\n\n- LinkedIn draft #40 was approved on 2026-09-25.\n"), "fact", "active"), "Marketing employee (shift)");
        var memory = factory.Services.GetRequiredService<EmployeeMemory>();
        Assert.True(memory.Tidy());
        var tidied = pages.List().Single(page => page.Title == "Marketing notebook").Body;
        Assert.DoesNotContain("draft #40", tidied); Assert.Contains("Solo founders", tidied);
        Assert.False(memory.Tidy());
        var feedback = await Send(HttpMethod.Get, "/api/feedback");
        Assert.Equal("not_useful", feedback.GetProperty("feedback")[0].GetProperty("verdict").GetString());
        Assert.Contains("Solo founders", feedback.GetProperty("notebook").GetProperty("known")[0].GetString());
        // Chat reads the same record, so it can say what the shift did and what the owner thought.
        var chat = await shifts.ChatContext(CancellationToken.None);
        Assert.Contains("Your last shift ran", chat); Assert.Contains("It produced: Segments: solo founders first", chat);
        Assert.Contains("It left for the owner to decide:", chat); Assert.Contains("Research filled the evidence gaps.", chat);
        Assert.Contains("Earlier plan: not useful (Too generic; name the segment.)", chat); Assert.Contains("Which segment do we lead with?", chat);
        Assert.Same(shifts, factory.Services.GetRequiredService<MarketingBackend>().WorkContext!.Target);
        Assert.Contains("with the language `action`", chat); Assert.Contains("Its report is the Library document wiki:", chat);
        Assert.Contains("No publishing channels are connected", chat);

        // A token budget stops the work before it runs over, keeping room for the report.
        canned.TokensPerTurn = 3000;
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-more", title = "Write the FAQ", status = "ready", priority = "normal", next_action = "Five questions.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-more-2", title = "Write the pricing note", status = "ready", priority = "normal", next_action = "One page.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/shifts", new { requestId = "shift-t", hours = 8, turnBudget = 20, tokenBudget = 9000 });
        var capped = await Send(HttpMethod.Post, "/api/shifts/shift-t/cycle", new { });
        Assert.Equal("completed", capped.GetProperty("status").GetString());
        Assert.Equal("The token budget was used.", capped.GetProperty("stopReason").GetString());
        Assert.Equal(3, capped.GetProperty("turnsUsed").GetInt32()); // plan, one piece (no room for its review), report
        Assert.Equal(9000, capped.GetProperty("tokensUsed").GetInt32());
        using (var tooSmall = await client.PostAsJsonAsync("/api/shifts", new { requestId = "shift-u", hours = 8, tokenBudget = 500 }))
            Assert.Equal(HttpStatusCode.BadRequest, tooSmall.StatusCode);
    }

    [Fact] public void TheNotebookKeepsTheOwnersEditsAndSitesStayOnTheAllowlist()
    {
        var state = EmployeeMemory.EmptyNotebook with { Known = ["Alpha fact"], OpenQuestions = ["Which segment?", "Which price?"] };
        var edited = EmployeeMemory.Render(state).Replace("- Which price?\n", "").Replace("## What worked\n\n_Nothing yet._", "## What worked\n\n- Founder stories");
        var parsed = EmployeeMemory.Parse(edited, state);
        Assert.Equal(["Which segment?"], parsed.OpenQuestions); Assert.Equal(["Founder stories"], parsed.Worked); Assert.Equal(["Alpha fact"], parsed.Known);

        Assert.Equal("acme.com", SiteReader.NormalizeSite("https://www.acme.com/pricing"));
        Assert.Equal("docs.acme.com", SiteReader.NormalizeSite("docs.acme.com"));
        Assert.Null(SiteReader.NormalizeSite("http://acme.com")); Assert.Null(SiteReader.NormalizeSite("localhost")); Assert.Null(SiteReader.NormalizeSite("10.0.0.1"));
        string[] sites = ["acme.com"];
        Assert.True(SiteReader.Allowed(new Uri("https://blog.acme.com/post"), sites));
        Assert.False(SiteReader.Allowed(new Uri("https://notacme.com/"), sites));
        Assert.False(SiteReader.Allowed(new Uri("https://acme.com:8443/"), sites));
        Assert.False(SiteReader.Allowed(new Uri("http://acme.com/"), sites));
        Assert.Throws<ArgumentException>(() => CompanyObjectives.Validate(CompanyObjectives.Empty with { ResearchSites = ["not a site"] }));
    }

    /// <summary>Network check, off by default: set SHIFT_RESEARCH_NETWORK=1 to run it against the public search.</summary>
    [Fact] public async Task ResearchSplitsTopicsAndReadsPublicDiscussions()
    {
        if (Environment.GetEnvironmentVariable("SHIFT_RESEARCH_NETWORK") != "1") return;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => { builder.UseSetting("Thaddeus:Data", Path.Combine(root, "net")); builder.UseSetting("Marketing:ShiftPump", "off"); });
        var sources = await factory.Services.GetRequiredService<EmployeeShifts>().Research("AI marketing employee, founder marketing, human approval AI", CancellationToken.None);
        Assert.NotEmpty(sources);
        Assert.All(sources, source => { Assert.StartsWith("https://news.ycombinator.com/item?id=", source.Url); Assert.False(string.IsNullOrWhiteSpace(source.Excerpt)); });
    }

    [Fact] public void WorkingHoursFindTheNextStartInTheOwnersTimeZone()
    {
        var weekdays = new ShiftSchedule(true, [1, 2, 3, 4, 5], "08:00", "17:00", "America/New_York", 60, 12, 15000, null, "Owner", DateTimeOffset.UtcNow);
        // Saturday noon UTC → Monday 08:00 in New York (EDT, UTC-4) = 12:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero), WorkSchedule.NextStart(weekdays, new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero))!.Value.ToUniversalTime());
        // Inside today's hours and not yet started: now. Already started today: tomorrow.
        var tuesdayTen = new DateTimeOffset(2026, 9, 29, 14, 0, 0, TimeSpan.Zero);
        Assert.Equal(tuesdayTen, WorkSchedule.NextStart(weekdays, tuesdayTen)!.Value.ToUniversalTime());
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), WorkSchedule.NextStart(weekdays with { LastStartedFor = "2026-09-29" }, tuesdayTen)!.Value.ToUniversalTime());
        Assert.Null(WorkSchedule.NextStart(weekdays with { Enabled = false }, tuesdayTen));
    }

    [Fact] public async Task TheWorkScheduleStartsTodaysShiftOnceAndRespectsAStop()
    {
        if (DateTime.UtcNow.TimeOfDay > TimeSpan.FromHours(23.5)) return; // the day's window is closing
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
        using (var bad = await client.PutAsJsonAsync("/api/shifts/schedule", new { enabled = true, days = new[] { 1 }, start = "17:00", end = "08:00", timeZone = "UTC" }))
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        using (var saved = await client.PutAsJsonAsync("/api/shifts/schedule", new { enabled = true, days = new[] { 0, 1, 2, 3, 4, 5, 6 }, start = "00:00", end = "23:59", timeZone = "UTC", cycleMinutes = 60, turnBudget = 8, tokenBudget = 15000 }))
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var schedule = factory.Services.GetRequiredService<WorkSchedule>();
        var shift = await schedule.Tick(CancellationToken.None);
        Assert.NotNull(shift);
        Assert.StartsWith("Work schedule (set by", shift!.StartedBy); Assert.Equal(15000, shift.TokenBudget); Assert.Equal(8, shift.TurnBudget);
        Assert.True(shift.EndsAt <= DateTimeOffset.UtcNow.Date.AddDays(1));
        Assert.Null(await schedule.Tick(CancellationToken.None)); // already on shift
        using (var stop = await client.PostAsJsonAsync($"/api/shifts/{shift.Id}/stop", new { })) Assert.True(stop.IsSuccessStatusCode);
        Assert.Null(await schedule.Tick(CancellationToken.None)); // stopped by the owner: not restarted today
    }

    [Fact] public async Task TheWeeklyRhythmWritesAMondayPlanAndAFridayUpdateFromTheRecords()
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
        async Task<JsonElement> Send(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body ?? new { }) };
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, path + " → " + (int)response.StatusCode + " " + text);
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        await Send(HttpMethod.Put, "/api/objectives", new { expectedVersion = 0, content = new { northStar = new { name = "Signups", metric = "signups", target = 3000, unit = "per month", by = Day(60), why = "Trials follow signups" },
            objectives = Array.Empty<object>(), competitors = Array.Empty<object>(), currentFocus = "Fix the signup funnel", nonGoals = Array.Empty<string>() } });
        await Send(HttpMethod.Post, "/api/scorecard/import", new { requestId = "w-1", csv = SeriesCsv(21, i => i <= 7 ? 130 : 100, _ => 12), source = "Test export" }); // yesterday back 7 days: 130; the 7 before: 100
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "w-t", title = "Rewrite the pricing page", status = "ready", priority = "high", next_action = "Two options.", action_state = "agent_ready" });

        var plan = await Send(HttpMethod.Post, "/api/weekly/plan");
        var wiki = await Send(HttpMethod.Get, "/api/company-wiki");
        var body = wiki.EnumerateArray().Single(page => page.GetProperty("id").GetString() == plan.GetProperty("wikiId").GetString()).GetProperty("body").GetString()!;
        Assert.Contains("## Focus this week", body); Assert.Contains("- Fix the signup funnel", body);
        Assert.Contains("- Rewrite the pricing page (high priority)", body); Assert.Contains("**Signups**", body);
        var update = await Send(HttpMethod.Post, "/api/weekly/update");
        wiki = await Send(HttpMethod.Get, "/api/company-wiki");
        body = wiki.EnumerateArray().Single(page => page.GetProperty("id").GetString() == update.GetProperty("wikiId").GetString()).GetProperty("body").GetString()!;
        Assert.Contains("## Numbers (last 7 days vs. the 7 before)", body); Assert.Contains("**Signups**: 130 a day (+30% vs. the week before, good)", body);
        Assert.Contains("Nothing was published this week.", body); Assert.Contains("## What we learned", body);
        var library = await Send(HttpMethod.Get, "/api/workspace-library");
        Assert.Contains(library.GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("key").GetString() == "wiki:" + update.GetProperty("wikiId").GetString() && entry.GetProperty("folder").GetString() == "Reports/Weekly");

        // On its own: Monday after 8:00 the plan, once; Friday after 16:00 the update, once.
        await Send(HttpMethod.Put, "/api/weekly", new { enabled = true, timeZone = "UTC", planDay = 1, planTime = "08:00", updateDay = 5, updateTime = "16:00", emailDraft = false });
        var weekly = factory.Services.GetRequiredService<WeeklyRhythm>();
        weekly.Clock = () => new DateTimeOffset(2026, 10, 5, 7, 30, 0, TimeSpan.Zero); // Monday before 8
        Assert.Null(await weekly.Tick(CancellationToken.None));
        weekly.Clock = () => new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
        Assert.Equal("plan", (await weekly.Tick(CancellationToken.None))!.Kind);
        // The first week of October: September's monthly report, once.
        var month = (await weekly.Tick(CancellationToken.None))!;
        Assert.Equal(("month", "2026-09", "Monthly report: September 2026"), (month.Kind, month.Week, month.Title));
        Assert.Null(await weekly.Tick(CancellationToken.None));
        var monthly = (await Send(HttpMethod.Get, "/api/company-wiki")).EnumerateArray().Single(page => page.GetProperty("id").GetString() == month.WikiId).GetProperty("body").GetString()!;
        Assert.Contains("## Numbers (daily average, this month vs. the month before)", monthly);
        Assert.Contains("## Experiments", monthly); Assert.Contains("_Model spend this month:", monthly);
        // Writing the same month again updates its document rather than adding a second one.
        // A second copy an older version left behind is archived when the month is written again.
        var reports = factory.Services.GetRequiredService<CompanyWiki>();
        var twin = reports.Save(new WikiChange(Guid.NewGuid().ToString("N"), null, 0, "company", "company", "Monthly report: September 2026", "An older copy.", "fact", "active"), "Marketing employee (weekly)");
        Assert.Equal(month.WikiId, (await weekly.Write("month", CancellationToken.None)).WikiId);
        Assert.Equal("archived", reports.List().Single(page => page.Id == twin.Id).Status);
        Assert.Contains((await Send(HttpMethod.Get, "/api/workspace-library")).GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("key").GetString() == "wiki:" + month.WikiId && entry.GetProperty("folder").GetString() == "Reports/Monthly");
        weekly.Clock = () => new DateTimeOffset(2026, 10, 9, 16, 30, 0, TimeSpan.Zero); // Friday
        Assert.Equal("update", (await weekly.Tick(CancellationToken.None))!.Kind);
        Assert.Null(await weekly.Tick(CancellationToken.None));
        weekly.Clock = () => new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero); // Saturday: no Monday plan for a finished week
        Assert.Null(await weekly.Tick(CancellationToken.None));
        Assert.Equal(5, (await Send(HttpMethod.Get, "/api/weekly")).GetProperty("latest").GetArrayLength());
    }

    [Fact] public void BigPacketsAreTrimmedToFitTheMeterAndKeepTheirShape()
    {
        var big = JsonSerializer.SerializeToElement(new { brief = new { product = "First Employee", audience = new string('a', 900) },
            sources = Enumerable.Range(1, 4).Select(n => new { number = n, title = "Source " + n, text = new string('s', 3000) }),
            related = Enumerable.Range(1, 3).Select(n => new { title = "Doc " + n, excerpt = new string('r', 1800) }), memory = new { notebook = new string('n', 3000) } });
        var fitted = EmployeeShifts.Fit(big, "preamble ");
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize("preamble " + fitted.GetRawText())) <= EmployeeShifts.PromptBytes);
        Assert.Equal(4, fitted.GetProperty("sources").GetArrayLength()); Assert.Equal("Source 3", fitted.GetProperty("sources")[2].GetProperty("title").GetString());
        Assert.Equal("First Employee", fitted.GetProperty("brief").GetProperty("product").GetString());
        Assert.EndsWith("…", fitted.GetProperty("sources")[0].GetProperty("text").GetString());
        var small = JsonSerializer.SerializeToElement(new { title = "short" });
        Assert.Equal(small.GetRawText(), EmployeeShifts.Fit(small, "p").GetRawText());
    }

    [Fact] public void RepeatedWorkIsRecognizedByItsWords()
    {
        Assert.True(EmployeeShifts.Similar("Compare three candidate buyer segments", "Research and compare first buyer segments"));
        Assert.False(EmployeeShifts.Similar("Compare three candidate buyer segments", "Draft a LinkedIn post introducing First Employee"));
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
        // Brief language in public copy: the reader is "you".
        Assert.Equal("warn", CampaignQa.Check("Email", "https://mail.google.com/", "Subject: Hi\n\nWhen the owner approves your access, you'll hear from us.").Checks.Single(check => check.Id == "voice").Result);
        Assert.Equal("pass", CampaignQa.Check("Email", "https://mail.google.com/", "Subject: Hi\n\nWhen we approve your access, you'll hear from us.").Checks.Single(check => check.Id == "voice").Result);
        var good = CampaignQa.Check("LinkedIn", "https://www.linkedin.com/feed/", "Founders keep telling us follow-through is the hard part. Learn more: https://example.com/?utm_source=linkedin&utm_campaign=q3");
        Assert.Equal("ready", good.Status);
    }

    /// <summary>A live runtime whose first turn the provider refuses, as a 401 does; the rest go through.</summary>
    sealed class RefusedOnce(IShiftRuntime inner) : IShiftRuntime
    {
        bool refused;
        public string Name => "openclaw";
        public bool Live => true;
        public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation)
        {
            if (refused) return inner.Turn(request, cancellation);
            refused = true;
            throw new ShiftTurnFailedException("The model provider refused this turn (Authentication failed (provider returned HTTP 401)). Its reservation stays counted; the next turn goes ahead.", 25000);
        }
    }

    [Fact] public async Task AProviderRefusalCostsOneTurnButDoesNotStopTheShift()
    {
        var canned = new CannedRuntime { TokensPerTurn = 1000 };
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host"));
            builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:FixtureLedger", Path.Combine(root, "ledger"));
            builder.UseSetting("Marketing:FixtureRunwayScript", Path.Combine(directory!.FullName, "business", "agent", "hire", "bin", "runway.py"));
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => services.AddSingleton<IShiftRuntime>(new RefusedOnce(canned)));
        });
        var shifts = factory.Services.GetRequiredService<EmployeeShifts>();
        shifts.Research = (_, _) => Task.FromResult<ResearchSource[]>([]);
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
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-1", title = "Weekly founder note", status = "ready", priority = "normal", next_action = "Do it.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/shifts", new { requestId = "shift-p", hours = 8, turnBudget = 10 });
        var shift = await Send(HttpMethod.Post, "/api/shifts/shift-p/cycle", new { });
        var plan = shift.GetProperty("cycles")[0].GetProperty("stages")[1];
        Assert.Equal("failed", plan.GetProperty("status").GetString());
        Assert.Contains("HTTP 401", plan.GetProperty("summary").GetString());
        // The refused turn is counted, reservation and all, and the shift is still running.
        Assert.Equal("running", shift.GetProperty("status").GetString());
        Assert.Equal(1, shift.GetProperty("turnsUsed").GetInt32());
        Assert.Equal(25000, shift.GetProperty("tokensUsed").GetInt32());
        shift = await Send(HttpMethod.Post, "/api/shifts/shift-p/cycle", new { });
        Assert.Equal("done", shift.GetProperty("cycles")[1].GetProperty("stages")[1].GetProperty("status").GetString());
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

        // The owner sets the north star; conflicting saves are refused.
        var goals = await Send(HttpMethod.Put, "/api/objectives", new { expectedVersion = 0, content = new { northStar = new { name = "Signups", metric = "signups", target = 3000, unit = "per month", by = Day(60), why = "Trials follow signups" },
            objectives = new[] { new { title = "Recover signup conversion", keyResults = new[] { new { text = "Signups back to 100 a day", metric = "signups", target = 100 } } } }, competitors = Array.Empty<object>(), currentFocus = "The funnel", nonGoals = new[] { "Paid ads" } } });
        Assert.Equal(1, goals.GetProperty("revision").GetProperty("version").GetInt32());
        using (var stale = await client.PutAsJsonAsync("/api/objectives", new { expectedVersion = 0, content = new { objectives = Array.Empty<object>(), competitors = Array.Empty<object>(), currentFocus = "", nonGoals = Array.Empty<string>() } }))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

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
        using (var read = await other.GetAsync("/api/objectives")) Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        await Send(HttpMethod.Put, "/api/team/roles/" + teammate.PrincipalId, new { role = "contributor" });
        using (var denied = await other.PutAsJsonAsync("/api/objectives", new { expectedVersion = 1, content = new { objectives = Array.Empty<object>(), competitors = Array.Empty<object>(), currentFocus = "", nonGoals = Array.Empty<string>() } }))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var progress = await Send(HttpMethod.Get, "/api/objectives");
        Assert.True(progress.GetProperty("progress").GetProperty("latest").GetDouble() > 0);
    }
}
