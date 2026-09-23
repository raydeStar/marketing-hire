using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class CompanyMeetingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "company-meeting-" + Guid.NewGuid().ToString("N"));
    private static readonly string[] Sources = ["https://news.ycombinator.com/item?id=47667504", "https://news.ycombinator.com/item?id=49703771"];
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private static MeetingCommand Command(string action, int version, string? content = null) => new(Guid.NewGuid().ToString("N"), version, action, content);
    private static MeetingCommand Approval(CompanyMeeting meeting) => new(Guid.NewGuid().ToString("N"), meeting.Version, "approve",
        PlanDigest: meeting.ProposalDigest, SourceUrls: Sources);
    private static async Task<CompanyMeeting> Create(CompanyMeetings service) => await service.Change(null,
        new("create", 0, "create", Title: "First customers", Agenda: "Find three angles and draft one local post", Ethos: "Evidence before claims; no spending", Participants: ["ceo", "marketing"]), "owner", default);
    private static void Seed(Store store, CompanyMeeting meeting) => store.Setting("company-meetings-v1", Wire.Pack(new MeetingLedger([meeting], [])));
    private sealed class FakeRuntime : ICompanyMeetingRuntime
    {
        public string ModelRoute => "openai/gpt-5.6-luna";
        public bool Ready = true, Unknown, MissingArtifact, BadKind, BadProfile, Preflight;
        public int Replies, Turns;
        public Dictionary<string, string> Released = [];
        public Dictionary<string, string> TaskStates = [];
        public TaskCompletionSource? Started, Continue;
        public Task<string> MeetingReply(string role, string id, string prompt, CancellationToken cancellation)
        {
            Replies++;
            return Task.FromResult(role == "marketing"
                ? JsonSerializer.Serialize(new { profile = BadProfile ? "generic_marketing" : "personal_brand_content_pilot_v1", summary = "Learn first", resources = "Two internal tasks, existing subscription", requiresOwnerApproval = false,
                    actions = new[] { new { kind = BadKind ? "email_send" : "evidence_brief", title = "Identify customer questions", outcome = "Save three sourced angles" },
                                      new { kind = "local_draft", title = "Write local draft", outcome = "Save one reviewable draft" } } })
                : JsonSerializer.Serialize(new { verdict = "accept", rationale = "Bounded and aligned", questions = Array.Empty<string>(), requiresOwnerApproval = false }));
        }
        public Task<bool> VerifyMeetingSources(string[] urls, CancellationToken cancellation) => Task.FromResult(urls.SequenceEqual(Sources));
        public Task<string> ReleaseMeetingTask(string requestId, string title, string action, CancellationToken cancellation)
        { if (!Released.TryGetValue(requestId, out var id)) Released[requestId] = id = Guid.NewGuid().ToString("N"); return Task.FromResult(id); }
        public Task<bool> MeetingTaskReady(string id, CancellationToken cancellation) => Task.FromResult(Ready);
        public async Task<MeetingActionResult> RunMeetingAction(CompanyMeeting meeting, MeetingAction action, MeetingGrant grant, CancellationToken cancellation)
        {
            Turns++; Started?.TrySetResult(); if (Continue != null) await Continue.Task.WaitAsync(cancellation);
            if (Preflight) throw new MeetingPreflightException("Source unavailable before model call", new IOException("source"));
            if (Unknown) throw new IOException("Outcome unknown");
            return new(MissingArtifact ? "" : "Saved " + action.Kind, grant.SourceUrls, []);
        }
        public Task UpdateMeetingTask(string taskId, string requestId, string status, string nextAction, CancellationToken cancellation)
        { TaskStates[taskId] = status; return Task.CompletedTask; }
        public Task PauseMeetingTask(string taskId, string requestId, CancellationToken cancellation)
        { TaskStates[taskId] = "paused"; return Task.CompletedTask; }
    }

    [Fact] public async Task CeoRecommendationAndSilenceNeverCreatePermission()
    {
        using var store = new Store(root); var runtime = new FakeRuntime(); var service = new CompanyMeetings(store, runtime);
        var meeting = await Create(service); var proposal = Command("propose", meeting.Version);
        meeting = await service.Change(meeting.Id, proposal, "owner", default);
        Assert.Equal("reviewed", meeting.Stage); Assert.Equal("accept", meeting.Review!.Verdict);
        Assert.Null(meeting.ApprovedBy); Assert.Null(meeting.Grant); Assert.Null(meeting.ReleaseAt);
        Assert.Equal(2, runtime.Replies);
        await service.Change(meeting.Id, proposal, "owner", default); Assert.Equal(2, runtime.Replies);
        await service.ProcessWork(default); Assert.Empty(runtime.Released); Assert.Equal(0, runtime.Turns);
        await service.Change(meeting.Id, Command("veto", meeting.Version), "owner", default);
        await service.ProcessWork(default); Assert.Empty(runtime.Released);
    }

    [Fact] public async Task ExactOwnerGrantReleasesTwoSequentialTasksAndReplayIsSafe()
    {
        using var store = new Store(root); var runtime = new FakeRuntime(); var service = new CompanyMeetings(store, runtime);
        var meeting = await Create(service); meeting = await service.Change(meeting.Id, Command("propose", meeting.Version), "owner", default);
        var approval = Approval(meeting);
        meeting = await service.Change(meeting.Id, approval, "Owner session 1", default);
        Assert.Equal("Owner session 1", meeting.ApprovedBy);
        Assert.Equal("owner-session", meeting.Grant!.AuthoritySource);
        Assert.Equal(meeting.ProposalDigest, meeting.Grant.PlanDigest);
        await service.Change(meeting.Id, approval, "Owner session 1", default);
        await service.ProcessWork(default); await service.ProcessWork(default); await service.ProcessWork(default);
        var saved = service.List()[0];
        Assert.Equal("closed", saved.Stage); Assert.Equal(2, runtime.Released.Count); Assert.Equal(2, runtime.Turns);
        Assert.Equal(2, saved.Grant!.DispatchAttempts); Assert.Equal(2, saved.Grant.TaskIds!.Length);
        Assert.Equal(2, saved.Artifacts!.Length); Assert.All(saved.Artifacts, artifact => Assert.False(artifact.OwnerAccepted));
        Assert.Contains(runtime.TaskStates.Values, status => status == "done");
        Assert.Contains(runtime.TaskStates.Values, status => status == "needs_you");
        var draft = saved.Artifacts.Single(a => a.Kind == "local_draft");
        saved = await service.Change(saved.Id, Command("accept-artifact", saved.Version, draft.Id), "Owner session 1", default);
        Assert.True(saved.Artifacts!.Single(a => a.Id == draft.Id).OwnerAccepted);
        Assert.True(saved.Artifacts!.Single(a => a.Id == draft.Id).AcceptanceSynced);
        Assert.Equal("done", runtime.TaskStates[draft.TaskId]);
        Assert.Equal(2, runtime.Turns);
    }

    [Fact] public async Task PersistedAcceptanceReconcilesBoardWithoutRedispatch()
    {
        using var store = new Store(root); var runtime = new FakeRuntime(); var service = new CompanyMeetings(store, runtime);
        var meeting = await Create(service); meeting = await service.Change(meeting.Id, Command("propose", meeting.Version), "owner", default);
        meeting = await service.Change(meeting.Id, Approval(meeting), "owner", default);
        await service.ProcessWork(default); await service.ProcessWork(default); await service.ProcessWork(default);
        var saved = service.List()[0]; var draft = saved.Artifacts!.Single(a => a.Kind == "local_draft");
        Seed(store, saved with { Artifacts = saved.Artifacts!.Select(a => a.Id == draft.Id
            ? a with { OwnerAccepted = true, AcceptedBy = "owner", AcceptedAt = DateTimeOffset.UtcNow, AcceptanceSynced = false }
            : a).ToArray() });
        Assert.Equal("needs_you", runtime.TaskStates[draft.TaskId]);

        var restored = new CompanyMeetings(store, runtime);
        await restored.ProcessWork(default);

        Assert.Equal("done", runtime.TaskStates[draft.TaskId]);
        Assert.True(restored.List()[0].Artifacts!.Single(a => a.Id == draft.Id).AcceptanceSynced);
        Assert.Equal(2, runtime.Turns);
        Assert.Equal(2, runtime.Released.Count);
    }

    [Fact] public async Task StaleDigestUnknownSourcesAndProhibitedActionCannotBeApproved()
    {
        using var store = new Store(root); var runtime = new FakeRuntime { BadKind = true }; var service = new CompanyMeetings(store, runtime);
        var meeting = await Create(service); meeting = await service.Change(meeting.Id, Command("propose", meeting.Version), "owner", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Change(meeting.Id, Approval(meeting), "owner", default));
        runtime.BadKind = false;
        meeting = await service.Change(meeting.Id, Command("propose", meeting.Version), "owner", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Change(meeting.Id, Approval(meeting) with { PlanDigest = "bad" }, "owner", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Change(meeting.Id, Approval(meeting) with { SourceUrls = ["https://news.ycombinator.com/item?id=1", Sources[1]] }, "owner", default));
        meeting = await service.Change(meeting.Id, Command("message", meeting.Version, "Change the audience"), "owner", default);
        Assert.Null(meeting.Grant); Assert.Null(meeting.Review);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Change(meeting.Id, Approval(meeting), "owner", default));
    }

    [Fact] public async Task GenericPlanCannotBorrowPilotWorkerAuthority()
    {
        using var store = new Store(root); var runtime = new FakeRuntime { BadProfile = true }; var service = new CompanyMeetings(store, runtime);
        var meeting = await Create(service); meeting = await service.Change(meeting.Id, Command("propose", meeting.Version), "owner", default);
        Assert.Equal("unclassified", meeting.Plan!.Profile);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Change(meeting.Id, Approval(meeting), "owner", default));
        await service.ProcessWork(default); Assert.Empty(runtime.Released);
    }

    [Fact] public async Task VetoCancelsActiveTurnAndPausesRemainingWork()
    {
        using var store = new Store(root); var runtime = new FakeRuntime { Started = new(), Continue = new() }; var service = new CompanyMeetings(store, runtime);
        var meeting = await Create(service); meeting = await service.Change(meeting.Id, Command("propose", meeting.Version), "owner", default);
        meeting = await service.Change(meeting.Id, Approval(meeting), "owner", default);
        var run = service.ProcessWork(default); await runtime.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var veto = await service.Change(meeting.Id, Command("veto", service.List()[0].Version), "owner", default).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("vetoed", veto.Stage); Assert.True(veto.Grant!.Revoked);
        await run; await service.ProcessWork(default);
        Assert.Equal(1, runtime.Turns); Assert.Equal("vetoed", service.List()[0].Stage);
        Assert.Equal("paused", service.List()[0].Plan!.Actions[1].State);
        Assert.All(runtime.TaskStates.Values, status => Assert.Equal("paused", status));
    }

    [Fact] public async Task RestartLeavesInterruptedWorkUnknownAndDoesNotRedispatch()
    {
        CompanyMeeting meeting;
        using (var store = new Store(root))
        {
            var service = new CompanyMeetings(store, new FakeRuntime()); meeting = await Create(service);
            meeting = await service.Change(meeting.Id, Command("propose", meeting.Version), "owner", default);
            meeting = await service.Change(meeting.Id, Approval(meeting), "owner", default);
            Seed(store, meeting with { Stage = "closed", Plan = meeting.Plan! with { Actions = [meeting.Plan.Actions[0] with { State = "working", TaskId = "a".PadRight(32, 'a') }, meeting.Plan.Actions[1] with { State = "queued", TaskId = "b".PadRight(32, 'b') }] } });
        }
        using var reopened = new Store(root); var runtime = new FakeRuntime(); var restored = new CompanyMeetings(reopened, runtime);
        await restored.ProcessWork(default);
        Assert.Equal("unknown", restored.List()[0].Plan!.Actions[0].State);
        Assert.Equal("paused", restored.List()[0].Plan!.Actions[1].State);
        Assert.Equal(0, runtime.Turns);
    }

    [Fact] public async Task MissingArtifactAndExpiredGrantStopFurtherDispatch()
    {
        using var store = new Store(root); var runtime = new FakeRuntime { MissingArtifact = true }; var service = new CompanyMeetings(store, runtime);
        var meeting = await Create(service); meeting = await service.Change(meeting.Id, Command("propose", meeting.Version), "owner", default);
        meeting = await service.Change(meeting.Id, Approval(meeting), "owner", default);
        await service.ProcessWork(default); await service.ProcessWork(default);
        Assert.Empty(service.List()[0].Artifacts ?? []);
        Assert.Equal("failed", service.List()[0].Plan!.Actions[0].State);
        Assert.Equal("paused", service.List()[0].Plan!.Actions[1].State);
        meeting = meeting with { Grant = meeting.Grant! with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) } };
        Seed(store, meeting);
        await service.ProcessWork(default);
        Assert.Equal("paused", service.List()[0].Stage);
    }

    [Fact] public async Task UnconfirmedWorkerOutcomeIsNotRetried()
    {
        using var store = new Store(root); var runtime = new FakeRuntime { Unknown = true }; var service = new CompanyMeetings(store, runtime);
        var meeting = await Create(service); meeting = await service.Change(meeting.Id, Command("propose", meeting.Version), "owner", default);
        meeting = await service.Change(meeting.Id, Approval(meeting), "owner", default);
        await service.ProcessWork(default); await service.ProcessWork(default);
        Assert.Equal(1, runtime.Turns);
        Assert.Equal("unknown", service.List()[0].Plan!.Actions[0].State);
        Assert.Equal("paused", service.List()[0].Plan!.Actions[1].State);
    }

    [Fact] public async Task ExecutionDeadlineRequestsCancellationAndLeavesOutcomeUnknown()
    {
        using var store = new Store(root); var runtime = new FakeRuntime { Started = new(), Continue = new() }; var service = new CompanyMeetings(store, runtime);
        var meeting = await Create(service); meeting = await service.Change(meeting.Id, Command("propose", meeting.Version), "owner", default);
        meeting = await service.Change(meeting.Id, Approval(meeting), "owner", default);
        Seed(store, meeting with { Grant = meeting.Grant! with { ExecutionDeadline = DateTimeOffset.UtcNow.AddMilliseconds(150) } });
        await service.ProcessWork(default);
        var saved = service.List()[0];
        Assert.Equal(1, runtime.Turns); Assert.Equal("unknown", saved.Plan!.Actions[0].State);
        Assert.Equal("paused", saved.Plan.Actions[1].State);
        Assert.Contains("canceled or timed out", saved.Error);
    }

    [Fact] public async Task UnavailableSourcePausesWithoutClaimingUnknownModelOutcome()
    {
        using var store = new Store(root); var runtime = new FakeRuntime { Preflight = true }; var service = new CompanyMeetings(store, runtime);
        var meeting = await Create(service); meeting = await service.Change(meeting.Id, Command("propose", meeting.Version), "owner", default);
        meeting = await service.Change(meeting.Id, Approval(meeting), "owner", default);
        await service.ProcessWork(default);
        var saved = service.List()[0];
        Assert.Equal("paused", saved.Stage);
        Assert.Equal("paused", saved.Plan!.Actions[0].State);
        Assert.Equal("paused", saved.Plan.Actions[1].State);
        Assert.Contains("Source unavailable", saved.Error);
    }
}
