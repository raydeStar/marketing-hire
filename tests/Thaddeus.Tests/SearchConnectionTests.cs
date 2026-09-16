using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class SearchConnectionTests : IDisposable
{
    private const string Key = "fictional-public-search-key";
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-search-key-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    private readonly Vault vault = new();
    private readonly SearchConnections connections;
    public SearchConnectionTests() { store = new(root); connections = new(store, vault); }
    private sealed class Vault : ICredentialVault
    {
        public readonly Dictionary<(string Scope, string Id), string> Values = [];
        public int Calls; public bool FailAfterWrite, FailRemove;
        public Task<string?> Execute(string operation, string scope, string id, string? value, CancellationToken cancellation)
        {
            Calls++;
            if (operation == "write") { Values[(scope, id)] = value!; if (FailAfterWrite) throw new InvalidOperationException("Fixture interrupted after native write."); }
            if (operation == "forget") { if (FailRemove) throw new InvalidOperationException("Fixture removal unavailable."); Values.Remove((scope, id)); }
            return Task.FromResult(operation == "read" ? Values.GetValueOrDefault((scope, id)) : null);
        }
    }
    private SearchCatalog Catalog => Wire.Unpack<SearchCatalog>(store.Setting("search-connection")!);
    private async Task<string> Version() => JsonSerializer.SerializeToElement(await connections.View(), Wire.Json).GetProperty("version").GetString()!;
    private async Task<PublicSearchGrant> Save(string mode = "system", string key = Key)
    { await connections.Save(new(await Version(), mode, key, true), default); return new("brave", Catalog.Selected!); }
    [Fact] public async Task VerifiedNativeStorageKeepsKeysOutOfTheLedgerAndCanReopen()
    {
        var grant = await Save(); Assert.Equal(2, vault.Calls);
        Assert.Equal(Key, await connections.Read(grant, default)); Assert.Equal(2, vault.Calls);
        Assert.DoesNotContain(Key, store.Setting("search-connection")!); Assert.DoesNotContain(Key, Wire.Pack(await connections.View()));
        Assert.DoesNotContain(Key, Wire.Pack(connections.Summary)); Assert.DoesNotContain(Key, new SearchConnectionEdit("v", "system", Key, true).ToString());
        var reopened = new SearchConnections(store, vault);
        Assert.True(JsonSerializer.SerializeToElement(await reopened.Check(await Version(), default), Wire.Json).GetProperty("available").GetBoolean());
        Assert.Equal(Key, await reopened.Read(grant, default)); Assert.Equal(3, vault.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.Check("stale", default)); Assert.Equal(3, vault.Calls);
        Assert.Null(store.Setting("provider")); Assert.Null(store.Setting("provider-credentials")); Assert.Empty(store.List());
    }
    [Fact] public async Task SessionOnlyKeyExpiresAndNeverFallsBackToAnotherCredential()
    {
        var grant = await Save("session"); Assert.Equal(Key, await connections.Read(grant, default)); Assert.Equal(0, vault.Calls);
        var restarted = new SearchConnections(store, vault);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.Read(grant, default));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await restarted.Check(await Version(), default));
        Assert.False(JsonSerializer.SerializeToElement(restarted.Summary, Wire.Json).GetProperty("configured").GetBoolean());
        Assert.Equal(0, vault.Calls);
    }
    [Fact] public async Task StaleEditsAndInvalidKeysNeverWriteToTheVault()
    {
        var version = await Version();
        await Assert.ThrowsAsync<ArgumentException>(() => connections.Save(new(version, "system", "with\nnewline", true), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.Save(new("stale", "system", Key, true), default));
        Assert.Equal(0, vault.Calls); Assert.Null(store.Setting("search-connection"));
    }
    [Fact] public async Task StandardPlanCanSearchTemporarilyAndChangeUsageWithoutReenteringTheKey()
    {
        await connections.Save(new(await Version(), "system", Key, false), default);
        var grant=new PublicSearchGrant("brave",Catalog.Selected!);
        Assert.Equal(Key,await connections.ReadTemporary(grant,default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>connections.Read(grant,default));
        var stale=await Version();
        await connections.SetUsage(new(stale,true),default);
        Assert.Equal(Key,await connections.Read(grant,default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>connections.SetUsage(new(stale,false),default));
        await connections.SetUsage(new(await Version(),false),default);
        Assert.Equal(Key,await new SearchConnections(store,vault).ReadTemporary(grant,default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>connections.Read(grant,default));
        Assert.Single(vault.Values);Assert.Empty(store.AllEvents());
    }
    [Fact] public async Task InterruptedNativeWriteLeavesAnUnusablePendingRecordForExplicitRemoval()
    {
        vault.FailAfterWrite = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Save());
        var record = Assert.Single(Catalog.Records); Assert.Equal("pending", record.Status); Assert.Null(Catalog.Selected);
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.Read(new("brave", record.Id), default));
        await connections.Forget(record.Id, await Version(), default); Assert.Empty(vault.Values); Assert.Empty(Catalog.Records);
    }
    [Fact] public async Task KeysAreBoundToTheSearchPurposeAndEndpoint()
    {
        var grant = await Save(); var catalog = Catalog;
        vault.Values[(catalog.Scope, grant.CredentialId)] = Wire.Pack(new SearchSecret("model", BravePublicSearch.Endpoint, Key));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SearchConnections(store, vault).Read(grant, default));
        vault.Values[(catalog.Scope, grant.CredentialId)] = Wire.Pack(new SearchSecret("public-search", "https://elsewhere.example/", Key));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SearchConnections(store, vault).Read(grant, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.Read(grant with { CredentialId = new string('f', 32) }, default));
    }
    [Fact] public async Task FrozenTaskReferenceDoesNotChangeWhenAnotherKeyIsSelected()
    {
        var first = await Save("session"); var second = await Save("session", "second-fictional-key");
        Assert.Equal(Key, await connections.Read(first, default)); Assert.Equal("second-fictional-key", await connections.Read(second, default));
        Assert.Equal(second.CredentialId, Catalog.Selected); Assert.Equal(2, Catalog.Records.Length);
    }
    [Fact] public async Task InUseAndUnconfirmedRemovalCannotSilentlyDropAGrant()
    {
        var grant = await Save(); var run = CapabilityTests.CreateWorkerRun(store);
        run.Goal = run.Goal with { Web = new([], 4, grant) }; store.Save(run, "fixture.search", new { });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await connections.Forget(grant.CredentialId, await Version(), default));
        run.State = RunState.Cancelled; store.Save(run, "fixture.cancelled", new { }); vault.FailRemove = true;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await connections.Forget(grant.CredentialId, await Version(), default));
        Assert.Equal("removing", Catalog.Records.Single().Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.Read(grant, default));
    }
    [Fact] public async Task ARestoredCatalogCannotResurrectARemovedNativeKey()
    {
        var grant = await Save(); var oldCatalog = store.Setting("search-connection")!;
        await connections.Forget(grant.CredentialId, await Version(), default); Assert.Empty(vault.Values);
        store.Setting("search-connection", oldCatalog);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SearchConnections(store, vault).Read(grant, default));
    }
    public void Dispose() { store.Dispose(); Directory.Delete(root, true); }
}
