using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class ConversationRetryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-retry-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    public ConversationRetryTests() => store = new(root);
    private static string Id() => Guid.NewGuid().ToString("N");
    private Runtime Runtime(IModelProvider provider) => new(store, _ => provider, new PlanValidator(), new EvidencePolicy());
    private sealed class Provider : IModelProvider
    {
        public List<Observation> Seen { get; } = [];
        public bool Fail { get; set; }
        public Task<ModelReply> Respond(Observation o, Func<string, Task> delta, CancellationToken c)
        {
            Seen.Add(o);
            if (Fail) throw new HttpRequestException("Fictional unavailable provider");
            return Task.FromResult(new ModelReply(null, "Answer " + Seen.Count, 10, 5));
        }
    }
    [Fact] public async Task RetryHasFreshAccountingFrozenHistoryAndCurrentProviderWithoutDuplicateUserMessage()
    {
        var provider = new Provider(); var runtime = Runtime(provider);
        var intro = runtime.Converse("My raven is Juniper", new()); await runtime.Execute(intro.Id);
        provider.Fail = true;
        var source = runtime.Converse("What is my raven called?", new(), new(MaxTotalTokens: 1234)); await runtime.Execute(source.Id);
        var original = Wire.Pack(store.Get(source.Id));
        provider.Fail = false;
        var later = runtime.Converse("An unrelated later message", new()); await runtime.Execute(later.Id);
        var chosen = new ProviderSnapshot(Model: "replacement-fixture");
        var retry = runtime.RetryConversation(source.Id, Id(), chosen);
        Assert.Equal(source.ConversationContext, retry.ConversationContext);
        Assert.DoesNotContain(retry.ConversationContext, m => m.Content.Contains("unrelated"));
        Assert.Equal(chosen, retry.Goal.Provider); Assert.Equal(source.Goal.Objective, retry.Goal.Objective); Assert.Equal(source.Goal.Limits, retry.Goal.Limits);
        Assert.Equal(0, retry.ModelCalls); Assert.Equal(0, retry.ChargedTokens);
        await runtime.Execute(retry.Id);
        Assert.Equal(original, Wire.Pack(store.Get(source.Id))); Assert.Equal(1234, store.Get(source.Id)!.ChargedTokens);
        Assert.Equal(15, store.Get(retry.Id)!.ChargedTokens); Assert.Equal(RunState.Succeeded, store.Get(retry.Id)!.State);
        Assert.Single(store.Chats(), m => m.Content == source.Goal.Objective);
        Assert.Contains(store.Events(0, retry.Id), e => e.Type == "conversation.retry.accepted");
    }
    [Fact] public async Task IdempotentAdmissionSurvivesExecutionAndRefusesCrossMessageKeyReuse()
    {
        var provider = new Provider(); var runtime = Runtime(provider);
        var source = runtime.Converse("First", new()); await runtime.Execute(source.Id);
        var operation = Id(); var retry = runtime.RetryConversation(source.Id, operation, new());
        Assert.Equal(retry.Id, runtime.RetryConversation(source.Id, operation, new()).Id);
        Assert.Throws<InvalidOperationException>(() => runtime.RetryConversation(source.Id, Id(), new()));
        await Task.WhenAll(runtime.Execute(retry.Id), runtime.Execute(retry.Id));
        Assert.Equal(retry.Id, runtime.RetryConversation(source.Id, operation, new()).Id); Assert.Equal(2, provider.Seen.Count);
        Assert.Throws<ArgumentException>(() => runtime.RetryConversation(retry.Id, operation, new()));
        Assert.Throws<ArgumentException>(() => runtime.RetryConversation(source.Id, "invalid", new()));
    }
    [Fact] public async Task LaterPromptsUseLatestSuccessfulAnswerAndRetainOtherMessages()
    {
        var provider = new Provider(); var runtime = Runtime(provider);
        var source = runtime.Converse("First", new()); await runtime.Execute(source.Id);
        var later = runtime.Converse("Second", new()); await runtime.Execute(later.Id);
        var retry = runtime.RetryConversation(source.Id, Id(), new()); await runtime.Execute(retry.Id);
        var next = runtime.Converse("Third", new());
        Assert.Equal(new[] { "First", "Answer 3", "Second", "Answer 2" }, next.ConversationContext.Select(m => m.Content));
        Assert.Equal(2, store.Chats().Count(m => m.Id == source.Id + "-assistant" || m.Id == retry.Id + "-assistant"));
    }
    [Fact] public async Task RetriesRevalidateUploadsAndUseFreshSelectedAppData()
    {
        var app = store.EditArtifact(Id(), new(Id(), "absent", ArtifactAppTests.Definition("todo")));
        var upload = store.AddUpload("fixture.txt", System.Text.Encoding.UTF8.GetBytes("Fictional note"));
        var provider = new Provider { Fail = true }; var runtime = Runtime(provider);
        var source = runtime.Converse("Discuss these", new(), artifactId: app.Id, localDate: "2026-09-16", uploadIds: [upload.Id]); await runtime.Execute(source.Id);
        var updated = store.EditArtifact(app.Id, new(Id(), app.Version, app.Definition with { Title = "New title" }));
        var retry = runtime.RetryConversation(source.Id, Id(), new());
        Assert.Equal(source.UploadIds, retry.UploadIds); Assert.Equal(updated.Version, retry.ArtifactContext!.Selected!.Version); Assert.Equal("2026-09-16", retry.ArtifactContext.LocalDate);
        await runtime.Cancel(retry.Id);
        store.EditArtifact(app.Id, new(Id(), updated.Version, Archived: true));
        Assert.Throws<InvalidOperationException>(() => runtime.RetryConversation(source.Id, Id(), new()));
        store.EditArtifact(app.Id, new(Id(), store.Artifact(app.Id)!.Version, Archived: false));
        store.EditUpload(upload.Id, new(upload.Version, true));
        Assert.Throws<ArgumentException>(() => runtime.RetryConversation(source.Id, Id(), new()));
    }
    [Theory][InlineData(RunState.Cancelled)][InlineData(RunState.NeedsAttention)][InlineData(RunState.Failed)]
    public void StoppedRepliesCanRetryButActiveRepliesCannot(RunState state)
    {
        var runtime = Runtime(new Provider()); var source = runtime.Converse("Stopped", new());
        Assert.Throws<InvalidOperationException>(() => runtime.RetryConversation(source.Id, Id(), new()));
        source.State = state; store.Save(source, "test.stopped", new { });
        var retry = runtime.RetryConversation(source.Id, Id(), new()); Assert.Equal(RunState.Queued, retry.State);
        retry.Background = true; retry.State = RunState.Running; store.Save(retry, "test.background", new { });
        Assert.Throws<InvalidOperationException>(() => runtime.RetryConversation(source.Id, Id(), new()));
    }
    [Fact] public async Task SuccessfulAppActionBlocksRetryOfEveryOlderAttempt()
    {
        var runtime = Runtime(new Provider { Fail = true }); var source = runtime.Converse("Build an app", new()); await runtime.Execute(source.Id);
        var retry = runtime.RetryConversation(source.Id, Id(), new());
        retry.State = RunState.Succeeded; retry.ArtifactResult = new(Id(), "version", "App created", true); store.Save(retry, "test.completed", new { });
        Assert.Throws<InvalidOperationException>(() => runtime.RetryConversation(source.Id, Id(), new()));
        Assert.Throws<InvalidOperationException>(() => runtime.RetryConversation(retry.Id, Id(), new()));
    }
    [Fact] public void ConnectionSetupReopensInsteadOfStartingModelRetriesIncludingLegacyAttempts()
    {
        var runtime = Runtime(new Provider());
        var setup = runtime.PrepareConnectionSetup("Connect my Gmail account for read-only email access.", new(), "google");
        var direct = Assert.Throws<InvalidOperationException>(() => runtime.RetryConversation(setup.Id, Id(), new()));
        Assert.Contains("Continue there", direct.Message); Assert.Single(store.List());

        var legacy = new Run
        {
            Goal = setup.Goal with { Limits = new(ModelCalls: 3) },
            State = RunState.Failed,
            Summary = "Model-call budget exhausted before dispatch.",
            ConversationRetry = new(setup.Id, setup.Id, Id())
        };
        store.Save(legacy, "test.legacy-connection-retry", new { });
        var failed = Assert.Throws<InvalidOperationException>(() => runtime.RetryConversation(legacy.Id, Id(), new()));
        Assert.Contains("secure connection setup", failed.Message); Assert.Equal(2, store.List().Count);
    }
    public void Dispose(){store.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
}
