using System.Collections.Concurrent;
using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public sealed record SearchConnectionEdit(string Version, string Storage, string Key, bool RetainResults)
{ public override string ToString() => "Search connection edit (credential omitted)"; }
public sealed record SearchCredential(string Id, string Storage, string Status, DateTimeOffset Created, bool RetainResults);
public sealed record SearchCatalog(string Scope, string? Selected, SearchCredential[] Records);
public sealed record SearchUsageEdit(string Version, bool RetainResults);
public sealed record SearchSecret(string Purpose, string Endpoint, string Key)
{ public override string ToString() => "Search secret (credential omitted)"; }

/// <summary>Search has a separate credential catalog; a model connection can never lend it a key.</summary>
public sealed class SearchConnections(Store store, ICredentialVault vault) : IPublicSearchCredentials
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly ConcurrentDictionary<string, string> cached = new();
    private SearchCatalog Catalog => store.Setting("search-connection") is { } value ? Wire.Unpack<SearchCatalog>(value) : new("", null, []);
    private string Version => Wire.Hash(store.Setting("search-connection") ?? "");
    private bool InUse(string id) => store.List().Any(run => run.Goal.Web?.Search?.CredentialId == id &&
        (run.ReservedTokens != 0 || run.State is RunState.Running or RunState.Queued || run.Research is { Phase: not "finished" }));
    private bool Available(SearchCredential? record) => record is { Status: "ready", RetainResults: true } && (record.Storage == "system" || cached.ContainsKey(record.Id));
    public object Summary
    {
        get
        {
            var catalog = Catalog; var selected = catalog.Records.FirstOrDefault(record => record.Id == catalog.Selected);
            return new { provider = "brave", configured = Available(selected), credentialId = catalog.Selected,
                temporaryConfigured = selected is {Status:"ready"} && (selected.Storage=="system"||cached.ContainsKey(selected.Id)), maxQueries = 3, providerVerified = false, retentionRequired = true, budget = store.SearchBudget() };
        }
    }
    public async Task<object> View(CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var catalog = Catalog;
            return new { version = Version, summary = Summary, systemStore = vault.Name,
                credentials = catalog.Records.Select(record => new { record.Id, record.Storage, record.Status, record.Created,
                    record.RetainResults, selected = record.Id == catalog.Selected, inUse = InUse(record.Id),
                    needsReentry = record.Storage == "session" && !cached.ContainsKey(record.Id) }).ToArray() };
        }
        finally { gate.Release(); }
    }
    public async Task Save(SearchConnectionEdit edit, CancellationToken cancellation)
    {
        if (edit.Storage is not ("system" or "session") || string.IsNullOrWhiteSpace(edit.Key) || edit.Key.Length > 2048 || edit.Key.Any(c => c is < '!' or > '~'))
            throw new ArgumentException("Choose key storage and enter a search API key using visible ASCII characters, up to 2048 characters.");
        await gate.WaitAsync(cancellation);
        try
        {
            if (edit.Version != Version) throw new InvalidOperationException("Search settings changed. Reload them before saving.");
            var catalog = Catalog;
            if (catalog.Records.Length >= 16) throw new InvalidOperationException("Remove an unused search key before adding another.");
            if (catalog.Scope == "") catalog = catalog with { Scope = Guid.NewGuid().ToString("N") };
            var record = new SearchCredential(Guid.NewGuid().ToString("N"), edit.Storage, "pending", DateTimeOffset.UtcNow, edit.RetainResults);
            var secret = Wire.Pack(new SearchSecret("public-search", BravePublicSearch.Endpoint, edit.Key));
            catalog = catalog with { Records = [.. catalog.Records, record] };
            store.Setting("search-connection", Wire.Pack(catalog));
            if (record.Storage == "system")
            {
                await vault.Execute("write", catalog.Scope, record.Id, secret, cancellation);
                if (await vault.Execute("read", catalog.Scope, record.Id, null, cancellation) != secret)
                    throw new InvalidOperationException("Search key storage could not be verified. Its pending record remains available for removal.");
            }
            cached[record.Id] = secret;
            catalog = catalog with { Selected = record.Id, Records = catalog.Records.Select(item => item.Id == record.Id ? item with { Status = "ready" } : item).ToArray() };
            store.Setting("search-connection", Wire.Pack(catalog));
        }
        finally { gate.Release(); }
    }
    public async Task SetUsage(SearchUsageEdit edit, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            if (edit.Version != Version) throw new InvalidOperationException("Search settings changed. Reload them before saving.");
            var catalog = Catalog;
            var selected = catalog.Records.FirstOrDefault(record => record.Id == catalog.Selected)
                ?? throw new InvalidOperationException("Save a search key first.");
            if (InUse(selected.Id)) throw new InvalidOperationException("Finish or cancel research using this key before changing its storage rights.");
            store.Setting("search-connection", Wire.Pack(catalog with { Records = catalog.Records.Select(record =>
                record.Id == selected.Id ? record with { RetainResults = edit.RetainResults } : record).ToArray() }));
        }
        finally { gate.Release(); }
    }
    public async Task<object> Check(string version, CancellationToken cancellation)
    {
        if (version != Version) throw new InvalidOperationException("Search settings changed. Reload them before checking the saved key.");
        var catalog = Catalog;
        if (catalog.Selected == null) throw new InvalidOperationException("No search key is selected.");
        _ = await ReadTemporary(new("brave", catalog.Selected), cancellation);
        if (version != Version) throw new InvalidOperationException("Search settings changed during the key check. Reload them.");
        return new { available = true, providerVerified = false,
            message = "The host can read the saved search key. No provider request was made; provider acceptance and quota remain unchecked." };
    }
    public async Task Forget(string id, string version, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            if (version != Version) throw new InvalidOperationException("Search settings changed. Reload them before removing a key.");
            var catalog = Catalog; var record = catalog.Records.FirstOrDefault(record => record.Id == id) ?? throw new ArgumentException("Search credential not found.");
            if (InUse(id)) throw new InvalidOperationException("Finish or cancel research using this search key before removing it.");
            catalog = catalog with { Records = catalog.Records.Select(item => item.Id == id ? item with { Status = "removing" } : item).ToArray() };
            store.Setting("search-connection", Wire.Pack(catalog)); cached.TryRemove(id, out _);
            if (record.Storage == "system")
            {
                await vault.Execute("forget", catalog.Scope, id, null, cancellation);
                if (await vault.Execute("read", catalog.Scope, id, null, cancellation) != null) throw new InvalidOperationException("Search key removal is not confirmed. It cannot be used for another request.");
            }
            store.Setting("search-connection", Wire.Pack(catalog with { Records = catalog.Records.Where(item => item.Id != id).ToArray() }));
        }
        finally { gate.Release(); }
    }
    public Task<string> Read(PublicSearchGrant grant, CancellationToken cancellation) => ReadCore(grant, true, cancellation);
    public Task<string> ReadTemporary(PublicSearchGrant grant, CancellationToken cancellation) => ReadCore(grant, false, cancellation);
    public string? SelectedId => Catalog.Selected;
    private async Task<string> ReadCore(PublicSearchGrant grant, bool retain, CancellationToken cancellation)
    {
        PublicSearchAccess.Validate(grant); await gate.WaitAsync(cancellation);
        try
        {
            var catalog = Catalog; var record = catalog.Records.FirstOrDefault(record => record.Id == grant.CredentialId);
            if (record is not { Status: "ready" }) throw new InvalidOperationException("The task's saved search connection is missing or unavailable. Reconnect it in Settings.");
            if (retain && !record.RetainResults) throw new InvalidOperationException("This search connection permits temporary results only. Use Search → Web, or supply sources directly for research.");
            if (!cached.TryGetValue(record.Id, out var packed))
            {
                if (record.Storage != "system") throw new InvalidOperationException("The session-only search key expired when the host stopped. Enter it again in Settings.");
                packed = await vault.Execute("read", catalog.Scope, record.Id, null, cancellation) ?? throw new InvalidOperationException("The search key is missing from the system credential store.");
            }
            SearchSecret secret;
            try { secret = Wire.Unpack<SearchSecret>(packed); }
            catch (JsonException) { throw new InvalidOperationException("The saved search key is unreadable."); }
            if (secret is not { Purpose: "public-search" } || secret.Endpoint != BravePublicSearch.Endpoint || string.IsNullOrWhiteSpace(secret.Key))
                throw new InvalidOperationException("The credential does not belong to this search provider.");
            cached[record.Id] = packed; return secret.Key;
        }
        finally { gate.Release(); }
    }
}
