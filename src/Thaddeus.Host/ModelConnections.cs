using System.Text;
using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public interface ICredentialVault
{
    Task<string?> Execute(string operation, string scope, string id, string? value, CancellationToken cancellation);
}

public sealed class ProcessCredentialVault(IHostProcessRunner runner) : ICredentialVault
{
    public async Task<string?> Execute(string operation, string scope, string id, string? value, CancellationToken cancellation)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Thaddeus.Host" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        string[] allowed = ["SystemRoot", "WINDIR", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "HOMEDRIVE", "HOMEPATH", "COMSPEC", "TEMP", "TMP", "PATH",
            "HOME", "XDG_RUNTIME_DIR", "DBUS_SESSION_BUS_ADDRESS", "XDG_DATA_HOME", "XDG_CONFIG_HOME", "LANG", "LC_ALL", "TMPDIR", "SECURITYSESSIONID", "__CF_USER_TEXT_ENCODING", "DISPLAY", "WAYLAND_DISPLAY"];
        var environment = System.Environment.GetEnvironmentVariables().Keys.Cast<string>().ToDictionary(name => name,
            name => allowed.Contains(name, StringComparer.OrdinalIgnoreCase) ? System.Environment.GetEnvironmentVariable(name) : null, StringComparer.OrdinalIgnoreCase);
        var result = await runner.Run(new(executable, ["--credential-helper"], AppContext.BaseDirectory, TimeSpan.FromSeconds(15),
            Wire.Pack(new VaultRequest(operation, scope, id, value)), 12_000, environment), cancellation);
        if (!result.Succeeded) throw Unavailable();
        try
        {
            var reply = Wire.Unpack<VaultReply>(result.Output);
            if (!reply.Ok) throw Unavailable();
            return reply.Value;
        }
        catch (JsonException) { throw Unavailable(); }
    }
    public static InvalidOperationException Unavailable() => new("The system credential store is unavailable or locked. Unlock it and retry, or explicitly choose storage until the host stops. Nothing was sent to a model.");
}

public sealed record CredentialRecord(string Id, string Endpoint, string Storage, string Status, DateTimeOffset Created);
public sealed record CredentialCatalog(string Scope, CredentialRecord[] Records);
public sealed record StoredProviderSecret(string Endpoint, string Key);
public sealed record ConnectionEdit(string Version, ProviderSnapshot Provider, string CredentialMode, string? Key = null)
{
    public override string ToString() => "Provider connection edit (credential omitted)";
}
public sealed record CredentialRemoval(string Version);

