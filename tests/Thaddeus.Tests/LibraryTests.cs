using Microsoft.Data.Sqlite;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class LibraryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-library-" + Guid.NewGuid().ToString("N"));
    private static string Id() => Guid.NewGuid().ToString("N");
    private static LibraryEdit New(string kind = "todo") => new(kind, "Review the design", "My own unfinished work.", "open", null, null, "absent");
    [Fact] public void ExplicitCompletionPersistsSeparatelyFromRunsAndCanBeReopened()
    {
        LibraryItem completed;
        using (var store = new Store(root))
        {
            var todo = store.EditLibrary(Id(), New() with { Due = new(2026, 9, 20) });
            var run = new Run { Goal = new("Review the design", [], "plans/", [], new(), new()), State = RunState.Succeeded };
            store.Save(run, "fixture.succeeded", new { });
            Assert.Equal("open", store.Library().Single().Status);
            completed = store.EditLibrary(todo.Id, New() with { Version = todo.Version, Status = "done", Due = todo.Due });
            Assert.Single(store.List()); Assert.Single(store.AllEvents());
        }
        using var reopened = new Store(root);
        Assert.Equal(completed, reopened.Library().Single());
        var restored = reopened.EditLibrary(completed.Id, New() with { Version = completed.Version });
        Assert.Equal("open", restored.Status);
        Assert.Equal(new[] { "created", "done", "open" }, reopened.LibraryChanges().Select(change => change.Kind));
        Assert.DoesNotContain("unfinished", Wire.Pack(reopened.LibraryChanges()));
    }
    [Fact] public void StaleEditsCannotLoseCompletionOrMoveItemsBetweenCollections()
    {
        using var store = new Store(root); var original = store.EditLibrary(Id(), New());
        var completed = store.EditLibrary(original.Id, New() with { Version = original.Version, Status = "done" });
        Assert.Throws<InvalidOperationException>(() => store.EditLibrary(original.Id, New() with { Version = original.Version, Content = "Stale note" }));
        Assert.Throws<ArgumentException>(() => store.EditLibrary(original.Id, New("idea") with { Version = completed.Version }));
        Assert.Equal(completed, store.Library().Single()); Assert.Equal(2, store.LibraryCursor());
    }
    [Fact] public void ItemAndChangeReceiptCommitTogether()
    {
        using var store = new Store(root, point => { if (point == "before-library-commit") throw new IOException("Fixture interrupted save"); });
        Assert.Throws<IOException>(() => store.EditLibrary(Id(), New()));
        Assert.Empty(store.Library()); Assert.Empty(store.LibraryChanges());
    }
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/private")]
    [InlineData("https://user:password@example.org")]
    public void SavedLinksCannotCarryExecutableSchemesOrEmbeddedCredentials(string url)
    {
        using var store = new Store(root);
        Assert.Throws<ArgumentException>(() => store.EditLibrary(Id(), New("feed") with { Url = url }));
        Assert.Empty(store.Library()); Assert.Empty(store.List());
    }
    [Fact] public void BoundsAndCollectionsAreValidatedBeforeStorage()
    {
        using var store = new Store(root);
        foreach (var edit in new[] { New() with { Title = " " }, New() with { Title = new('x', 161) }, New() with { Content = new('x', 12001) },
            New("arbitrary"), New("idea") with { Status = "done" }, New("feed") with { Due = new(2026, 9, 20) } })
            Assert.Throws<ArgumentException>(() => store.EditLibrary(Id(), edit));
        Assert.Throws<ArgumentException>(() => store.EditLibrary("../../elsewhere", New()));
        Assert.Empty(store.LibraryChanges());
    }
    [Fact] public void ArchiveRetainsContentAndPersonalDeletionClearsEveryCollection()
    {
        using var store = new Store(root);
        foreach (var kind in new[] { "todo", "idea", "feed" })
        {
            var item = store.EditLibrary(Id(), New(kind));
            var archived = store.EditLibrary(item.Id, New(kind) with { Version = item.Version, Status = "archived" });
            Assert.Equal(item.Content, archived.Content);
        }
        Assert.Equal(3, store.Library().Length); Assert.Equal(6, store.LibraryChanges().Length);
        var before = store.LibraryCursor();
        store.DeletePersonalData(); Assert.Empty(store.Library()); Assert.Empty(store.LibraryChanges());
        store.EditLibrary(Id(), New()); Assert.True(store.LibraryCursor() > before); // A rapid clear-and-add must still wake other browsers.
    }
    [Fact] public void SchemaThreeUpgradeDoesNotReinterpretOldRunsAsTodos()
    {
        var run = new Run { Goal = new("Old conversation", [], "plans/", [], new(), new()), State = RunState.Succeeded };
        using (var store = new Store(root)) store.Save(run, "fixture.original", new { });
        string before;
        using (var db = new SqliteConnection($"Data Source={Path.Combine(root, "ledger.sqlite")}"))
        {
            db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT body FROM runs"; before = (string)cmd.ExecuteScalar()!;
            cmd.CommandText = "DROP TABLE library; DROP TABLE library_changes; DELETE FROM schema_migrations WHERE version>=4; PRAGMA user_version=3;"; cmd.ExecuteNonQuery();
        }
        using (var store = new Store(root)) { Assert.Empty(store.Library()); Assert.Equal(run.Id, store.List().Single().Id); }
        using var check = new SqliteConnection($"Data Source={Path.Combine(root, "ledger.sqlite")}"); check.Open(); using var command = check.CreateCommand();
        command.CommandText = "SELECT body FROM runs"; Assert.Equal(before, command.ExecuteScalar());
        command.CommandText = "PRAGMA user_version"; Assert.Equal((long)Store.CurrentSchemaVersion, command.ExecuteScalar());
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
}
