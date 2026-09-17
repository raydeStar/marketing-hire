using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class SoulTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-soul-" + Guid.NewGuid().ToString("N"));
    private Store store;

    public SoulTests() => store = new(root);

    [Fact]
    public void Soul_IsARealVersionedFile_WithOptimisticUpdates()
    {
        var original = store.Soul();
        Assert.Equal(Store.SoulFileName, original.Path);
        Assert.True(File.Exists(store.SoulPath));
        Assert.Contains("Sir Thaddeus", original.Content);

        var updated = store.UpdateSoul(original.Content + "\n\nBe a shade more cheerful.", original.Version, "settings", "fixture-one");
        Assert.Equal(Wire.Hash(updated.Content), updated.Version);
        Assert.Equal(updated.Content, File.ReadAllText(store.SoulPath));
        Assert.Single(store.SoulHistory());
        Assert.Throws<InvalidOperationException>(() => store.UpdateSoul("stale", original.Version, "settings", "fixture-stale"));
        Assert.Equal(updated.Content, store.Soul().Content);

        store.Dispose(); store = new(root);
        Assert.Equal(updated.Content, store.Soul().Content);
        Assert.Single(store.SoulHistory());
    }

    [Fact]
    public void DeletePersonalData_ResetsCustomizedSoul()
    {
        var original = store.Soul();
        store.UpdateSoul("# Different Soul\n\nA fictional test personality.", original.Version, "settings", "fixture-delete");
        store.DeletePersonalData();
        Assert.Equal(PersonalityProfile.Thaddeus.Instructions, store.Soul().Content);
        Assert.Empty(store.SoulHistory());
    }

    [Fact]
    public async Task ChatSoulEdit_WaitsForExactApproval_ThenVerifiesReadBack()
    {
        var before = store.Soul();
        var proposed = before.Content + "\n\nFavor hopeful language when the facts permit it.";
        var provider = new SoulProvider(proposed);
        var runtime = new Runtime(store, _ => provider, new PlanValidator(), new EvidencePolicy());
        var run = runtime.Converse("Be slightly less depressing.", new());

        await runtime.Execute(run.Id);
        var waiting = store.Get(run.Id)!;
        Assert.Equal(RunState.AwaitingApproval, waiting.State);
        Assert.Equal(SoulConversation.ToolName, waiting.Approval!.Action.Name);
        Assert.Equal(Store.SoulFileName, waiting.Approval.Action.Path);
        Assert.Equal(proposed, waiting.Approval.Action.Content);
        Assert.Equal(before.Content, store.Soul().Content);
        Assert.Equal(before.Content, waiting.Evidence.Single(item => item.Path == Store.SoulFileName).Content);
        Assert.Equal(before.Version, provider.Seen!.Soul!.Version);

        var completed = await runtime.Decide(waiting.Id, waiting.Approval.Id, waiting.Approval.Digest, true);
        Assert.Equal(RunState.Succeeded, completed.State);
        Assert.Equal(proposed, store.Soul().Content);
        Assert.Contains(store.Chats(), message => message.Role == "assistant" && message.Content.Contains("updated my Soul"));
    }

    [Fact]
    public async Task ChatSoulApproval_RefusesAChangedSoul()
    {
        var before = store.Soul();
        var runtime = new Runtime(store, _ => new SoulProvider(before.Content + "\n\nUse brighter language."), new PlanValidator(), new EvidencePolicy());
        var run = runtime.Converse("Be brighter.", new());
        await runtime.Execute(run.Id);
        var waiting = store.Get(run.Id)!;
        store.UpdateSoul(before.Content + "\n\nA direct owner edit.", before.Version, "settings", "fixture-owner-edit");

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Decide(waiting.Id, waiting.Approval!.Id, waiting.Approval.Digest, true));
        Assert.Contains("direct owner edit", store.Soul().Content, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        store.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private sealed class SoulProvider(string proposed) : IModelProvider
    {
        public Observation? Seen { get; private set; }
        public Task<ModelReply> Respond(Observation observation, Func<string, Task> onDelta, CancellationToken cancellation)
        {
            Seen = observation;
            return Task.FromResult(new ModelReply(new(SoulConversation.ToolName, "", Wire.Pack(new
            {
                baseVersion = observation.Soul!.Version,
                content = proposed
            })), null));
        }
    }
}
