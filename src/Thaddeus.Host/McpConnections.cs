using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public sealed record McpToolRecord(string RemoteName, string ModelName, string Description, JsonElement InputSchema, string Effect);
public sealed record McpConnectorRecord(string Id, string Name, string Endpoint, string Storage, string Status,
    DateTimeOffset Created, DateTimeOffset Updated, McpToolRecord[] Tools, string Version, bool Enabled = true);
public sealed record McpConnectorCatalog(string Scope, McpConnectorRecord[] Connectors);
public sealed record StoredMcpSecret(string Endpoint, string Token);
public sealed record McpConnectorEdit(string Version, string Name, string Endpoint, string Storage, string? Token = null)
{
    public override string ToString() => "MCP connector edit (credential omitted)";
}
public sealed record McpConnectorChange(string Version);

/// <summary>Owns external MCP discovery, credentials and dispatch outside the model process.</summary>
public sealed class McpConnections(Store store, ICredentialVault vault) : IConnectedToolBroker
{
    private const string SettingName = "mcp-connectors";
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, string> sessionTokens = [];
    private McpConnectorCatalog Catalog => store.Setting(SettingName) is { } value
        ? Wire.Unpack<McpConnectorCatalog>(value) : new("", []);
    private string Version => Wire.Hash(store.Setting(SettingName) ?? "");

    public ConnectedToolDefinition[] Snapshot()
    {
        var catalog = Catalog;
        return catalog.Connectors
            .Where(connector => connector.Enabled && connector.Status == "ready" && (connector.Storage != "session" || sessionTokens.ContainsKey(connector.Id)))
            .SelectMany(connector => connector.Tools.Select(tool => new ConnectedToolDefinition(connector.Id, connector.Name,
                tool.RemoteName, tool.ModelName, tool.Description, tool.InputSchema, tool.Effect, connector.Version)))
            .Take(64).ToArray();
    }

    public async Task<object> View(CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var catalog = Catalog;
            return new
            {
                version = Version,
                systemStore = NativeCredentialVault.Name,
                connectors = catalog.Connectors.Select(connector => new
                {
                    connector.Id, connector.Name, connector.Endpoint, connector.Storage, connector.Status,
                    connector.Created, connector.Updated, connector.Enabled, connector.Tools,
                    needsReentry = connector.Storage == "session" && !sessionTokens.ContainsKey(connector.Id),
                    inUse = InUse(connector.Id)
                }).ToArray()
            };
        }
        finally { gate.Release(); }
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
            token = await ReadToken(Catalog, connector, cancellation);
        }
        finally { gate.Release(); }
        McpToolRecord[] tools;
        try { tools = await Discover(connector.Id, connector.Name, connector.Endpoint, token, cancellation); }
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
            if (InUse(id)) throw new InvalidOperationException("Finish or cancel work using this connector before removing it.");
            sessionTokens.Remove(id);
            if (connector.Storage == "system")
            {
                await vault.Execute("forget", catalog.Scope, id, null, cancellation);
                if (await vault.Execute("read", catalog.Scope, id, null, cancellation) != null)
                    throw new InvalidOperationException("Credential removal is not confirmed. The connector remains registered for inspection.");
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
            connector = catalog.Connectors.SingleOrDefault(item => item.Id == tool.ConnectorId) ?? throw new InvalidOperationException("The reviewed connector was removed before dispatch.");
            if (!connector.Enabled || connector.Status != "ready" || connector.Version != tool.ConnectionVersion)
                throw new InvalidOperationException("The connector changed after review. Start a new request so its tools can be reviewed again.");
            var current = connector.Tools.SingleOrDefault(item => item.RemoteName == tool.RemoteName && item.ModelName == tool.ModelName);
            if (current == null || current.InputSchema.GetRawText() != tool.InputSchema.GetRawText())
                throw new InvalidOperationException("The connector tool changed after review. No request was sent.");
            token = await ReadToken(catalog, connector, cancellation);
        }
        finally { gate.Release(); }

        try
        {
            using var http = Http(token);
            await using var transport = new HttpClientTransport(new() { Endpoint = new(connector.Endpoint), Name = connector.Name, EnableStandaloneGetStream = false, ConnectionTimeout = TimeSpan.FromSeconds(15) }, http);
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellation);
            var values = JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments.GetRawText(), Wire.Json) ?? [];
            var result = await client.CallToolAsync(tool.RemoteName, values, cancellationToken: cancellation);
            var encoded = JsonSerializer.SerializeToElement(result, Wire.Json);
            if (encoded.GetRawText().Length > 100_000) return new(JsonSerializer.SerializeToElement(new { error = "The connector result exceeded Thaddeus's 100,000-character review limit." }, Wire.Json), true);
            return new(encoded, result.IsError ?? false);
        }
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
        return tools.ToArray();
    }

    private static HttpClient Http(string? token)
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false })
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
