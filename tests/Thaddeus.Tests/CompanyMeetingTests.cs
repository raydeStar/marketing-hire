using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class CompanyMeetingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "company-meeting-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private static MeetingCommand Command(string action, int version, string? content = null) => new(Guid.NewGuid().ToString("N"), version, action, content);
    private static async Task<CompanyMeeting> Create(CompanyMeetings service) => await service.Change(null,
        new("create", 0, "create", Title: "First customers", Agenda: "Research our audience", Ethos: "Evidence before claims; no spending", Participants: ["ceo", "marketing"]), "owner", default);
    private static void Seed(Store store, CompanyMeeting meeting) => store.Setting("company-meetings-v1", Wire.Pack(new MeetingLedger([meeting], [])));
    private sealed class FakeRuntime : ICompanyMeetingRuntime
    {
        public bool Gate, Ready = true, Unknown;
        public int Replies, Turns;
        public Dictionary<string, string> Released = [];
        public TaskCompletionSource? Started, Continue;
        public Task<string> MeetingReply(string role, string id, string prompt, CancellationToken cancellation)
        {
            Replies++;
            return Task.FromResult(role == "marketing"
                ? JsonSerializer.Serialize(new { summary = "Learn first", resources = "One internal research task, 5 minutes, existing subscription", requiresOwnerApproval = Gate, actions = new[] { new { title = "Identify customer questions", outcome = "Save three sourced questions" } } })
                : JsonSerializer.Serialize(new { verdict = "accept", rationale = "Bounded and aligned", questions = Array.Empty<string>(), requiresOwnerApproval = Gate }));
        }
        public Task<string> ReleaseMeetingTask(string requestId, string title, string action, CancellationToken cancellation)
        { if (!Released.TryGetValue(requestId, out var id)) Released[requestId] = id = Guid.NewGuid().ToString("N"); return Task.FromResult(id); }
        public Task<bool> MeetingTaskReady(string id, CancellationToken cancellation) => Task.FromResult(Ready);
        public async Task<IResult> Chat(JsonElement input, CancellationToken cancellation)
        { Turns++; Started?.TrySetResult(); if (Continue != null) await Continue.Task; return Unknown ? Results.StatusCode(502) : Results.Ok(new { status = "succeeded" }); }
    }

    [Fact] public async Task RoutinePlanGetsCeoApprovalButNoWorkBeforeVetoWindow()
    {
        using var store = new Store(root); var runtime = new FakeRuntime(); var service = new CompanyMeetings(store, runtime);
        var meeting = await Create(service); var command = Command("propose", meeting.Version);
        meeting = await service.Change(meeting.Id, command, "owner", default);
        Assert.Equal("approved", meeting.Stage); Assert.Equal("CEO", meeting.ApprovedBy); Assert.NotNull(meeting.ReleaseAt);
        Assert.Equal(2, runtime.Replies);
        await service.Change(meeting.Id, command, "owner", default); Assert.Equal(2, runtime.Replies);
        await service.ProcessWork(default); Assert.Empty(runtime.Released); Assert.Equal(0, runtime.Turns);
        await service.Change(meeting.Id, Command("veto", meeting.Version), "owner", default);
        await service.ProcessWork(default); Assert.Empty(runtime.Released); Assert.Equal("vetoed", service.List()[0].Stage);
    }

    [Fact] public async Task ResourcePlanWaitsForOwnerAndReleaseIsReplaySafe()
    {
        using var store = new Store(root); var runtime = new FakeRuntime { Gate = true }; var service = new CompanyMeetings(store, runtime);
        var meeting = await Create(service);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Change(meeting.Id, Command("close", meeting.Version), "owner", default));
        meeting = await service.Change(meeting.Id, Command("propose", meeting.Version), "owner", default);
        Assert.Equal("reviewed", meeting.Stage); Assert.Null(meeting.ApprovedBy); Assert.Null(meeting.ReleaseAt);
        await service.ProcessWork(default); Assert.Empty(runtime.Released);
        var approve = Command("approve", meeting.Version);
        meeting = await service.Change(meeting.Id, approve, "owner-session", default);
        Assert.Equal("owner-session", meeting.ApprovedBy);
        await service.Change(meeting.Id, approve, "owner-session", default);
        await service.ProcessWork(default); await service.ProcessWork(default);
        Assert.Single(runtime.Released); Assert.Equal(1, runtime.Turns);
        var saved = service.List()[0]; Assert.Equal("closed", saved.Stage); Assert.Equal("turn_complete", saved.Plan!.Actions[0].State);
        Assert.Contains(saved.Messages, m => m.Speaker == "CEO");
    }

    [Fact] public async Task NewDirectionInvalidatesApprovalAndBoardPausePreventsDispatch()
    {
        using var store = new Store(root); var runtime = new FakeRuntime { Ready = false }; var service = new CompanyMeetings(store, runtime);
        var meeting = await Create(service); meeting = await service.Change(meeting.Id, Command("propose", meeting.Version), "owner", default);
        meeting = await service.Change(meeting.Id, Command("message", meeting.Version, "Change the target audience"), "owner", default);
        Assert.Null(meeting.Review); Assert.Null(meeting.ApprovedRevision); Assert.Null(meeting.ReleaseAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Change(meeting.Id, Command("approve", meeting.Version), "owner", default));
        meeting = await service.Change(meeting.Id, Command("propose", meeting.Version), "owner", default);
        meeting = await service.Change(meeting.Id, Command("close", meeting.Version), "owner", default);
        await service.ProcessWork(default); Assert.Equal(0, runtime.Turns); Assert.Equal("paused", service.List()[0].Plan!.Actions[0].State);
    }

    [Fact] public async Task VetoDuringActiveTurnPreservesNotesAndStopsRemainingWork()
    {
        using var store = new Store(root); var runtime = new FakeRuntime { Started = new(), Continue = new() }; var service = new CompanyMeetings(store, runtime);
        var meeting = await Create(service); meeting = await service.Change(meeting.Id, Command("propose", meeting.Version), "owner", default);
        meeting = meeting with { Stage = "releasing", Plan = meeting.Plan! with { Actions = [meeting.Plan.Actions[0], new("Second task", "Should never run")] } };
        Seed(store, meeting);
        var run = service.ProcessWork(default); await runtime.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var veto = await service.Change(meeting.Id, Command("veto", meeting.Version), "owner", default).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("vetoed", veto.Stage); runtime.Continue.TrySetResult(); await run;
        await service.ProcessWork(default); Assert.Equal(1, runtime.Turns); Assert.Equal("vetoed", service.List()[0].Stage);
        Assert.Equal("paused", service.List()[0].Plan!.Actions[1].State);
    }

    [Fact] public async Task RestartPreservesMinutesAndDoesNotRepeatUnconfirmedWork()
    {
        CompanyMeeting meeting;
        using (var store = new Store(root))
        {
            var service = new CompanyMeetings(store, new FakeRuntime()); meeting = await Create(service);
            meeting = await service.Change(meeting.Id, Command("propose", meeting.Version), "owner", default);
            Seed(store, meeting with { Stage = "closed", Plan = meeting.Plan! with { Actions = [meeting.Plan.Actions[0] with { State = "working", TaskId = "a".PadRight(32, 'a') }] } });
        }
        using var reopened = new Store(root); var runtime = new FakeRuntime(); var restored = new CompanyMeetings(reopened, runtime);
        await restored.ProcessWork(default);
        Assert.Equal("unknown", restored.List()[0].Plan!.Actions[0].State); Assert.Equal(0, runtime.Turns); Assert.Equal(meeting.Messages.Length, restored.List()[0].Messages.Length);
    }

    [Fact] public async Task UnconfirmedExecutionPausesRemainingActions()
    {
        using var store = new Store(root); var runtime = new FakeRuntime { Unknown = true }; var service = new CompanyMeetings(store, runtime);
        var meeting = await Create(service); meeting = await service.Change(meeting.Id, Command("propose", meeting.Version), "owner", default);
        Seed(store, meeting with { Stage = "releasing", Plan = meeting.Plan! with { Actions = [meeting.Plan.Actions[0], new("Next task", "Wait for reconciliation")] } });
        await service.ProcessWork(default); await service.ProcessWork(default);
        Assert.Equal(1, runtime.Turns); Assert.Equal("unknown", service.List()[0].Plan!.Actions[0].State); Assert.Equal("paused", service.List()[0].Plan!.Actions[1].State);
    }

    [Fact] public async Task MissingResourceAssessmentAndOpenQuestionsCannotAutoApprove()
    {
        using var store = new Store(root); var service = new CompanyMeetings(store, new FakeRuntime()); var meeting = await Create(service);
        var plan = CompanyMeetings.ApplyReply(meeting, "propose", """{"summary":"Draft","resources":"Unknown","actions":[{"title":"Draft outline","outcome":"One page"}]}""");
        Assert.True(plan.Plan!.RequiresOwnerApproval);
        var review = CompanyMeetings.ApplyReply(plan, "review", """{"verdict":"accept","rationale":"Need detail","questions":["What is the budget?"],"requiresOwnerApproval":false}""");
        Assert.Equal("revise", review.Review!.Verdict);
        Assert.ThrowsAny<JsonException>(() => CompanyMeetings.ApplyReply(plan, "review", "not a decision"));
    }
}
