using System.Security.Cryptography;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record VaultKey(string Service, string Account, string Kind, string Status, DateTimeOffset ConnectedAt);
public record VaultHealth(string Store, bool Working, string Detail, DateTimeOffset CheckedAt);

/// <summary>Where the workspace's keys and tokens live, and whether the configured credential vault works.
/// The overview lists stored connections by name; it never returns their secrets.</summary>
public sealed class VaultOverview(ICredentialVault vault, DataConnections data, Publishing publishing, McpConnections google)
{
    VaultHealth? last;

    /// <summary>A round trip through the store: write a throwaway value, read it back, delete it. Checked at most once a minute.</summary>
    public async Task<VaultHealth> Check(CancellationToken cancellation, bool force = false)
    {
        if (!force && last is { } recent && recent.CheckedAt > DateTimeOffset.UtcNow.AddMinutes(-1)) return recent;
        var scope = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var probe = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        try
        {
            await vault.Execute("write", scope, id, probe, cancellation);
            var back = await vault.Execute("read", scope, id, null, cancellation);
            await vault.Execute("forget", scope, id, null, cancellation);
            last = back == probe
                ? new(vault.Name, true, "Keys are written to and read from this computer's credential store only.", DateTimeOffset.UtcNow)
                : new(vault.Name, false, "The credential store didn't return what was written. Keys can't be saved until it does.", DateTimeOffset.UtcNow);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or PlatformNotSupportedException)
        {
            last = new(vault.Name, false, "The credential store is locked or unavailable: " + error.Message, DateTimeOffset.UtcNow);
        }
        return last;
    }

    /// <summary>Every stored connection, by service and account; never its secret.</summary>
    public async Task<VaultKey[]> Keys(CancellationToken cancellation)
    {
        var keys = new List<VaultKey>();
        keys.AddRange(data.Connected().Select(item => new VaultKey(DataConnections.Kinds.TryGetValue(item.Kind, out var kind) ? kind.Name : item.Kind,
            item.ResourceName ?? item.Account ?? "", item.Kind, item.Status, item.CreatedAt)));
        keys.AddRange(publishing.Ledger().Connections.Where(item => item.Status == "ready")
            .Select(item => new VaultKey(Publishing.Kinds.TryGetValue(item.Kind, out var kind) ? kind.Name : item.Kind, item.Account, item.Kind, item.Status, item.CreatedAt)));
        try { if (await google.SavedGoogleClient(cancellation) is { } client) keys.Add(new VaultKey("Google app", client.ClientId.Split('-')[0] + "…", "google-app", "ready", DateTimeOffset.MinValue)); }
        catch (InvalidOperationException) { }
        return [.. keys];
    }
}
