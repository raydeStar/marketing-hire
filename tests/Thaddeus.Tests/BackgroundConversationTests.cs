using System.Collections.Concurrent;
using Thaddeus.Core;
using Thaddeus.Infrastructure;
namespace Thaddeus.Tests;

public sealed class BackgroundConversationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-background-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    public BackgroundConversationTests() => store = new(root);
    private Runtime Runtime(IModelProvider provider) => new(store, _ => provider, new PlanValidator(), new EvidencePolicy(), conversationBackgroundDelay: TimeSpan.FromMilliseconds(10));
    private async Task Background(string id)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!store.Get(id)!.Background) await Task.Delay(5, deadline.Token);
    }
    [Fact] public async Task TwoBackgroundRepliesLeaveOneForegroundSlotAndCancellationIsScoped()
    {
        var provider = new HeldProvider(); var runtime = Runtime(provider);
        var first = runtime.Converse("First project", new()); var firstTask = runtime.Execute(first.Id); await Background(first.Id);
        var second = runtime.Converse("Second project", new()); var secondTask = runtime.Execute(second.Id); await Background(second.Id);
        var chat = runtime.Converse("Ordinary chat", new()); var chatTask = runtime.Execute(chat.Id);
        Assert.Throws<InvalidOperationException>(() => runtime.Converse("Fourth request", new()));
        await runtime.Cancel(first.Id); await firstTask;
        Assert.Equal(RunState.Cancelled, store.Get(first.Id)!.State); Assert.Equal(64000, store.Get(first.Id)!.ChargedTokens);
        provider.Replies["Ordinary chat"].SetResult(new(null, "Chat can finish first.", 10, 5)); await chatTask;
        Assert.Equal(RunState.Running, store.Get(second.Id)!.State);
        provider.Replies["Second project"].SetResult(new(null, "Second project result.", 20, 8)); await secondTask;
        Assert.Equal(28, store.Get(second.Id)!.ChargedTokens); Assert.Equal(15, store.Get(chat.Id)!.ChargedTokens);
        Assert.Equal("Chat can finish first.", store.Chats().Single(message => message.Id == chat.Id + "-assistant").Content);
        Assert.Single(store.Events(0, second.Id), e => e.Type == "conversation.background");
        Assert.Equal(64000, store.Get(second.Id)!.Goal.Limits.MaxTotalTokens);
    }
    [Fact] public async Task ExplicitDeadlineStillStopsBackgroundWorkWithoutACompletionOrAutomaticRetry()
    {
        var provider = new HeldProvider(); var runtime = Runtime(provider);
        var run = runtime.Converse("Short deadline", new(), new(Seconds: 1));
        await runtime.Execute(run.Id);
        var saved = store.Get(run.Id)!;
        Assert.True(saved.Background); Assert.Equal(RunState.NeedsAttention, saved.State);
        Assert.Contains("Time budget", saved.Summary); Assert.Equal(1, saved.ModelCalls);
        Assert.Single(store.Chats()); Assert.Equal(64000, saved.ChargedTokens);
    }
    [Fact] public void RestartKeepsBackgroundIdentityAndDoesNotReplayAnUnknownDispatch()
    {
        var runtime = Runtime(new HeldProvider()); var run = runtime.Converse("Interrupted project", new());
        run.Background = true; run.State = RunState.Running; run.ReservedTokens = 320;
        store.Save(run, "test.dispatch", new {}); runtime.Recover(); runtime.Recover();
        var saved = store.Get(run.Id)!;
        Assert.True(saved.Background); Assert.Equal(RunState.NeedsAttention, saved.State); Assert.Equal(320, saved.ChargedTokens);
        Assert.NotNull(runtime.Converse("We can still chat", new()));
    }
    [Theory][InlineData(429,"rate-limited")][InlineData(504,"timed out")][InlineData(502,"bridge failed")][InlineData(401,"authentication")]
    public async Task ProviderFailuresPreserveTheUsefulClassification(int status, string expected)
    {
        var runtime = Runtime(new FailedProvider(status)); var run = runtime.Converse("Request", new()); await runtime.Execute(run.Id);
        Assert.Contains(expected, store.Get(run.Id)!.Summary); Assert.Equal(RunState.Failed, store.Get(run.Id)!.State);
    }
    private sealed class HeldProvider : IModelProvider
    {
        public ConcurrentDictionary<string, TaskCompletionSource<ModelReply>> Replies { get; } = new();
        public Task<ModelReply> Respond(Observation o, Func<string,Task> delta, CancellationToken cancellation)
        {
            var reply = new TaskCompletionSource<ModelReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            Replies[o.Goal.Objective] = reply; return reply.Task.WaitAsync(cancellation);
        }
    }
    private sealed class FailedProvider(int status) : IModelProvider
    {
        public Task<ModelReply> Respond(Observation o, Func<string,Task> delta, CancellationToken cancellation) => throw new HttpRequestException("Private provider text", null, (System.Net.HttpStatusCode)status);
    }
    public void Dispose(){store.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
}
