using System.Text;
using System.Text.Json;
using Thaddeus.Core;

namespace Thaddeus.Host;

public sealed record GoogleClientImport(string Version, string CredentialsJson)
{
    public override string ToString() => "Google app setup (credentials omitted)";
}

internal sealed record GoogleDesktopClient(string ClientId, string ClientSecret)
{
    public override string ToString() => "Google Desktop app (credentials omitted)";
}

public sealed partial class McpConnections
{
    internal static bool OpenGoogleBrowser(Uri authorization, Action<Uri>? launch = null)
    {
        if (authorization.Scheme != "https" || authorization.Host != "accounts.google.com" || !authorization.IsDefaultPort || authorization.UserInfo != "")
            throw new InvalidOperationException("Google returned an unexpected sign-in address.");
        try
        {
            if (launch != null) launch(authorization);
            else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(authorization.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<object> GoogleClientView(McpConnectorCatalog catalog, CancellationToken cancellation)
    {
        try
        {
            var configured = await ReadGoogleClient(catalog, cancellation) != null;
            return new { configured, status = configured ? "ready" : "missing", canRemove = catalog.GoogleClientId != null };
        }
        catch (InvalidOperationException)
        {
            return new { configured = false, status = "unavailable", canRemove = catalog.GoogleClientId != null };
        }
    }

    internal async Task<GoogleDesktopClient?> ReadGoogleClient(McpConnectorCatalog catalog, CancellationToken cancellation)
    {
        if (catalog.GoogleClientId == null) return null;
        var packed = await vault.Execute("read", catalog.Scope, catalog.GoogleClientId, null, cancellation);
        if (packed == null) return null;
        try
        {
            var client = Wire.Unpack<GoogleDesktopClient>(packed);
            return new(ValidateOAuthValue(client.ClientId, "client ID", 512), ValidateOAuthValue(client.ClientSecret, "client secret", 512));
        }
        catch (Exception error) when (error is JsonException or ArgumentException or NullReferenceException)
        {
            throw new InvalidOperationException("Google app setup could not be read. Remove the saved app setup and import it again.");
        }
    }

    public async Task ImportGoogleClient(GoogleClientImport edit, CancellationToken cancellation)
    {
        var client = ParseGoogleClient(edit.CredentialsJson);
        var packed = Wire.Pack(client);
        if (Encoding.UTF8.GetByteCount(packed) > 2500) throw new ArgumentException("The Google app credentials exceed the system credential size limit.");
        await gate.WaitAsync(cancellation);
        try
        {
            CheckVersion(edit.Version);
            var catalog = Catalog;
            if (catalog.GoogleClientId != null) throw new InvalidOperationException("Remove the saved app setup before importing a replacement. Existing account connections will remain intact.");
            if (catalog.Scope == "") catalog = catalog with { Scope = Guid.NewGuid().ToString("N") };
            catalog = catalog with { GoogleClientId = Guid.NewGuid().ToString("N") };
            // Leave a removable reference even if the vault refuses the write. No keys in the ledger, dear reader.
            store.Setting(SettingName, Wire.Pack(catalog));
            await vault.Execute("write", catalog.Scope, catalog.GoogleClientId, packed, cancellation);
            if (await vault.Execute("read", catalog.Scope, catalog.GoogleClientId, null, cancellation) != packed)
                throw new InvalidOperationException("Google app setup could not be verified in the system credential store. Remove it and try importing again.");
        }
        finally { gate.Release(); }
    }

    public async Task ForgetGoogleClient(McpConnectorChange change, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            CheckVersion(change.Version);
            var catalog = Catalog;
            if (catalog.GoogleClientId == null) return;
            await vault.Execute("forget", catalog.Scope, catalog.GoogleClientId, null, cancellation);
            if (await vault.Execute("read", catalog.Scope, catalog.GoogleClientId, null, cancellation) != null)
                throw new InvalidOperationException("Google app setup removal could not be confirmed. Try again after unlocking the system credential store.");
            store.Setting(SettingName, Wire.Pack(catalog with { GoogleClientId = null }));
        }
        finally { gate.Release(); }
    }

    internal static GoogleDesktopClient ParseGoogleClient(string? json)
    {
        const string guidance = "Choose the original Desktop app credentials JSON downloaded from Google Cloud. Web-app and service-account files cannot be used here.";
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16_384) throw new ArgumentException(guidance);
        try
        {
            using var document = JsonDocument.Parse(json, new() { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("web", out _) || !root.TryGetProperty("installed", out var installed) || installed.ValueKind != JsonValueKind.Object)
                throw new ArgumentException(guidance);
            var clientId = ValidateOAuthValue(installed.GetProperty("client_id").GetString(), "client ID", 512);
            var secret = ValidateOAuthValue(installed.GetProperty("client_secret").GetString(), "client secret", 512);
            if (!clientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal) || clientId.Any(char.IsWhiteSpace)
                || installed.GetProperty("auth_uri").GetString() != "https://accounts.google.com/o/oauth2/auth"
                || installed.GetProperty("token_uri").GetString() != "https://oauth2.googleapis.com/token")
                throw new ArgumentException(guidance);
            // Imported endpoints, redirects and extra fields never choose where credentials go.
            return new(clientId, secret);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ArgumentException(guidance);
        }
    }
}
