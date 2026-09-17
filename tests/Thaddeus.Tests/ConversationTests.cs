using Thaddeus.Core;
using Thaddeus.Infrastructure;
namespace Thaddeus.Tests;

public sealed class ConversationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-chat-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    public ConversationTests() => store = new(root);
    private Runtime Runtime(IModelProvider provider) => new(store, _ => provider, new PlanValidator(), new EvidencePolicy());
    [Fact] public async Task Conversation_PersistsContextAndNeverWritesKnowledge()
    {
        var provider = new ChatProvider(); var rt = Runtime(provider);
        var first = rt.Converse("Remember: my project is Lantern.", new()); await rt.Execute(first.Id);
        var second = rt.Converse("What is my project called?", new()); await rt.Execute(second.Id);
        Assert.Equal(4, store.Chats().Count); Assert.Equal(2, provider.Observations[1].History!.Count);
        Assert.Contains("Lantern", provider.Observations[1].History![0].Content);
        Assert.Equal(RunState.Succeeded, store.Get(second.Id)!.State); Assert.Equal(0, store.Get(second.Id)!.ToolCalls);
        Assert.Empty(store.Pages()); Assert.Contains(store.Events(0, first.Id), e => e.Type == "model.delta");
        Assert.Contains(store.Get(first.Id)!.Validation!.Unverified, s => s.Contains("Factual"));
    }
    [Fact] public async Task Conversation_ToolCallIsRejected()
    {
        var rt = Runtime(new ToolProvider()); var run = rt.Converse("Please write a page", new()); await rt.Execute(run.Id);
        Assert.Equal(RunState.Failed, store.Get(run.Id)!.State); Assert.Empty(store.Pages()); Assert.Single(store.Chats());
    }
    [Fact] public async Task SimultaneousConversationAdmission_IsSerialized()
    {
        var rt = Runtime(new ChatProvider());
        var admitted = await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Task.Run(()=>{try{return rt.Converse("hello",new()).Id;}catch(InvalidOperationException){return null;}})));
        Assert.Single(admitted, id => id != null);
    }
    [Fact] public void Conversation_CannotSmuggleReadScope()
    {
        var rt = Runtime(new ChatProvider());
        Assert.Throws<ArgumentException>(()=>rt.Create(new("hello",["notes/secret.md"],"plans/",[],new(),new(),"conversation")));
    }
    [Theory]
    [InlineData("Connect my Google Calendar", "google")]
    [InlineData("Please link my GitHub account", "mcp")]
    public void ConnectionSetup_IsLocalAndDoesNotCallTheModel(string message, string target)
    {
        var provider = new ChatProvider(); var rt = Runtime(provider);
        Assert.Equal(target, Thaddeus.Infrastructure.Runtime.ConnectionSetupIntent(message));
        var run = rt.PrepareConnectionSetup(message, new(), target);
        var saved = store.Get(run.Id)!;
        Assert.Equal(RunState.Succeeded, saved.State); Assert.Equal(target, saved.ConnectionSetup);
        if (message.Contains("Calendar", StringComparison.Ordinal)) Assert.Equal("calendar", saved.ConnectionSetupProduct);
        Assert.Equal(0, saved.ModelCalls); Assert.Equal(0, saved.ToolCalls); Assert.Empty(provider.Observations);
        Assert.Equal(2, store.Chats().Count); Assert.DoesNotContain("client secret", string.Join(' ', store.Chats().Select(chat => chat.Content)), StringComparison.OrdinalIgnoreCase);
        Assert.All(store.AllEvents(), item => Assert.Contains("credentialsAcceptedInChat", item.Data.ToString(), StringComparison.Ordinal));
    }
    [Theory]
    [InlineData("Set up a meeting on my calendar")]
    [InlineData("Connect these two ideas")]
    [InlineData("Check my email")]
    public void OrdinaryRequestsDoNotOpenConnectionSetup(string message) => Assert.Null(Thaddeus.Infrastructure.Runtime.ConnectionSetupIntent(message));

    [Fact] public async Task Cancellation_RetainsUnknownChargeWithoutAssistantMessage()
    {
        var provider = new WaitingProvider(); var rt = Runtime(provider); var run = rt.Converse("hello", new());
        var execution = rt.Execute(run.Id); await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await rt.Cancel(run.Id); await execution;
        var saved = store.Get(run.Id)!;
        Assert.Equal(RunState.Cancelled, saved.State); Assert.Equal(64000, saved.ChargedTokens); Assert.Null(saved.InputTokens);
        Assert.Single(store.Chats()); Assert.Empty(store.Pages());
    }
    [Fact] public void Recovery_ChargesInterruptedDispatchOnce()
    {
        var rt = Runtime(new ChatProvider()); var run = rt.Converse("hello", new());
        run.State = RunState.Running; run.ReservedTokens = 500; store.Save(run, "test.dispatch", new {});
        rt.Recover(); rt.Recover();
        Assert.Equal(500, store.Get(run.Id)!.ChargedTokens); Assert.Equal(0, store.Get(run.Id)!.ReservedTokens);
        Assert.Equal(RunState.NeedsAttention, store.Get(run.Id)!.State);
    }
    private sealed class WaitingProvider : IModelProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ModelReply> Respond(Observation o, Func<string, Task> delta, CancellationToken c)
        {
            Started.SetResult(); await Task.Delay(Timeout.Infinite, c); return new(null, "unreachable");
        }
    }
    public void Dispose(){store.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
    private sealed class ChatProvider : IModelProvider
    {
        public List<Observation> Observations {get;}=[];
        public async Task<ModelReply> Respond(Observation o,Func<string,Task> delta,CancellationToken c){Observations.Add(o);await delta("A thoughtful ");await delta("reply.");return new(null,"A thoughtful reply.",10,4);}
    }
    private sealed class ToolProvider : IModelProvider
    {
        public Task<ModelReply> Respond(Observation o,Func<string,Task> delta,CancellationToken c)=>Task.FromResult(new ModelReply(new("knowledge.write","plans/forbidden.md","bad"),null));
    }
}
