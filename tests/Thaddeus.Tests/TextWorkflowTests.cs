using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

/// <summary>An owner who only texts: the employee texts them back, and a change they ask for by text runs only after their own yes.</summary>
public sealed class TextWorkflowTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "text-workflow-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++) { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }

    /// <summary>Plow's API as the employee sees it: its line, the owner's chat, and that chat's messages (newest first).</summary>
    sealed class FakePlow : HttpMessageHandler
    {
        public List<string> Sent { get; } = [];
        public List<object> Messages { get; } = [];
        public int Posts;
        public static object Owner(string body) => new { uid = Guid.NewGuid().ToString("N"), direction = "inbound", body, sender = new { type = "member", role = "owner", uid = "mem_owner" } };
        public static object Member(string body) => new { uid = Guid.NewGuid().ToString("N"), direction = "inbound", body, sender = new { type = "member", role = "member", uid = "mem_other" } };
        public static object Agent(string body) => new { uid = Guid.NewGuid().ToString("N"), direction = "outbound", body, sender = new { type = "agent", relationship = "self", line = new { uid = "ln_self" } } };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("Bearer plow_fixture", request.Headers.Authorization?.ToString());
            var path = request.RequestUri!.PathAndQuery;
            object body = path switch
            {
                "/v1/agents/me" => new { agent = new { uid = "0123456789abcdef0123456789abcdef" }, line = new { uid = "ln_self" } },
                "/v1/chats" => new { has_more = false, data = new object[] {
                    new { uid = "cht_group", status = "active", participants = new object[] { new { type = "agent", relationship = "self", line = new { uid = "ln_self" } }, new { type = "member", role = "owner" }, new { type = "member", role = "member" } } },
                    new { uid = "cht_owner", status = "active", participants = new object[] { new { type = "agent", relationship = "self", line = new { uid = "ln_self" } }, new { type = "member", role = "owner" } } } } },
                "/v1/chats/cht_owner/messages?limit=30" => new { has_more = false, data = Enumerable.Reverse(Messages).ToArray() },
                "/v1/chats/cht_owner/messages" when request.Method == HttpMethod.Post => Record(await request.Content!.ReadAsStringAsync(cancellationToken)),
                _ => throw new InvalidOperationException("Unexpected Plow call " + request.Method + " " + path),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        }
        object Record(string json) { Posts++; var text = JsonDocument.Parse(json).RootElement.GetProperty("body").GetString()!; Sent.Add(text); Messages.Add(Agent(text)); return new { uid = "msg_" + Posts }; }
    }

    static IConfiguration Plow(bool on = true) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Thaddeus:PhoneMode"] = on ? "plow" : "direct", ["PLOW_AGENT_TOKEN"] = "plow_fixture", ["PLOW_API_BASE"] = "https://api.plow.test",
        ["Thaddeus:PhoneOrigin"] = "https://0123456789abcdef0123456789abcdef.plow.run",
    }).Build();

    WebApplicationFactory<Program> Start()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"))) directory = directory.Parent;
        return factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host"));
            builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:FixtureLedger", Path.Combine(root, "ledger"));
            builder.UseSetting("Marketing:FixtureRunwayScript", Path.Combine(directory!.FullName, "business", "agent", "hire", "bin", "runway.py"));
            builder.UseSetting("Marketing:ShiftPump", "off");
        });
    }

    [Fact] public async Task TheOwnerIsTextedOnceInTheirOwnChat_AndNotWhileTheyWatchTheCockpit()
    {
        var store = new Store(Path.Combine(root, "store"));
        try
        {
            var plow = new FakePlow();
            var texts = new OwnerTexts(Plow(), store, NullLogger<OwnerTexts>.Instance, plow);
            Assert.True(texts.Enabled);
            Assert.True(await texts.Send("run:a", "Done with what you asked: 1 piece ready for your review.", CancellationToken.None));
            Assert.Equal(["Done with what you asked: 1 piece ready for your review."], plow.Sent);   // the owner's own chat, not the group
            // The same news twice is one text, even after a restart.
            Assert.False(await new OwnerTexts(Plow(), store, NullLogger<OwnerTexts>.Instance, plow).Send("run:a", "again", CancellationToken.None));
            // The owner has the cockpit open: they see it there.
            texts.Seen();
            Assert.False(await texts.Send("run:b", "Watching.", CancellationToken.None));
            texts.Clock = () => DateTimeOffset.UtcNow.AddMinutes(3);
            Assert.True(await texts.Send("run:b", "Not watching any more.", CancellationToken.None));
            // A local cockpit has no line: nothing is texted and Plow is never called.
            Assert.False(new OwnerTexts(Plow(on: false), store, NullLogger<OwnerTexts>.Instance, plow).Enabled);
            Assert.Equal(2, plow.Posts);
        }
        finally { store.Dispose(); }
    }

    [Fact] public async Task AChangeByTextRunsOnlyAfterTheOwnerWasAskedItWordForWordAndSaidYes()
    {
        Start();
        var services = factory!.Services;
        var marketing = services.GetRequiredService<MarketingBackend>();
        var added = await marketing.ShiftHire(null, "draft", "add", "--channel", "LinkedIn", "--destination", "https://www.linkedin.com/feed/",
            "--content", "Our walnut desk is here. It is $1,290 and adjusts from 25 to 50 inches.", "--rationale", "Launch.", "--rules-url", "UNVERIFIED");
        var id = added.Value!.Value.GetProperty("draft").GetInt32();
        var plow = new FakePlow();
        var texts = new OwnerTexts(Plow(), services.GetRequiredService<Store>(), NullLogger<OwnerTexts>.Instance, plow);
        var commands = new TextCommands(services.GetRequiredService<Store>(), marketing, services.GetRequiredService<EmployeeShifts>(), services.GetRequiredService<WorkSchedule>(),
            services.GetRequiredService<WeeklyRhythm>(), services.GetRequiredService<Publishing>(), texts, NullLogger<TextCommands>.Instance);

        // The employee sees what waits for the owner, the way the cockpit shows it.
        var status = JsonSerializer.SerializeToElement(await commands.Status(CancellationToken.None));
        Assert.Equal(id, status.GetProperty("draftsWaitingForApproval")[0].GetProperty("id").GetInt32());

        // It asks; the host words the question and gives it a code.
        var proposed = JsonSerializer.SerializeToElement(await commands.Propose(JsonSerializer.SerializeToElement(new { type = "approve", draft = id }), CancellationToken.None));
        var change = proposed.GetProperty("id").GetString()!; var question = proposed.GetProperty("confirmText").GetString()!;
        Assert.StartsWith($"Approve LinkedIn draft #{id}", question);
        Assert.Matches(@"Reply YES to confirm\. \(change [A-Z2-9]{4}\)$", question);

        async Task<string> Refused() => (await Assert.ThrowsAsync<InvalidOperationException>(() => commands.Confirm(change, CancellationToken.None))).Message;
        // Not asked word for word (a paraphrase could have asked something else): nothing happens.
        plow.Messages.Add(FakePlow.Agent("Want me to approve it? Reply yes."));
        plow.Messages.Add(FakePlow.Owner("yes"));
        Assert.Contains("word for word", await Refused());
        plow.Messages.Add(FakePlow.Agent(question));
        Assert.Contains("hasn't replied", await Refused());
        // Someone else in a thread saying yes isn't the owner; a hedged reply isn't a yes.
        plow.Messages.Add(FakePlow.Member("yes"));
        Assert.Contains("hasn't replied", await Refused());
        plow.Messages.Add(FakePlow.Owner("ok wait, not yet"));
        Assert.Contains("wasn't a yes", await Refused());
        Assert.Equal("pending", (await marketing.ShiftHire(null, "draft", "get", "--id", id.ToString())).Value!.Value.GetProperty("status").GetString());

        // The owner's own yes, after the exact question: approved through the cockpit's own decision path.
        plow.Messages.Add(FakePlow.Owner("Yes!"));
        var done = JsonSerializer.SerializeToElement(await commands.Confirm(change, CancellationToken.None));
        Assert.Equal($"Approved draft #{id}. Nothing was posted.", done.GetProperty("done").GetString());
        var draft = (await marketing.ShiftHire(null, "draft", "get", "--id", id.ToString())).Value!.Value;
        Assert.Equal("approved", draft.GetProperty("status").GetString());
        Assert.Equal("Owner session owner-by-text", draft.GetProperty("decided_by").GetString());
        // Confirming again changes nothing; a change it can't make is refused before anyone is asked.
        Assert.Equal(done.GetProperty("done").GetString(), JsonSerializer.SerializeToElement(await commands.Confirm(change, CancellationToken.None)).GetProperty("done").GetString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.Propose(JsonSerializer.SerializeToElement(new { type = "approve", draft = id }), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => commands.Propose(JsonSerializer.SerializeToElement(new { type = "delete_everything" }), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => commands.Propose(JsonSerializer.SerializeToElement(new { type = "hours", start = "09:00", end = "17:00" }), CancellationToken.None));

        // Working hours by text, in the owner's time zone, saved as the cockpit's own schedule.
        var hours = JsonSerializer.SerializeToElement(await commands.Propose(JsonSerializer.SerializeToElement(new { type = "hours", days = new[] { 1, 2, 3, 4, 5 }, start = "09:00", end = "17:00", timeZone = "America/Denver" }), CancellationToken.None));
        Assert.StartsWith("Set working hours to Mon–Fri, 09:00 to 17:00 (America/Denver)", hours.GetProperty("confirmText").GetString());
        plow.Messages.Add(FakePlow.Agent(hours.GetProperty("confirmText").GetString()!));
        plow.Messages.Add(FakePlow.Owner("yep"));
        await commands.Confirm(hours.GetProperty("id").GetString()!, CancellationToken.None);
        var saved = services.GetRequiredService<WorkSchedule>().Current()!;
        Assert.Equal((true, "09:00", "17:00", "America/Denver", TextCommands.By), (saved.Enabled, saved.Start, saved.End, saved.TimeZone, saved.UpdatedBy));
    }

    [Fact] public async Task OnlyTheEmployeesOwnContainerReachesTheCockpitByText()
    {
        Start();
        var services = factory!.Services;
        var commands = new TextCommands(services.GetRequiredService<Store>(), services.GetRequiredService<MarketingBackend>(), services.GetRequiredService<EmployeeShifts>(),
            services.GetRequiredService<WorkSchedule>(), services.GetRequiredService<WeeklyRhythm>(), services.GetRequiredService<Publishing>(),
            new OwnerTexts(Plow(), services.GetRequiredService<Store>(), NullLogger<OwnerTexts>.Instance, new FakePlow()), NullLogger<TextCommands>.Instance);
        async Task<int> Call(Action<HttpContext> shape)
        {
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Method = "GET"; context.Request.Path = "/_agent/status";
            context.Connection.RemoteIpAddress = IPAddress.Loopback; context.Request.Headers["X-HireZero-Agent"] = commands.Secret();
            context.Response.Body = new MemoryStream();
            shape(context);
            await commands.Handle(context);
            return context.Response.StatusCode;
        }
        Assert.Equal(200, await Call(_ => { }));
        Assert.Equal(403, await Call(context => context.Request.Headers["X-HireZero-Agent"] = "guess"));
        Assert.Equal(403, await Call(context => context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.8")));
        Assert.Equal(403, await Call(context => context.Request.Headers["X-Plow-User"] = "usr_owner"));   // through the web entrance
    }

    [Fact] public async Task TheWorkersNextTurnWaitsWhileATextIsBeingAnswered()
    {
        Start();
        var marketing = factory!.Services.GetRequiredService<MarketingBackend>();
        var mark = Path.Combine(root, "text-turn.json");
        await marketing.TextTurnDone(CancellationToken.None);          // not on Plow: nothing to wait for
        marketing.TextTurnFile = mark;
        await marketing.TextTurnDone(CancellationToken.None);          // no text being answered
        File.WriteAllText(mark, JsonSerializer.Serialize(new { runId = "r1", at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }));
        var waiting = marketing.TextTurnDone(CancellationToken.None);
        await Task.Delay(1500);
        Assert.False(waiting.IsCompleted);                              // the text goes first
        File.Delete(mark);
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));               // its reply sent, the worker goes on
        // A mark a restart left behind doesn't hold the worker.
        File.WriteAllText(mark, JsonSerializer.Serialize(new { runId = "r2", at = DateTimeOffset.UtcNow.AddMinutes(-4).ToUnixTimeMilliseconds() }));
        await marketing.TextTurnDone(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact] public void ABlankForAFactOnlyTheOwnerHasCantBePublished()
    {
        Assert.Equal("blocked", CampaignQa.Check("LinkedIn", "https://www.linkedin.com/feed/", "Meet the walnut desk: [price], ships [launch date]. Order now at https://example.com").Status);
        var ready = CampaignQa.Check("LinkedIn", "https://www.linkedin.com/feed/", "Meet the walnut desk [1]. See [the details](https://example.com/desk). Order now at https://example.com");
        Assert.DoesNotContain(ready.Checks, check => check.Label.StartsWith("No placeholders", StringComparison.Ordinal) && check.Result == "fail");
    }
}
