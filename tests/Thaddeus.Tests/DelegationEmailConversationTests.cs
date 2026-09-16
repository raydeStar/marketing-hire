using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class DelegationEmailConversationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-delegation-email-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    private readonly MutableClock clock = new(new DateTimeOffset(2026, 9, 16, 16, 0, 0, TimeSpan.Zero));
    private static readonly ProviderSnapshot Profile = new("compatible", "fixture", "high", "http://localhost:1234/v1");

    private static ConnectedToolDefinition EmailTool(string version = "connector-v1", bool reversedSchema = false)
    {
        var schema = reversedSchema
            ? JsonSerializer.SerializeToElement(new { required = new[] { "to", "subject", "body" }, properties = new { body = new { type = "string" }, subject = new { type = "string" }, to = new { type = "string" } }, type = "object", additionalProperties = false })
            : JsonSerializer.SerializeToElement(new { type = "object", properties = new { to = new { type = "string" }, subject = new { type = "string" }, body = new { type = "string" } }, required = new[] { "to", "subject", "body" }, additionalProperties = false });
        return new("mail-connector", "Owner test mail", "send_email", "mcp_mail_send_email", "Send an email message.", schema,
            "write or external action", version);
    }

    public DelegationEmailConversationTests() => store = new(root);

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Broker : IConnectedToolBroker
    {
        public ConnectedToolDefinition[] Tools = [EmailTool()];
        public int Calls;
        public JsonElement? Arguments;
        public string Mode = "accepted";
        public ConnectedToolDefinition[] Snapshot() => Tools;
        public Task<CapabilityResult> Call(ConnectedToolDefinition tool, JsonElement arguments, CancellationToken cancellation)
        {
            Calls++; Arguments = arguments.Clone();
            if (Mode == "unknown") throw new IOException("Connection ended after dispatch.");
            return Task.FromResult(Mode == "error"
                ? new CapabilityResult(JsonSerializer.SerializeToElement(new { error = "rejected fixture" }), true)
                : new CapabilityResult(JsonSerializer.SerializeToElement(new { messageId = "fixture-message-123", accepted = true })));
        }
    }

    private sealed class Model : IModelProvider
    {
        public int Calls;
        public TokenQuote Quote(Observation observation) => new(0, true, "fixture", 0);
        public Task<ModelReply> Respond(Observation observation, Func<string, Task> onDelta, CancellationToken cancellation)
        {
            Calls++;
            var delegation = Assert.IsType<DelegationToolContext>(observation.Delegation);
            var connected = Assert.IsType<ConnectedToolContext>(observation.ConnectedTools);
            var tool = Assert.Single(DelegationEmailConversation.Eligible(connected.Tools)).Tool;
            if (delegation.Receipts.Length == 0)
            {
                Assert.True(delegation.CanPropose);
                var arguments = new { to = "owner-test@example.invalid", subject = "Running late", body = "I am running late." };
                return Task.FromResult(new ModelReply(new(DelegationEmailConversation.ToolName(tool), "",
                    Wire.Pack(new { dueUtc = delegation.RequestedAt.AddHours(2), timeZone = delegation.TimeZone, arguments })), null));
            }
            Assert.False(delegation.CanPropose);
            Assert.True(delegation.Receipts[0].Result.GetProperty("persisted").GetBoolean());
            Assert.False(delegation.Receipts[0].Result.GetProperty("sent").GetBoolean());
            return Task.FromResult(new ModelReply(null, "The exact email is scheduled for two hours from now. It has not been sent yet."));
        }
    }

    private (Runtime Runtime, Model Model, Broker Broker, DelegationScheduler Scheduler) Create()
    {
        var broker = new Broker();
        var scheduler = new DelegationScheduler(store, new ConnectedEmailDelegationDispatcher(broker), clock);
        var model = new Model();
        var runtime = new Runtime(store, _ => model, new PlanValidator(), new EvidencePolicy(), connectedTools: broker,
            delegations: scheduler, timeProvider: clock);
        return (runtime, model, broker, scheduler);
    }

    private static async Task WaitForFinal(Store store, string id)
    {
        for (var attempt = 0; attempt < 300 && store.Get(id)!.State is RunState.Running or RunState.Paused; attempt++)
            await Task.Delay(10);
    }

    private async Task<(Run Review, DelegationJob Job, Runtime Runtime, Model Model, Broker Broker, DelegationScheduler Scheduler)> Approve()
    {
        var fixture = Create();
        var accepted = fixture.Runtime.Converse("In two hours, email owner-test@example.invalid and say I am running late.", Profile);
        await fixture.Runtime.Execute(accepted.Id);
        var review = store.Get(accepted.Id)!;
        Assert.Equal(RunState.AwaitingApproval, review.State);
        Assert.StartsWith("delegation_email_", review.Approval!.Action.Name);
        Assert.Empty(store.DelegationJobs());
        Assert.Equal(0, fixture.Broker.Calls);

        await fixture.Runtime.Decide(review.Id, review.Approval.Id, review.Approval.Digest, true);
        await WaitForFinal(store, review.Id);
        return (review, Assert.Single(store.DelegationJobs()), fixture.Runtime, fixture.Model, fixture.Broker, fixture.Scheduler);
    }

    [Fact]
    public async Task ExactApprovalPersistsGrantThenDispatchesOnceAtDueTime()
    {
        var fixture = await Approve();
        var completed = store.Get(fixture.Review.Id)!;
        Assert.Equal(RunState.Succeeded, completed.State);
        Assert.Equal(clock.Now.AddHours(2), fixture.Job.NextRunUtc);
        Assert.Equal("owner-test@example.invalid", fixture.Job.Action.Target);
        Assert.Equal("Owner test mail", store.DelegationGrant(fixture.Job.GrantId)!.AccountId);
        Assert.Equal(2, fixture.Model.Calls);
        Assert.Equal(0, fixture.Broker.Calls);
        Assert.Contains(store.Chats(), message => message.Role == "assistant" && message.Content.Contains("not been sent yet"));

        clock.Now = fixture.Job.NextRunUtc!.Value;
        Assert.Equal(1, await fixture.Scheduler.Tick());
        Assert.Equal(0, await fixture.Scheduler.Tick());
        Assert.Equal(1, fixture.Broker.Calls);
        Assert.Equal("owner-test@example.invalid", fixture.Broker.Arguments!.Value.GetProperty("to").GetString());
        Assert.Equal("I am running late.", fixture.Broker.Arguments.Value.GetProperty("body").GetString());
        var occurrence = Assert.Single(store.DelegationOccurrences());
        Assert.Equal("succeeded", occurrence.State);
        Assert.Equal("mail-connector:send_email", occurrence.ProviderId);
        Assert.Equal("fixture-message-123", occurrence.ProviderEvidence!.Value.GetProperty("messageId").GetString());
    }

    [Fact]
    public async Task DenialCreatesNoJobAndSendsNothing()
    {
        var fixture = Create();
        var run = fixture.Runtime.Converse("Email owner-test@example.invalid in two hours.", Profile);
        await fixture.Runtime.Execute(run.Id);
        var review = store.Get(run.Id)!;

        await fixture.Runtime.Decide(run.Id, review.Approval!.Id, review.Approval.Digest, false);

        Assert.Equal(RunState.Denied, store.Get(run.Id)!.State);
        Assert.Empty(store.DelegationJobs());
        Assert.Empty(store.DelegationOccurrences());
        Assert.Equal(0, fixture.Broker.Calls);
        Assert.Contains("nothing scheduled or sent", store.Get(run.Id)!.Summary);
    }

    [Fact]
    public async Task ChangedConnectorRefusesStaleApprovalBeforeScheduling()
    {
        var fixture = Create();
        var run = fixture.Runtime.Converse("Email owner-test@example.invalid in two hours.", Profile);
        await fixture.Runtime.Execute(run.Id);
        var review = store.Get(run.Id)!;
        fixture.Broker.Tools = [EmailTool("connector-v2")];

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Runtime.Decide(run.Id, review.Approval!.Id, review.Approval.Digest, true));

        Assert.Empty(store.DelegationJobs());
        Assert.Equal(0, fixture.Broker.Calls);
        Assert.Equal(RunState.AwaitingApproval, store.Get(run.Id)!.State);
    }

    [Fact]
    public async Task SemanticallyIdenticalReorderedSchemaRemainsAuthorized()
    {
        var fixture = Create();
        var run = fixture.Runtime.Converse("Email owner-test@example.invalid in two hours.", Profile);
        await fixture.Runtime.Execute(run.Id);
        var review = store.Get(run.Id)!;
        fixture.Broker.Tools = [EmailTool(reversedSchema: true)];

        await fixture.Runtime.Decide(run.Id, review.Approval!.Id, review.Approval.Digest, true);
        await WaitForFinal(store, run.Id);

        Assert.Single(store.DelegationJobs());
        Assert.Equal(RunState.Succeeded, store.Get(run.Id)!.State);
    }

    [Theory]
    [InlineData("error", "failed")]
    [InlineData("unknown", "unknown")]
    public async Task ProviderFailureOrUncertaintyRemainsVisibleAndNeverRetries(string mode, string expectedState)
    {
        var fixture = await Approve();
        fixture.Broker.Mode = mode;
        clock.Now = fixture.Job.NextRunUtc!.Value;

        Assert.Equal(1, await fixture.Scheduler.Tick());
        Assert.Equal(0, await fixture.Scheduler.Tick());

        Assert.Equal(1, fixture.Broker.Calls);
        Assert.Equal(expectedState, store.DelegationJobs().Single().State);
        Assert.Equal(expectedState, store.DelegationOccurrences().Single().State);
    }

    [Fact]
    public async Task ExactReviewedPayloadSurvivesHostRestartAndStillDispatchesOnlyOnce()
    {
        var restartRoot = Path.Combine(root, "restart");
        var broker = new Broker();
        var due = clock.Now.AddHours(2);
        var payload = new ScheduledEmailPayload(EmailTool(),
            JsonSerializer.SerializeToElement(new { to = "restart@example.invalid", subject = "Restart fixture", body = "Persist this exact body." }),
            "Owner test mail", "restart@example.invalid", "Restart fixture", "Persist this exact body.");
        using (var first = new Store(restartRoot))
        {
            var scheduler = new DelegationScheduler(first, new ConnectedEmailDelegationDispatcher(broker), clock);
            scheduler.CreateEmail(new(payload, due, TimeZoneInfo.Local.Id), sourceRunId: null);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        clock.Now = due;
        using (var reopened = new Store(restartRoot))
        {
            var scheduler = new DelegationScheduler(reopened, new ConnectedEmailDelegationDispatcher(broker), clock);
            Assert.Equal(0, scheduler.Recover());
            Assert.Equal(1, await scheduler.Tick());
            Assert.Equal(0, await scheduler.Tick());
            Assert.Equal("succeeded", reopened.DelegationJobs().Single().State);
            Assert.Equal("succeeded", reopened.DelegationOccurrences().Single().State);
        }

        Assert.Equal(1, broker.Calls);
        Assert.Equal("restart@example.invalid", broker.Arguments!.Value.GetProperty("to").GetString());
        Assert.Equal("Persist this exact body.", broker.Arguments.Value.GetProperty("body").GetString());
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
