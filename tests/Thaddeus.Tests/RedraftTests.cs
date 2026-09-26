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

/// <summary>Work sent back with feedback comes back rewritten: a document as a new version of the same page, a post as a new draft.</summary>
public sealed class RedraftTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "redrafts-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++) { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }

    sealed class RedraftRuntime : IShiftRuntime
    {
        public List<JsonElement> Creates { get; } = [];
        public string Name => "scripted";
        public bool Live => false;
        public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation)
        {
            var data = request.Data;
            string reply;
            if (request.Stage == "prioritize")
                reply = JsonSerializer.Serialize(new { priorities = data.GetProperty("queue").EnumerateArray().Select(task => new { title = task.GetProperty("title").GetString(), reason = "Assigned", deliverable = "document", taskId = task.GetProperty("id").GetString() }), newTasks = Array.Empty<object>(), note = "Queue." });
            else if (request.Stage == "create")
            {
                Creates.Add(data.Clone());
                var asked = data.TryGetProperty("redraft", out var redraft) && redraft.ValueKind == JsonValueKind.Object;
                var title = data.GetProperty("task").GetProperty("title").GetString()!;
                reply = asked && redraft.GetProperty("of").GetString()!.StartsWith("draft:")
                    ? JsonSerializer.Serialize(new { deliverable = "draft", title = "LinkedIn post, again", channel = redraft.GetProperty("channel").GetString(), destination = redraft.GetProperty("destination").GetString(),
                        body = "A founder told us marketing is the task they drop first. Here is how we keep it moving without losing the final say.", rationale = "Led with the customer story." })
                    : asked ? JsonSerializer.Serialize(new { deliverable = "document", title = "Positioning memo", body = "Rewritten: the customer story first, then the approval promise, then proof.", kind = "hypothesis", folder = "Research" })
                    : title.Contains("post") ? JsonSerializer.Serialize(new { deliverable = "draft", title = "LinkedIn post", channel = "LinkedIn", destination = "https://www.linkedin.com/feed/", body = "HireZero works shifts on marketing and asks before anything goes out.", rationale = "Announces it." })
                    : JsonSerializer.Serialize(new { deliverable = "document", title = "Positioning memo", body = "The approval promise first, then proof, then the customer.", kind = "hypothesis", folder = "Research" });
            }
            else if (request.Stage == "review")
                reply = JsonSerializer.Serialize(new { scores = new { strategy = 5, customer = 5, distinctive = 5, channel = 5, brand = 5, action = 5, claims = 5, shareable = 5 }, issues = Array.Empty<string>(), revised = (object?)null });
            else
                reply = JsonSerializer.Serialize(new { learnings = Array.Empty<string>(), nextShiftFocus = "", notebook = new { known = Array.Empty<string>(), decided = Array.Empty<string>(), openQuestions = Array.Empty<string>(), worked = Array.Empty<string>(), didNotWork = Array.Empty<string>(), resolved = Array.Empty<string>() } });
            return Task.FromResult(new ShiftTurnResult(reply, 300));
        }
    }
    sealed class Loopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => { app.Use((context, proceed) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return proceed(); }); next(app); };
    }

    [Fact] public async Task SentBackWorkIsRewrittenToAnswerTheFeedback()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        var runtime = new RedraftRuntime();
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
        async Task<(HttpStatusCode Status, JsonElement Body)> Call(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body ?? new { }) };
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, text.Length > 0 ? JsonDocument.Parse(text).RootElement.Clone() : default);
        }
        async Task<JsonElement> Send(HttpMethod method, string path, object? body = null)
        {
            var (status, result) = await Call(method, path, body);
            Assert.True((int)status < 300, path + " → " + (int)status + " " + result);
            return result;
        }

        // This workspace is an affiliate's: the employee writes as them, disclosed, with their link.
        Assert.Equal(HttpStatusCode.BadRequest, (await Call(HttpMethod.Put, "/api/workspace-role", new { role = "boss" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Call(HttpMethod.Put, "/api/workspace-role", new { role = "affiliate", offer = "http://hirezero.app/?ref=maya" })).Status);
        await Send(HttpMethod.Put, "/api/workspace-role", new { role = "affiliate", person = "Maya reviews tools for small-shop owners on YouTube.", offer = "https://hirezero.app/?ref=maya" });
        Assert.Equal("affiliate", (await Send(HttpMethod.Get, "/api/workspace-role")).GetProperty("role").GetString());

        // A first shift writes a memo and a post.
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-memo", title = "Positioning memo", status = "ready", priority = "high", next_action = "Write it.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-post", title = "LinkedIn post about shifts", status = "ready", priority = "normal", next_action = "Draft it.", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/shifts", new { requestId = "shift-r1", hours = 8, turnBudget = 30 });
        await Send(HttpMethod.Post, "/api/shifts/shift-r1/cycle");
        var whose = runtime.Creates[0].GetProperty("objectives").GetProperty("whoseMarketing").GetString()!;
        Assert.Contains("independent affiliate", whose); Assert.Contains("https://hirezero.app/?ref=maya", whose); Assert.Contains(WorkspaceRole.DefaultDisclosure, whose);
        Assert.Contains("Maya reviews tools", await factory.Services.GetRequiredService<EmployeeShifts>().ChatContext(CancellationToken.None));
        var wiki = factory.Services.GetRequiredService<CompanyWiki>();
        var memo = Assert.Single(wiki.List(), page => page.Title == "Positioning memo");
        var draft = (await Send(HttpMethod.Get, "/api/marketing/state")).GetProperty("drafts").EnumerateArray().Single(item => item.GetProperty("channel").GetString() == "LinkedIn");
        var draftId = draft.GetProperty("id").GetInt32();

        // Feedback is required; the owner sends both back.
        Assert.Equal(HttpStatusCode.BadRequest, (await Call(HttpMethod.Post, "/api/redrafts", new { key = "wiki:" + memo.Id, feedback = "" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Call(HttpMethod.Post, "/api/redrafts", new { key = "task:1", feedback = "Shorter." })).Status);
        var sent = await Send(HttpMethod.Post, "/api/redrafts", new { key = "wiki:" + memo.Id, feedback = "Lead with the customer story." });
        Assert.True(sent.GetProperty("queued").GetBoolean());
        Assert.False((await Send(HttpMethod.Post, "/api/redrafts", new { key = "wiki:" + memo.Id, feedback = "Also shorter." })).GetProperty("queued").GetBoolean());
        // The owner attaches an image from the Library to the post; a text file can't go with a post, and four is the most.
        var store = factory.Services.GetRequiredService<Store>();
        var png = store.AddUpload("keep-the-final-say.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));
        var notes = store.AddUpload("notes.txt", "plain notes"u8.ToArray());
        var attached = await Send(HttpMethod.Post, $"/api/drafts/{draftId}/media", new { mediaId = png.Id, attach = true });
        Assert.Equal("keep-the-final-say.png", Assert.Single(attached.EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await Call(HttpMethod.Post, $"/api/drafts/{draftId}/media", new { mediaId = notes.Id, attach = true })).Status);
        Assert.Equal(png.Id, (await Send(HttpMethod.Get, "/api/drafts/media")).GetProperty(draftId.ToString())[0].GetProperty("id").GetString());
        await Send(HttpMethod.Post, "/api/redrafts", new { key = "draft:" + draftId, feedback = "Too salesy; open with what a founder told us." });
        var tasks = (await Send(HttpMethod.Get, "/api/marketing/state")).GetProperty("tasks").EnumerateArray().ToArray();
        Assert.Contains(tasks, task => task.GetProperty("title").GetString() == "Redraft: Positioning memo" && task.GetProperty("priority").GetString() == "high");

        // The next cycle rewrites them with the original and the feedback in hand.
        var cycle = await Send(HttpMethod.Post, "/api/shifts/shift-r1/cycle");
        var memoPacket = runtime.Creates.Last(packet => packet.TryGetProperty("redraft", out var r) && r.ValueKind == JsonValueKind.Object && r.GetProperty("of").GetString()!.StartsWith("wiki:"));
        Assert.Equal("Lead with the customer story.", memoPacket.GetProperty("redraft").GetProperty("feedback").GetString());
        Assert.StartsWith("The approval promise first", memoPacket.GetProperty("redraft").GetProperty("original").GetString());
        var rewritten = Assert.Single(wiki.List(), page => page.Title == "Positioning memo");
        Assert.Equal((memo.Id, 2), (rewritten.Id, rewritten.Version));
        Assert.StartsWith("Rewritten: the customer story first", rewritten.Body);
        Assert.Contains("Redrafted Positioning memo after the owner's feedback.", cycle.GetProperty("cycles").EnumerateArray().Last().GetProperty("stages")[2].GetProperty("summary").GetString());

        var postPacket = runtime.Creates.Last(packet => packet.TryGetProperty("redraft", out var r) && r.ValueKind == JsonValueKind.Object && r.GetProperty("of").GetString()!.StartsWith("draft:"));
        Assert.Equal("draft", postPacket.GetProperty("priority").GetProperty("deliverable").GetString());
        var again = (await Send(HttpMethod.Get, "/api/marketing/state")).GetProperty("drafts").EnumerateArray().First(item => item.GetProperty("id").GetInt32() != draftId && item.GetProperty("channel").GetString() == "LinkedIn");
        Assert.StartsWith($"Redraft of LinkedIn draft #{draftId} after your feedback", again.GetProperty("rationale").GetString());
        Assert.All(factory.Services.GetRequiredService<Redrafts>().All(), item => Assert.NotNull(item.DoneAt));
        // The redraft carries the image the owner attached to the original.
        Assert.Equal(png.Id, Assert.Single(factory.Services.GetRequiredService<DraftMedia>().For(again.GetProperty("id").GetInt32().ToString())).Id);

        // The feedback is remembered and on the record.
        Assert.Contains(factory.Services.GetRequiredService<EmployeeMemory>().Feedback(), entry => entry.Verdict == "redraft" && entry.Note == "Lead with the customer story.");
        Assert.Contains(factory.Services.GetRequiredService<DecisionLog>().Entries(), entry => entry.Decision == "Sent back for a redraft" && entry.What == "Positioning memo");
    }
}
