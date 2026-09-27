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

/// <summary>Several posts for one assignment become separate drafts, each in its network's own format.</summary>
public sealed class DraftSeriesTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "series-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++) { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }

    sealed class SeriesRuntime : IShiftRuntime
    {
        public string Name => "scripted";
        public bool Live => false;
        public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation)
        {
            var data = request.Data;
            string reply;
            if (request.Stage == "prioritize")
                reply = JsonSerializer.Serialize(new { priorities = data.GetProperty("queue").EnumerateArray().Select(task => new { title = task.GetProperty("title").GetString(), reason = "Assigned", deliverable = "draft", taskId = task.GetProperty("id").GetString() }), newTasks = Array.Empty<object>(), note = "Launch posts." });
            else if (request.Stage == "create" && data.GetProperty("task").GetProperty("title").GetString()!.StartsWith("Three"))
                reply = JsonSerializer.Serialize(new
                {
                    deliverable = "draft", title = "Launch-week LinkedIn posts", channel = "LinkedIn", body = "ignored",
                    image = new { text = "Nothing goes out without your yes.", sub = "HireZero", look = "accent" },
                    drafts = new[]
                    {
                        new { channel = "LinkedIn", destination = (string?)null, body = "### Post 1\n\n**An AI marketing employee should ask before it posts.**\n\nNothing goes out until you approve it.", rationale = "Approval first." },
                        new { channel = "LinkedIn", destination = (string?)null, body = "What one shift did for HireZero itself, from the records: a plan, three drafts and a report.", rationale = "Proof from the records." },
                        new { channel = "LinkedIn", destination = (string?)null, body = "We're in the OpenClaw hackathon. Try it: [hirezero.app](https://hirezero.app/) and tell me what's missing.", rationale = "The ask." },
                        new { channel = "Product Hunt", destination = (string?)null, body = "Tagline: A marketing employee that asks first. Description: shifts, approvals, receipts.", rationale = "The listing." },
                    }
                });
            else if (request.Stage == "create" && data.GetProperty("task").GetProperty("title").GetString()!.StartsWith("Long"))
                reply = JsonSerializer.Serialize(new { deliverable = "draft", title = "What one shift does", channel = "Blog", destination = "https://hirezero.app/blog/",
                    body = "## Sense\n\nThe shift starts by reading what changed.", rationale = "The walkthrough.", @continue = "Prioritize, create and the approval step" });
            else if (request.Stage == "continue")
            {
                Assert.EndsWith("reading what changed.", data.GetProperty("soFar").GetString());
                reply = JsonSerializer.Serialize(new { body = "## Decide\n\nNothing goes out until you approve it.", @continue = (string?)null });
            }
            else if (request.Stage == "create")
                reply = JsonSerializer.Serialize(new { deliverable = "draft", title = "Show HN: HireZero – an AI marketing employee that asks first", channel = "Hacker News", destination = "https://news.ycombinator.com/submit",
                    body = "I built HireZero, an open-source marketing employee that works shifts and asks before anything goes out.", rationale = "Plain and technical. " + new string('r', 980) });   // with the review note, over the ledger's 1,000
            else if (request.Stage == "review" && data.GetProperty("deliverable").GetProperty("title").GetString() == "Launch-week LinkedIn posts")
            {
                // The review sharpens the second post and keeps the --- lines, so the reviewed parts are used.
                var parts = data.GetProperty("deliverable").GetProperty("body").GetString()!.Split("\n\n---\n\n");
                Assert.Equal(4, parts.Length);
                parts[1] = "One shift, from the records: a launch plan, three drafts and a report. Nothing was posted without me.";
                // The reviewer is told each part's channel, and a label it adds anyway is taken off.
                Assert.Equal(["LinkedIn", "LinkedIn", "LinkedIn", "Product Hunt"], data.GetProperty("deliverable").GetProperty("series").EnumerateArray().Select(item => item.GetString()!));
                parts[2] = "LinkedIn\n\n" + parts[2];
                reply = JsonSerializer.Serialize(new { scores = new { strategy = 4, customer = 3, distinctive = 4, channel = 4, brand = 4, action = 4, claims = 4, shareable = 3 }, issues = new[] { "Post 2 needs proof" }, revised = new { title = "Launch-week LinkedIn posts", body = string.Join("\n\n---\n\n", parts) } });
            }
            else if (request.Stage == "review")
                reply = JsonSerializer.Serialize(new { scores = new { strategy = 4, customer = 4, distinctive = 4, channel = 4, brand = 4, action = 4, claims = 4, shareable = 4 }, issues = Array.Empty<string>(), revised = (object?)null });
            else
                reply = JsonSerializer.Serialize(new { learnings = Array.Empty<string>(), nextShiftFocus = "", notebook = new { known = Array.Empty<string>(), decided = Array.Empty<string>(), openQuestions = Array.Empty<string>(), worked = Array.Empty<string>(), didNotWork = Array.Empty<string>(), resolved = Array.Empty<string>() } });
            return Task.FromResult(new ShiftTurnResult(reply, 500));
        }
    }
    sealed class Loopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => { app.Use((context, proceed) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return proceed(); }); next(app); };
    }

    [Fact] public async Task ASeriesBecomesSeparateDraftsInEachNetworksFormat()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host")); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:FixtureLedger", Path.Combine(root, "ledger"));
            builder.UseSetting("Marketing:FixtureRunwayScript", Path.Combine(directory!.FullName, "business", "agent", "hire", "bin", "runway.py"));
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => { services.AddSingleton<IShiftRuntime>(new SeriesRuntime()); services.AddSingleton<IStartupFilter, Loopback>(); });
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
        var cards = new List<(string Text, string Look, int Width, int Height)>();
        factory.Services.GetRequiredService<EmployeeShifts>().RenderImage = (text, sub, look, width, height, mark, _) =>
        { cards.Add((text, look, width, height)); return Task.FromResult<byte[]>([137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13]); };

        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-series", title = "Three LinkedIn posts for launch week", status = "ready", priority = "high", next_action = "Three posts, each its own draft.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-hn", title = "Show HN post", status = "ready", priority = "high", next_action = "A Show HN submission.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-long", title = "Long blog post", status = "ready", priority = "normal", next_action = "A long walkthrough.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/shifts", new { requestId = "shift-series", hours = 8, turnBudget = 12 });
        var shift = await Send(HttpMethod.Post, "/api/shifts/shift-series/cycle");
        var summary = shift.GetProperty("cycles")[0].GetProperty("stages")[2].GetProperty("summary").GetString()!;
        Assert.Contains("Drafted 4 posts (LinkedIn #", summary);
        Assert.Contains("Product Hunt as draft text", summary);   // nowhere to post it: kept as text, the rest still drafted
        // The series asked for a post image: one card, sized for LinkedIn, filed beside the first draft.
        Assert.Equal(("Nothing goes out without your yes.", "accent", 1200, 627), Assert.Single(cards));
        Assert.True(summary.Contains("Made its image (1200×627) in Library → Campaigns → Images."), summary);
        var image = Assert.Single(factory.Services.GetRequiredService<Store>().Uploads(), file => file.Name == "launch-week-linkedin-posts-image.png");
        Assert.Contains(factory.Services.GetRequiredService<WorkspaceLibrary>().View("").Entries, entry => entry.Key == "media:" + image.Id && entry.Folder == "Campaigns/Images" && entry.Tags.Any(tag => tag.StartsWith("draft-")));
        Assert.Equal(6, shift.GetProperty("decisions").GetArrayLength());
        Assert.Contains("Wrote part 2 of What one shift does.", summary);

        var state = await Send(HttpMethod.Get, "/api/marketing/state");
        var drafts = state.GetProperty("drafts").EnumerateArray().OrderBy(item => item.GetProperty("id").GetInt32()).ToArray();
        var linkedIn = drafts.Where(item => item.GetProperty("channel").GetString() == "LinkedIn").Select(item => item.GetProperty("content").GetString()!).ToArray();
        Assert.Equal<string>([
            "Post 1\n\nAn AI marketing employee should ask before it posts.\n\nNothing goes out until you approve it.",
            "One shift, from the records: a launch plan, three drafts and a report. Nothing was posted without me.",
            "We're in the OpenClaw hackathon. Try it: hirezero.app https://hirezero.app/ and tell me what's missing."], linkedIn);
        Assert.All(drafts.Where(item => item.GetProperty("channel").GetString() == "LinkedIn"), item => Assert.Equal("https://www.linkedin.com/feed/", item.GetProperty("destination").GetString()));
        // The long post arrives whole: both parts, one draft, Markdown kept for the blog.
        Assert.Equal("## Sense\n\nThe shift starts by reading what changed.\n\n## Decide\n\nNothing goes out until you approve it.", drafts.Single(item => item.GetProperty("channel").GetString() == "Blog").GetProperty("content").GetString());
        var hn = drafts.Single(item => item.GetProperty("channel").GetString() == "Hacker News");
        Assert.StartsWith("Title: Show HN: HireZero – an AI marketing employee that asks first\n\nI built HireZero", hn.GetProperty("content").GetString());
        var task = state.GetProperty("tasks").EnumerateArray().Single(item => item.GetProperty("title").GetString() == "Three LinkedIn posts for launch week");
        Assert.Matches(@"^Review drafts #\d+, #\d+, #\d+ in the cockpit; also the Product Hunt text in Library", task.GetProperty("next_action").GetString());
        var listing = Assert.Single(factory.Services.GetRequiredService<CompanyWiki>().List(), page => page.Title == "Launch-week LinkedIn posts: Product Hunt");
        Assert.Contains("Tagline: A marketing employee that asks first.", listing.Body);
    }

    [Fact] public async Task PostingTimesStartFromTheNetworksHabitsAndStayAnHourAway()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host")); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:ShiftPump", "off");
        });
        var publishing = factory.Services.GetRequiredService<Publishing>();
        DateTimeOffset Local(int day, int hour, int minute = 0) { var local = new DateTime(2026, 10, day, hour, minute, 0); return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)); }
        // October 5, 2026 is a Monday.
        Assert.Equal(Local(6, 8, 30), publishing.SuggestedTime("LinkedIn", Local(5, 12)).At);
        Assert.Equal(Local(7, 8, 30), publishing.SuggestedTime("LinkedIn", Local(6, 8)).At);   // 8:30 today is under an hour away
        Assert.Equal(Local(12, 9), publishing.SuggestedTime("X", Local(9, 9, 30)).At);          // Friday after nine: Monday
        Assert.Contains("not measured for you yet", publishing.SuggestedTime("Bluesky", Local(5, 12)).Why);
        await Task.CompletedTask;
    }

    [Fact] public void TheWorkUnderReviewIsTheLastThingTrimmed()
    {
        var body = string.Join(" ", Enumerable.Repeat("A launch-week plan line that must reach the reviewer whole.", 150));
        var packet = JsonSerializer.SerializeToElement(new { deliverable = new { title = "Plan", body }, brief = new { product_summary = new string('b', 9000) }, sources = new[] { new { text = new string('s', 6000) } } });
        var fitted = EmployeeShifts.Fit(packet, "preamble", ["body"]);
        Assert.Equal(body, fitted.GetProperty("deliverable").GetProperty("body").GetString());
        Assert.True(fitted.GetProperty("brief").GetProperty("product_summary").GetString()!.Length < 9000);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(fitted.GetRawText()) <= EmployeeShifts.PromptBytes);
        // Work too big to fit even with the context trimmed is still trimmed, rather than overflowing the prompt.
        var huge = JsonSerializer.SerializeToElement(new { deliverable = new { body = new string('x', 40000) } });
        Assert.True(EmployeeShifts.Fit(huge, "p", ["body"]).GetProperty("deliverable").GetProperty("body").GetString()!.Length < 40000);
    }

    [Fact] public void AssignedTasksFillTheRoomAPlanLeaves()
    {
        JsonElement Task(string id, string title, string priority, string next) => JsonSerializer.SerializeToElement(new { id, title, priority, next_action = next });
        var queue = new List<JsonElement> { Task("a", "Blog post #3", "high", "Write it."), Task("b", "Product Hunt launch kit", "normal", "A series of drafts: listing, LinkedIn post, X post."),
            Task("c", "45-second video", "normal", "Make a landscape video."), Task("d", "Interview kit", "low", "A document.") };
        var plan = JsonSerializer.SerializeToElement(new { priorities = new[] { new { title = "Blog post #3", reason = "Deadline", deliverable = "draft", taskId = "a" } }, newTasks = Array.Empty<object>(), note = "Product Hunt is later." });
        var (priorities, _, note) = EmployeeShifts.ValidatePriorities(plan, queue);
        Assert.Equal([("a", "draft"), ("b", "draft"), ("c", "video")], priorities.Select(item => (item.GetProperty("taskId").GetString()!, item.GetProperty("deliverable").GetString()!)));
        Assert.EndsWith("Added 2 assigned task(s) the plan left out.", note);
    }

    [Fact] public void TheOwnersSendBacksComeFirst()
    {
        // The live cycle: the plan took the video, a competitor memo and one redraft, leaving four send-backs waiting.
        JsonElement Task(string id, string title) => JsonSerializer.SerializeToElement(new { id, title, priority = "high", next_action = "Rewrite it." });
        var queue = new List<JsonElement> { Task("v", "Hackathon demo video"), Task("r1", "Redraft: LinkedIn draft #25"), Task("r2", "Redraft: Bluesky draft #24"), Task("r3", "Redraft: X draft #23") };
        var plan = JsonSerializer.SerializeToElement(new { priorities = new object[] {
            new { title = "Hackathon demo video", reason = "Deadline", deliverable = "video", taskId = "v" },
            new { title = "Jasper pricing change", reason = "Signal", deliverable = "document", signalRef = "s1" },
            new { title = "Redraft: LinkedIn draft #25", reason = "Sent back", deliverable = "draft", taskId = "r1" } }, newTasks = Array.Empty<object>(), note = "Video first." });
        var (priorities, _, note) = EmployeeShifts.ValidatePriorities(plan, queue);
        Assert.Equal(["r1", "r2", "r3"], priorities.Select(item => item.GetProperty("taskId").GetString()!).Order());
        Assert.Contains("Put 2 of the owner's send-back(s) first.", note);
    }

    [Fact] public void ASeriesWrittenAsOneBodyIsSplitIntoItsDrafts()
    {
        var body = "1) Channel: Product Hunt\n\nTagline: A marketing employee that asks first\n\n---\n\n2) Channel: LinkedIn\n\nI'm launching HireZero.\n\n---\n\n**Channel:** X\n\nLaunching today.";
        var parts = EmployeeShifts.Series(JsonSerializer.SerializeToElement(new { deliverable = "document", title = "Launch kit", body, rationale = "Launch day." }))!;
        Assert.Equal([("Product Hunt", "Tagline: A marketing employee that asks first"), ("LinkedIn", "I'm launching HireZero."), ("X", "Launching today.")], parts.Select(part => (part.Channel, part.Body)));
        // A document that merely has sections isn't a series.
        Assert.Null(EmployeeShifts.Series(JsonSerializer.SerializeToElement(new { deliverable = "document", title = "Plan", body = "## Monday\n\nPlan.\n\n---\n\n## Tuesday\n\nMore." })));
        // A placeholder left for the owner is caught before launch.
        Assert.Equal("fail", CampaignQa.Check("LinkedIn", "https://www.linkedin.com/feed/", "Launching today. Product Hunt link: [add the approved Product Hunt URL before publishing]").Checks.Single(check => check.Id == "placeholders").Result);
    }

    [Fact] public void EachNetworkGetsItsOwnFormat()
    {
        Assert.Equal("Heading\n\nbold and italic, see docs https://x.test/a", EmployeeShifts.ForChannel("LinkedIn", "t", "## Heading\n\n**bold** and *italic*, see [docs](https://x.test/a)"));
        Assert.Equal("2 * 3 = 6, file_name stays", EmployeeShifts.ForChannel("X", "t", "2 * 3 = 6, file_name stays"));
        Assert.Equal("**Markdown** stays on a blog", EmployeeShifts.ForChannel("Blog", "t", "**Markdown** stays on a blog"));
        Assert.Equal("Title: Ask HN\n\nBody", EmployeeShifts.ForChannel("Hacker News", "Ask HN", "Body"));
        Assert.Equal("Title: Given\n\nBody", EmployeeShifts.ForChannel("Reddit", "Other", "Title: Given\n\nBody"));
        // "[Image text: …]" written into a post is the image it asked for, not part of the post.
        Assert.Equal("If you do marketing in spare hours…", EmployeeShifts.ForChannel("LinkedIn", "t", "[Image text: Keep the final say.]\n\nIf you do marketing in spare hours…"));
        Assert.Equal("Keep the final say.", EmployeeShifts.ImageInBody("[Image text: Keep the final say.]\n\nIf you do…"));
        // An older draft that still carries the line is posted without it.
        Assert.Equal("If you do…", EmployeeShifts.WithoutImageLine("[Image text: Keep the final say.]\n\nIf you do…"));
        // Folders the model names become the Library's own.
        Assert.Equal("Research/Competitive landscape", EmployeeShifts.EmployeeFolder("Library / Research / Competitive landscape"));
        Assert.Equal("Campaigns/Videos", EmployeeShifts.EmployeeFolder("Campaigns/Video"));
        Assert.Equal("Research", EmployeeShifts.EmployeeFolder("Research"));
        // The north star says its deadline once.
        var star = CompanyObjectives.Validate(new ObjectivesContent(new NorthStar("Qualified conversations", null, 20, "by Oct 31", "2026-10-31", "Why"), [], null, [], "", [])).NorthStar!;
        Assert.True(string.IsNullOrEmpty(star.Unit));
        Assert.Equal("signups", CompanyObjectives.Validate(new ObjectivesContent(new NorthStar("Signups", null, 20, "signups by Oct 31", "2026-10-31", "Why"), [], null, [], "", [])).NorthStar!.Unit);
    }
}
