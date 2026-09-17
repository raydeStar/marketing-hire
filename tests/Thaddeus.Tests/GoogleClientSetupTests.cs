using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class GoogleClientSetupTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-google-setup-" + Guid.NewGuid().ToString("N"));
    private Store store;
    private readonly Vault vault = new();
    internal const string Secret = "fictional-browser-secret";
    internal static readonly string Credentials = JsonSerializer.Serialize(new { installed = new {
        client_id = "123-fictional.apps.googleusercontent.com", client_secret = Secret,
        auth_uri = "https://accounts.google.com/o/oauth2/auth", token_uri = "https://oauth2.googleapis.com/token",
        project_id = "fictional-test-project", redirect_uris = new[] { "http://localhost" }
    }});
    public GoogleClientSetupTests() => store = new(root);
    private sealed class Vault : ICredentialVault
    {
        public Dictionary<(string, string), string> Values { get; } = [];
        public bool Locked { get; set; }
        public Task<string?> Execute(string operation, string scope, string id, string? value, CancellationToken cancellation)
        {
            if (Locked) throw ProcessCredentialVault.Unavailable();
            Assert.True(NativeCredentialVault.Identifier(scope)); Assert.True(NativeCredentialVault.Identifier(id));
            if (operation == "write") Values[(scope, id)] = value!;
            if (operation == "forget") Values.Remove((scope, id));
            return Task.FromResult(operation == "read" ? Values.GetValueOrDefault((scope, id)) : null);
        }
    }
    private static async Task<JsonElement> View(McpConnections connections) => JsonSerializer.SerializeToElement(await connections.View(), Wire.Json);

    [Fact]
    public async Task ImportSurvivesRestartAndOnlyVaultContainsCredentials()
    {
        var connections = new McpConnections(store, vault);
        var before = await View(connections);
        Assert.False(before.GetProperty("google").GetProperty("clientSetup").GetProperty("configured").GetBoolean());
        await connections.ImportGoogleClient(new(before.GetProperty("version").GetString()!, Credentials), default);
        store.Dispose(); store = new(root);
        var restarted = new McpConnections(store, vault);
        var after = await View(restarted);
        Assert.True(after.GetProperty("google").GetProperty("clientSetup").GetProperty("configured").GetBoolean());
        var catalog = Wire.Unpack<McpConnectorCatalog>(store.Setting("mcp-connectors")!);
        var client = await restarted.ReadGoogleClient(catalog, default);
        Assert.Equal("123-fictional.apps.googleusercontent.com", client!.ClientId);
        Assert.Equal(Secret, client.ClientSecret);
        Assert.Contains(Secret, Assert.Single(vault.Values).Value);
        Assert.DoesNotContain(Secret, after.GetRawText() + store.Setting("mcp-connectors") + Wire.Pack(store.AllEvents()) + Wire.Pack(store.Chats()));
        Assert.DoesNotContain("123-fictional", after.GetRawText());
        Assert.DoesNotContain(Secret, new GoogleClientImport("v", Credentials).ToString() + client.ToString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.ImportGoogleClient(new(before.GetProperty("version").GetString()!, Credentials), default));
        await restarted.ForgetGoogleClient(new(after.GetProperty("version").GetString()!), default);
        Assert.Empty(vault.Values);
        Assert.False((await View(restarted)).GetProperty("google").GetProperty("clientSetup").GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task MissingOrLockedSetupCannotStartConsentOrAnyNetworkRequest()
    {
        var calls = 0;
        var connections = new McpConnections(store, vault, handlerFactory: () => { calls++; throw new InvalidOperationException(); });
        var before = await View(connections);
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.BeginGoogle(new(before.GetProperty("version").GetString()!, "gmail-read"), default));
        await connections.ImportGoogleClient(new(before.GetProperty("version").GetString()!, Credentials), default);
        vault.Locked = true;
        var locked = await View(connections);
        Assert.Equal("unavailable", locked.GetProperty("google").GetProperty("clientSetup").GetProperty("status").GetString());
        Assert.False(locked.GetProperty("google").GetProperty("clientSetup").GetProperty("configured").GetBoolean());
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.BeginGoogle(new(locked.GetProperty("version").GetString()!, "calendar"), default));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task RemovingAppSetupPreservesExistingAccountCredentials()
    {
        var scope = Guid.NewGuid().ToString("N"); var connectorId = Guid.NewGuid().ToString("N");
        var connector = new McpConnectorRecord(connectorId, "Existing Google account", "https://gmailmcp.googleapis.com/mcp/v1", "oauth", "ready", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], "v1");
        store.Setting("mcp-connectors", Wire.Pack(new McpConnectorCatalog(scope, [connector])));
        vault.Values[(scope, McpConnections.OAuthId(connectorId, "config"))] = "existing-app-config";
        vault.Values[(scope, McpConnections.OAuthId(connectorId, "token"))] = "existing-refresh-token";
        var connections = new McpConnections(store, vault);
        await connections.ImportGoogleClient(new((await View(connections)).GetProperty("version").GetString()!, Credentials), default);
        await connections.ForgetGoogleClient(new((await View(connections)).GetProperty("version").GetString()!), default);
        Assert.Equal(2, vault.Values.Count);
        Assert.Equal("existing-refresh-token", vault.Values[(scope, McpConnections.OAuthId(connectorId, "token"))]);
        Assert.Equal(connector.Id, Assert.Single(Wire.Unpack<McpConnectorCatalog>(store.Setting("mcp-connectors")!).Connectors).Id);
    }

    [Theory]
    [InlineData("{\"web\":{}}")]
    [InlineData("{\"type\":\"service_account\"}")]
    [InlineData("{\"installed\":{}}")]
    [InlineData("[1,2,3]")]
    [InlineData("not-json")]
    public async Task InvalidFilesNeverReachTheVault(string input)
    {
        var connections = new McpConnections(store, vault);
        await Assert.ThrowsAsync<ArgumentException>(() => connections.ImportGoogleClient(new(Wire.Hash(""), input), default));
        Assert.Empty(vault.Values); Assert.Null(store.Setting("mcp-connectors"));
    }

    [Fact]
    public void ImportedUrlsCannotRedirectCredentialsAndErrorsDoNotEchoInput()
    {
        var error = Assert.Throws<ArgumentException>(() => McpConnections.ParseGoogleClient(Credentials.Replace("https://oauth2.googleapis.com/token", "https://attacker.invalid/" + Secret)));
        Assert.DoesNotContain(Secret, error.Message);
        Assert.Throws<ArgumentException>(() => McpConnections.ParseGoogleClient(new string('x', 16_385)));
        Assert.Throws<ArgumentException>(() => McpConnections.ParseGoogleClient("{\"installed\":{\"client_id\":\"" + Secret));
    }

    [Fact]
    public void BrowserLaunchIsLimitedToGoogleAndFailureAllowsAnExplicitFallback()
    {
        Uri? opened = null;
        Assert.True(McpConnections.OpenGoogleBrowser(new("https://accounts.google.com/o/oauth2/auth?state=fixture"), uri => opened = uri));
        Assert.Equal("accounts.google.com", opened!.Host);
        Assert.Throws<InvalidOperationException>(() => McpConnections.OpenGoogleBrowser(new("https://attacker.invalid/"), _ => Assert.Fail("Unexpected browser launch")));
        Assert.Throws<InvalidOperationException>(() => McpConnections.OpenGoogleBrowser(new("file:///C:/Windows/"), _ => Assert.Fail("Unexpected browser launch")));
        Assert.False(McpConnections.OpenGoogleBrowser(new("https://accounts.google.com/o/oauth2/auth"), _ => throw new System.ComponentModel.Win32Exception()));
    }

    public void Dispose()
    {
        store.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
