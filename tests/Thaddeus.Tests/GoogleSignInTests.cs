using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class GoogleSignInTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-google-signin-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    private readonly Vault vault = new();
    private readonly List<string> requests = [];
    private Uri? consent;
    private string? fixtureError;
    private string? tokenScope = "openid https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/gmail.readonly";
    private string? refreshToken = "fixture-refresh";
    private const string Credentials = """{"installed":{"client_id":"fixture.apps.googleusercontent.com","client_secret":"fixture-secret","auth_uri":"https://accounts.google.com/o/oauth2/auth","token_uri":"https://oauth2.googleapis.com/token"}}""";

    public GoogleSignInTests() => store = new(root);

    private sealed class Vault : ICredentialVault
    {
        public Dictionary<(string, string), string> Values { get; } = [];
        public Task<string?> Execute(string operation, string scope, string id, string? value, CancellationToken cancellation)
        {
            if (operation == "write") Values[(scope, id)] = value!;
            if (operation == "forget") Values.Remove((scope, id));
            return Task.FromResult(operation == "read" ? Values.GetValueOrDefault((scope, id)) : null);
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) => send(request, cancellation);
    }

    private McpConnections Connections() => new(store, vault, handlerFactory: () => new Handler(async (request, cancellation) =>
    {
        try { return await Respond(request, cancellation); }
        catch (Exception error) { fixtureError = error.ToString(); throw; }
    }), openGoogleBrowser: uri => consent = uri);
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, Wire.Json);
    private async Task<JsonElement> Start(McpConnections connections, string[]? products = null)
    {
        await connections.ImportGoogleClient(new(Wire.Hash(store.Setting("mcp-connectors") ?? ""), Credentials), default);
        return Json(await connections.BeginGoogle(new(Wire.Hash(store.Setting("mcp-connectors")!), Products: products ?? ["gmail-read"]), default));
    }
    private async Task<JsonElement> Finished(McpConnections connections, string id)
    {
        for (var count = 0; count < 250; count++)
        {
            var status = Json(connections.GoogleStatus(id));
            if (status.GetProperty("phase").GetString() is "connected" or "failed") return status;
            await Task.Delay(20);
        }
        throw new TimeoutException("The fictional Google sign-in did not finish.");
    }

    private async Task<HttpResponseMessage> Respond(HttpRequestMessage request, CancellationToken cancellation)
    {
        var uri = request.RequestUri!.AbsoluteUri;
        requests.Add(uri);
        if (uri == "https://oauth2.googleapis.com/token")
        {
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellation));
            Assert.Equal("fixture-code", form["code"].ToString());
            Assert.Equal("authorization_code", form["grant_type"].ToString());
            Assert.Equal("http://127.0.0.1:5179/api/settings/mcp/google/callback", form["redirect_uri"].ToString());
            var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"].ToString())));
            Assert.Equal(QueryHelpers.ParseQuery(consent!.Query)["code_challenge"].ToString(), challenge);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = "fixture-access", refresh_token = refreshToken, scope = tokenScope, token_type = "Bearer", expires_in = 3600 }) };
        }
        if (uri == "https://openidconnect.googleapis.com/v1/userinfo")
        {
            Assert.Equal("Bearer fixture-access", request.Headers.Authorization?.ToString());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { email = "owner@example.invalid", email_verified = true }) };
        }
        Assert.Equal("https://gmailmcp.googleapis.com/mcp/v1", uri);
        if (request.Method == HttpMethod.Delete) return new(HttpStatusCode.OK);
        using var message = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
        // Google's public catalogue does not challenge anonymous clients. A butler must still ask who is at the door.
        if (!message.RootElement.TryGetProperty("id", out var id)) return new(HttpStatusCode.Accepted);
        if (message.RootElement.GetProperty("method").GetString() == "server/discover")
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { jsonrpc = "2.0", id = id.Clone(), error = new { code = -32601, message = "Method not found" } }) };
        object result = message.RootElement.GetProperty("method").GetString() switch
        {
            "initialize" => new { protocolVersion = "2025-11-25", capabilities = new { tools = new { } }, serverInfo = new { name = "public-google-fixture", version = "1" } },
            "tools/list" => new { tools = new[] { new { name = "search_threads", description = "Search mail", inputSchema = new { type = "object" }, annotations = new { readOnlyHint = true } } } },
            _ => throw new InvalidOperationException("Unexpected fixture operation: " + message.RootElement.GetProperty("method").GetString())
        };
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { jsonrpc = "2.0", id = id.Clone(), result }) };
    }

    [Fact]
    public async Task ExplicitConnectOpensConsentEvenWhenMcpDiscoveryIsAnonymous()
    {
        var connections = Connections();
        var started = await Start(connections);
        Assert.NotNull(consent);
        var query = QueryHelpers.ParseQuery(consent.Query);
        Assert.Equal("accounts.google.com", consent.Host);
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.Equal("offline", query["access_type"].ToString());
        Assert.Equal("consent", query["prompt"].ToString());
        Assert.DoesNotContain("gmail.send", query["scope"].ToString());
        Assert.DoesNotContain("fixture-secret", consent.AbsoluteUri);
        Assert.Empty(requests);
        Assert.Throws<ArgumentException>(() => connections.CompleteGoogle("fixture-code", "wrong", null, null));
        Assert.Throws<ArgumentException>(() => connections.CompleteGoogle("fixture-code", query["state"], "https://attacker.invalid", null));
        connections.CompleteGoogle("fixture-code", query["state"], "https://accounts.google.com", null);
        Assert.Throws<ArgumentException>(() => connections.CompleteGoogle("fixture-code", query["state"], null, null));
        var status = await Finished(connections, started.GetProperty("attemptId").GetString()!);
        Assert.True(status.GetProperty("phase").GetString() == "connected", $"Status: {status}; fixture failure: {fixtureError}; requests: {string.Join(", ", requests)}");
        Assert.Single(connections.Snapshot());
        var view = Json(await connections.View());
        Assert.Equal("owner@example.invalid", view.GetProperty("connectors")[0].GetProperty("account").GetString());
        Assert.Contains("https://www.googleapis.com/auth/userinfo.email", view.GetProperty("connectors")[0].GetProperty("grantedScopes").EnumerateArray().Select(scope => scope.GetString()));
        Assert.DoesNotContain("fixture-access", store.Setting("mcp-connectors")!);
        Assert.DoesNotContain("fixture-refresh", store.Setting("mcp-connectors")!);
        Assert.Contains(vault.Values.Values, value => value.Contains("fixture-refresh"));
    }

    [Fact]
    public async Task OmittedTokenScopeMeansTheExactRequestedGrant()
    {
        tokenScope = null;
        var connections = Connections(); var started = await Start(connections);
        connections.CompleteGoogle("fixture-code", QueryHelpers.ParseQuery(consent!.Query)["state"], null, null);
        var status = await Finished(connections, started.GetProperty("attemptId").GetString()!);
        Assert.Equal("connected", status.GetProperty("phase").GetString());
        var connector = Json(await connections.View()).GetProperty("connectors").EnumerateArray().Single();
        Assert.Contains("https://www.googleapis.com/auth/gmail.readonly",
            connector.GetProperty("grantedScopes").EnumerateArray().Select(scope => scope.GetString()));
        Assert.Single(connections.Snapshot());
    }

    [Fact]
    public async Task OneConsentRetainsTheUsableSubsetWhenOnePermissionIsDeclined()
    {
        tokenScope = "openid https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/gmail.readonly";
        var connections = Connections(); var started = await Start(connections, ["gmail-read", "gmail-send"]);
        var query = QueryHelpers.ParseQuery(consent!.Query);
        Assert.Contains("gmail.readonly", query["scope"].ToString());
        Assert.Contains("gmail.send", query["scope"].ToString());
        connections.CompleteGoogle("fixture-code", query["state"], null, null);
        var status = await Finished(connections, started.GetProperty("attemptId").GetString()!);
        Assert.Equal("connected", status.GetProperty("phase").GetString());
        Assert.Equal(["gmail-read"], status.GetProperty("connectedProducts").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("Send approved messages", status.GetProperty("skippedProducts")[0].GetString());
        var connector = Json(await connections.View()).GetProperty("connectors").EnumerateArray().Single();
        Assert.Equal("Google Gmail — Read mail", connector.GetProperty("name").GetString());
        Assert.Equal("owner@example.invalid", connector.GetProperty("account").GetString());
    }

    [Fact]
    public async Task OneConsentCreatesMultipleSelectedGoogleConnections()
    {
        tokenScope = "openid https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/gmail.send";
        var connections = Connections(); var started = await Start(connections, ["gmail-read", "gmail-send"]);
        var query = QueryHelpers.ParseQuery(consent!.Query);
        connections.CompleteGoogle("fixture-code", query["state"], null, null);
        var status = await Finished(connections, started.GetProperty("attemptId").GetString()!);
        Assert.Equal("connected", status.GetProperty("phase").GetString());
        Assert.Equal(2, status.GetProperty("connectedProducts").GetArrayLength());
        Assert.Empty(status.GetProperty("skippedProducts").EnumerateArray());
        var view = Json(await connections.View());
        Assert.Equal(2, view.GetProperty("connectors").GetArrayLength());
        Assert.Contains(connections.Snapshot(), tool => tool.RemoteName == GoogleGmailApi.ToolName);
        Assert.Contains(connections.Snapshot(), tool => tool.Effect == "read external data");
    }

    [Fact]
    public async Task DeniedConsentDoesNotExchangeTokensOrCreateAConnection()
    {
        var connections = Connections(); var started = await Start(connections);
        connections.CompleteGoogle(null, QueryHelpers.ParseQuery(consent!.Query)["state"], null, "access_denied");
        var status = await Finished(connections, started.GetProperty("attemptId").GetString()!);
        Assert.Equal("failed", status.GetProperty("phase").GetString());
        Assert.Empty(requests); Assert.Empty(connections.Snapshot());
        Assert.DoesNotContain(vault.Values.Values, value => value.Contains("fixture-refresh"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PartialConsentOrMissingRefreshTokenDoesNotRegisterTools(bool partial)
    {
        if (partial) tokenScope = "openid email"; else refreshToken = null;
        var connections = Connections(); var started = await Start(connections);
        connections.CompleteGoogle("fixture-code", QueryHelpers.ParseQuery(consent!.Query)["state"], null, null);
        var status = await Finished(connections, started.GetProperty("attemptId").GetString()!);
        Assert.Equal("failed", status.GetProperty("phase").GetString());
        Assert.Empty(connections.Snapshot());
        Assert.Equal(partial ? 2 : 1, requests.Count); Assert.Empty(Json(await connections.View()).GetProperty("connectors").EnumerateArray());
        Assert.DoesNotContain(vault.Values.Values, value => value.Contains("fixture-refresh"));
    }

    public void Dispose()
    {
        store.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
