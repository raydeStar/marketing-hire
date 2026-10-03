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

    /// <summary>Records where requests went; answers like Plow's API behind a hosted install's proxy.</summary>
    sealed class ProxyPlow : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.Method + " " + request.RequestUri);
            var path = request.RequestUri!.AbsolutePath;
            object body = path.EndsWith("/agents/me", StringComparison.Ordinal) ? new { line = new { uid = "ln_self" } }
                : path.EndsWith("/chats", StringComparison.Ordinal) ? new { has_more = false, data = new object[] { new { uid = "cht_owner", status = "active", participants = new object[] {
                    new { type = "agent", relationship = "self", line = new { uid = "ln_self" } }, new { type = "member", role = "owner" } } } } }
                : new { uid = "msg_1" };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") });
        }
    }

    [Fact] public async Task AHostedInstallTextsThroughItsPlatformProxy()
    {
        var store = new Store(Path.Combine(root, "proxy-store"));
        try
        {
            // A hosted install's API root is the platform's per-install proxy: plain HTTP, a private address, a path of its own.
            IConfiguration Proxy(string address) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Thaddeus:PhoneMode"] = "plow", ["PLOW_AGENT_TOKEN"] = "proxied", ["PLOW_API_BASE"] = address }).Build();
            var plow = new ProxyPlow();
            var texts = new OwnerTexts(Proxy("http://10.12.0.7:8443/agent-api/"), store, NullLogger<OwnerTexts>.Instance, plow);
            Assert.True(texts.Enabled);
            Assert.True(await texts.Send("run:proxy", "Done with what you asked.", CancellationToken.None));
            Assert.Equal(["GET http://10.12.0.7:8443/agent-api/v1/agents/me", "GET http://10.12.0.7:8443/agent-api/v1/chats",
                "POST http://10.12.0.7:8443/agent-api/v1/chats/cht_owner/messages"], plow.Urls);
            // An address with credentials or a query in it isn't one the platform would supply.
            Assert.False(new OwnerTexts(Proxy("http://user:pass@10.12.0.7/"), store, NullLogger<OwnerTexts>.Instance, plow).Enabled);
            Assert.False(new OwnerTexts(Proxy("ftp://10.12.0.7/"), store, NullLogger<OwnerTexts>.Instance, plow).Enabled);
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
            services.GetRequiredService<WeeklyRhythm>(), services.GetRequiredService<Publishing>(), texts, services.GetRequiredService<Playbooks>(), services.GetRequiredService<CompanyObjectives>(), NullLogger<TextCommands>.Instance);

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

    [Fact] public async Task TheCockpitsOtherButtonsWorkByTextToo()
    {
        Start();
        var services = factory!.Services;
        var marketing = services.GetRequiredService<MarketingBackend>();
        var publishing = services.GetRequiredService<Publishing>();
        var plow = new FakePlow();
        var texts = new OwnerTexts(Plow(), services.GetRequiredService<Store>(), NullLogger<OwnerTexts>.Instance, plow);
        var commands = new TextCommands(services.GetRequiredService<Store>(), marketing, services.GetRequiredService<EmployeeShifts>(), services.GetRequiredService<WorkSchedule>(),
            services.GetRequiredService<WeeklyRhythm>(), publishing, texts, services.GetRequiredService<Playbooks>(), services.GetRequiredService<CompanyObjectives>(), NullLogger<TextCommands>.Instance);
        async Task<int> Draft(string channel, string destination, string words) =>
            (await marketing.ShiftHire(null, "draft", "add", "--channel", channel, "--destination", destination, "--content", words, "--rationale", "Test.", "--rules-url", "UNVERIFIED")).Value!.Value.GetProperty("draft").GetInt32();
        async Task<string> Status(int id) => (await marketing.ShiftHire(null, "draft", "get", "--id", id.ToString())).Value!.Value.GetProperty("status").GetString()!;
        // What the owner does: reads the host's question, answers yes.
        async Task<string> Yes(object change)
        {
            var proposed = JsonSerializer.SerializeToElement(await commands.Propose(JsonSerializer.SerializeToElement(change), CancellationToken.None));
            plow.Messages.Add(FakePlow.Agent(proposed.GetProperty("confirmText").GetString()!));
            plow.Messages.Add(FakePlow.Owner("yes"));
            return JsonSerializer.SerializeToElement(await commands.Confirm(proposed.GetProperty("id").GetString()!, CancellationToken.None)).GetProperty("done").GetString()!;
        }

        var dropped = await Draft("LinkedIn", "https://www.linkedin.com/feed/", "A post the owner doesn't want. Read more at https://example.com");
        Assert.StartsWith("Rejected", await Yes(new { type = "reject", draft = dropped, note = "too salesy" }));
        Assert.Equal("rejected", await Status(dropped));

        // Nothing is connected for Instagram: posting it means it's ready for the owner to post themselves.
        var now = await Draft("Instagram", "https://www.instagram.com/", "Fresh pumpkin loaf, weekends only. Come by and grab one: https://example.com");
        Assert.Equal("Ready for you to post; the text is in the cockpit.", await Yes(new { type = "post", draft = now }));
        Assert.Equal("approved", await Status(now));

        // Scheduled with nothing connected: a reminder, texted with the post's words when its time comes.
        var later = await Draft("Instagram", "https://www.instagram.com/", "Pumpkin loaf is back this weekend. Grab one before it's gone: https://example.com");
        var at = DateTimeOffset.UtcNow.AddDays(1).ToOffset(TimeSpan.FromHours(-6));
        Assert.StartsWith("I'll text you the post at", await Yes(new { type = "schedule", draft = later, at = at.ToString("yyyy-MM-ddTHH:mm:sszzz") }));
        var reminders = new List<(string Key, string Text)>();
        publishing.TextOwner = (key, text, _) => { reminders.Add((key, text)); return Task.FromResult(true); };
        publishing.Clock = () => at.AddMinutes(1);
        await publishing.PublishDue(CancellationToken.None);
        var (reminderKey, reminder) = Assert.Single(reminders);
        Assert.StartsWith("due:", reminderKey);
        Assert.StartsWith($"Time to post your Instagram (draft #{later}). Here it is:\n\nPumpkin loaf is back this weekend.", reminder);
        await publishing.PublishDue(CancellationToken.None);
        Assert.Single(reminders);   // once

        // A shift by text, and stopping it.
        Assert.StartsWith("The shift is on until", await Yes(new { type = "shift", minutes = 60 }));
        Assert.Contains(services.GetRequiredService<EmployeeShifts>().History(), shift => shift.Status == "running" && shift.StartedBy == TextCommands.By);
        Assert.Equal("Stopped.", await Yes(new { type = "stop" }));
        Assert.DoesNotContain(services.GetRequiredService<EmployeeShifts>().History(), shift => shift.Status is "running" or "paused");

        Assert.StartsWith("The Monday plan and Friday update are on.", await Yes(new { type = "weekly", enabled = true }));
        Assert.True(services.GetRequiredService<WeeklyRhythm>().Settings().Enabled);
        Assert.StartsWith("Saved to your brief", await Yes(new { type = "brief", field = "audience", value = "Neighbors within a mile of the shop." }));
        Assert.Equal("Neighbors within a mile of the shop.", (await marketing.ShiftHire(null, "profile", "get")).Value!.Value.GetProperty("audience").GetString());

        // The first shift by text is the cockpit's own: it needs what they sell and who buys it, then queues the first win and its pieces.
        await Assert.ThrowsAsync<ArgumentException>(() => commands.Propose(JsonSerializer.SerializeToElement(new { type = "first_shift" }), CancellationToken.None));
        Assert.StartsWith("Saved to your brief", await Yes(new { type = "brief", field = "product_summary", value = "Crumb & Co, a neighborhood bakery." }));
        var offered = JsonSerializer.SerializeToElement(await commands.Propose(JsonSerializer.SerializeToElement(new { type = "first_shift" }), CancellationToken.None));
        var offer = offered.GetProperty("confirmText").GetString()!;
        Assert.StartsWith("Start your first shift: the single biggest fix", offer);
        Assert.Contains("plus your first week of posts", offer);   // the pieces by name, not their long summaries
        plow.Messages.Add(FakePlow.Agent(offer)); plow.Messages.Add(FakePlow.Owner("yes"));
        var started = JsonSerializer.SerializeToElement(await commands.Confirm(offered.GetProperty("id").GetString()!, CancellationToken.None));
        Assert.StartsWith("Your first shift has started", started.GetProperty("done").GetString());
        var queued = (await marketing.ShiftHire(null, "task", "list")).Value!.Value.EnumerateArray().Select(task => task.GetProperty("title").GetString()).ToArray();
        Assert.Contains(EmployeeShifts.FirstWinTitle, queued);
    }

    [Fact] public async Task AnXPostByTextComesWithALinkThatOpensXWithItFilledIn()
    {
        Start();
        var services = factory!.Services;
        var marketing = services.GetRequiredService<MarketingBackend>();
        var publishing = services.GetRequiredService<Publishing>();
        var shifts = services.GetRequiredService<EmployeeShifts>();
        var plow = new FakePlow();
        var texts = new OwnerTexts(Plow(), services.GetRequiredService<Store>(), NullLogger<OwnerTexts>.Instance, plow);
        var commands = new TextCommands(services.GetRequiredService<Store>(), marketing, shifts, services.GetRequiredService<WorkSchedule>(),
            services.GetRequiredService<WeeklyRhythm>(), publishing, texts, services.GetRequiredService<Playbooks>(), services.GetRequiredService<CompanyObjectives>(), NullLogger<TextCommands>.Instance);
        async Task<int> Draft(string destination, string words) =>
            (await marketing.ShiftHire(null, "draft", "add", "--channel", "X", "--destination", destination, "--content", words, "--rationale", "Test.", "--rules-url", "UNVERIFIED")).Value!.Value.GetProperty("draft").GetInt32();
        async Task<(string Question, string Done)> Yes(object change)
        {
            var proposed = JsonSerializer.SerializeToElement(await commands.Propose(JsonSerializer.SerializeToElement(change), CancellationToken.None));
            var question = proposed.GetProperty("confirmText").GetString()!;
            plow.Messages.Add(FakePlow.Agent(question));
            plow.Messages.Add(FakePlow.Owner("yes"));
            return (question, JsonSerializer.SerializeToElement(await commands.Confirm(proposed.GetProperty("id").GetString()!, CancellationToken.None)).GetProperty("done").GetString()!);
        }
        static string Words(string link) => Uri.UnescapeDataString(link[(link.IndexOf("text=", StringComparison.Ordinal) + 5)..]);

        // The shift's text says how to get the link for an X post nobody can post for the owner.
        var words = "Our walnut desk ships Monday. #woodworking & more\nhttps://example.com/desk";
        var id = await Draft("https://x.com/home", words);
        var now = DateTimeOffset.UtcNow;
        var told = await shifts.RunText(new EmployeeShift("s-x", "completed", 1, 60, 10, 1, 0, "scripted", "Owner", now, now.AddHours(1), null, now, null, [], null, [], [$"draft:{id} X post"], []));
        Assert.Contains($"To post #{id} on X, reply \"post {id}\" and say yes when I ask: I'll text you a link that opens X with it filled in.", told);

        // Asked first; after the owner's yes it is approved, and the host itself texts the link: the model never copies it.
        var (question, done) = await Yes(new { type = "post", draft = id });
        Assert.Contains("I'll text you a link that opens it on X with the words filled in.", question);
        Assert.Equal("Ready for you to post: I texted you a link that opens X with it filled in. Tap it, then Post.", done);
        Assert.Equal("approved", (await marketing.ShiftHire(null, "draft", "get", "--id", id.ToString())).Value!.Value.GetProperty("status").GetString());
        var sent = plow.Sent.Last();
        Assert.StartsWith("Tap to post it on X; it opens with the words filled in:\nhttps://x.com/intent/post?text=Our%20walnut%20desk", sent);
        Assert.Equal(words, Words(sent.Split('\n', 2)[1]));
        Assert.Equal("awaiting_link", publishing.Ledger().Publications.Single(post => post.DraftId == id).Status);

        // A reply opens as a reply to its post.
        var reply = await Draft("https://x.com/rival/status/1790000000000000001", "Congrats on the launch!");
        (question, _) = await Yes(new { type = "post", draft = reply });
        Assert.Contains("a link that opens your reply on X", question);
        Assert.EndsWith("https://x.com/intent/post?in_reply_to=1790000000000000001&text=Congrats%20on%20the%20launch%21", plow.Sent.Last());

        // An owner looking at the cockpit isn't texted; the answer carries the link instead.
        texts.Seen();
        var sentBefore = plow.Sent.Count;
        (_, done) = await Yes(new { type = "post", draft = await Draft("https://x.com/home", "Third post: the oak desk is back Friday.") });
        Assert.Equal("Ready for you to post. Tap to open X with it filled in: https://x.com/intent/post?text=Third%20post%3A%20the%20oak%20desk%20is%20back%20Friday.", done);
        Assert.Equal(sentBefore, plow.Sent.Count);

        // Scheduled: the reminder carries the link with the words.
        var later = await Draft("https://x.com/home", "Back Monday with the oak one.");
        var at = DateTimeOffset.UtcNow.AddDays(1).ToOffset(TimeSpan.FromHours(-6));
        (question, done) = await Yes(new { type = "schedule", draft = later, at = at.ToString("yyyy-MM-ddTHH:mm:sszzz") });
        Assert.Contains("to post yourself, with a link that opens X with the words filled in", question);
        Assert.StartsWith("I'll text you the post at", done);
        var reminders = new List<string>();
        publishing.TextOwner = (_, text, _) => { reminders.Add(text); return Task.FromResult(true); };
        publishing.Clock = () => at.AddMinutes(1);
        await publishing.PublishDue(CancellationToken.None);
        var reminder = Assert.Single(reminders);
        Assert.StartsWith($"Time to post your X (draft #{later}). Here it is:\n\nBack Monday with the oak one.\n\nTap to post it on X; it opens with the words filled in:\nhttps://x.com/intent/post?text=Back%20Monday", reminder);
    }

    [Fact] public void OnlyXGetsAComposeLink_AndNeverOneTooLongToText()
    {
        Assert.Equal("https://x.com/intent/post?text=Hi%20there", Publishing.ComposeLink("Twitter", "https://twitter.com/home", "Hi there"));
        Assert.Equal("https://x.com/intent/post?in_reply_to=42&text=Yes", Publishing.ComposeLink("X", "https://twitter.com/someone/status/42", "Yes"));
        Assert.Null(Publishing.ComposeLink("LinkedIn", "https://www.linkedin.com/feed/", "Hi there"));
        // 140 emoji fit X's count, but their link wouldn't fit a text whole.
        Assert.Null(Publishing.ComposeLink("X", "https://x.com/home", string.Concat(Enumerable.Repeat("🎉", 140))));
    }

    [Fact] public async Task OnlyTheEmployeesOwnContainerReachesTheCockpitByText()
    {
        Start();
        var services = factory!.Services;
        var commands = new TextCommands(services.GetRequiredService<Store>(), services.GetRequiredService<MarketingBackend>(), services.GetRequiredService<EmployeeShifts>(),
            services.GetRequiredService<WorkSchedule>(), services.GetRequiredService<WeeklyRhythm>(), services.GetRequiredService<Publishing>(),
            new OwnerTexts(Plow(), services.GetRequiredService<Store>(), NullLogger<OwnerTexts>.Instance, new FakePlow()), services.GetRequiredService<Playbooks>(), services.GetRequiredService<CompanyObjectives>(), NullLogger<TextCommands>.Instance);
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
        File.WriteAllText(mark, JsonSerializer.Serialize(new { runId = "r2", at = DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeMilliseconds() }));
        await marketing.TextTurnDone(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact] public void ABlankForAFactOnlyTheOwnerHasCantBePublished()
    {
        Assert.Equal("blocked", CampaignQa.Check("LinkedIn", "https://www.linkedin.com/feed/", "Meet the walnut desk: [price], ships [launch date]. Order now at https://example.com").Status);
        var ready = CampaignQa.Check("LinkedIn", "https://www.linkedin.com/feed/", "Meet the walnut desk [1]. See [the details](https://example.com/desk). Order now at https://example.com");
        Assert.DoesNotContain(ready.Checks, check => check.Label.StartsWith("No placeholders", StringComparison.Ordinal) && check.Result == "fail");
    }
}
