using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using ModelContextProtocol;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public sealed record McpToolRecord(string RemoteName, string ModelName, string Description, JsonElement InputSchema, string Effect);
public sealed record McpConnectorRecord(string Id, string Name, string Endpoint, string Storage, string Status,
    DateTimeOffset Created, DateTimeOffset Updated, McpToolRecord[] Tools, string Version, bool Enabled = true,
    string? Account = null, string[]? GrantedScopes = null);
public sealed record McpConnectorCatalog(string Scope, McpConnectorRecord[] Connectors, string? GoogleClientId = null);
public sealed record GoogleConnectionState(string[] Products, string[] Accounts);
public sealed record StoredMcpSecret(string Endpoint, string Token);
public sealed record StoredMcpOAuth(string Endpoint, string Product, string ClientId, string ClientSecret);
public sealed record McpConnectorEdit(string Version, string Name, string Endpoint, string Storage, string? Token = null)
{
    public override string ToString() => "MCP connector edit (credential omitted)";
}
public sealed record McpConnectorChange(string Version);
public sealed record GoogleMcpStart(string Version, string? Product = null, string? ClientId = null, string? ClientSecret = null,
    string[]? Products = null)
{
    public override string ToString() => "Google Workspace MCP sign-in (client secret omitted)";
}

/// <summary>Owns external MCP discovery, credentials and dispatch outside the model process.</summary>
public sealed partial class McpConnections(Store store, ICredentialVault vault, string localOrigin = "http://localhost:5179",
    Func<HttpMessageHandler>? handlerFactory = null, Action<Uri>? openGoogleBrowser = null) : IConnectedToolBroker
{
    private const string SettingName = "mcp-connectors";
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, string> sessionTokens = [];
    private readonly Dictionary<string, TokenContainer> oauthTokens = [];
    private readonly Dictionary<string, OAuthAttempt> oauthAttempts = [];
    private readonly Uri oauthRedirect = GoogleRedirect(localOrigin);
    private McpConnectorCatalog Catalog => store.Setting(SettingName) is { } value
        ? Wire.Unpack<McpConnectorCatalog>(value) : new("", []);
    private string Version => Wire.Hash(store.Setting(SettingName) ?? "");

    public ConnectedToolDefinition[] Snapshot()
    {
        var catalog = Catalog;
        return catalog.Connectors
            .Select(EffectiveGoogle)
            .Where(connector => connector.Enabled && connector.Status == "ready" && (connector.Storage != "session" || sessionTokens.ContainsKey(connector.Id)))
            .SelectMany(connector => connector.Tools.Select(tool => new ConnectedToolDefinition(connector.Id, connector.Name,
                tool.RemoteName, tool.ModelName, tool.Description, tool.InputSchema, tool.Effect, connector.Version)))
            .Take(64).ToArray();
    }

    public GoogleConnectionState GoogleState()
    {
        var google = Catalog.Connectors.Where(connector => connector.Storage == "oauth" && connector.Status == "ready").ToArray();
        return new(google.SelectMany(connector => GoogleProduct.Capabilities(connector.Name)).Distinct(StringComparer.Ordinal).ToArray(),
            google.Select(connector => connector.Account).OfType<string>().Where(account => !string.IsNullOrWhiteSpace(account)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<object> View(CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var catalog = Catalog;
            var connectors = catalog.Connectors.Select(EffectiveGoogle).ToArray();
            return new
            {
                version = Version,
                systemStore = vault.Name,
                google = new
                {
                    redirectUri = oauthRedirect.AbsoluteUri,
                    clientType = "Desktop app",
                    clientSetup = await GoogleClientView(catalog, cancellation),
                    products = GoogleProduct.Views(connectors)
                },
                connectors = connectors.Select(connector => new
                {
                    connector.Id, connector.Name, connector.Endpoint, connector.Storage, connector.Status,
                    connector.Created, connector.Updated, connector.Enabled, connector.Tools, connector.Account,
                    grantedScopes = connector.GrantedScopes ?? [],
                    needsReentry = connector.Storage == "session" && !sessionTokens.ContainsKey(connector.Id),
                    authentication = connector.Storage == "oauth" ? "Google OAuth" : connector.Storage,
                    inUse = InUse(connector.Id)
                }).ToArray()
            };
        }
        finally { gate.Release(); }
    }

    public async Task<object> BeginGoogle(GoogleMcpStart edit, CancellationToken cancellation)
    {
        var requested = edit.Products is { Length: > 0 } selected ? selected.Select(id => (string?)id) : [edit.Product];
        var products = GoogleProduct.FindMany(requested);
        await gate.WaitAsync(cancellation);
        try
        {
            CleanupAttempts();
            CheckVersion(edit.Version);
            var catalog = Catalog;
            // Older open tabs can finish their explicit setup; new tabs use the host's saved registration.
            var client = edit.ClientId != null || edit.ClientSecret != null
                ? new GoogleDesktopClient(ValidateOAuthValue(edit.ClientId, "client ID", 512), ValidateOAuthValue(edit.ClientSecret, "client secret", 512))
                : await ReadGoogleClient(catalog, cancellation) ?? throw new InvalidOperationException("Google setup is needed once before sign-in. Import the Desktop app credentials file in App setup.");
            if (catalog.Connectors.Length + products.Length > 12) throw new InvalidOperationException("Remove an unused connector before adding these Google permissions.");
            var duplicate = products.FirstOrDefault(product => catalog.Connectors.Any(item => string.Equals(item.Name, product.Name, StringComparison.OrdinalIgnoreCase)));
            if (duplicate != null) throw new ArgumentException(duplicate.Name + " is already connected. Choose only permissions that still need access.");
            var attempt = new OAuthAttempt(Guid.NewGuid().ToString("N"), products, client.ClientId, client.ClientSecret, edit.Version);
            lock (oauthAttempts) oauthAttempts.Add(attempt.Id, attempt);
            _ = RunGoogle(attempt);
            return await attempt.Ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellation);
        }
        finally { gate.Release(); }
    }

    public object GoogleStatus(string id)
    {
        lock (oauthAttempts)
        {
            if (!oauthAttempts.TryGetValue(id, out var attempt)) throw new ArgumentException("Google sign-in attempt not found or expired.");
            return new
            {
                attemptId = attempt.Id, phase = attempt.Phase, error = attempt.Error, account = attempt.Account,
                connectedProducts = attempt.ConnectedProducts, skippedProducts = attempt.SkippedProducts
            };
        }
    }

    public void CompleteGoogle(string? code, string? state, string? issuer, string? error)
    {
        OAuthAttempt? attempt;
        lock (oauthAttempts)
        {
            attempt = oauthAttempts.Values.SingleOrDefault(item => !item.Callback.Task.IsCompleted && Fixed(item.State, state));
            if (attempt == null) throw new ArgumentException("This Google sign-in response is unknown, expired, or already used.");
            if (!string.IsNullOrEmpty(error))
            {
                attempt.Callback.TrySetException(new InvalidOperationException("Google did not authorize this connection."));
                return;
            }
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state)) throw new ArgumentException("Google returned an incomplete sign-in response.");
            if (!string.IsNullOrEmpty(issuer) && issuer is not ("https://accounts.google.com" or "accounts.google.com"))
                throw new ArgumentException("Google returned an unexpected authorization issuer.");
            attempt.Callback.TrySetResult(new AuthorizationResult { Code = code, State = state, Iss = issuer });
        }
    }

    public async Task Save(McpConnectorEdit edit, CancellationToken cancellation)
    {
        var name = ValidateName(edit.Name);
        var endpoint = Endpoint(edit.Endpoint).AbsoluteUri;
        if (edit.Storage is not ("none" or "session" or "system")) throw new ArgumentException("Choose no credential, system credential storage, or storage until the host stops.");
        var token = ValidateToken(edit.Storage, edit.Token);
        await gate.WaitAsync(cancellation);
        try
        {
            CheckVersion(edit.Version);
            var current = Catalog;
            if (current.Connectors.Length >= 12) throw new InvalidOperationException("Remove an unused connector before adding another.");
            if (current.Connectors.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("A connector with this name already exists.");
        }
        finally { gate.Release(); }
        var id = Guid.NewGuid().ToString("N");
        McpToolRecord[] tools;
        try { tools = await Discover(id, name, endpoint, token, cancellation); }
        catch (Exception ex) when (ex is HttpRequestException or McpException or IOException or JsonException or OperationCanceledException)
        {
            throw new InvalidOperationException("The MCP server could not be verified. Check its URL, credential, and Streamable HTTP support; nothing was saved.", ex);
        }

        await gate.WaitAsync(cancellation);
        try
        {
            CheckVersion(edit.Version);
            var catalog = Catalog;
            if (catalog.Connectors.Length >= 12) throw new InvalidOperationException("Remove an unused connector before adding another.");
            if (catalog.Connectors.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("A connector with this name already exists.");
            if (catalog.Scope == "") catalog = catalog with { Scope = Guid.NewGuid().ToString("N") };
            var now = DateTimeOffset.UtcNow;
            var connectorVersion = ConnectorVersion(id, endpoint, edit.Storage, tools, now);
            var record = new McpConnectorRecord(id, name, endpoint, edit.Storage, edit.Storage == "system" ? "pending" : "ready", now, now, tools, connectorVersion);
            if (edit.Storage == "session") sessionTokens[id] = token!;
            // Record system write intent first. If the host stops, the pending item remains removable without advertising tools.
            if (edit.Storage == "system") store.Setting(SettingName, Wire.Pack(catalog with { Connectors = [.. catalog.Connectors, record] }));
            if (edit.Storage == "system")
            {
                var secret = Wire.Pack(new StoredMcpSecret(endpoint, token!));
                if (Encoding.UTF8.GetByteCount(secret) > 2500) throw new ArgumentException("This token and endpoint exceed the system credential size limit.");
                await vault.Execute("write", catalog.Scope, id, secret, cancellation);
                if (await vault.Execute("read", catalog.Scope, id, null, cancellation) != secret)
                    throw new InvalidOperationException("The system credential could not be verified. The connector remains pending so it can be inspected or removed.");
                record = record with { Status = "ready" };
            }
            store.Setting(SettingName, Wire.Pack(catalog with { Connectors = [.. catalog.Connectors, record] }));
        }
        finally { gate.Release(); }
    }

    public async Task Refresh(string id, McpConnectorChange change, CancellationToken cancellation)
    {
        McpConnectorRecord connector;
        string? token;
        await gate.WaitAsync(cancellation);
        try
        {
            CheckVersion(change.Version);
            connector = Catalog.Connectors.SingleOrDefault(item => item.Id == id) ?? throw new ArgumentException("Connector not found.");
            token = connector.Storage == "oauth" ? null : await ReadToken(Catalog, connector, cancellation);
        }
        finally { gate.Release(); }
        McpToolRecord[] tools;
        try { tools = connector.Storage == "oauth" ? await DiscoverOAuth(connector, cancellation) : await Discover(connector.Id, connector.Name, connector.Endpoint, token, cancellation); }
        catch (Exception ex) when (ex is HttpRequestException or McpException or IOException or JsonException or OperationCanceledException)
        {
            throw new InvalidOperationException("The MCP server could not be refreshed. Its prior tool catalog remains unchanged.", ex);
        }
        await gate.WaitAsync(cancellation);
        try
        {
            CheckVersion(change.Version);
            var catalog = Catalog;
            var current = catalog.Connectors.SingleOrDefault(item => item.Id == id) ?? throw new ArgumentException("Connector not found.");
            var now = DateTimeOffset.UtcNow;
            var updated = current with { Tools = tools, Updated = now, Version = ConnectorVersion(id, current.Endpoint, current.Storage, tools, now) };
            store.Setting(SettingName, Wire.Pack(catalog with { Connectors = catalog.Connectors.Select(item => item.Id == id ? updated : item).ToArray() }));
        }
        finally { gate.Release(); }
    }

    public async Task Forget(string id, McpConnectorChange change, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            CheckVersion(change.Version);
            var catalog = Catalog;
            var connector = catalog.Connectors.SingleOrDefault(item => item.Id == id) ?? throw new ArgumentException("Connector not found.");
            sessionTokens.Remove(id);
            if (connector.Storage == "system")
            {
                await vault.Execute("forget", catalog.Scope, id, null, cancellation);
                if (await vault.Execute("read", catalog.Scope, id, null, cancellation) != null)
                    throw new InvalidOperationException("Credential removal is not confirmed. The connector remains registered for inspection.");
            }
            if (connector.Storage == "oauth")
            {
                var configId = OAuthId(id, "config"); var tokenId = OAuthId(id, "token");
                await vault.Execute("forget", catalog.Scope, configId, null, cancellation);
                await vault.Execute("forget", catalog.Scope, tokenId, null, cancellation);
                if (await vault.Execute("read", catalog.Scope, configId, null, cancellation) != null ||
                    await vault.Execute("read", catalog.Scope, tokenId, null, cancellation) != null)
                    throw new InvalidOperationException("Google credential removal is not confirmed. The connector remains registered for inspection.");
                oauthTokens.Remove(id);
            }
            store.Setting(SettingName, Wire.Pack(catalog with { Connectors = catalog.Connectors.Where(item => item.Id != id).ToArray() }));
        }
        finally { gate.Release(); }
    }

    public async Task<CapabilityResult> Call(ConnectedToolDefinition tool, JsonElement arguments, CancellationToken cancellation)
    {
        McpConnectorRecord connector;
        string? token;
        await gate.WaitAsync(cancellation);
        try
        {
            var catalog = Catalog;
            connector = EffectiveGoogle(catalog.Connectors.SingleOrDefault(item => item.Id == tool.ConnectorId)
                ?? throw new InvalidOperationException("The reviewed connector was removed before dispatch."));
            if (!connector.Enabled || connector.Status != "ready" || connector.Version != tool.ConnectionVersion)
                throw new InvalidOperationException("The connector changed after review. Start a new request so its tools can be reviewed again.");
            var current = connector.Tools.SingleOrDefault(item => item.RemoteName == tool.RemoteName && item.ModelName == tool.ModelName);
            if (current == null || current.InputSchema.GetRawText() != tool.InputSchema.GetRawText())
                throw new InvalidOperationException("The connector tool changed after review. No request was sent.");
            token = connector.Storage == "oauth" ? null : await ReadToken(catalog, connector, cancellation);
        }
        finally { gate.Release(); }

        try
        {
            if (connector.Storage == "oauth")
            {
                if (tool.RemoteName == GoogleGmailApi.ToolName)
                    return await SendGmail(connector, arguments, cancellation);
                if (GoogleWorkspaceReadApi.IsGmailTool(tool.RemoteName) || GoogleWorkspaceReadApi.IsCalendarTool(tool.RemoteName))
                    return await ReadGoogle(connector, tool.RemoteName, arguments, cancellation);
            }
            using var http = Http(token);
            var options = new HttpClientTransportOptions { Endpoint = new(connector.Endpoint), Name = connector.Name, EnableStandaloneGetStream = false, ConnectionTimeout = TimeSpan.FromSeconds(15) };
            if (connector.Storage == "oauth") options.OAuth = await OAuthOptions(Catalog, connector, interactive: null, cancellation);
            await using var transport = new HttpClientTransport(options, http);
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellation);
            var values = JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments.GetRawText(), Wire.Json) ?? [];
            var result = await client.CallToolAsync(tool.RemoteName, values, cancellationToken: cancellation);
            var encoded = JsonSerializer.SerializeToElement(result, Wire.Json);
            if (encoded.GetRawText().Length > 100_000) return new(JsonSerializer.SerializeToElement(new { error = "The connector result exceeded Thaddeus's 100,000-character review limit." }, Wire.Json), true);
            return new(encoded, result.IsError ?? false);
        }
        catch (DelegationOutcomeUnknownException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or McpException or IOException or JsonException or OperationCanceledException)
        {
            throw new InvalidOperationException("The reviewed MCP request failed. Its outcome is not retried automatically.", ex);
        }
    }

    private async Task<McpToolRecord[]> Discover(string id, string name, string endpoint, string? token, CancellationToken cancellation)
    {
        using var http = Http(token);
        await using var transport = new HttpClientTransport(new() { Endpoint = new(endpoint), Name = name, EnableStandaloneGetStream = false, ConnectionTimeout = TimeSpan.FromSeconds(15) }, http);
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellation);
        var advertised = await client.ListToolsAsync(cancellationToken: cancellation);
        return await Tools(id, advertised);
    }

    private static Task<McpToolRecord[]> Tools(string id, IList<McpClientTool> advertised)
    {
        if (advertised.Count == 0) throw new InvalidOperationException("The MCP server advertised no tools.");
        if (advertised.Count > 32) throw new InvalidOperationException("This MCP server advertises more than the supported limit of 32 tools.");
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        var tools = new List<McpToolRecord>();
        foreach (var tool in advertised)
        {
            if (string.IsNullOrWhiteSpace(tool.Name) || tool.Name.Length > 128) throw new InvalidOperationException("The MCP server advertised an invalid tool name.");
            var alias = ModelName(id, tool.Name);
            if (!aliases.Add(alias)) throw new InvalidOperationException("Two MCP tools collapse to the same safe model name.");
            var description = string.IsNullOrWhiteSpace(tool.Description) ? "External MCP capability." : tool.Description.Trim();
            if (description.Length > 1000) description = description[..1000];
            var schema = tool.JsonSchema.Clone();
            if (schema.ValueKind != JsonValueKind.Object || schema.GetRawText().Length > 30_000) throw new InvalidOperationException("The MCP server advertised an invalid or oversized input schema.");
            var protocol = JsonSerializer.SerializeToElement(tool.ProtocolTool, Wire.Json);
            var effect = Effect(protocol);
            tools.Add(new(tool.Name, alias, description, schema, effect));
        }
        return Task.FromResult(tools.ToArray());
    }

    private async Task<McpToolRecord[]> DiscoverOAuth(McpConnectorRecord connector, CancellationToken cancellation)
    {
        var config = await OAuthConfig(Catalog, connector, cancellation);
        var product = GoogleProduct.Find(config.Product);
        if (connector.Endpoint != product.Endpoint) throw new InvalidOperationException("The Google connection no longer matches its reviewed product.");
        return await PrepareGoogle(product, connector.Id, await GoogleAccessToken(connector, cancellation), cancellation);
    }

    private async Task RunGoogle(OAuthAttempt attempt)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            // A public MCP catalogue is not a sign-in challenge. Ring Google's actual front door first.
            var tokens = await AuthorizeGoogle(attempt, timeout.Token);
            var scopes = GrantedScopes(tokens.Scope, attempt.Scopes);
            tokens.Scope = string.Join(' ', scopes);
            var account = await GoogleAccount(tokens.AccessToken, timeout.Token);
            attempt.Account = account;
            var prepared = new List<PreparedGoogle>();
            var skipped = new List<string>();
            foreach (var product in attempt.Products)
            {
                if (!HasScopes(scopes, product.Scopes))
                {
                    skipped.Add(product.Name + " (permission was not granted)");
                    continue;
                }
                var connectorId = attempt.ConnectorIds[product.Id];
                try
                {
                    var tools = await PrepareGoogle(product, connectorId, tokens.AccessToken!, timeout.Token);
                    prepared.Add(new(product, connectorId, tools));
                }
                catch (Exception error) when (error is HttpRequestException or McpException or IOException or JsonException or InvalidOperationException)
                {
                    skipped.Add(product.Name + " (" + GoogleVerificationFailure(error) + ")");
                }
            }
            if (prepared.Count == 0)
                throw new InvalidOperationException($"Google signed in as {account}, but none of the selected Gmail or Calendar permissions became usable. Reconnect and approve the permissions you want Thaddeus to use.");
            await CommitOAuth(attempt, prepared, tokens, account, scopes, timeout.Token);
            attempt.ConnectedProducts = prepared.Select(item => item.Product.Id).ToArray();
            attempt.SkippedProducts = skipped.ToArray();
            attempt.Phase = "connected";
        }
        catch (Exception error)
        {
            attempt.Error = error is OperationCanceledException ? "Google sign-in expired. Start it again from Settings." : SafeOAuthError(error);
            attempt.Phase = "failed";
            attempt.Ready.TrySetException(new InvalidOperationException(attempt.Error));
        }
        finally { attempt.ClientSecret = ""; }
    }

    private async Task<McpToolRecord[]> PrepareGoogle(GoogleProduct product, string connectorId, string accessToken,
        CancellationToken cancellation)
    {
        McpToolRecord[] tools;
        using var http = Http(null);
        if (product.Id is "gmail-read" or "gmail")
        {
            await GoogleWorkspaceReadApi.VerifyGmail(http, accessToken, cancellation);
            tools = GoogleWorkspaceReadApi.GmailTools(name => ModelName(connectorId, name));
        }
        else tools = [];
        if (product.Id == "calendar")
        {
            await GoogleWorkspaceReadApi.VerifyCalendar(http, accessToken, cancellation);
            tools = GoogleWorkspaceReadApi.CalendarTools(name => ModelName(connectorId, name));
        }
        if (product.Mode is "send" or "combined")
            tools = [.. tools, GoogleGmailApi.Tool(ModelName(connectorId, GoogleGmailApi.ToolName))];
        if (tools.Length == 0) throw new InvalidOperationException("Google advertised no capability within the reviewed permission scope.");
        return tools;
    }

    private static McpConnectorRecord EffectiveGoogle(McpConnectorRecord connector)
    {
        if (connector.Storage != "oauth") return connector;
        var capabilities = GoogleProduct.Capabilities(connector.Name);
        if (capabilities.Length == 0) return connector;
        var tools = new List<McpToolRecord>();
        if (capabilities.Contains("gmail-read", StringComparer.Ordinal))
            tools.AddRange(GoogleWorkspaceReadApi.GmailTools(name => ModelName(connector.Id, name)));
        if (capabilities.Contains("calendar", StringComparer.Ordinal))
            tools.AddRange(GoogleWorkspaceReadApi.CalendarTools(name => ModelName(connector.Id, name)));
        if (capabilities.Contains("gmail-send", StringComparer.Ordinal))
            tools.Add(GoogleGmailApi.Tool(ModelName(connector.Id, GoogleGmailApi.ToolName)));
        var effective = tools.ToArray();
        return connector with
        {
            Tools = effective,
            Version = ConnectorVersion(connector.Id, connector.Endpoint, connector.Storage, effective, connector.Updated)
        };
    }

    private async Task CommitOAuth(OAuthAttempt attempt, IReadOnlyList<PreparedGoogle> prepared, TokenContainer tokens, string account,
        string[] scopes, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            CheckVersion(attempt.CatalogVersion);
            var catalog = Catalog;
            if (catalog.Scope == "") catalog = catalog with { Scope = Guid.NewGuid().ToString("N") };
            var now = DateTimeOffset.UtcNow;
            var records = prepared.Select(item => new McpConnectorRecord(item.ConnectorId, item.Product.Name, item.Product.Endpoint, "oauth", "pending", now, now, item.Tools,
                ConnectorVersion(item.ConnectorId, item.Product.Endpoint, "oauth", item.Tools, now), Account: account, GrantedScopes: scopes)).ToArray();
            store.Setting(SettingName, Wire.Pack(catalog with { Connectors = [.. catalog.Connectors, .. records] }));
            foreach (var pair in prepared.Zip(records))
            {
                var config = Wire.Pack(new StoredMcpOAuth(pair.First.Product.Endpoint, pair.First.Product.Id, attempt.ClientId, attempt.ClientSecret));
                if (Encoding.UTF8.GetByteCount(config) > 2500) throw new ArgumentException("The Google OAuth client credentials exceed the system credential size limit.");
                await vault.Execute("write", catalog.Scope, OAuthId(pair.Second.Id, "config"), config, cancellation);
                var cache = new VaultOAuthTokenCache(vault, catalog.Scope, pair.Second.Id, oauthTokens);
                await cache.StoreTokensAsync(tokens, cancellation);
                if (await vault.Execute("read", catalog.Scope, OAuthId(pair.Second.Id, "config"), null, cancellation) != config ||
                    await vault.Execute("read", catalog.Scope, OAuthId(pair.Second.Id, "token"), null, cancellation) == null)
                    throw new InvalidOperationException("Google credentials could not be verified in the system credential store.");
            }
            var ready = records.Select(record => record with { Status = "ready" }).ToArray();
            store.Setting(SettingName, Wire.Pack(catalog with { Connectors = [.. catalog.Connectors, .. ready] }));
        }
        finally { gate.Release(); }
    }

    private async Task<ClientOAuthOptions> OAuthOptions(McpConnectorCatalog catalog, McpConnectorRecord connector, Func<AuthorizationCallbackContext, CancellationToken, Task<AuthorizationResult?>>? interactive, CancellationToken cancellation)
    {
        var config = await OAuthConfig(catalog, connector, cancellation);
        var product = GoogleProduct.Find(config.Product);
        return OAuthOptions(product, config.ClientId, config.ClientSecret, new VaultOAuthTokenCache(vault, catalog.Scope, connector.Id, oauthTokens), interactive ?? ((_, _) =>
            throw new InvalidOperationException("Google authorization needs attention. Remove and reconnect this connector in Settings.")));
    }

    private ClientOAuthOptions OAuthOptions(GoogleProduct product, string clientId, string clientSecret, ITokenCache cache,
        Func<AuthorizationCallbackContext, CancellationToken, Task<AuthorizationResult?>> callback) => new()
    {
        RedirectUri = oauthRedirect,
        ClientId = clientId,
        ClientSecret = clientSecret,
        Scopes = product.Scopes,
        ScopeSelector = _ => product.Scopes,
        AuthorizationCallbackHandler = callback,
        AdditionalAuthorizationParameters = new Dictionary<string, string> { ["access_type"] = "offline", ["prompt"] = "consent" },
        TokenCache = cache
    };

    private async Task<StoredMcpOAuth> OAuthConfig(McpConnectorCatalog catalog, McpConnectorRecord connector, CancellationToken cancellation)
    {
        var packed = await vault.Execute("read", catalog.Scope, OAuthId(connector.Id, "config"), null, cancellation)
            ?? throw new InvalidOperationException("The Google OAuth client credential is missing. Disconnect and reconnect this service.");
        StoredMcpOAuth config;
        try { config = Wire.Unpack<StoredMcpOAuth>(packed); }
        catch (JsonException) { throw new InvalidOperationException("The Google OAuth client credential is unreadable. Disconnect and reconnect this service."); }
        if (config.Endpoint != connector.Endpoint) throw new InvalidOperationException("The Google OAuth credential does not match this connector.");
        return config;
    }

    internal static string[] GrantedScopes(string? granted, string[] requested)
    {
        // RFC 6749 section 5.1 permits the token response to omit scope when it
        // is identical to the authorization request. Preserve that exact grant;
        // an explicit response is still authoritative for granular consent.
        var scopes = string.IsNullOrWhiteSpace(granted) ? requested : granted.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return scopes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static bool HasScopes(IEnumerable<string> granted, string[] required)
    {
        var actual = granted.ToHashSet(StringComparer.Ordinal);
        return required.All(scope => actual.Contains(scope) ||
            (scope == "email" && actual.Contains("https://www.googleapis.com/auth/userinfo.email")));
    }

    internal static string[] RequireScopes(string? granted, string[] required)
    {
        var actual = (granted ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        // Google may return the canonical identity URL for the requested OIDC shorthand.
        var missing = required.Where(scope => !actual.Contains(scope) &&
            !(scope == "email" && actual.Contains("https://www.googleapis.com/auth/userinfo.email"))).ToArray();
        if (missing.Length != 0) throw new InvalidOperationException("Google did not grant every requested permission. Disconnect and reconnect, then approve only if the displayed permissions are acceptable.");
        // Report Google's actual grant. The tool catalog still enforces the selected workflow.
        return actual.Order(StringComparer.Ordinal).ToArray();
    }

    private async Task<string> GoogleAccount(string? accessToken, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) throw new InvalidOperationException("Google authorization returned no usable access token.");
        try
        {
            using var http = Http(accessToken);
            using var response = await http.GetAsync("https://openidconnect.googleapis.com/v1/userinfo", cancellation);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Google could not verify the connected account identity.");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var email = body.RootElement.TryGetProperty("email", out var value) ? value.GetString()?.Trim() : null;
            var verified = body.RootElement.TryGetProperty("email_verified", out var proof) && proof.ValueKind == JsonValueKind.True;
            if (!verified || string.IsNullOrWhiteSpace(email) || email.Length > 320)
                throw new InvalidOperationException("Google did not return a verified account identity.");
            return email;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or OperationCanceledException)
        { throw new InvalidOperationException("Google could not verify the connected account identity.", error); }
    }

    private async Task<CapabilityResult> SendGmail(McpConnectorRecord connector, JsonElement arguments, CancellationToken cancellation)
    {
        var config = await OAuthConfig(Catalog, connector, cancellation);
        var product = GoogleProduct.Find(config.Product);
        if (product.Mode is not ("send" or "combined") || connector.Endpoint != product.Endpoint || string.IsNullOrWhiteSpace(connector.Account))
            throw new InvalidOperationException("The reviewed Gmail account identity is unavailable. Disconnect and reconnect Gmail.");
        RequireScopes(string.Join(' ', connector.GrantedScopes ?? []), product.Scopes);
        var accessToken = await GoogleAccessToken(connector, cancellation);
        using var http = Http(null);
        return new(await GoogleGmailApi.Send(http, accessToken, connector.Account, arguments, cancellation));
    }

    private async Task<CapabilityResult> ReadGoogle(McpConnectorRecord connector, string toolName, JsonElement arguments,
        CancellationToken cancellation)
    {
        var config = await OAuthConfig(Catalog, connector, cancellation);
        var product = GoogleProduct.Find(config.Product);
        if (connector.Endpoint != product.Endpoint || string.IsNullOrWhiteSpace(connector.Account))
            throw new InvalidOperationException("The reviewed Google account identity is unavailable. Disconnect and reconnect Google.");
        var gmail = GoogleWorkspaceReadApi.IsGmailTool(toolName);
        if (gmail && product.Id is not ("gmail-read" or "gmail") || !gmail && product.Id != "calendar")
            throw new InvalidOperationException("The reviewed Google read tool does not belong to this connection.");
        var accessToken = await GoogleAccessToken(connector, cancellation);
        using var http = Http(null);
        var value = gmail
            ? await GoogleWorkspaceReadApi.CallGmail(http, accessToken, toolName, arguments, cancellation)
            : await GoogleWorkspaceReadApi.CallCalendar(http, accessToken, toolName, arguments, cancellation);
        if (value.GetRawText().Length > 100_000)
            return new(JsonSerializer.SerializeToElement(new { error = "The Google read result exceeded Thaddeus's 100,000-character review limit." }, Wire.Json), true);
        return new(value);
    }

    private async Task<string> GoogleAccessToken(McpConnectorRecord connector, CancellationToken cancellation)
    {
        var catalog = Catalog;
        var config = await OAuthConfig(catalog, connector, cancellation);
        var product = GoogleProduct.Find(config.Product);
        var cache = new VaultOAuthTokenCache(vault, catalog.Scope, connector.Id, oauthTokens);
        var tokens = await cache.GetTokensAsync(cancellation)
            ?? throw new InvalidOperationException("The Google authorization is missing. Disconnect and reconnect this service.");
        RequireScopes(tokens.Scope, product.Scopes);
        if (!string.IsNullOrWhiteSpace(tokens.AccessToken) && tokens.ExpiresIn is { } remaining &&
            tokens.ObtainedAt.AddSeconds(remaining) > DateTimeOffset.UtcNow.AddMinutes(1)) return tokens.AccessToken;
        if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
            throw new InvalidOperationException("Google did not provide reusable authorization. Disconnect and reconnect this service.");
        using var http = Http(null);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = config.ClientId,
            ["client_secret"] = config.ClientSecret,
            ["refresh_token"] = tokens.RefreshToken,
            ["grant_type"] = "refresh_token"
        });
        HttpResponseMessage response;
        try { response = await http.PostAsync("https://oauth2.googleapis.com/token", content, cancellation); }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException)
        { throw new InvalidOperationException("Google authorization could not be refreshed before dispatch. No Google request was made.", error); }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Google authorization expired or was revoked. No Google request was made; reconnect before continuing.");
            try
            {
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
                var access = body.RootElement.GetProperty("access_token").GetString();
                var expires = body.RootElement.GetProperty("expires_in").GetInt32();
                var tokenType = body.RootElement.TryGetProperty("token_type", out var type) ? type.GetString() ?? "" : "Bearer";
                var scope = body.RootElement.TryGetProperty("scope", out var granted) ? granted.GetString() : tokens.Scope;
                RequireScopes(scope, product.Scopes);
                if (string.IsNullOrWhiteSpace(access) || !string.Equals(tokenType, "Bearer", StringComparison.OrdinalIgnoreCase) || expires is < 60 or > 86400)
                    throw new JsonException();
                var refreshed = new TokenContainer
                {
                    TokenType = tokenType, AccessToken = access, RefreshToken = tokens.RefreshToken, ExpiresIn = expires,
                    Scope = scope, ObtainedAt = DateTimeOffset.UtcNow, ClientId = config.ClientId, ClientSecret = config.ClientSecret,
                    TokenEndpointAuthMethod = tokens.TokenEndpointAuthMethod, AuthorizationServer = tokens.AuthorizationServer
                };
                await cache.StoreTokensAsync(refreshed, cancellation);
                return access;
            }
            catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
            { throw new InvalidOperationException("Google returned an unusable token refresh response. No Google request was made.", error); }
        }
    }

    private HttpClient Http(string? token)
    {
        var http = new HttpClient(handlerFactory?.Invoke() ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(45), MaxResponseContentBufferSize = 1_000_000
        };
        if (!string.IsNullOrEmpty(token)) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private async Task<string?> ReadToken(McpConnectorCatalog catalog, McpConnectorRecord connector, CancellationToken cancellation)
    {
        if (connector.Storage == "none") return null;
        if (connector.Storage == "session")
            return sessionTokens.TryGetValue(connector.Id, out var token) ? token : throw new InvalidOperationException("This connector token was kept only until the host stopped. Remove and reconnect it in Settings.");
        var packed = await vault.Execute("read", catalog.Scope, connector.Id, null, cancellation) ?? throw new InvalidOperationException("The connector token is missing from the system credential store.");
        StoredMcpSecret secret;
        try { secret = Wire.Unpack<StoredMcpSecret>(packed); }
        catch (JsonException) { throw new InvalidOperationException("The stored connector credential is unreadable. Reconnect it in Settings."); }
        if (secret.Endpoint != connector.Endpoint || string.IsNullOrEmpty(secret.Token)) throw new InvalidOperationException("The stored connector credential does not match this endpoint.");
        return secret.Token;
    }

    private void CleanupAttempts()
    {
        lock (oauthAttempts)
        {
            foreach (var id in oauthAttempts.Where(item => DateTimeOffset.UtcNow - item.Value.Created > TimeSpan.FromMinutes(15)).Select(item => item.Key).ToArray())
                oauthAttempts.Remove(id);
        }
    }

    internal static Uri GoogleRedirect(string origin)
    {
        var source = new Uri(origin);
        var loopback = new UriBuilder(source) { Host = "127.0.0.1", Path = "/api/settings/mcp/google/callback" };
        return loopback.Uri;
    }
    private static bool Fixed(string? expected, string? supplied)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(supplied)) return false;
        var left = Encoding.UTF8.GetBytes(expected); var right = Encoding.UTF8.GetBytes(supplied);
        try { return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right); }
        finally { Array.Clear(left); Array.Clear(right); }
    }
    private static string ValidateOAuthValue(string? value, string label, int maximum)
    {
        value = value?.Trim() ?? "";
        if (value.Length is < 1 || value.Length > maximum || value.Any(char.IsControl)) throw new ArgumentException($"Enter the Google OAuth {label} (up to {maximum} characters).");
        return value;
    }
    internal static string OAuthId(string connectorId, string purpose) => Wire.Hash(connectorId + "\n" + purpose)[..32];
    private static string SafeOAuthError(Exception error) => error switch
    {
        InvalidOperationException invalid when invalid.Message.StartsWith("Google ", StringComparison.Ordinal) => invalid.Message,
        ArgumentException argument => argument.Message,
        _ => "Google Workspace could not be connected. Check the Desktop OAuth client, consent screen, enabled API, audience and test-user access, then try again."
    };
    private static string GoogleVerificationFailure(Exception error) => error is InvalidOperationException invalid &&
        (invalid.Message.StartsWith("Gmail ", StringComparison.Ordinal) || invalid.Message.StartsWith("Google Calendar ", StringComparison.Ordinal))
        ? invalid.Message : "service verification failed";

    private sealed record PreparedGoogle(GoogleProduct Product, string ConnectorId, McpToolRecord[] Tools);

    private sealed class OAuthAttempt(string id, GoogleProduct[] products, string clientId, string clientSecret, string catalogVersion)
    {
        public string Id { get; } = id;
        public GoogleProduct[] Products { get; } = products;
        public Dictionary<string, string> ConnectorIds { get; } = products.ToDictionary(product => product.Id, _ => Guid.NewGuid().ToString("N"), StringComparer.Ordinal);
        public string[] Scopes { get; } = products.SelectMany(product => product.Scopes).Distinct(StringComparer.Ordinal).ToArray();
        public string ClientId { get; } = clientId;
        public string ClientSecret { get; set; } = clientSecret;
        public string CatalogVersion { get; } = catalogVersion;
        public DateTimeOffset Created { get; } = DateTimeOffset.UtcNow;
        public string? State { get; set; }
        public string Phase { get; set; } = "starting";
        public string? Error { get; set; }
        public string? Account { get; set; }
        public string[] ConnectedProducts { get; set; } = [];
        public string[] SkippedProducts { get; set; } = [];
        public TaskCompletionSource<object> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<AuthorizationResult> Callback { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record GoogleProduct(string Id, string Name, string Endpoint, string Access, string Mode, string[] Scopes,
        bool Visible = true)
    {
        private static readonly GoogleProduct[] Products =
        [
            new("gmail-read", "Google Gmail — Read mail", "https://gmailmcp.googleapis.com/mcp/v1", "Read mail only", "read",
                ["openid", "email", "https://www.googleapis.com/auth/gmail.readonly"]),
            new("gmail-send", "Google Gmail — Send approved messages", "https://gmailmcp.googleapis.com/mcp/v1", "Send exact approved messages only", "send",
                ["openid", "email", "https://www.googleapis.com/auth/gmail.send"]),
            new("calendar", "Google Calendar", "https://calendarmcp.googleapis.com/mcp/v1", "Read calendars, events and free/busy", "read",
                ["openid", "email", "https://www.googleapis.com/auth/calendar.calendarlist.readonly", "https://www.googleapis.com/auth/calendar.events.freebusy", "https://www.googleapis.com/auth/calendar.events.readonly"]),
            // Existing pre-split connections remain refreshable; new setup never offers this broader permission set.
            new("gmail", "Google Gmail", "https://gmailmcp.googleapis.com/mcp/v1", "Read mail and send exact approved messages", "combined",
                ["openid", "email", "https://www.googleapis.com/auth/gmail.readonly", "https://www.googleapis.com/auth/gmail.send"], Visible: false)
        ];
        public static object[] Views(McpConnectorRecord[] connectors) => Products.Where(item => item.Visible)
            .Select(item => (object)new
            {
                id = item.Id, name = item.Name.Replace("Google ", ""), access = item.Access, scopes = item.Scopes,
                connected = connectors.Any(connector => connector.Status == "ready" && string.Equals(connector.Name, item.Name, StringComparison.OrdinalIgnoreCase))
            }).ToArray();
        public static GoogleProduct Find(string? id) => Products.SingleOrDefault(item => item.Id == id)
            ?? throw new ArgumentException("Choose Gmail or Google Calendar.");
        public static GoogleProduct[] FindMany(IEnumerable<string?> ids)
        {
            var selected = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Select(Find).DistinctBy(product => product.Id).ToArray();
            if (selected.Length == 0) throw new ArgumentException("Choose at least one Google permission.");
            if (selected.Length > 3) throw new ArgumentException("Choose no more than the available Google permissions.");
            return selected;
        }
        public static string[] Capabilities(string name)
        {
            var product = Products.SingleOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            return product?.Mode == "combined" ? ["gmail-read", "gmail-send"] : product == null ? [] : [product.Id];
        }
    }


    private sealed class VaultOAuthTokenCache(ICredentialVault vault, string scope, string connectorId, Dictionary<string, TokenContainer> memory) : ITokenCache
    {
        private readonly SemaphoreSlim cacheGate = new(1, 1);
        public async ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
        {
            var durable = new TokenContainer
            {
                TokenType = tokens.TokenType, AccessToken = "", RefreshToken = tokens.RefreshToken, ExpiresIn = 0, Scope = tokens.Scope,
                ObtainedAt = DateTimeOffset.UnixEpoch, ClientId = tokens.ClientId, ClientSecret = tokens.ClientSecret,
                TokenEndpointAuthMethod = tokens.TokenEndpointAuthMethod, AuthorizationServer = tokens.AuthorizationServer
            };
            var packed = Wire.Pack(durable);
            if (Encoding.UTF8.GetByteCount(packed) > 2500) throw new InvalidOperationException("The reusable Google OAuth credential exceeds the system credential size limit.");
            await vault.Execute("write", scope, OAuthId(connectorId, "token"), packed, cancellationToken);
            lock (memory) memory[connectorId] = tokens;
        }
        public async ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
        {
            TokenContainer? current;
            lock (memory) if (memory.TryGetValue(connectorId, out current)) return current;
            await cacheGate.WaitAsync(cancellationToken);
            try
            {
                lock (memory) if (memory.TryGetValue(connectorId, out current)) return current;
                var packed = await vault.Execute("read", scope, OAuthId(connectorId, "token"), null, cancellationToken);
                if (packed == null) return null;
                try { return Wire.Unpack<TokenContainer>(packed); }
                catch (JsonException) { throw new InvalidOperationException("The stored Google OAuth token is unreadable. Remove and reconnect this connector."); }
            }
            finally { cacheGate.Release(); }
        }
    }

    private bool InUse(string id) => store.List().Any(run => run.ConnectedTools.Any(tool => tool.ConnectorId == id) &&
        run.State is RunState.Queued or RunState.Running or RunState.AwaitingApproval or RunState.Paused) ||
        store.DelegationJobs().Any(job => job.Kind is "email" or "brief" &&
            job.State is "scheduled" or "working" or "paused" or "needs-approval" && UsesConnector(job, id));

    private static bool UsesConnector(DelegationJob job, string id)
    {
        try
        {
            if (job.Kind == "email") return job.Action.Payload.Deserialize<ScheduledEmailPayload>(Wire.Json)?.Tool.ConnectorId == id;
            if (job.Kind == "brief")
            {
                var brief = job.Action.Payload.Deserialize<ScheduledBriefPayload>(Wire.Json);
                return brief?.Email.Tool.ConnectorId == id || brief?.Calendar.Tool.ConnectorId == id;
            }
            return false;
        }
        catch (JsonException) { return false; }
    }
    private void CheckVersion(string version) { if (version != Version) throw new InvalidOperationException("Connector settings changed. Refresh before saving."); }
    private static string ValidateName(string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl)) throw new ArgumentException("Enter a connector name between 1 and 80 characters.");
        return name;
    }
    private static string? ValidateToken(string storage, string? token)
    {
        if (storage == "none")
        {
            if (!string.IsNullOrEmpty(token)) throw new ArgumentException("Choose credential storage before entering a token.");
            return null;
        }
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048 || token.Any(c => c is < '!' or > '~'))
            throw new ArgumentException("Enter a bearer token using visible ASCII characters without whitespace, up to 2,048 characters.");
        return token;
    }
    public static Uri Endpoint(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new ArgumentException("MCP requires HTTPS, or HTTP on loopback, without embedded credentials, query strings, or fragments.");
        return uri;
    }
    private static string ModelName(string id, string remote)
    {
        var safe = new string(remote.ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_').ToArray()).Trim('_');
        if (safe.Length == 0) safe = "tool";
        var prefix = "mcp_" + id[..8] + "_";
        return prefix + safe[..Math.Min(safe.Length, 64 - prefix.Length)];
    }
    private static string Effect(JsonElement protocol)
    {
        if (!protocol.TryGetProperty("annotations", out var annotations) || annotations.ValueKind != JsonValueKind.Object) return "write or external action";
        if (annotations.TryGetProperty("destructiveHint", out var destructive) && destructive.ValueKind == JsonValueKind.True) return "potentially destructive external action";
        if (annotations.TryGetProperty("readOnlyHint", out var readOnly) && readOnly.ValueKind == JsonValueKind.True) return "read external data";
        return "write or external action";
    }
    private static string ConnectorVersion(string id, string endpoint, string storage, McpToolRecord[] tools, DateTimeOffset updated) =>
        Wire.Hash(Wire.Pack(new { id, endpoint, storage, tools, updated }));
}
