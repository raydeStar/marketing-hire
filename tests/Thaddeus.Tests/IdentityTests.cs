using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class IdentityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-identity-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    public IdentityTests() => store = new(root);

    [Fact]
    public void Identity_IsARealVersionedFile_WithOptimisticUpdates()
    {
        var original = store.Identity();
        Assert.Equal(Store.IdentityFileName, original.Path);
        Assert.True(File.Exists(store.IdentityPath));
        const string content = "# Identity\n\n**Name:** Thaddeus\n\n**Role:** A fictional test butler.";
        var updated = store.UpdateIdentity(content, original.Version, "fixture-one");
        Assert.Equal(content, updated.Content);
        Assert.Equal(content, File.ReadAllText(store.IdentityPath));
        Assert.Single(store.IdentityHistory());
        Assert.Throws<InvalidOperationException>(() => store.UpdateIdentity("stale", original.Version, "fixture-stale"));
        Assert.Equal(updated.Content, store.Identity().Content);
    }

    [Fact]
    public void DeletePersonalData_ResetsCustomizedIdentity()
    {
        var original = store.Identity();
        store.UpdateIdentity("# Different Identity\n\nA fictional test role.", original.Version, "fixture-delete");
        store.DeletePersonalData();
        Assert.Equal(Store.DefaultIdentityContent, store.Identity().Content);
        Assert.Empty(store.IdentityHistory());
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(root, true);
    }
}
