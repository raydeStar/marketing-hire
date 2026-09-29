using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

/// <summary>The owner's own HireZero site: launch-list sign-ups and new accounts per day into the scorecard, where the north star can read them.</summary>
public sealed class SiteSignupsTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "site-signups-" + Guid.NewGuid().ToString("N"));
    readonly Vault vault = new();
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++) { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }

    sealed class Vault : ICredentialVault
    {
        public Dictionary<(string, string), string> Entries = [];
        public Task<string?> Execute(string operation, string scope, string id, string? value, CancellationToken cancellation)
        {
            if (operation == "write") Entries[(scope, id)] = value!;
            if (operation == "forget") Entries.Remove((scope, id));
            return Task.FromResult(operation == "read" ? Entries.GetValueOrDefault((scope, id)) : null);
        }
    }
    sealed class Loopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => { app.Use((context, proceed) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return proceed(); }); next(app); };
    }

    const string Key = "hz_agent_0123456789abcdef", OldSiteKey = "hz_agent_oldsite_0123456789";
    static string Ago(int days) => DateTime.UtcNow.Date.AddDays(-days).ToString("yyyy-MM-dd");

    /// <summary>Stands in for the site's /mcp: signups_daily with the right key; an older CMS without the tool with the other.</summary>
    sealed class FakeSite : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];
        static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        static object Text(object value, bool error = false) => new { jsonrpc = "2.0", id = 1, result = new { content = new[] { new { type = "text", text = value as string ?? JsonSerializer.Serialize(value) } }, isError = error } };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri != "https://hirezero.example/mcp") return new HttpResponseMessage(HttpStatusCode.NotFound);
            var bearer = request.Headers.Authorization?.Parameter;
            using var call = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var parameters = call.RootElement.GetProperty("params");
            // Connecting the site for drafts checks its tools: the CMS's drafts-only ones, and nothing that publishes.
            if (call.RootElement.GetProperty("method").GetString() == "tools/list")
                return bearer == Key ? Json(new { jsonrpc = "2.0", id = 1, result = new { tools = new[] { "save_post_draft", "save_landing_draft", "get_landing", "signups_daily" }.Select(name => new { name }) } })
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized);
            Asked.Add(parameters.GetProperty("name").GetString() + ":" + parameters.GetProperty("arguments").GetProperty("days").GetInt32());
            if (bearer == OldSiteKey) return Json(Text("Unknown tool: signups_daily", true));
            if (bearer != Key) return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":\"A valid agent token is required.\"}", Encoding.UTF8, "application/json") };
            var days = parameters.GetProperty("arguments").GetProperty("days").GetInt32();
            // Like the CMS: the last `days` days through today, today included (partial).
            return Json(Text(new
            {
                days = Enumerable.Range(0, days).Reverse().Select(ago => new { date = Ago(ago), signups = ago switch { 0 => 9, 1 => 2, 3 => 1, _ => 0 }, accounts = ago == 1 ? 1 : 0 }).ToArray(),
                totals = new { launchList = 12, accounts = 1 }
            }));
        }
    }

    [Fact] public async Task SignUpsAndAccountsReadIntoTheScorecardDailyWithTheSitesKey()
    {
        var site = new FakeSite();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host")); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => { services.AddSingleton<ICredentialVault>(vault); services.AddSingleton<IStartupFilter, Loopback>(); });
        });
        var data = factory.Services.GetRequiredService<DataConnections>();
        data.Handler = () => site;
        var publishing = factory.Services.GetRequiredService<Publishing>();
        publishing.Handler = () => site;
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var owner = factory.Services.GetRequiredService<Security>().Issue(context, "Owner", true);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        async Task<(HttpStatusCode Status, string Text)> Call(object body)
        {
            using var response = await client.PostAsJsonAsync("/api/data-connections/hirezero-signups", body);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        // Without a key, it uses the HireZero site connected under Publishing; with none there, it asks for the key.
        Assert.NotNull(data.SiteKey);
        Assert.Null(await data.SiteKey!(default));
        Assert.Equal(HttpStatusCode.BadRequest, (await Call(new { })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Call(new { address = "http://hirezero.example", token = Key })).Status);   // https only, off this computer
        // A site whose CMS predates the tool says what to do, and leaves nothing behind.
        var old = await Call(new { address = "https://hirezero.example", token = OldSiteKey });
        Assert.Contains("deploy the latest version of its CMS", old.Text);
        Assert.Empty(data.Connected());

        site.Asked.Clear();
        var (status, text) = await Call(new { address = "https://hirezero.example/", token = Key });
        Assert.True(status == HttpStatusCode.OK, text);
        var connection = Assert.Single(data.Connected());
        Assert.Equal(("ready", "Sign-ups on hirezero.example", "https://hirezero.example"), (connection.Status, connection.ResourceName, connection.BaseUrl));
        Assert.Equal(["signups_daily:91"], site.Asked);   // the first sync reads 90 complete days, plus today's partial one that is dropped
        // One key, both jobs: the same site now takes approved fixes as drafts, without pasting the key again.
        var drafts = Assert.Single(publishing.Ledger().Connections, item => item.Kind == "hirezero");
        Assert.Equal(("ready", "https://hirezero.example"), (drafts.Status, drafts.Address));

        var ledger = factory.Services.GetRequiredService<Scorecard>().Ledger();
        var metrics = ledger.Metrics.ToDictionary(item => item.Name);
        double On(string name, int daysAgo) => ledger.Observations.Single(item => item.Metric == metrics[name].Key && item.Date == Ago(daysAgo)).Value;
        Assert.Equal((2, 0, 1), (On("Beta sign-ups", 1), On("Beta sign-ups", 2), On("Beta sign-ups", 3)));
        Assert.Equal((1, 0), (On("Accounts created", 1), On("Accounts created", 2)));
        // The list's size at the end of each day: 12 now, less today's 9 is 3 at the end of yesterday, 1 before yesterday's 2.
        Assert.Equal((3, 1, 1, 0), (On("Beta sign-ups (total)", 1), On("Beta sign-ups (total)", 2), On("Beta sign-ups (total)", 3), On("Beta sign-ups (total)", 4)));
        Assert.DoesNotContain(ledger.Observations, item => item.Date == Ago(0));   // today isn't over

        // The same site with the key from Publishing, after disconnecting.
        await data.Forget(connection.Id, default);
        data.SiteKey = _ => Task.FromResult<(string, string)?>(("https://hirezero.example", Key));
        Assert.Equal(HttpStatusCode.OK, (await Call(new { })).Status);
        Assert.Equal("hirezero.example", Assert.Single(data.Connected()).Resource);
        Assert.DoesNotContain(vault.Entries.Values, value => value == OldSiteKey);
    }
}
