using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class ApprovalRuleTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-approval-rules-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    private static readonly ConnectedToolDefinition Tool = new("calendar-fixture", "Calendar", "events_list", "mcp_calendar_events_list",
        "List calendar events.", JsonSerializer.SerializeToElement(new { type = "object", properties = new { day = new { type = "string" } } }),
        "read external data", "calendar-v1");

    public ApprovalRuleTests() => store = new(root);

    private sealed class Broker : IConnectedToolBroker
    {
        public int Calls;
        public ConnectedToolDefinition[] Snapshot() => [Tool];
        public Task<CapabilityResult> Call(ConnectedToolDefinition tool, JsonElement arguments, CancellationToken cancellation)
        {
            Calls++;
            return Task.FromResult(new CapabilityResult(JsonSerializer.SerializeToElement(new { events = Array.Empty<string>() })));
        }
    }

    private sealed class Model : IModelProvider
    {
        public TokenQuote Quote(Observation observation) => new(0, true, "fixture", 0);
        public Task<ModelReply> Respond(Observation observation, Func<string, Task> onDelta, CancellationToken cancellation) =>
            Task.FromResult(observation.ConnectedTools?.Receipts.Length == 0
                ? new ModelReply(new(Tool.ModelName, "", "{\"day\":\"tomorrow\"}"), null)
                : new ModelReply(null, "Your calendar is clear."));
    }

    private (Runtime Runtime, Broker Broker) Create()
    {
        var broker = new Broker();
        return (new Runtime(store, _ => new Model(), new PlanValidator(), new EvidencePolicy(), connectedTools: broker), broker);
    }

    [Fact]
    public async Task RememberedAllowAppliesOnlyToTheSameBoundActionAndRemovalRestoresReview()
    {
        var fixture = Create();
        var first = fixture.Runtime.Converse("Check my calendar.", new());
        await fixture.Runtime.Execute(first.Id);
        var review = store.Get(first.Id)!;
        Assert.Equal("ask", review.ApprovalPolicy!.Decision);
        await fixture.Runtime.Decide(review.Id, review.Approval!.Id, review.Approval.Digest, true, "allow");
        await Finished(review.Id);

        var rule = Assert.Single(fixture.Runtime.ApprovalRules());
        Assert.Equal("allow", rule.Decision);
        Assert.Contains("Calendar", rule.Label);

        var second = fixture.Runtime.Converse("Check my calendar again.", new());
        await fixture.Runtime.Execute(second.Id);
        await Finished(second.Id);
        Assert.Equal(RunState.Succeeded, store.Get(second.Id)!.State);
        Assert.Equal("allow", store.Get(second.Id)!.ApprovalPolicy!.Decision);
        Assert.Equal(2, fixture.Broker.Calls);

        Assert.Empty(fixture.Runtime.RemoveApprovalRule(rule.Scope));
        var third = fixture.Runtime.Converse("Check my calendar once more.", new());
        await fixture.Runtime.Execute(third.Id);
        Assert.Equal(RunState.AwaitingApproval, store.Get(third.Id)!.State);
        Assert.Equal("ask", store.Get(third.Id)!.ApprovalPolicy!.Decision);
    }

    [Fact]
    public async Task RememberedDenyStopsMatchingActionWithoutCallingConnector()
    {
        var fixture = Create();
        var first = fixture.Runtime.Converse("Check my calendar.", new());
        await fixture.Runtime.Execute(first.Id);
        var review = store.Get(first.Id)!;
        await fixture.Runtime.Decide(review.Id, review.Approval!.Id, review.Approval.Digest, false, "deny");

        var second = fixture.Runtime.Converse("Check my calendar again.", new());
        await fixture.Runtime.Execute(second.Id);
        await Finished(second.Id);
        Assert.Equal(RunState.Denied, store.Get(second.Id)!.State);
        Assert.Equal("deny", store.Get(second.Id)!.ApprovalPolicy!.Decision);
        Assert.Equal(0, fixture.Broker.Calls);
    }

    [Fact]
    public void ApprovalSettingsChatIntentIsLocalAndCreatesAVisibleReply()
    {
        Assert.True(Runtime.ApprovalSettingsIntent("Show my approval settings"));
        Assert.False(Runtime.ApprovalSettingsIntent("Explain approvals to me"));
        var runtime = new Runtime(store, _ => throw new InvalidOperationException("No model call expected."), new PlanValidator(), new EvidencePolicy());
        var run = runtime.PrepareApprovalSettings("Show my approval settings", new());
        Assert.Equal("approvals", run.SettingsSection);
        Assert.Equal(0, run.ModelCalls);
        Assert.Contains(store.Chats(), message => message.Id == run.Id + "-assistant" && message.Content.Contains("Remove any rule"));
    }

    private async Task Finished(string id)
    {
        for (var attempt = 0; attempt < 500 && store.Get(id)!.State is RunState.Queued or RunState.Running or RunState.Paused or RunState.AwaitingApproval; attempt++)
            await Task.Delay(10);
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
