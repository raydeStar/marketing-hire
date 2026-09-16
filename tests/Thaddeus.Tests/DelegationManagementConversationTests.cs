using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class DelegationManagementConversationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-delegation-management-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    private readonly MutableClock clock = new(new DateTimeOffset(2026, 9, 16, 16, 0, 0, TimeSpan.Zero));

    public DelegationManagementConversationTests() => store = new(root);

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Dispatcher : IDelegationDispatcher
    {
        public Task<DelegationDispatchResult> Dispatch(DelegationJob job, DelegationOccurrence occurrence, CancellationToken cancellation) =>
            throw new InvalidOperationException("Management tests never dispatch a due occurrence.");
    }

    private sealed class Model(string mode, DateTimeOffset? due = null) : IModelProvider
    {
        public int Calls;
        public TokenQuote Quote(Observation observation) => new(0, true, "fixture", 0);
        public Task<ModelReply> Respond(Observation observation, Func<string, Task> onDelta, CancellationToken cancellation)
        {
            Calls++;
            var context = Assert.IsType<DelegationToolContext>(observation.Delegation);
            var job = Assert.Single(context.Jobs);
            Assert.True(context.CanManage);
            return mode switch
            {
                "cancel" => Task.FromResult(new ModelReply(new(DelegationManagementConversation.CancelTool, "",
                    Wire.Pack(new DelegationCancelProposal(job.Id, job.Version))), null)),
                "reschedule" => Task.FromResult(new ModelReply(new(DelegationManagementConversation.RescheduleTool, "",
                    Wire.Pack(new DelegationRescheduleProposal(job.Id, job.Version, due!.Value, context.TimeZone))), null)),
                _ => Task.FromResult(new ModelReply(null, $"{job.Title} is scheduled for {job.NextRunUtc:O}."))
            };
        }
    }

    private (Runtime Runtime, DelegationScheduler Scheduler, DelegationJob Job) Create(string mode, DateTimeOffset? due = null)
    {
        var scheduler = new DelegationScheduler(store, new Dispatcher(), clock);
        var job = scheduler.CreateReminder("Call dentist", "Call the dentist.", clock.Now.AddHours(2), "America/Denver").Job;
        var model = new Model(mode, due);
        return (new Runtime(store, _ => model, new PlanValidator(), new EvidencePolicy(), delegations: scheduler, timeProvider: clock), scheduler, job);
    }

    [Fact]
    public async Task ReadOnlyListingNeedsNoApprovalAndChangesNothing()
    {
        var fixture = Create("list");
        var run = fixture.Runtime.Converse("What do I have scheduled?", new());
        await fixture.Runtime.Execute(run.Id);
        var completed = store.Get(run.Id)!;
        Assert.Equal(RunState.Succeeded, completed.State);
        Assert.Null(completed.Approval);
        Assert.Equal(0, completed.ToolCalls);
        Assert.Equal("scheduled", store.DelegationJobs().Single().State);
        Assert.Contains(store.Chats(), message => message.Role == "assistant" && message.Content.Contains("Call dentist"));
    }

    [Fact]
    public async Task CancellationWaitsForExactApprovalAndRevokesGrant()
    {
        var fixture = Create("cancel");
        var run = fixture.Runtime.Converse("Cancel my dentist reminder.", new());
        await fixture.Runtime.Execute(run.Id);
        var review = store.Get(run.Id)!;
        Assert.Equal(RunState.AwaitingApproval, review.State);
        Assert.Equal(DelegationManagementConversation.CancelTool, review.Approval!.Action.Name);
        Assert.Equal("scheduled", store.DelegationJobs().Single().State);

        var completed = await fixture.Runtime.Decide(review.Id, review.Approval.Id, review.Approval.Digest, true);
        var cancelled = store.DelegationJobs().Single();
        Assert.Equal(RunState.Succeeded, completed.State);
        Assert.Equal("cancelled", cancelled.State);
        Assert.True(cancelled.CancellationRequested);
        Assert.True(store.DelegationGrant(cancelled.GrantId)!.Revoked);
        Assert.Contains(completed.Capabilities, receipt => receipt.Name == DelegationManagementConversation.CancelTool &&
            receipt.Authority == "owner-reviewed-delegation-management");
    }

    [Fact]
    public async Task DenialLeavesTheJobAndGrantUntouched()
    {
        var fixture = Create("cancel");
        var run = fixture.Runtime.Converse("Cancel my dentist reminder.", new());
        await fixture.Runtime.Execute(run.Id); var review = store.Get(run.Id)!;
        var before = store.DelegationJobs().Single(); var grant = store.DelegationGrant(before.GrantId)!;

        await fixture.Runtime.Decide(review.Id, review.Approval!.Id, review.Approval.Digest, false);

        Assert.Equal(Wire.Pack(before), Wire.Pack(store.DelegationJobs().Single()));
        Assert.Equal(Wire.Pack(grant), Wire.Pack(store.DelegationGrant(before.GrantId)));
        Assert.Equal(RunState.Denied, store.Get(run.Id)!.State);
    }

    [Fact]
    public async Task RescheduleRotatesScheduleAuthorityAndVerifiesReadBack()
    {
        var due = clock.Now.AddHours(4);
        var fixture = Create("reschedule", due);
        var originalGrant = store.DelegationGrant(fixture.Job.GrantId)!;
        var run = fixture.Runtime.Converse("Move my dentist reminder to four hours from now.", new());
        await fixture.Runtime.Execute(run.Id); var review = store.Get(run.Id)!;
        Assert.Equal(DelegationManagementConversation.RescheduleTool, review.Approval!.Action.Name);

        var completed = await fixture.Runtime.Decide(review.Id, review.Approval.Id, review.Approval.Digest, true);
        var changed = store.DelegationJobs().Single(); var grant = store.DelegationGrant(changed.GrantId)!;
        Assert.Equal(due, changed.NextRunUtc);
        Assert.NotEqual(fixture.Job.ScheduleVersion, changed.ScheduleVersion);
        Assert.Equal(changed.ScheduleVersion, grant.ScheduleVersion);
        Assert.Equal(originalGrant.Version + 1, grant.Version);
        Assert.False(grant.Revoked);
        Assert.Equal(RunState.Succeeded, completed.State);
        Assert.Contains("Rescheduled", store.Chats().Last().Content);
    }

    [Fact]
    public async Task StaleReviewedRescheduleCannotOverwriteANewerVersion()
    {
        var fixture = Create("reschedule", clock.Now.AddHours(4));
        var run = fixture.Runtime.Converse("Move my dentist reminder to four hours from now.", new());
        await fixture.Runtime.Execute(run.Id); var review = store.Get(run.Id)!;
        var current = store.DelegationJobs().Single();
        store.RescheduleReminder(current.Id, current.Version, clock.Now.AddHours(3), current.Schedule.TimeZone, clock.Now);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Runtime.Decide(review.Id, review.Approval!.Id, review.Approval.Digest, true));

        Assert.Equal(clock.Now.AddHours(3), store.DelegationJobs().Single().NextRunUtc);
        Assert.Equal(0, store.Get(run.Id)!.ToolCalls);
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
