using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class ModelConnectionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-credentials-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    private readonly Vault vault = new();
    private readonly ModelConnections connections;
    private static readonly ProviderSnapshot Model = new("compatible", "fixture-model", "high", "https://provider.invalid/v1");
    private const string Key = "fictional-credential-not-a-real-provider-key";
    private sealed class Vault : ICredentialVault
    {
        public Dictionary<(string, string), string> Values = [];
        public int Calls; public bool FailAfterWrite, FailRemove;
        public Task<string?> Execute(string operation, string scope, string id, string? value, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested(); Calls++;
            if (operation == "write") { Values[(scope, id)] = value!; if (FailAfterWrite) throw ProcessCredentialVault.Unavailable(); }
            if (operation == "forget") { if (FailRemove) throw ProcessCredentialVault.Unavailable(); Values.Remove((scope, id)); }
            return Task.FromResult(operation == "read" ? Values.GetValueOrDefault((scope, id)) : null);
        }
    }
    public ModelConnectionTests() { store = new(root); connections = new(store, vault); }
    private ProviderSnapshot Saved => Wire.Unpack<ProviderSnapshot>(store.Setting("provider")!);
    private CredentialCatalog Catalog => Wire.Unpack<CredentialCatalog>(store.Setting("provider-credentials")!);
    private async Task<string> Version(ModelConnections? service = null) => JsonSerializer.SerializeToElement(await (service ?? connections).View(), Wire.Json).GetProperty("version").GetString()!;
    private Task Save(string mode = "system", string? key = Key) => SaveModel(Model, mode, key);
    private async Task SaveModel(ProviderSnapshot provider, string mode, string? key, ModelConnections? service = null)
    {
        var target = service ?? connections; await target.Save(new(await Version(target), provider, mode, key), default);
    }
    [Fact] public async Task NativeSaveVerifiesReadbackAndPersistsOnlyReferences()
    {
        await Save(); Assert.Equal(2, vault.Calls); Assert.Single(Catalog.Records); Assert.Equal("ready", Catalog.Records[0].Status);
        Assert.Equal(Key, await connections.Read(Saved, default)); Assert.Equal(2, vault.Calls);
        Assert.DoesNotContain(Key, store.Setting("provider")!); Assert.DoesNotContain(Key, store.Setting("provider-credentials")!);
        Assert.DoesNotContain(Key, Wire.Pack(await connections.View())); Assert.Empty(store.List());
        var fresh = new ModelConnections(store, vault); Assert.Equal(Key, await fresh.Read(Saved, default)); Assert.Equal(3, vault.Calls);
    }
    [Fact] public async Task SessionKeyExpiresOnHostRestartWithoutPersistentFallback()
    {
        await Save("session"); var frozen = Saved;
        Assert.Equal(Key, await connections.Read(frozen, default)); Assert.Equal(0, vault.Calls);
        var restarted = new ModelConnections(store, vault, "unrelated-environment-key", Model.Endpoint);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.Read(frozen, default)); Assert.Equal(0, vault.Calls);
        await SaveModel(Model, "none", null, restarted); Assert.Null(await restarted.Read(Saved, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.Read(frozen, default));
    }
    [Fact] public async Task KeyCannotFollowEndpointChangesOrForeignReferences()
    {
        await Save(); var saved = Saved; var version = await Version();
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.Save(new(version, saved with { Endpoint = "https://elsewhere.invalid/v1" }, "keep"), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.Read(saved with { Endpoint = "https://elsewhere.invalid/v1" }, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.Read(saved with { CredentialId = new string('a', 32) }, default));
        Assert.Equal(2, vault.Calls); Assert.Equal(saved, Saved); Assert.Equal(version, await Version());
    }
    [Fact] public async Task VaultPayloadIndependentlyBindsDestination()
    {
        await Save(); vault.Values[(Catalog.Scope, Saved.CredentialId!)] = Wire.Pack(new StoredProviderSecret("https://elsewhere.invalid/v1/", Key));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ModelConnections(store, vault).Read(Saved, default));
    }
    [Fact] public async Task StaleConnectionVersionCannotWriteASecondSecret()
    {
        var stale = await Version(); await Save();
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.Save(new(stale, Model, "system", "different-fictional-key"), default));
        Assert.Equal(2, vault.Calls); Assert.Single(Catalog.Records); Assert.Equal(Key, await connections.Read(Saved, default));
    }
    [Fact] public async Task UnconfirmedNativeWriteLeavesVisiblePendingIntentAndPreviousProvider()
    {
        await connections.SaveLegacy(Model, default); var before = Saved; vault.FailAfterWrite = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Save());
        Assert.Equal(before, Saved); Assert.Equal("pending", Assert.Single(Catalog.Records).Status); Assert.Single(vault.Values);
        var pending = Catalog.Records[0];
        await connections.Forget(pending.Id, await Version(), default); Assert.Empty(Catalog.Records); Assert.Empty(vault.Values);
    }
    [Fact] public async Task FailedRemovalDisablesTheReferenceAndCanBeRetried()
    {
        await Save(); var frozen = Saved; vault.FailRemove = true;
        var version = await Version();
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.Forget(frozen.CredentialId!, version, default));
        Assert.Equal("removing", Assert.Single(Catalog.Records).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.Read(frozen, default));
        vault.FailRemove = false; await connections.Forget(frozen.CredentialId!, await Version(), default);
        Assert.Empty(vault.Values); Assert.Empty(Catalog.Records); Assert.Equal(frozen, Saved);
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.Read(frozen, default));
    }
    [Fact] public async Task ActiveTaskMustFinishOrCancelBeforeItsKeyIsRemoved()
    {
        await Save(); var run = new Run { Goal = new("fixture", [], "plans/", [], new(), Saved), State = RunState.Queued };
        store.Save(run, "fixture.created", new { }); var version = await Version();
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.Forget(Saved.CredentialId!, version, default));
        Assert.Equal(version, await Version()); Assert.Equal(2, vault.Calls);
        run.State = RunState.Cancelled; store.Save(run, "fixture.cancelled", new { });
        await connections.Forget(Saved.CredentialId!, version, default); Assert.Empty(vault.Values);
    }
    [Fact] public async Task EnvironmentKeyIsBoundBeforeMutableSettingsAndNeverFollowsAnotherUrl()
    {
        await connections.SaveLegacy(Model, default); var source = new ModelConnections(store, vault, Key);
        Assert.Equal(Key, await source.Read(Model, default));
        var changed = Model with { Endpoint = "https://elsewhere.invalid/v1" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.SaveLegacy(changed, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.Read(changed, default));
        await SaveModel(changed, "none", null, source); Assert.Null(await source.Read(Saved, default));
        Assert.Equal(Key, await source.Read(Model, default)); Assert.DoesNotContain(Key, Wire.Pack(await source.View()));
    }
    [Fact] public async Task ExplicitEnvironmentDestinationWorksForAnUnconfiguredStudy()
    {
        var source = new ModelConnections(store, vault, Key, Model.Endpoint);
        await SaveModel(Model, "environment", null, source); Assert.Equal(Key, await source.Read(Saved, default));
        Assert.Empty(Catalog.Records); Assert.Equal(0, vault.Calls);
    }
    [Fact] public async Task ReasoningCanBeDisabledForCompatibleLocalToolModels()
    {
        await SaveModel(Model with { Reasoning = "none" }, "none", null);
        Assert.Equal("none", Saved.Reasoning); Assert.Equal("none", Saved.CredentialId);
    }
    [Theory]
    [InlineData("")]
    [InlineData("key with spaces")]
    [InlineData("key\r\nHeader: leaked")]
    public async Task InvalidSecretIsRefusedWithoutNativeWrite(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Save("system", key)); Assert.Equal(0, vault.Calls); Assert.Null(store.Setting("provider-credentials"));
    }
    [Fact] public async Task EndpointCanonicalizationPreservesPathCaseAndIgnoresDefaultPortAndTrailingSlash()
    {
        await Save(); Assert.Equal(Key, await connections.Read(Saved with { Endpoint = "https://PROVIDER.invalid:443/v1/" }, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.Read(Saved with { Endpoint = "https://provider.invalid/V1" }, default));
    }
    [Fact] public void LegacyProviderSerializationRemainsByteCompatible()
    {
        const string original = "{\"kind\":\"compatible\",\"model\":\"fixture-model\",\"reasoning\":\"high\",\"endpoint\":\"https://provider.invalid/v1\"}";
        Assert.Equal(original, Wire.Pack(Model)); Assert.Equal(original, Wire.Pack(Wire.Unpack<ProviderSnapshot>(original)));
        Assert.DoesNotContain(Key, new ConnectionEdit("version", Model, "system", Key).ToString());
    }
    public void Dispose() { store.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
}
