using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class InboxWatchTests
{
    private static readonly ProviderSnapshot Profile = new("compatible", "fixture", "low", "http://localhost:1234/v1");
    private static readonly ConnectedToolDefinition Mail = new("mail-connector", "Google Gmail", "search_email",
        "mcp_mail_search_email", "Search email messages without modifying them.",
        JsonSerializer.SerializeToElement(new { type = "object", properties = new { query = new { type = "string" }, limit = new { type = "integer" } }, required = new[] { "query", "limit" }, additionalProperties = false }),
        "read external data", "connector-v1");

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Broker : IConnectedToolBroker
    {
        public ConnectedToolDefinition[] Tools = [Mail];
        public JsonElement Result = JsonSerializer.SerializeToElement(new { messages = Array.Empty<object>() });
        public bool Fail;
        public int Calls;
        public ConnectedToolDefinition[] Snapshot() => Tools;
        public Task<CapabilityResult> Call(ConnectedToolDefinition tool, JsonElement arguments, CancellationToken cancellation)
        {
            Calls++;
            Assert.Equal("search_email", tool.RemoteName);
            Assert.Equal(10, arguments.GetProperty("limit").GetInt32());
            Assert.DoesNotContain("{{", arguments.GetRawText());
            if (Fail) throw new InvalidOperationException("Fixture access revoked.");
            return Task.FromResult(new CapabilityResult(Result));
        }
    }

    private sealed class Model : IModelProvider
    {
        public int Calls;
        public Func<Observation, ModelReply> Reply = _ => new(null, "{\"alerts\":[]}", 40, 10);
        public TokenQuote Quote(Observation observation) => new(200, true, "fixture", 400);
        public Task<ModelReply> Respond(Observation observation, Func<string, Task> onDelta, CancellationToken cancellation)
        { Calls++; return Task.FromResult(Reply(observation)); }
    }

    private static InboxWatchDelegationProposal Proposal() => new(new(new(Mail,
        JsonSerializer.SerializeToElement(new { query = "in:inbox after:{{sinceUnix}}", limit = 10 })), Profile,
        InboxWatchConversation.DefaultInstruction, "owner:in-app", "America/Denver"));

    private static (DelegationScheduler Scheduler, DelegationJob Job) Create(Store store, Broker broker, Model model, Clock clock)
    {
        var dispatcher = new ConnectedInboxWatchDispatcher(store, broker, _ => model);
        var scheduler = new DelegationScheduler(store, dispatcher, clock);
        var created = scheduler.CreateInboxWatch(Proposal(), requestedAt: clock.Now);
        return (scheduler, created.Job);
    }

    [Fact]
    public void EligibilityIsConnectorNeutralAndStillReadOnly()
    {
        var outlook = new ConnectedToolDefinition("office-mail", "Microsoft 365 Inbox", "list_messages",
            "mcp_office_list_messages", "List inbox email messages without modifying them.",
            JsonSerializer.SerializeToElement(new { type = "object", properties = new { since = new { type = "string" }, top = new { type = "integer" } }, required = new[] { "since", "top" }, additionalProperties = false }),
            "read external data", "office-v1");
        var shape = Assert.Single(InboxWatchConversation.Eligible([outlook]));
        Assert.Equal("Microsoft 365 Inbox", shape.Tool.ConnectorName);
        Assert.Equal("top", shape.LimitField); Assert.Equal("since", shape.SinceField);

        var mutating = outlook with { Effect = "write external data", RemoteName = "archive_email" };
        Assert.Empty(InboxWatchConversation.Eligible([mutating]));
    }

    [Fact]
    public async Task ChatRequestsExactRecurringReadApprovalBeforePersistingWatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "thaddeus-inbox-watch-chat-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new Store(root); var broker = new Broker(); var clock = new Clock(new DateTimeOffset(2026, 9, 17, 11, 0, 0, TimeSpan.Zero));
            var model = new Model();
            model.Reply = observation =>
            {
                var delegation = Assert.IsType<DelegationToolContext>(observation.Delegation);
                if (delegation.Receipts.Length != 0) return new(null, "The read-only inbox watch is active.");
                var connected = Assert.IsType<ConnectedToolContext>(observation.ConnectedTools);
                var shape = Assert.Single(InboxWatchConversation.Eligible(connected.Tools));
                return new(new ToolRequest(InboxWatchConversation.ToolName(shape), "", Wire.Pack(new
                {
                    importanceInstruction = InboxWatchConversation.DefaultInstruction,
                    destination = "owner:in-app",
                    emailArguments = new { query = "in:inbox after:{{sinceUnix}}", limit = 10 }
                })), null);
            };
            var scheduler = new DelegationScheduler(store, new ConnectedInboxWatchDispatcher(store, broker, _ => model), clock);
            var runtime = new Runtime(store, _ => model, new PlanValidator(), new EvidencePolicy(), connectedTools: broker,
                delegations: scheduler, timeProvider: clock);
            var run = runtime.Converse("Check my email periodically and tell me only when something matters.", Profile);
            await runtime.Execute(run.Id);
            var review = store.Get(run.Id)!;
            Assert.Equal(RunState.AwaitingApproval, review.State);
            Assert.StartsWith("delegation_inbox_watch_", review.Approval!.Action.Name);
            Assert.Empty(store.DelegationJobs()); Assert.Equal(0, broker.Calls);

            await runtime.Decide(review.Id, review.Approval.Id, review.Approval.Digest, true);
            for (var attempt = 0; attempt < 200 && store.Get(run.Id)!.State is RunState.Running or RunState.Paused; attempt++) await Task.Delay(10);
            var job = Assert.Single(store.DelegationJobs()); var grant = store.DelegationGrant(job.GrantId)!;
            Assert.Equal("inbox-watch", job.Kind); Assert.Equal("interval", job.Schedule.Kind); Assert.Equal(5, job.Schedule.IntervalMinutes);
            Assert.Equal(8_640, grant.MaxOccurrences); Assert.True(grant.Expires <= clock.Now.AddDays(31));
            Assert.NotNull(store.InboxWatchState(job.Id)); Assert.Equal(0, broker.Calls);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ImportantAndRoutineMessagesAreSeparatedWithoutDuplicateAlerts()
    {
        var root = Path.Combine(Path.GetTempPath(), "thaddeus-inbox-watch-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new Store(root); var broker = new Broker(); var model = new Model();
            var clock = new Clock(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));
            var fixture = Create(store, broker, model, clock);
            broker.Result = JsonSerializer.SerializeToElement(new { messages = new object[]
            {
                new { id = "important-1", threadId = "thread-1", sender = "Avery <avery@example.test>", subject = "Contract response due today", snippet = "Please answer by 4 PM.", link = "https://mail.google.com/mail/u/0/#inbox/important-1" },
                new { id = "newsletter-1", threadId = "thread-2", sender = "Shop", subject = "This week's offers", snippet = "Save twenty percent." }
            }});
            model.Reply = observation =>
            {
                Assert.Contains("important-1", observation.Goal.Objective);
                Assert.Contains("newsletter-1", observation.Goal.Objective);
                return new(null, "{\"alerts\":[{\"messageId\":\"important-1\",\"reason\":\"A direct response is due today.\"}]}", 80, 20);
            };

            clock.Now = fixture.Job.NextRunUtc!.Value;
            Assert.Equal(1, await fixture.Scheduler.Tick());
            var first = Assert.Single(store.DelegationOccurrences(fixture.Job.Id));
            Assert.Null(first.ReadAt);
            Assert.Contains("Contract response due today", first.ProviderEvidence!.Value.GetProperty("attention").GetString());
            Assert.Contains("Open original email", first.ProviderEvidence.Value.GetProperty("attention").GetString());
            var state = store.InboxWatchState(fixture.Job.Id)!;
            Assert.Contains("important-1", state.AlertedMessageIds);
            Assert.Contains("newsletter-1", state.ProcessedMessageIds);

            clock.Now = store.DelegationJobs().Single().NextRunUtc!.Value;
            Assert.Equal(1, await fixture.Scheduler.Tick());
            Assert.Equal(1, model.Calls);
            var second = store.DelegationOccurrences(fixture.Job.Id).OrderBy(item => item.Sequence).Last();
            Assert.NotNull(second.ReadAt);
            Assert.Equal("quiet", second.NotificationStatus);

            broker.Result = JsonSerializer.SerializeToElement(new { messages = new object[]
            {
                new { id = "important-2", threadId = "thread-1", sender = "Avery <avery@example.test>", subject = "Re: Contract response due today", snippet = "The deadline moved to 3 PM.", link = "https://mail.google.com/mail/u/0/#inbox/important-2" },
                new { id = "important-1", threadId = "thread-1", sender = "Avery", subject = "Contract response due today", snippet = "Old message." }
            }});
            model.Reply = _ => new(null, "{\"alerts\":[{\"messageId\":\"important-2\",\"reason\":\"A time-sensitive deadline changed.\"}]}", 60, 20);
            clock.Now = store.DelegationJobs().Single().NextRunUtc!.Value;
            Assert.Equal(1, await fixture.Scheduler.Tick());
            Assert.Equal(2, model.Calls);
            Assert.Contains("important-2", store.InboxWatchState(fixture.Job.Id)!.AlertedMessageIds);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task EmptyCheckSkipsModelAndRevokedAccessPausesWithVisibleFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "thaddeus-inbox-watch-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new Store(root); var broker = new Broker(); var model = new Model();
            var clock = new Clock(new DateTimeOffset(2026, 9, 17, 13, 0, 0, TimeSpan.Zero));
            var fixture = Create(store, broker, model, clock);
            clock.Now = fixture.Job.NextRunUtc!.Value;
            Assert.Equal(1, await fixture.Scheduler.Tick());
            Assert.Equal(0, model.Calls);
            Assert.NotNull(Assert.Single(store.DelegationOccurrences(fixture.Job.Id)).ReadAt);

            broker.Fail = true; clock.Now = store.DelegationJobs().Single().NextRunUtc!.Value;
            Assert.Equal(1, await fixture.Scheduler.Tick());
            var failed = store.DelegationOccurrences(fixture.Job.Id).OrderBy(item => item.Sequence).Last();
            Assert.Equal("failed", failed.State);
            Assert.Contains("paused", failed.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("paused", store.DelegationJobs().Single().State);
            Assert.Null(store.DelegationJobs().Single().NextRunUtc);
            var calls = broker.Calls;
            clock.Now = clock.Now.AddHours(1); Assert.Equal(0, await fixture.Scheduler.Tick()); Assert.Equal(calls, broker.Calls);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RestartKeepsProcessedIdentityAndDoesNotRepeatAlert()
    {
        var root = Path.Combine(Path.GetTempPath(), "thaddeus-inbox-watch-" + Guid.NewGuid().ToString("N"));
        try
        {
            var broker = new Broker { Result = JsonSerializer.SerializeToElement(new { messages = new[] { new
            { id = "restart-1", sender = "Morgan", subject = "Please review", snippet = "Can you respond?", link = "https://mail.google.com/mail/u/0/#inbox/restart-1" } } }) };
            var model = new Model { Reply = _ => new(null, "{\"alerts\":[{\"messageId\":\"restart-1\",\"reason\":\"A response was requested.\"}]}") };
            var clock = new Clock(new DateTimeOffset(2026, 9, 17, 14, 0, 0, TimeSpan.Zero)); string jobId; DateTimeOffset next;
            using (var firstStore = new Store(root))
            {
                var first = Create(firstStore, broker, model, clock); jobId = first.Job.Id; clock.Now = first.Job.NextRunUtc!.Value;
                await first.Scheduler.Tick(); next = firstStore.DelegationJobs().Single().NextRunUtc!.Value;
            }
            using (var restartedStore = new Store(root))
            {
                var restartedModel = new Model();
                var scheduler = new DelegationScheduler(restartedStore, new ConnectedInboxWatchDispatcher(restartedStore, broker, _ => restartedModel), clock);
                Assert.Equal(0, scheduler.Recover()); clock.Now = next; Assert.Equal(1, await scheduler.Tick());
                Assert.Equal(0, restartedModel.Calls);
                Assert.Single(restartedStore.InboxWatchState(jobId)!.AlertedMessageIds);
                Assert.Equal(2, restartedStore.DelegationOccurrences(jobId).Length);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
