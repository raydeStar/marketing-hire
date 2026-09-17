using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using ModelContextProtocol.Authentication;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class McpDelegationConnectionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-mcp-delegation-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;

    public McpDelegationConnectionTests() => store = new(root);

    private sealed class Vault : ICredentialVault
    {
        public Dictionary<(string Scope, string Id), string> Values { get; } = [];
        public Task<string?> Execute(string operation, string scope, string id, string? value, CancellationToken cancellation)
        {
            if (operation == "write") Values[(scope, id)] = value!;
            if (operation == "forget") Values.Remove((scope, id));
            return Task.FromResult(operation == "read" ? Values.GetValueOrDefault((scope, id)) : null);
        }
    }

    private sealed class Dispatcher : IDelegationDispatcher
    {
        public Task<DelegationDispatchResult> Dispatch(DelegationJob job, DelegationOccurrence occurrence, CancellationToken cancellation) =>
            Task.FromResult(new DelegationDispatchResult("accepted", "Fixture accepted.", true));
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    [Fact]
    public async Task DisconnectRevokesConnectorImmediatelyAndPreventsPendingEmailDispatch()
    {
        var schema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { to = new { type = "string" }, body = new { type = "string" } }, required = new[] { "to", "body" } });
        var record = new McpConnectorRecord("mail-id", "Owner test mail", "https://mail.invalid/mcp", "none", "ready",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            [new("send_email", "mcp_mail_send_email", "Send an email message.", schema, "write or external action")], "connector-v1");
        var packed = Wire.Pack(new McpConnectorCatalog("fixture-scope", [record]));
        store.Setting("mcp-connectors", packed);
        var connections = new McpConnections(store, new Vault());
        var tool = Assert.Single(connections.Snapshot());
        var payload = new ScheduledEmailPayload(tool,
            JsonSerializer.SerializeToElement(new { to = "owner@example.invalid", body = "Fixture only." }),
            tool.ConnectorName, "owner@example.invalid", null, "Fixture only.");
        var scheduler = new DelegationScheduler(store, new Dispatcher());
        var created = scheduler.CreateEmail(new(payload, DateTimeOffset.UtcNow.AddHours(2), TimeZoneInfo.Local.Id));
        var version = Wire.Hash(packed);

        var view = JsonSerializer.SerializeToElement(await connections.View(), Wire.Json);
        Assert.True(view.GetProperty("connectors")[0].GetProperty("inUse").GetBoolean());
        await connections.Forget(record.Id, new(version), default);
        Assert.Empty(connections.Snapshot());
        var email = new ConnectedEmailDelegationDispatcher(connections);
        var occurrence = new DelegationOccurrence("occurrence", 0, created.Job.Id, created.Job.ScheduleVersion, 1,
            created.Job.NextRunUtc!.Value, "working", "operation", "intent-recorded", "claim", DateTimeOffset.UtcNow, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => email.Dispatch(created.Job, occurrence, default));
    }

    [Fact]
    public async Task GmailSendRefreshesInHostMemoryThenUsesTheSupportedSendEndpoint()
    {
        var vault = new Vault(); const string scope = "fixture-google-scope"; const string connectorId = "google-gmail-id";
        var scopes = new[] { "openid", "email", "https://www.googleapis.com/auth/gmail.readonly", "https://www.googleapis.com/auth/gmail.send" };
        var toolRecord = GoogleGmailApi.Tool("mcp_google_gmail_send");
        var record = new McpConnectorRecord(connectorId, "Google Gmail", "https://gmailmcp.googleapis.com/mcp/v1", "oauth", "ready",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [toolRecord], "connection-v1", Account: "sender@example.com", GrantedScopes: scopes);
        store.Setting("mcp-connectors", Wire.Pack(new McpConnectorCatalog(scope, [record])));
        vault.Values[(scope, McpConnections.OAuthId(connectorId, "config"))] = Wire.Pack(new StoredMcpOAuth(record.Endpoint, "gmail", "desktop-client", "desktop-secret"));
        vault.Values[(scope, McpConnections.OAuthId(connectorId, "token"))] = Wire.Pack(new TokenContainer
        {
            TokenType = "Bearer", AccessToken = "", RefreshToken = "refresh-fixture", ExpiresIn = 0,
            Scope = string.Join(' ', scopes), ObtainedAt = DateTimeOffset.UnixEpoch, ClientId = "desktop-client", ClientSecret = "desktop-secret"
        });
        var requests = new List<string>();
        var connections = new McpConnections(store, vault, handlerFactory: () => new Handler(async (request, cancellation) =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            if (request.RequestUri.AbsoluteUri == "https://oauth2.googleapis.com/token")
            {
                var form = await request.Content!.ReadAsStringAsync(cancellation);
                Assert.Contains("refresh_token=refresh-fixture", form); Assert.DoesNotContain("sender@example.com", form);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = "access-fixture", expires_in = 3600, token_type = "Bearer", scope = string.Join(' ', scopes) }) };
            }
            Assert.Equal("Bearer access-fixture", request.Headers.Authorization!.ToString());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = "sent-1", threadId = "thread-1", labelIds = new[] { "SENT" } }) };
        }));
        var tool = Assert.Single(connections.Snapshot());
        var result = await connections.Call(tool, JsonSerializer.SerializeToElement(new { to = "recipient@example.com", subject = "Reviewed", body = "Exact body." }), default);
        Assert.False(result.IsError); Assert.Equal("sent-1", result.Value.GetProperty("providerMessageId").GetString());
        Assert.Equal(["https://oauth2.googleapis.com/token", "https://gmail.googleapis.com/gmail/v1/users/me/messages/send"], requests);
        Assert.DoesNotContain("access-fixture", store.Setting("mcp-connectors")!);
    }

    [Fact]
    public async Task RevokedRefreshAndPartialConsentCannotReachGmailSend()
    {
        Assert.Throws<InvalidOperationException>(() => McpConnections.RequireScopes("openid email https://www.googleapis.com/auth/gmail.readonly",
            ["openid", "email", "https://www.googleapis.com/auth/gmail.readonly", "https://www.googleapis.com/auth/gmail.send"]));
        var vault = new Vault(); const string scope = "fixture-revoked-scope"; const string connectorId = "revoked-gmail-id";
        var scopes = new[] { "openid", "email", "https://www.googleapis.com/auth/gmail.readonly", "https://www.googleapis.com/auth/gmail.send" };
        var toolRecord = GoogleGmailApi.Tool("mcp_google_gmail_send");
        var record = new McpConnectorRecord(connectorId, "Google Gmail", "https://gmailmcp.googleapis.com/mcp/v1", "oauth", "ready",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [toolRecord], "connection-v1", Account: "sender@example.com", GrantedScopes: scopes);
        store.Setting("mcp-connectors", Wire.Pack(new McpConnectorCatalog(scope, [record])));
        vault.Values[(scope, McpConnections.OAuthId(connectorId, "config"))] = Wire.Pack(new StoredMcpOAuth(record.Endpoint, "gmail", "desktop-client", "desktop-secret"));
        vault.Values[(scope, McpConnections.OAuthId(connectorId, "token"))] = Wire.Pack(new TokenContainer
        {
            TokenType = "Bearer", AccessToken = "", RefreshToken = "revoked", ExpiresIn = 0,
            Scope = string.Join(' ', scopes), ObtainedAt = DateTimeOffset.UnixEpoch, ClientId = "desktop-client", ClientSecret = "desktop-secret"
        });
        var calls = 0;
        var connections = new McpConnections(store, vault, handlerFactory: () => new Handler((request, _) =>
        {
            calls++; Assert.Equal("https://oauth2.googleapis.com/token", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        }));
        var tool = Assert.Single(connections.Snapshot());
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.Call(tool,
            JsonSerializer.SerializeToElement(new { to = "recipient@example.com", subject = "Blocked", body = "No send." }), default));
        Assert.Equal(1, calls);
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
