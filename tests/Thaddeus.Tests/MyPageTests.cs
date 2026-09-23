using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class MyPageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-my-page-" + Guid.NewGuid().ToString("N"));
    private static string Id() => Guid.NewGuid().ToString("N");
    private static ArtifactApp App(Store store) => store.EditArtifact(Id(), new(Id(), "absent", ArtifactAppTests.Definition("todo")));

    [Fact] public void PreferenceSurvivesRestartAndArchivingDoesNotEraseThePinOrAppData()
    {
        MyPageSetting pin; ArtifactApp app;
        using (var store = new Store(root))
        {
            Assert.Equal(new(), store.MyPage()); app = App(store);
            pin = store.EditMyPage(new("artifact", app.Id, "absent"));
            Assert.Equal(Wire.Pack(app), Wire.Pack(store.Artifact(app.Id))); Assert.Single(store.ArtifactRevisions(app.Id));
            Assert.Empty(store.List()); Assert.Empty(store.DelegationJobs());
            store.EditArtifact(app.Id, new(Id(), app.Version, Archived: true));
        }
        using var reopened = new Store(root);
        Assert.Equal(pin, reopened.MyPage());
        Assert.Throws<ArgumentException>(() => reopened.EditMyPage(new("artifact", app.Id, pin.Version)));
        Assert.Equal("today", reopened.EditMyPage(new("today", null, pin.Version)).Mode);
        Assert.True(reopened.Artifact(app.Id)!.Archived);
    }

    [Fact] public void StaleAndInvalidPinsCannotReplaceANewerChoiceAndPersonalClearRemovesThePin()
    {
        using var store = new Store(root); var app = App(store);
        var saved = store.EditMyPage(new("artifact", app.Id, "absent"));
        Assert.Throws<InvalidOperationException>(() => store.EditMyPage(new("today", null, "absent")));
        foreach (var edit in new[] { new MyPageEdit("other", null, saved.Version), new("today", app.Id, saved.Version), new("artifact", Id(), saved.Version), new("artifact", null, saved.Version) })
            Assert.Throws<ArgumentException>(() => store.EditMyPage(edit));
        Assert.Equal(saved, store.MyPage());
        store.DeletePersonalData(); Assert.Equal("today", store.MyPage().Mode); Assert.Null(store.MyPage().ArtifactId);
        Assert.NotEqual(saved.Version, store.MyPage().Version);
    }

    [Fact] public async Task ChatPinUsesTheAdmittedCatalogAndSavesARealReceiptWithoutExecutingTheApp()
    {
        using var store = new Store(root); var app = App(store);
        var runtime = new Runtime(store, _ => new PinProvider(app.Id), new PlanValidator(), new EvidencePolicy());
        var run = runtime.Converse("Pin the list to My page", new()); await runtime.Execute(run.Id);
        Assert.Equal(RunState.Succeeded, store.Get(run.Id)!.State); Assert.Equal(app.Id, store.MyPage().ArtifactId);
        Assert.Equal(1, store.Get(run.Id)!.ToolCalls); Assert.Equal(Wire.Pack(app), Wire.Pack(store.Artifact(app.Id)));
        Assert.Contains(store.Events(0, run.Id), item => item.Type == "my-page.changed");
        Assert.Empty(store.DelegationJobs());
    }

    [Fact] public async Task ChatPinRejectsAnAppAddedAfterTheMessageAndANewerPreference()
    {
        using var store = new Store(root); var app = App(store);
        var runtime = new Runtime(store, _ => new PinProvider(app.Id), new PlanValidator(), new EvidencePolicy());
        var run = runtime.Converse("Pin the list", new()); var newer = store.EditMyPage(new("today", null, "absent"));
        await runtime.Execute(run.Id); Assert.Equal(RunState.Failed, store.Get(run.Id)!.State); Assert.Equal(newer, store.MyPage());
        var invalid = new Runtime(store, _ => new PinProvider(Id()), new PlanValidator(), new EvidencePolicy());
        var unknown = invalid.Converse("Pin another app", new()); await invalid.Execute(unknown.Id);
        Assert.Equal(RunState.Failed, store.Get(unknown.Id)!.State); Assert.Equal(newer, store.MyPage());
    }

    [Fact] public void InterruptedChatCommitCannotSaveAPreferenceWithoutItsReceipt()
    {
        using var store = new Store(root, point => { if (point == "before-my-page-commit") throw new IOException("Fixture interruption"); });
        var app = App(store); var run = new Run { Goal = new("Pin this", [], "plans/", [], new(), new()) };
        store.Save(run, "fixture.created", new { });
        Assert.Throws<IOException>(() => store.CompleteMyPageConversation(run, new("artifact", app.Id, "absent")));
        Assert.Equal("absent", store.MyPage().Version); Assert.Empty(store.Chats());
        Assert.DoesNotContain(store.Events(0, run.Id), item => item.Type == "my-page.changed");
    }

    private sealed class PinProvider(string id) : IModelProvider
    {
        public Task<ModelReply> Respond(Observation o, Func<string, Task> onDelta, CancellationToken cancellation) => Task.FromResult(
            new ModelReply(new("my_page_set", "", Wire.Pack(new MyPageEdit("artifact", id, o.Artifacts!.MyPage!.Version))), null, 20, 20));
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
}
