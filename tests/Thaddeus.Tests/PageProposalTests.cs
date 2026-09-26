using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class PageProposalTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "page-copy-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++) { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }

    sealed class Vault : ICredentialVault
    {
        readonly Dictionary<(string, string), string> entries = [];
        public Task<string?> Execute(string operation, string scope, string id, string? value, CancellationToken cancellation)
        {
            if (operation == "write") entries[(scope, id)] = value!;
            return Task.FromResult(operation == "read" ? entries.GetValueOrDefault((scope, id)) : null);
        }
    }
    sealed class Loopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => { app.Use((context, proceed) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return proceed(); }); next(app); };
    }
    sealed class FakeWordPress : HttpMessageHandler
    {
        public List<string> Pages { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url == "https://acme.test/wp-json/wp/v2/users/me?context=edit") return Json(new { name = "Mark" });
            if (url == "https://acme.test/wp-json/wp/v2/pages") { Pages.Add(await request.Content!.ReadAsStringAsync(cancellationToken)); return Json(new { id = 42, status = "draft", link = "https://acme.test/?page_id=42" }, HttpStatusCode.Created); }
            return new(HttpStatusCode.NotFound);
        }
        static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    }
    /// <summary>A model that plans one page deliverable for the queued task and writes it.</summary>
    sealed class PageRuntime : IShiftRuntime
    {
        public string Name => "scripted";
        public bool Live => false;
        public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation)
        {
            var data = request.Data;
            var reply = request.Stage switch
            {
                "prioritize" => JsonSerializer.Serialize(new { priorities = data.GetProperty("queue").EnumerateArray().Select(task => new { title = task.GetProperty("title").GetString(), reason = "Assigned", deliverable = "page", taskId = task.GetProperty("id").GetString() }), newTasks = Array.Empty<object>(), note = "The page." }),
                "create" => JsonSerializer.Serialize(new { deliverable = "page", page = "https://acme.test/pricing", title = "Pricing page", body = "# Plans that ask first\n\nStarter is for founders doing their own marketing. Every post waits for your approval.", rationale = "Leads with approval, the brief's first proof point." }),
                "review" => JsonSerializer.Serialize(new { scores = new { strategy = 4, customer = 4, distinctive = 4, channel = 4, brand = 4, action = 4, claims = 4, shareable = 4 }, issues = Array.Empty<string>(), revised = (object?)null }),
                _ => JsonSerializer.Serialize(new { learnings = Array.Empty<string>(), nextShiftFocus = "", notebook = new { known = Array.Empty<string>(), decided = Array.Empty<string>(), openQuestions = Array.Empty<string>(), worked = Array.Empty<string>(), didNotWork = Array.Empty<string>(), resolved = Array.Empty<string>() } }),
            };
            return Task.FromResult(new ShiftTurnResult(reply, 500));
        }
    }

    [Fact] public async Task AShiftProposesPageCopyTheOwnerDecidesAndWordPressGetsOnlyADraft()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        var wordpress = new FakeWordPress();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host")); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:FixtureLedger", Path.Combine(root, "ledger"));
            builder.UseSetting("Marketing:FixtureRunwayScript", Path.Combine(directory!.FullName, "business", "agent", "hire", "bin", "runway.py"));
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => { services.AddSingleton<IShiftRuntime>(new PageRuntime()); services.AddSingleton<ICredentialVault>(new Vault()); services.AddSingleton<IStartupFilter, Loopback>(); });
        });
        var shifts = factory.Services.GetRequiredService<EmployeeShifts>();
        shifts.ReadSite = (url, sites, _) => { Assert.Equal(["acme.test"], sites); return Task.FromResult((url, "Pricing", "Pricing. Plans for teams. Contact sales.")); };
        factory.Services.GetRequiredService<Publishing>().Handler = () => wordpress;
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

        // Page proposals are only for the owner's own site.
        var proposals = factory.Services.GetRequiredService<PageProposals>();
        Assert.Throws<InvalidOperationException>(() => proposals.Propose("https://acme.test/x", "x", "", new string('a', 60), "r", "t"));
        await Send(HttpMethod.Put, "/api/objectives", new { expectedVersion = 0, content = new { objectives = Array.Empty<object>(), competitors = Array.Empty<object>(), currentFocus = "", nonGoals = Array.Empty<string>(), ownSite = "acme.test", researchSites = new[] { "rival.test" } } });
        Assert.Throws<InvalidOperationException>(() => proposals.Propose("https://rival.test/pricing", "x", "", new string('a', 60), "r", "t"));

        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-page", title = "New copy for the pricing page", status = "ready", priority = "normal", next_action = "Propose new copy.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/shifts", new { requestId = "shift-page", hours = 8, turnBudget = 10 });
        var shift = await Send(HttpMethod.Post, "/api/shifts/shift-page/cycle");
        Assert.Contains("Proposed new copy for acme.test/pricing", shift.GetProperty("cycles")[0].GetProperty("stages")[2].GetProperty("summary").GetString());
        var decision = shift.GetProperty("decisions").EnumerateArray().Single().GetString()!;
        Assert.StartsWith("pagecopy:", decision);

        var listed = await Send(HttpMethod.Get, "/api/page-proposals");
        Assert.Equal("acme.test", listed.GetProperty("ownSite").GetString());
        var proposal = listed.GetProperty("proposals")[0];
        var id = proposal.GetProperty("id").GetString()!;
        Assert.Equal(decision.Split(' ')[0], "pagecopy:" + id);
        Assert.Equal("Pricing. Plans for teams. Contact sales.", proposal.GetProperty("before").GetString());
        Assert.StartsWith("# Plans that ask first", proposal.GetProperty("after").GetString());
        Assert.Equal("pending", proposal.GetProperty("status").GetString());
        // Made for a task, it waits in the Inbox as that task, once; not again as page copy.
        async Task<string[]> Waiting() => [.. (await Send(HttpMethod.Get, "/api/attention")).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!)];
        Assert.Empty(await Waiting());
        Assert.Contains($"link:pagecopy:{id}:", string.Join(" ", factory.Services.GetRequiredService<EmployeeShifts>().History().Single().Handled));

        // Nothing is saved anywhere until the owner approves; then WordPress gets a draft page, never the live page.
        using (var early = await client.PostAsJsonAsync($"/api/page-proposals/{id}/wordpress", new { connectionId = "none" })) Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        var approved = await Send(HttpMethod.Post, $"/api/page-proposals/{id}/decision", new { decision = "approved", note = "Good headline." });
        Assert.Equal("approved", approved.GetProperty("status").GetString());
        using (var twice = await client.PostAsJsonAsync($"/api/page-proposals/{id}/decision", new { decision = "rejected" })) Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        // Approved: the next cycle closes the task, and putting the copy on the page stays on the owner's list until it's applied.
        Assert.Equal(["pagecopy:" + id + ":apply"], await Waiting());
        var next = await Send(HttpMethod.Post, "/api/shifts/shift-page/cycle");
        Assert.Contains("Closed 1 task(s) the owner decided.", next.GetProperty("cycles")[1].GetProperty("stages")[0].GetProperty("summary").GetString());
        var closed = (await Send(HttpMethod.Get, "/api/marketing/state")).GetProperty("tasks").EnumerateArray().Single(item => item.GetProperty("title").GetString() == "New copy for the pricing page");
        Assert.Equal("done", closed.GetProperty("status").GetString());
        var site = await Send(HttpMethod.Post, "/api/publishing/connect/wordpress", new { address = "https://acme.test", account = "mark", secret = "abcd efgh ijkl mnop" });
        var applied = await Send(HttpMethod.Post, $"/api/page-proposals/{id}/wordpress", new { connectionId = site.GetProperty("id").GetString() });
        Assert.Equal("applied", applied.GetProperty("status").GetString());
        Assert.Empty(await Waiting());
        Assert.Equal("https://acme.test/wp-admin/post.php?post=42&action=edit", applied.GetProperty("appliedUrl").GetString());
        using var page = JsonDocument.Parse(Assert.Single(wordpress.Pages));
        Assert.Equal("draft", page.RootElement.GetProperty("status").GetString());
        Assert.Equal("Draft: Pricing page", page.RootElement.GetProperty("title").GetString());
    }
}
