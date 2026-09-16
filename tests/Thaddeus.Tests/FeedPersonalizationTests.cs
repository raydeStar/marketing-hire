using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class FeedPersonalizationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-feed-personal-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static FeedEntry Seed(Store store)
    {
        const string url = "https://news.example.org/rss";
        store.Subscribe(url, Now);
        store.FinishFeedRefresh(store.ClaimFeedRefresh(Now)!, new(url, new("Fixture", [new("one", "A science story", "An excerpt", "https://news.example.org/one", Now)], false, null)), Now);
        return store.Feeds().Entries.Single();
    }

    [Fact] public async Task ClicksAreIdempotentAndFeedbackSurvivesRefreshRestartAndBackup()
    {
        string state;
        var data = Path.Combine(root, "data");
        using (var store = new Store(data))
        {
            var entry = Seed(store);
            store.RecordFeedInteraction(entry.Id, "absent", "open", Now);
            var revision = store.FeedRevision();
            store.RecordFeedInteraction(entry.Id, "absent", "open", Now.AddHours(1));
            Assert.Equal(revision, store.FeedRevision());
            Assert.Equal(Now, store.Feeds().Entries.Single().Engagement!.Opened);
            store.RecordFeedInteraction(entry.Id, "absent", "more", Now);
            store.RecordFeedInteraction(entry.Id, "absent", "less", Now.AddMinutes(1));
            Assert.Equal(-1, store.Feeds().Entries.Single().Engagement!.Preference);
            // Feedback must not break a read/save request already on the screen.
            store.SaveFeedEntry(entry.Id, entry.Version);
            store.RecordFeedInteraction(entry.Id, "absent", "save", Now);
            store.RecordFeedInteraction(entry.Id, "absent", "discuss", Now);
            var claim = store.ClaimFeedRefresh(Now.AddHours(2))!;
            store.FinishFeedRefresh(claim, new(claim.Url, new("Fixture", [new("one", "Updated science headline", "New excerpt", "https://news.example.org/one", Now)], false, null)), Now.AddHours(2));
            Assert.Equal(-1, store.Feeds().Entries.Single().Engagement!.Preference);
            Assert.NotNull(store.Feeds().Entries.Single().Engagement!.Saved);
            Assert.Single(store.Library()); Assert.Empty(store.List()); Assert.Empty(store.AllEvents());
            state = Wire.Pack(store.Feeds());
        }
        using (var reopened = new Store(data)) Assert.Equal(state, Wire.Pack(reopened.Feeds()));
        await StudyBackup.Create(data, Path.Combine(root, "backup"));
        await StudyBackup.Restore(Path.Combine(root, "backup"), Path.Combine(root, "restored"));
        using var restored = new Store(Path.Combine(root, "restored")); Assert.Equal(state, Wire.Pack(restored.Feeds()));
    }

    [Fact] public void ResetAndOptOutClearLearningAndRejectOldTabsWithoutTouchingReading()
    {
        using var store = new Store(root); var entry = Seed(store);
        store.ReadFeedEntry(entry.Id, entry.Version, true); entry = store.Feeds().Entries.Single();
        store.SaveFeedEntry(entry.Id, entry.Version); store.RecordFeedInteraction(entry.Id, "absent", "open", Now);
        var preferences = store.ChangeFeedPreferences("absent", true, true);
        Assert.Null(store.Feeds().Entries.Single().Engagement); Assert.True(store.Feeds().Entries.Single().Read); Assert.Single(store.Library());
        Assert.Throws<InvalidOperationException>(() => store.RecordFeedInteraction(entry.Id, "absent", "open", Now));
        Assert.Throws<InvalidOperationException>(() => store.ChangeFeedPreferences("absent", true, false));
        store.RecordFeedInteraction(entry.Id, preferences.Version, "more", Now);
        preferences = store.ChangeFeedPreferences(preferences.Version, false, false);
        store.RecordFeedInteraction(entry.Id, preferences.Version, "discuss", Now);
        Assert.Null(store.Feeds().Entries.Single().Engagement); Assert.False(store.Feeds().Preferences!.Enabled);
        store.DeletePersonalData(); Assert.Empty(store.Feeds().Entries); Assert.Equal(new FeedPreferences(), store.FeedPreferences());
    }

    [Fact] public void InvalidSignalsDoNotChangeStateAndSourceRemovalErasesItsLearning()
    {
        using var store = new Store(root); var entry = Seed(store); var before = Wire.Pack(store.Feeds());
        Assert.Throws<ArgumentException>(() => store.RecordFeedInteraction(entry.Id, "absent", "hover", Now));
        Assert.Throws<InvalidOperationException>(() => store.RecordFeedInteraction(entry.Id, "absent", "save", Now));
        Assert.Throws<InvalidOperationException>(() => store.RecordFeedInteraction("missing", "absent", "open", Now));
        Assert.Equal(before, Wire.Pack(store.Feeds()));
        store.RecordFeedInteraction(entry.Id, "absent", "open", Now);
        var source = store.Feeds().Subscriptions.Single(); store.RemoveSubscription(source.Id, source.Version);
        Assert.Empty(store.Feeds().Entries);
    }

    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
}
