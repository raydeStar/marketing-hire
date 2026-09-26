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

/// <summary>Work about customers starts from the owner's own notes of customer conversations.</summary>
public sealed class CustomerNotesTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "notes-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++) { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }

    sealed class NotesRuntime : IShiftRuntime
    {
        public List<JsonElement> Sources { get; } = [];
        public string Name => "scripted";
        public bool Live => false;
        public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation)
        {
            var data = request.Data;
            string reply;
            if (request.Stage == "prioritize")
                reply = JsonSerializer.Serialize(new { priorities = data.GetProperty("queue").EnumerateArray().Select(task => new { title = task.GetProperty("title").GetString(), reason = "Assigned", deliverable = "document", taskId = task.GetProperty("id").GetString() }), newTasks = Array.Empty<object>(), note = "Notes." });
            else if (request.Stage == "create")
            {
                Sources.Add(data.GetProperty("sources").Clone());
                reply = JsonSerializer.Serialize(new { deliverable = "document", title = "What founders told us", kind = "fact", folder = "Research",
                    body = "Two founders said marketing happens \"after midnight, if at all\" [1] and that they would not let a tool post without them [2]. Two conversations, not a pattern yet." });
            }
            else if (request.Stage == "review")
                reply = JsonSerializer.Serialize(new { scores = new { strategy = 4, customer = 5, distinctive = 4, channel = 4, brand = 4, action = 4, claims = 5, shareable = 4 }, issues = Array.Empty<string>(), revised = (object?)null });
            else
                reply = JsonSerializer.Serialize(new { learnings = Array.Empty<string>(), nextShiftFocus = "", notebook = new { known = Array.Empty<string>(), decided = Array.Empty<string>(), openQuestions = Array.Empty<string>(), worked = Array.Empty<string>(), didNotWork = Array.Empty<string>(), resolved = Array.Empty<string>() } });
            return Task.FromResult(new ShiftTurnResult(reply, 500));
        }
    }
    sealed class Loopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => { app.Use((context, proceed) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return proceed(); }); next(app); };
    }

    [Fact] public async Task CustomerNotesInTheLibraryBecomeCitedSourcesForCustomerWork()
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

        // Two conversations: a Library document and a text file, both filed in Research → Customer notes. A third note elsewhere isn't read.
        var wiki = factory.Services.GetRequiredService<CompanyWiki>();
        var library = factory.Services.GetRequiredService<WorkspaceLibrary>();
        var store = factory.Services.GetRequiredService<Store>();
        var call = wiki.Save(new WikiChange("w-1", null, 0, "company", "company", "Call with Dana (agency founder)", "Dana: marketing happens after midnight, if at all.", "fact", "active"), "Owner");
        library.SaveEntry("wiki:" + call.Id, new LibraryEntryChange(library.View("").Version, "Research/Customer notes", []), "Owner", "owner");
        var file = store.AddUpload("call-sam.txt", Encoding.UTF8.GetBytes("Sam: I would never let a tool post without me."));
        library.SaveEntry("media:" + file.Id, new LibraryEntryChange(library.View("").Version, "Research/Customer notes", []), "Owner", "owner");
        var other = wiki.Save(new WikiChange("w-2", null, 0, "company", "company", "Unrelated", "Office wifi password notes.", "fact", "active"), "Owner");
        library.SaveEntry("wiki:" + other.Id, new LibraryEntryChange(library.View("").Version, "Company", []), "Owner", "owner");

        await Send(HttpMethod.Post, "/api/marketing/tasks", new { requestId = "t-voc", title = "Synthesize the customer interviews", status = "ready", priority = "high", next_action = "What did founders tell us?", action_state = "agent_ready" });
        await Send(HttpMethod.Post, "/api/shifts", new { requestId = "shift-voc", hours = 8, turnBudget = 10 });
        var shift = await Send(HttpMethod.Post, "/api/shifts/shift-voc/cycle");
        Assert.Contains("Read 2 customer notes from the Library.", shift.GetProperty("cycles")[0].GetProperty("stages")[2].GetProperty("summary").GetString());

        var sources = Assert.Single(runtime.Sources).EnumerateArray().ToArray();
        Assert.Equal(["Customer notes", "Customer notes"], sources.Select(item => item.GetProperty("via").GetString()));
        Assert.Contains(sources, item => item.GetProperty("text").GetString()!.Contains("after midnight"));
        Assert.Contains(sources, item => item.GetProperty("text").GetString() == "Sam: I would never let a tool post without me.");
        Assert.DoesNotContain(sources, item => item.GetProperty("text").GetString()!.Contains("wifi"));

        // Cited as the owner's notes, without links to anything outside the workspace.
        var page = Assert.Single(wiki.List(), item => item.Title == "What founders told us");
        Assert.Contains("the owner's customer notes (Library)", page.Body);
        Assert.DoesNotContain("library:", page.Body);
    }
}
