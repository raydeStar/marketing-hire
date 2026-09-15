using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class ConnectionApiTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-connection-api-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> factory;
    private readonly Vault vault = new();
    private Store? store;
    private const string Key = "fictional-only-api-credential";
    private sealed class Vault : ICredentialVault
    {
        public Dictionary<(string, string), string> Entries = [];
        public Task<string?> Execute(string operation, string scope, string id, string? value, CancellationToken cancellation)
        {
            if (operation == "write") Entries[(scope, id)] = value!;
            if (operation == "forget") Entries.Remove((scope, id));
            return Task.FromResult(operation == "read" ? Entries.GetValueOrDefault((scope, id)) : null);
        }
    }
    private sealed class Address : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, proceed) => { context.Connection.RemoteIpAddress = context.Request.Headers.ContainsKey("Fixture-Remote") ? IPAddress.Parse("192.0.2.7") : IPAddress.Loopback; return proceed(); }); next(app);
        };
    }
    public ConnectionApiTests()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.ConfigureServices(services => { services.AddSingleton<ICredentialVault>(vault); services.AddSingleton<IStartupFilter, Address>(); });
        });
    }
    private HttpClient Client(bool? owner = true, bool csrf = true)
    {
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        store ??= factory.Services.GetRequiredService<Store>(); client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        if (owner != null)
        {
            var context = new DefaultHttpContext(); var session = factory.Services.GetRequiredService<Security>().Issue(context, "Fixture browser", owner.Value);
            client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
            if (csrf) client.DefaultRequestHeaders.Add("X-CSRF", session.Csrf);
        }
        return client;
    }
    private static async Task<JsonElement> View(HttpClient client) => await client.GetFromJsonAsync<JsonElement>("/api/settings/connection");
    private static ConnectionEdit Edit(string version, string mode = "system") => new(version, new("compatible", "fixture-model", "high", "https://provider.invalid/v1"), mode, mode is "system" or "session" ? Key : null);
    [Theory]
    [InlineData(null, true, 401)]
    [InlineData(false, true, 403)]
    [InlineData(true, false, 403)]
    public async Task KeyManagementRequiresOwnerAndCsrf(bool? owner, bool csrf, int code)
    {
        using var client = Client(owner, csrf);
        Assert.Equal((HttpStatusCode)code, (await client.PutAsJsonAsync("/api/settings/connection", Edit("unknown"))).StatusCode);
        Assert.Empty(vault.Entries); Assert.Null(store!.Setting("provider-credentials"));
        Assert.Equal((HttpStatusCode)code, (await client.PostAsJsonAsync("/api/settings/connection/credentials/missing/remove", new { version = "unknown" })).StatusCode);
    }
    [Fact] public async Task RemoteOwnerSessionCannotManageKeys()
    {
        using var client = Client(); client.DefaultRequestHeaders.Add("Fixture-Remote", "true");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/settings/connection")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/settings/connection", Edit("unknown"))).StatusCode);
        Assert.Empty(vault.Entries);
    }
    [Theory]
    [InlineData(null, true, false, 401)]
    [InlineData(false, true, false, 403)]
    [InlineData(true, false, false, 403)]
    [InlineData(true, true, true, 403)]
    public async Task SearchLimitRequiresLocalOwnerAndCsrf(bool? owner, bool csrf, bool remote, int code)
    {
        using var client = Client(owner, csrf);
        if (remote) client.DefaultRequestHeaders.Add("Fixture-Remote", "true");
        var current = store!.SearchBudget();
        using var result = await client.PutAsJsonAsync("/api/settings/search/budget", new SearchBudgetEdit(current.Version, 0));
        Assert.Equal((HttpStatusCode)code, result.StatusCode);
        Assert.Equal(100, store.SearchBudget().MonthlyLimit); Assert.Empty(vault.Entries);
    }
    [Fact] public async Task SearchLimitIsVisibleAndStaleEditsFailWithoutResettingTheSetting()
    {
        using var client = Client(); var before = store!.SearchBudget();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/settings/search/budget", new SearchBudgetEdit(before.Version, 25))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/settings/search/budget", new SearchBudgetEdit(before.Version, 100))).StatusCode);
        var state = await client.GetFromJsonAsync<JsonElement>("/api/state");
        Assert.Equal(25, state.GetProperty("search").GetProperty("budget").GetProperty("monthlyLimit").GetInt32());
        Assert.Equal(0, store.SearchBudget().Used); Assert.Empty(vault.Entries); Assert.Empty(store.List());
    }
    [Fact] public async Task ConnectionNeverEchoesKeyAndRemovedKeyStopsChatBeforeChargingTokens()
    {
        using var client = Client(); var before = await View(client);
        using var save = await client.PutAsJsonAsync("/api/settings/connection", Edit(before.GetProperty("version").GetString()!));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode); Assert.DoesNotContain(Key, await save.Content.ReadAsStringAsync());
        var saved = await View(client); var id = saved.GetProperty("provider").GetProperty("credentialId").GetString()!;
        Assert.Single(vault.Entries); Assert.DoesNotContain(Key, store!.Setting("provider-credentials")!);
        foreach (var route in new[] { "/api/state", "/api/export", "/api/settings/connection", "/api/settings/diagnostics" })
            Assert.DoesNotContain(Key, await client.GetStringAsync(route));
        using var removal = await client.PostAsJsonAsync($"/api/settings/connection/credentials/{id}/remove", new { version = saved.GetProperty("version").GetString() });
        Assert.Equal(HttpStatusCode.OK, removal.StatusCode); Assert.Empty(vault.Entries);
        using var submit = await client.PostAsJsonAsync("/api/chat", new { mode = "chat", content = "A fictional admission check." });
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var run = await submit.Content.ReadFromJsonAsync<Run>(Wire.Json) ?? throw new InvalidOperationException();
        for (var i = 0; i < 100 && store.Get(run.Id)!.State is RunState.Queued or RunState.Running; i++) await Task.Delay(20);
        var failed = store.Get(run.Id)!; Assert.Equal(RunState.Failed, failed.State);
        Assert.Equal(0, failed.ModelCalls); Assert.Equal(0, failed.ChargedTokens); Assert.Equal(0, failed.ReservedTokens);
        Assert.DoesNotContain(Key, Wire.Pack(store.AllEvents()));
    }
    [Fact] public async Task StaleOwnerSaveHasNoCredentialSideEffect()
    {
        using var client = Client(); var before = await View(client); var version = before.GetProperty("version").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/settings/connection", Edit(version))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/settings/connection", Edit(version))).StatusCode);
        Assert.Single(vault.Entries);
    }
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        await factory.DisposeAsync(); store?.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
