using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class DelegationBriefConversationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-delegation-brief-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    private readonly MutableClock clock = new(new DateTimeOffset(2026, 9, 16, 16, 0, 0, TimeSpan.Zero));
    private static readonly ProviderSnapshot Profile = new("compatible", "fixture", "high", "http://localhost:1234/v1");

    private static ConnectedToolDefinition EmailTool(string version = "connector-v1") => new("mail-connector", "Owner mail",
        "search_email", "mcp_mail_search_email", "Search email messages without modifying them.",
        JsonSerializer.SerializeToElement(new { type = "object", properties = new { query = new { type = "string" }, limit = new { type = "integer" }, since = new { type = "string" } }, required = new[] { "query", "limit", "since" }, additionalProperties = false }),
        "read external data", version);

    private static ConnectedToolDefinition CalendarTool(string version = "connector-v1") => new("calendar-connector", "Owner calendar",
        "list_calendar_events", "mcp_calendar_list_events", "List calendar events without modifying them.",
        JsonSerializer.SerializeToElement(new { type = "object", properties = new { timeMin = new { type = "string" }, timeMax = new { type = "string" } }, required = new[] { "timeMin", "timeMax" }, additionalProperties = false }),
        "read external data", version);

    public DelegationBriefConversationTests() => store = new(root);

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Broker : IConnectedToolBroker
    {
        public ConnectedToolDefinition[] Tools = [EmailTool(), CalendarTool()];
        public readonly List<(string Name, JsonElement Arguments)> Calls = [];
        public bool FailEmail;
        public bool FailCalendar;
        public ConnectedToolDefinition[] Snapshot() => Tools;
        public Task<CapabilityResult> Call(ConnectedToolDefinition tool, JsonElement arguments, CancellationToken cancellation)
        {
            Calls.Add((tool.RemoteName, arguments.Clone()));
            if ((FailEmail && tool.ConnectorId == "mail-connector") || (FailCalendar && tool.ConnectorId == "calendar-connector"))
                throw new IOException("Fixture source unavailable.");
            return Task.FromResult(tool.ConnectorId == "mail-connector"
                ? new CapabilityResult(JsonSerializer.SerializeToElement(new { messages = new[] { new { id = "mail-1", subject = "Review contract", link = "https://mail.invalid/mail-1" } } }))
                : new CapabilityResult(JsonSerializer.SerializeToElement(new { events = new[] { new { id = "event-1", title = "Dentist", link = "https://calendar.invalid/event-1" } } })));
        }
    }

    private sealed class Model : IModelProvider
    {
        public int ConversationCalls;
        public int BriefCalls;
        public bool FailBrief;
        public string LocalTime = "17:00";
        public TokenQuote Quote(Observation observation) => new(100, true, "fixture", 500);
        public Task<ModelReply> Respond(Observation observation, Func<string, Task> onDelta, CancellationToken cancellation)
        {
            if (observation.Delegation == null)
            {
                BriefCalls++;
                if (FailBrief) throw new IOException("Fixture model unavailable.");
                Assert.Contains("mail-1", observation.Goal.Objective);
                Assert.Contains("event-1", observation.Goal.Objective);
                return Task.FromResult(new ModelReply(null, "# Morning brief\n\n- [Dentist](https://calendar.invalid/event-1)\n- [Review contract](https://mail.invalid/mail-1)", 100, 40));
            }

            ConversationCalls++;
            var delegation = observation.Delegation;
            var connected = Assert.IsType<ConnectedToolContext>(observation.ConnectedTools);
            var shape = Assert.Single(DelegationBriefConversation.Eligible(connected.Tools));
            if (delegation.Receipts.Length == 0)
            {
                return Task.FromResult(new ModelReply(new ToolRequest(DelegationBriefConversation.ToolName(shape), "", Wire.Pack(new
                {
                    localTime = LocalTime,
                    timeZone = delegation.TimeZone,
                    destination = "owner:in-app",
                    emailSelectionRule = "Up to 12 inbox messages received during the prior 24 hours, newest first.",
                    emailArguments = new { query = "in:inbox", limit = 12, since = "{{sinceUtc}}" },
                    calendarArguments = new { timeMin = "{{startUtc}}", timeMax = "{{endUtc}}" }
                })), null));
            }
            Assert.True(delegation.Receipts[0].Result.GetProperty("persisted").GetBoolean());
            return Task.FromResult(new ModelReply(null, "Your weekday brief is scheduled and will remain read-only."));
        }
    }

    private (Runtime Runtime, Model Model, Broker Broker, DelegationScheduler Scheduler) Create()
    {
        var broker = new Broker(); var model = new Model();
        var dispatcher = new ConnectedBriefDelegationDispatcher(broker, _ => model);
        var scheduler = new DelegationScheduler(store, dispatcher, clock);
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
        var accepted = fixture.Runtime.Converse("Every weekday at 5 PM, give me my calendar and important email.", Profile);
        await fixture.Runtime.Execute(accepted.Id);
        var review = store.Get(accepted.Id)!;
        Assert.Equal(RunState.AwaitingApproval, review.State);
        Assert.StartsWith("delegation_brief_", review.Approval!.Action.Name);
        Assert.Empty(store.DelegationJobs());
        Assert.Empty(fixture.Broker.Calls);

        await fixture.Runtime.Decide(review.Id, review.Approval.Id, review.Approval.Digest, true);
        await WaitForFinal(store, review.Id);
        return (review, Assert.Single(store.DelegationJobs()), fixture.Runtime, fixture.Model, fixture.Broker, fixture.Scheduler);
    }

    [Fact]
    public async Task ExactApprovalPersistsReadOnlyGrantAndCreatesSourceLinkedUnreadBrief()
    {
        var fixture = await Approve();
        Assert.Equal(RunState.Succeeded, store.Get(fixture.Review.Id)!.State);
        Assert.Equal("brief", fixture.Job.Kind);
        Assert.Equal("owner:in-app", fixture.Job.Action.Target);
        Assert.Empty(fixture.Broker.Calls);
        var grant = store.DelegationGrant(fixture.Job.GrantId)!;
        Assert.Equal(520, grant.MaxExternalCalls);
        Assert.Equal(260, grant.MaxModelCalls);

        clock.Now = fixture.Job.NextRunUtc!.Value;
        Assert.Equal(1, await fixture.Scheduler.Tick());
        Assert.Equal(0, await fixture.Scheduler.Tick());
        Assert.Equal(2, fixture.Broker.Calls.Count);
        Assert.Equal(1, fixture.Model.BriefCalls);

        var email = fixture.Broker.Calls.Single(call => call.Name == "search_email").Arguments;
        Assert.Equal(12, email.GetProperty("limit").GetInt32());
        Assert.DoesNotContain("{{", email.GetRawText());
        var calendar = fixture.Broker.Calls.Single(call => call.Name == "list_calendar_events").Arguments;
        var start = DateTimeOffset.Parse(calendar.GetProperty("timeMin").GetString()!);
        var end = DateTimeOffset.Parse(calendar.GetProperty("timeMax").GetString()!);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(fixture.Job.Schedule.TimeZone);
        Assert.Equal(new DateTime(2026, 9, 16), TimeZoneInfo.ConvertTime(start, zone).Date);
        Assert.Equal(new DateTime(2026, 9, 17), TimeZoneInfo.ConvertTime(end, zone).Date);

        var occurrence = Assert.Single(store.DelegationOccurrences(fixture.Job.Id));
        Assert.Equal("succeeded", occurrence.State);
        Assert.Null(occurrence.ReadAt);
        Assert.True(occurrence.ProviderEvidence!.Value.GetProperty("sourceMutation").ValueKind == JsonValueKind.False);
        Assert.Contains("https://calendar.invalid/event-1", occurrence.ProviderEvidence.Value.GetProperty("brief").GetString());
        Assert.Equal("scheduled", store.DelegationJobs().Single().State);
        Assert.True(store.DelegationJobs().Single().NextRunUtc > clock.Now);
    }

    [Fact]
    public async Task MissingBriefTimeAsksOnceThenFollowUpCanReachExactReview()
    {
        var fixture = Create();
        var run = fixture.Runtime.Converse("Every weekday, give me my calendar and important email.", Profile);
        await fixture.Runtime.Execute(run.Id);

        var clarification = store.Get(run.Id)!;
        Assert.Equal(RunState.AwaitingInput, clarification.State);
        Assert.Null(clarification.Approval);
        Assert.Empty(store.DelegationJobs());
        Assert.Empty(fixture.Broker.Calls);
        Assert.Contains("What local time", store.Chats().Last(message => message.Role == "assistant").Content);

        var followUp = fixture.Runtime.Converse("At 5 PM.", Profile);
        await fixture.Runtime.Execute(followUp.Id);
        var review = store.Get(followUp.Id)!;
        Assert.Equal(RunState.AwaitingApproval, review.State);
        Assert.StartsWith("delegation_brief_", review.Approval!.Action.Name);
        Assert.Empty(store.DelegationJobs());
        Assert.Empty(fixture.Broker.Calls);
    }

    [Fact]
    public async Task ModelCannotReplaceTheOwnersExplicitBriefTime()
    {
        var fixture = Create();
        var run = fixture.Runtime.Converse("Every workday at 8 AM, summarize my calendar and important email.", Profile);
        await fixture.Runtime.Execute(run.Id);

        var clarification = store.Get(run.Id)!;
        Assert.Equal(RunState.AwaitingInput, clarification.State);
        Assert.Null(clarification.Approval);
        Assert.Empty(store.DelegationJobs());
        Assert.Empty(fixture.Broker.Calls);
        Assert.Contains("What local time", store.Chats().Last(message => message.Role == "assistant").Content);
    }

    [Fact]
    public async Task DenialReadsNothingAndPersistsNoJob()
    {
        var fixture = Create();
        var accepted = fixture.Runtime.Converse("Send me a weekday calendar and email brief at 5 PM.", Profile);
        await fixture.Runtime.Execute(accepted.Id); var review = store.Get(accepted.Id)!;
        await fixture.Runtime.Decide(review.Id, review.Approval!.Id, review.Approval.Digest, false);

        Assert.Equal(RunState.Denied, store.Get(accepted.Id)!.State);
        Assert.Contains("nothing scheduled or read", store.Get(accepted.Id)!.Summary);
        Assert.Empty(store.DelegationJobs());
        Assert.Empty(fixture.Broker.Calls);
    }

    [Fact]
    public async Task ChangedConnectorRefusesStaleApprovalBeforeAnyRead()
    {
        var fixture = Create();
        var accepted = fixture.Runtime.Converse("Send me a weekday calendar and email brief at 5 PM.", Profile);
        await fixture.Runtime.Execute(accepted.Id); var review = store.Get(accepted.Id)!;
        fixture.Broker.Tools = [EmailTool("connector-v2"), CalendarTool()];

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Runtime.Decide(review.Id, review.Approval!.Id, review.Approval.Digest, true));
        Assert.Empty(store.DelegationJobs());
        Assert.Empty(fixture.Broker.Calls);
        Assert.Equal(RunState.AwaitingApproval, store.Get(accepted.Id)!.State);
    }

    [Fact]
    public async Task UnavailableSourcesAreDistinguishedFromEmptyAndRecurringScheduleContinues()
    {
        var fixture = await Approve();
        fixture.Broker.FailEmail = true;
        fixture.Broker.FailCalendar = true;
        clock.Now = fixture.Job.NextRunUtc!.Value;

        Assert.Equal(1, await fixture.Scheduler.Tick());

        var occurrence = Assert.Single(store.DelegationOccurrences(fixture.Job.Id));
        Assert.Equal("succeeded", occurrence.State);
        Assert.Equal(0, fixture.Model.BriefCalls);
        var brief = occurrence.ProviderEvidence!.Value.GetProperty("brief").GetString()!;
        Assert.Contains("Calendar unavailable", brief);
        Assert.Contains("No empty-inbox or empty-calendar claim", brief);
        Assert.Equal("scheduled", store.DelegationJobs().Single().State);
    }

    [Fact]
    public async Task ModelFailureLeavesVisibleFailedOccurrenceWithoutStoppingRecurrence()
    {
        var fixture = await Approve();
        fixture.Model.FailBrief = true;
        clock.Now = fixture.Job.NextRunUtc!.Value;

        Assert.Equal(1, await fixture.Scheduler.Tick());

        var occurrence = Assert.Single(store.DelegationOccurrences(fixture.Job.Id));
        Assert.Equal("failed", occurrence.State);
        Assert.Contains("could not be composed", occurrence.Summary);
        Assert.Equal("scheduled", store.DelegationJobs().Single().State);
        Assert.True(store.DelegationJobs().Single().NextRunUtc > clock.Now);
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