/// <summary>Owner setup stores only references in the ledger; host transports resolve endpoint-bound keys.</summary>
public sealed class ModelConnections(Store store, ICredentialVault vault, string? environmentKey = null, string? environmentEndpoint = null) : IProviderCredentials
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly Dictionary<string, string> available = [];
    private readonly string? environmentBinding = EnvironmentBinding(store, environmentKey, environmentEndpoint);
    private CredentialCatalog Catalog => store.Setting("provider-credentials") is { } value ? Wire.Unpack<CredentialCatalog>(value) : new("", []);
    private ProviderSnapshot Provider => Wire.Unpack<ProviderSnapshot>(store.Setting("provider") ?? Wire.Pack(new ProviderSnapshot()));
    private string Version => Wire.Hash((store.Setting("provider") ?? "") + "\n" + (store.Setting("provider-credentials") ?? ""));
    public static string Endpoint(ProviderSnapshot provider) => CompatibleProvider.Endpoint(provider).AbsoluteUri;
    private static string? EnvironmentBinding(Store store, string? key, string? endpoint)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var provider = Wire.Unpack<ProviderSnapshot>(store.Setting("provider") ?? Wire.Pack(new ProviderSnapshot()));
        if (endpoint != null) return Endpoint(new("compatible", "environment", Endpoint: endpoint));
        return provider.Kind == "compatible" ? Endpoint(provider) : null;
    }
    private bool InUse(string id) => store.List().Any(run => run.Goal.Provider.CredentialId == id &&
        (run.ReservedTokens != 0 || run.State is RunState.Running or RunState.Queued || run.Research is { Phase: not "finished" }));
    private void CheckVersion(string version) { if (version != Version) throw new InvalidOperationException("Connection settings changed. Refresh before saving."); }
    private static void Validate(ProviderSnapshot provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (provider.Kind is not ("compatible" or "scripted")) throw new ArgumentException("Choose a demo or compatible provider.");
        if (provider.Reasoning is not ("none" or "low" or "medium" or "high") || string.IsNullOrWhiteSpace(provider.Model) || provider.Model.Length > 200) throw new ArgumentException("Enter a model ID and supported reasoning effort.");
        if (provider.Kind == "compatible") _ = Endpoint(provider);
    }
    public async Task<object> View(CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var provider = Provider; var catalog = Catalog;
            var current = catalog.Records.FirstOrDefault(record => record.Id == provider.CredentialId);
            var mode = provider.Kind == "scripted" || provider.CredentialId == "none" ? "none" : current?.Storage ?? (provider.CredentialId != null ? "missing" : string.IsNullOrEmpty(environmentKey) ? "none" : "environment");
            return new { version = Version, provider, credentialMode = mode, systemStore = NativeCredentialVault.Name,
                environmentEndpoint = environmentBinding, environmentPresent = !string.IsNullOrEmpty(environmentKey),
                credentials = catalog.Records.Select(record => new { record.Id, record.Endpoint, record.Storage, record.Status, record.Created, inUse = InUse(record.Id),
                    needsReentry = record.Storage == "session" && !available.ContainsKey(record.Id), selected = record.Id == provider.CredentialId }).ToArray() };
        }
        finally { gate.Release(); }
    }
    public async Task Save(ConnectionEdit edit, CancellationToken cancellation)
    {
        Validate(edit.Provider);
        await gate.WaitAsync(cancellation);
        try
        {
            CheckVersion(edit.Version);
            var provider = edit.Provider; var catalog = Catalog;
            if (catalog.Scope == "") catalog = catalog with { Scope = Guid.NewGuid().ToString("N") };
            if (provider.Kind == "scripted")
            {
                if (!string.IsNullOrEmpty(edit.Key)) throw new ArgumentException("The scripted demo does not use an API key.");
                provider = provider with { CredentialId = null, Endpoint = null };
            }
            else
            {
                var endpoint = Endpoint(provider);
                if (edit.CredentialMode is not ("system" or "session") && !string.IsNullOrEmpty(edit.Key)) throw new ArgumentException("Choose how the entered key should be stored.");
                switch (edit.CredentialMode)
                {
                    case "none": provider = provider with { CredentialId = "none" }; break;
                    case "environment":
                        if (string.IsNullOrEmpty(environmentKey) || environmentBinding != endpoint) throw new InvalidOperationException("The environment key is not bound to this endpoint. Enter a key for this destination or choose no key.");
                        provider = provider with { CredentialId = null }; break;
                    case "keep":
                        provider = provider with { CredentialId = Provider.CredentialId };
                        if (Provider.Kind != "compatible" || Endpoint(Provider) != endpoint) throw new InvalidOperationException("Changing the endpoint requires an explicit credential choice.");
                        if (provider.CredentialId is { } kept && kept != "none" && !catalog.Records.Any(record => record.Id == kept && record.Endpoint == endpoint && record.Status == "ready"))
                            throw new InvalidOperationException("The saved credential needs attention. Enter a replacement or choose no key.");
                        break;
                    case "session":
                    case "system":
                        if (catalog.Records.Length >= 16) throw new InvalidOperationException("Remove an unused credential before adding another.");
                        if (string.IsNullOrWhiteSpace(edit.Key) || edit.Key.Length > 2048 || edit.Key.Any(c => c is < '!' or > '~')) throw new ArgumentException("Enter an API key using visible ASCII characters without whitespace, up to 2048 characters.");
                        var secret = Wire.Pack(new StoredProviderSecret(endpoint, edit.Key));
                        if (Encoding.UTF8.GetByteCount(secret) > 2500) throw new ArgumentException("This key and endpoint exceed the system credential size limit.");
                        var record = new CredentialRecord(Guid.NewGuid().ToString("N"), endpoint, edit.CredentialMode, "pending", DateTimeOffset.UtcNow);
                        catalog = catalog with { Records = [.. catalog.Records, record] };
                        // Write intent first. A host interruption or late native write remains visible for explicit cleanup.
                        store.Setting("provider-credentials", Wire.Pack(catalog));
                        if (edit.CredentialMode == "system")
                        {
                            await vault.Execute("write", catalog.Scope, record.Id, secret, cancellation);
                            var actual = await vault.Execute("read", catalog.Scope, record.Id, null, cancellation);
                            if (actual != secret) throw new InvalidOperationException("The stored key could not be verified. The previous connection remains selected; inspect the pending credential.");
                        }
                        available[record.Id] = secret;
                        catalog = catalog with { Records = catalog.Records.Select(item => item.Id == record.Id ? record with { Status = "ready" } : item).ToArray() };
                        provider = provider with { CredentialId = record.Id }; break;
                    default: throw new ArgumentException("Choose an explicit credential storage mode.");
                }
            }
            store.ProviderSettings(Wire.Pack(provider), Wire.Pack(catalog));
        }
        finally { gate.Release(); }
    }
    public async Task SaveLegacy(ProviderSnapshot provider, CancellationToken cancellation)
    {
        // Older clients can retain their saved binding; they cannot attach a credential to a different URL.
        Validate(provider); await gate.WaitAsync(cancellation);
        try
        {
            var old = Provider;
            if (provider.CredentialId != old.CredentialId) throw new ArgumentException("Use connection setup to change credentials.");
            if (provider.Kind == "compatible")
            {
                if (provider.CredentialId is not (null or "none") && (old.Kind != "compatible" || Endpoint(old) != Endpoint(provider))) throw new InvalidOperationException("Changing the endpoint requires an explicit credential choice.");
                if (provider.CredentialId == null && !string.IsNullOrEmpty(environmentKey) && environmentBinding != Endpoint(provider)) throw new InvalidOperationException("Use connection setup to choose credentials for this endpoint.");
            }
            store.Setting("provider", Wire.Pack(provider));
        }
        finally { gate.Release(); }
    }
    public async Task Forget(string id, string version, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            CheckVersion(version); var catalog = Catalog;
            var record = catalog.Records.FirstOrDefault(item => item.Id == id) ?? throw new ArgumentException("Credential not found.");
            if (InUse(id)) throw new InvalidOperationException("Cancel or finish work using this credential before removing it.");
            catalog = catalog with { Records = catalog.Records.Select(item => item.Id == id ? item with { Status = "removing" } : item).ToArray() };
            store.Setting("provider-credentials", Wire.Pack(catalog)); available.Remove(id);
            if (record.Storage == "system")
            {
                await vault.Execute("forget", catalog.Scope, id, null, cancellation);
                if (await vault.Execute("read", catalog.Scope, id, null, cancellation) != null) throw new InvalidOperationException("Credential removal is not confirmed. No new dispatch can use it.");
            }
            catalog = catalog with { Records = catalog.Records.Where(item => item.Id != id).ToArray() };
            // Keep the selected reference: removing a key must never silently turn authenticated calls into anonymous calls.
            store.Setting("provider-credentials", Wire.Pack(catalog));
        }
        finally { gate.Release(); }
    }
    public async Task<string?> Read(ProviderSnapshot provider, CancellationToken cancellation)
    {
        if (provider.Kind == "scripted" || provider.CredentialId == "none") return null;
        var endpoint = Endpoint(provider);
        await gate.WaitAsync(cancellation);
        try
        {
            if (provider.CredentialId == null)
            {
                if (string.IsNullOrEmpty(environmentKey)) return null;
                if (environmentBinding != endpoint) throw new InvalidOperationException("The environment credential belongs to another endpoint. Configure this connection before dispatch.");
                return environmentKey;
            }
            var catalog = Catalog;
            var record = catalog.Records.FirstOrDefault(item => item.Id == provider.CredentialId);
            if (record == null || record.Endpoint != endpoint || record.Status != "ready") throw new InvalidOperationException("The credential is missing, changed or being removed. Reconnect this provider before dispatch.");
            if (!available.TryGetValue(record.Id, out var packed))
            {
                if (record.Storage != "system") throw new InvalidOperationException("This key was kept only until the host stopped. Enter it again in Settings before dispatch.");
                packed = await vault.Execute("read", catalog.Scope, record.Id, null, cancellation) ?? throw new InvalidOperationException("The key is missing from the system credential store. Reconnect this provider before dispatch.");
            }
            StoredProviderSecret secret;
            try { secret = Wire.Unpack<StoredProviderSecret>(packed); }
            catch (JsonException) { throw new InvalidOperationException("The stored credential is unreadable. Reconnect this provider before dispatch."); }
            if (secret == null || secret.Endpoint != endpoint || string.IsNullOrEmpty(secret.Key)) throw new InvalidOperationException("The stored credential does not match this endpoint. No model was dispatched.");
            available[record.Id] = packed; return secret.Key;
        }
        finally { gate.Release(); }
    }
}
