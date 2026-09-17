using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class UserTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-user-" + Guid.NewGuid().ToString("N"));
    private Store store;

    public UserTests() => store = new(root);

    [Fact]
    public void User_IsARealVersionedFile_WithOptimisticUpdates()
    {
        var original = store.User();
        Assert.Equal(Store.UserFileName, original.Path);
        Assert.True(File.Exists(store.UserPath));
        Assert.Contains("Nothing saved yet", original.Content);

        var content = "# User\n\n## Preferences\n- Prefers concise morning summaries.";
        var updated = store.UpdateUser(content, original.Version, "settings", "fixture-one");
        Assert.Equal(Wire.Hash(content), updated.Version);
        Assert.Equal(content, File.ReadAllText(store.UserPath));
        Assert.Single(store.UserHistory());
        Assert.Throws<InvalidOperationException>(() => store.UpdateUser("stale", original.Version, "settings", "fixture-stale"));

        store.Dispose(); store = new(root);
        Assert.Equal(content, store.User().Content);
        Assert.Single(store.UserHistory());
    }

    [Fact]
    public void DeletePersonalData_ResetsCustomizedUser()
    {
        var original = store.User();
        store.UpdateUser("# User\n\n- Fictional profile detail.", original.Version, "settings", "fixture-delete");
        store.DeletePersonalData();
        Assert.Equal(Store.DefaultUserContent, store.User().Content);
        Assert.Empty(store.UserHistory());
    }

    [Fact]
    public async Task ChatUserEdit_WaitsForExactApproval_ThenVerifiesReadBack()
    {
        var before = store.User();
        var proposed = "# User\n\n## Preferences\n- Prefers concise morning summaries.";
        var provider = new UserProvider(proposed);
        var runtime = new Runtime(store, _ => provider, new PlanValidator(), new EvidencePolicy());
        var run = runtime.Converse("I prefer concise morning summaries.", new());

        await runtime.Execute(run.Id);
        var waiting = store.Get(run.Id)!;
        Assert.Equal(RunState.AwaitingApproval, waiting.State);
        Assert.Equal(UserConversation.ToolName, waiting.Approval!.Action.Name);
        Assert.Equal(Store.UserFileName, waiting.Approval.Action.Path);
        Assert.Equal(proposed, waiting.Approval.Action.Content);
        Assert.Equal(before.Content, store.User().Content);
        Assert.Equal(before.Content, waiting.Evidence.Single(item => item.Path == Store.UserFileName).Content);
        Assert.Equal(before.Version, provider.Seen!.User!.Version);

        var completed = await runtime.Decide(waiting.Id, waiting.Approval.Id, waiting.Approval.Digest, true);
        Assert.Equal(RunState.Succeeded, completed.State);
        Assert.Equal(proposed, store.User().Content);
        Assert.Contains(store.Chats(), message => message.Role == "assistant" && message.Content.Contains("updated your profile"));
    }

    [Fact]
    public async Task ChatUserApproval_RefusesAChangedProfile()
    {
        var before = store.User();
        var runtime = new Runtime(store, _ => new UserProvider("# User\n\n- Prefers tea."), new PlanValidator(), new EvidencePolicy());
        var run = runtime.Converse("I prefer tea.", new());
        await runtime.Execute(run.Id);
        var waiting = store.Get(run.Id)!;
        store.UpdateUser("# User\n\n- Direct owner edit.", before.Version, "settings", "fixture-owner-edit");

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Decide(waiting.Id, waiting.Approval!.Id, waiting.Approval.Digest, true));
        Assert.Contains("Direct owner edit", store.User().Content);
    }

    public void Dispose()
    {
        store.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private sealed class UserProvider(string proposed) : IModelProvider
    {
        public Observation? Seen { get; private set; }
        public Task<ModelReply> Respond(Observation observation, Func<string, Task> onDelta, CancellationToken cancellation)
        {
            Seen = observation;
            return Task.FromResult(new ModelReply(new(UserConversation.ToolName, "", Wire.Pack(new
            {
                baseVersion = observation.User!.Version,
                content = proposed
            })), null));
        }
    }
}
