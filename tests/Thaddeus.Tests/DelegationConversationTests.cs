using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class DelegationConversationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-delegation-chat-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    private readonly MutableClock clock = new(new DateTimeOffset(2026, 9, 16, 16, 0, 0, TimeSpan.Zero));

    public DelegationConversationTests() => store = new(root);

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Dispatcher : IDelegationDispatcher
    {
        public int Calls;
        public Task<DelegationDispatchResult> Dispatch(DelegationJob job, DelegationOccurrence occurrence, CancellationToken cancellation)
        {
            Calls++;
            return Task.FromResult(new DelegationDispatchResult("accepted", "Delivered.", true, NotificationStatus: "delivered"));
        }
    }

    private sealed class Model : IModelProvider
    {
        public int Calls;
        public TokenQuote Quote(Observation observation) => new(0, true, "fixture", 0);
        public Task<ModelReply> Respond(Observation observation, Func<string, Task> onDelta, CancellationToken cancellation)
        {
            Calls++;
            var context = Assert.IsType<DelegationToolContext>(observation.Delegation);
            if (context.Receipts.Length == 0)
            {
                Assert.True(context.CanPropose);
                var immediate = observation.Goal.Objective.Contains("right now", StringComparison.OrdinalIgnoreCase);
                var proposal = new ReminderProposal(immediate ? "Testing" : "Call dentist", immediate ? "Testing" : "Call the dentist.",
                    immediate ? context.RequestedAt : context.RequestedAt.AddHours(2), context.TimeZone);
                return Task.FromResult(new ModelReply(new(DelegationConversation.ToolName, "", Wire.Pack(proposal)), null));
            }
            Assert.False(context.CanPropose);
            Assert.True(context.Receipts[0].Result.GetProperty("persisted").GetBoolean());
            return Task.FromResult(new ModelReply(null, "Your dentist reminder is scheduled for two hours from now."));
        }
    }

    private (Runtime Runtime, Model Model, Dispatcher Dispatcher, DelegationScheduler Scheduler) Create()
    {
        var dispatcher = new Dispatcher();
        var scheduler = new DelegationScheduler(store, dispatcher, clock);
        var model = new Model();
        return (new Runtime(store, _ => model, new PlanValidator(), new EvidencePolicy(), delegations: scheduler, timeProvider: clock), model, dispatcher, scheduler);
    }

    [Fact]
    public async Task ReminderWaitsForExactApprovalPersistsGrantThenReturnsReceiptToChat()
    {
        var fixture = Create();
        var accepted = fixture.Runtime.Converse("In two hours, remind me to call the dentist.", new());
        Assert.Equal(clock.Now, accepted.DelegationRequestedAt);

        await fixture.Runtime.Execute(accepted.Id);
        var review = store.Get(accepted.Id)!;
        Assert.Equal(RunState.AwaitingApproval, review.State);
        Assert.Equal(DelegationConversation.ToolName, review.Approval!.Action.Name);
        Assert.Empty(store.DelegationJobs());

        await fixture.Runtime.Decide(review.Id, review.Approval.Id, review.Approval.Digest, true);
        for (var attempt = 0; attempt < 300 && store.Get(review.Id)!.State is RunState.Running or RunState.Paused; attempt++)
            await Task.Delay(10);

        var completed = store.Get(review.Id)!;
        Assert.Equal(RunState.Succeeded, completed.State);
        var job = Assert.Single(store.DelegationJobs());
        Assert.Equal(clock.Now.AddHours(2), job.NextRunUtc);
        Assert.Equal(review.Id, job.SourceRunId);
        Assert.Equal("owner:windows+in-app", job.Action.Target);
        Assert.False(store.DelegationGrant(job.GrantId)!.Revoked);
        Assert.Equal(2, fixture.Model.Calls);
        Assert.Equal(0, fixture.Dispatcher.Calls);
        Assert.Contains(completed.Capabilities, receipt => receipt.Authority == "owner-reviewed-delegation" && receipt.Name == DelegationConversation.ToolName);
        Assert.Contains(store.Chats(), message => message.Role == "assistant" && message.Content.Contains("scheduled", StringComparison.OrdinalIgnoreCase));
        clock.Now = job.NextRunUtc!.Value;
        Assert.Equal(1, await fixture.Scheduler.Tick());
        Assert.Contains(store.Events(0, review.Id), item => item.Type == "delegation.occurrence.succeeded" && item.Data.GetProperty("jobId").GetString() == job.Id);
    }

    [Fact]
    public async Task DenialCreatesNoJobOrGrant()
    {
        var fixture = Create();
        var run = fixture.Runtime.Converse("Please remind me in two hours to call the dentist.", new());
        await fixture.Runtime.Execute(run.Id);
        var review = store.Get(run.Id)!;

        await fixture.Runtime.Decide(run.Id, review.Approval!.Id, review.Approval.Digest, false);

        Assert.Equal(RunState.Denied, store.Get(run.Id)!.State);
        Assert.Empty(store.DelegationJobs());
        Assert.Empty(store.DelegationOccurrences());
        Assert.Equal(1, fixture.Model.Calls);
        Assert.Equal(0, fixture.Dispatcher.Calls);
    }

    [Fact]
    public async Task RightNowWaitsForReviewThenDispatchesOnceAtApprovalTime()
    {
        var fixture = Create();
        var run = fixture.Runtime.Converse("Fire off a notification called Testing right now.", new());
        await fixture.Runtime.Execute(run.Id);
        var review = store.Get(run.Id)!;

        Assert.Equal(RunState.AwaitingApproval, review.State);
        Assert.Equal("Review reminder · immediately after approval", review.Summary);
        var proposed = JsonSerializer.Deserialize<ReminderProposal>(review.Approval!.Action.Content!, Wire.Json)!;
        Assert.True(proposed.Immediate);
        Assert.Equal(run.DelegationRequestedAt, proposed.DueUtc);
        Assert.Empty(store.DelegationJobs());

        clock.Now = clock.Now.AddMinutes(5);
        var approvedAt = clock.Now;
        await fixture.Runtime.Decide(review.Id, review.Approval.Id, review.Approval.Digest, true);
        for (var attempt = 0; attempt < 300 && store.Get(review.Id)!.State is RunState.Running or RunState.Paused; attempt++)
            await Task.Delay(10);

        var job = Assert.Single(store.DelegationJobs());
        Assert.Equal(approvedAt, job.NextRunUtc);
        Assert.Equal(1, await fixture.Scheduler.Tick());
        Assert.Equal(1, fixture.Dispatcher.Calls);
        Assert.Equal(0, await fixture.Scheduler.Tick());
        Assert.Single(store.DelegationOccurrences(job.Id));
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
