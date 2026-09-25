using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class DataConnectionTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "data-connections-" + Guid.NewGuid().ToString("N"));
    private readonly Vault vault = new();
    private WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++)
        {
            try { Directory.Delete(root, true); }
            catch (IOException) when (attempt < 10) { await Task.Delay(200); }
        }
    }

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
    private sealed class Loopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, proceed) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return proceed(); }); next(app);
        };
    }

    /// <summary>Stands in for Google's token, userinfo, Admin, Data and Search Console APIs, and for Plausible.</summary>
    private sealed class FakeApis : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];
        public string? LastVerifier { get; private set; }
        static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            var bearer = request.Headers.Authorization?.Parameter;
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add(request.Method + " " + url);
            var days = Enumerable.Range(1, 20).Select(back => DateTime.UtcNow.Date.AddDays(-back)).ToArray();
            if (url == "https://oauth2.googleapis.com/token")
            {
                var form = QueryHelpers.ParseQuery(body);
                if (form["grant_type"] == "authorization_code")
                {
                    LastVerifier = form["code_verifier"];
                    return form["code"] == "good" && LastVerifier?.Length > 40
                        ? Json(new { access_token = "first", refresh_token = "refresh-secret", token_type = "Bearer", expires_in = 3599, scope = "openid https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/analytics.readonly https://www.googleapis.com/auth/webmasters.readonly" })
                        : Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest);
                }
                return form["refresh_token"] == "refresh-secret" ? Json(new { access_token = "fresh", expires_in = 3599 }) : Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest);
            }
            if (url.StartsWith("https://openidconnect.googleapis.com/v1/userinfo", StringComparison.Ordinal)) return Json(new { email = "owner@example.com" });
            if (url.StartsWith("https://plausible.io/api/v2/query", StringComparison.Ordinal))
                return bearer == "plausible-key-0123456789"
                    ? Json(new { results = days.Select(day => new { dimensions = new[] { day.ToString("yyyy-MM-dd") }, metrics = new object[] { 40, 50, 120, 55.5 } }) })
                    : Json(new { error = "Invalid API key" }, HttpStatusCode.Unauthorized);
            if (bearer != "fresh") return Json(new { error = new { message = "Request had invalid authentication credentials." } }, HttpStatusCode.Unauthorized);
            if (url.StartsWith("https://analyticsadmin.googleapis.com/v1beta/accountSummaries", StringComparison.Ordinal))
                return Json(new { accountSummaries = new[] { new { displayName = "Acme", propertySummaries = new[] { new { property = "properties/123", displayName = "acme.com" } } } } });
            if (url == "https://analyticsdata.googleapis.com/v1beta/properties/123:runReport")
            {
                using var report = JsonDocument.Parse(body);
                var metrics = report.RootElement.GetProperty("metrics").GetArrayLength();
                return Json(new { rows = days.Select(day => new { dimensionValues = new[] { new { value = day.ToString("yyyyMMdd") } }, metricValues = Enumerable.Range(0, metrics).Select(index => new { value = (100 + index).ToString() }) }) });
            }
            if (url == "https://www.googleapis.com/webmasters/v3/sites")
                return Json(new { siteEntry = new[] { new { siteUrl = "sc-domain:acme.com", permissionLevel = "siteOwner" }, new { siteUrl = "https://other.com/", permissionLevel = "siteUnverifiedUser" } } });
            if (url.StartsWith("https://www.googleapis.com/webmasters/v3/sites/sc-domain%3Aacme.com/searchAnalytics/query", StringComparison.Ordinal))
                return Json(new { rows = days.Select(day => new { keys = new[] { day.ToString("yyyy-MM-dd") }, clicks = 12, impressions = 400, ctr = 0.03, position = 8.4 }) });
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    [Fact] public async Task GoogleAndPlausibleSyncIntoTheScorecardWithSecretsKeptInTheVault()
    {
        var apis = new FakeApis();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => { services.AddSingleton<ICredentialVault>(vault); services.AddSingleton<IStartupFilter, Loopback>(); });
        });
        var data = factory.Services.GetRequiredService<DataConnections>();
        data.Handler = () => apis;
        data.GoogleClient = _ => Task.FromResult<(string, string)?>(("client-id.apps.googleusercontent.com", "client-secret"));
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var owner = factory.Services.GetRequiredService<Security>().Issue(context, "Owner", true);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        async Task<JsonElement> Send(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path) { Content = body == null ? null : JsonContent.Create(body) };
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, path + " → " + (int)response.StatusCode + " " + text);
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }

        Assert.True((await Send(HttpMethod.Get, "/api/data-connections")).GetProperty("googleReady").GetBoolean());
        // Google Analytics: consent with PKCE, then choose the property.
        var begun = await Send(HttpMethod.Post, "/api/data-connections/google", new { kind = "google-analytics" });
        var authorization = new Uri(begun.GetProperty("authorizationUrl").GetString()!);
        var query = QueryHelpers.ParseQuery(authorization.Query);
        Assert.Equal("accounts.google.com", authorization.Host);
        Assert.Equal("http://127.0.0.1:5179/api/data-connections/google/callback", query["redirect_uri"]);
        Assert.Contains("https://www.googleapis.com/auth/analytics.readonly", query["scope"].ToString());
        Assert.Equal("S256", query["code_challenge_method"]); Assert.Equal("offline", query["access_type"]);
        using (var forged = await client.GetAsync("/api/data-connections/google/callback?code=good&state=forged")) Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        using (var callback = await client.GetAsync($"/api/data-connections/google/callback?code=good&state={Uri.EscapeDataString(query["state"]!)}"))
            Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        using (var replay = await client.GetAsync($"/api/data-connections/google/callback?code=good&state={Uri.EscapeDataString(query["state"]!)}"))
            Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        var id = begun.GetProperty("id").GetString()!;
        var connection = (await Send(HttpMethod.Get, "/api/data-connections")).GetProperty("connections").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        Assert.Equal("choose", connection.GetProperty("status").GetString()); Assert.Equal("owner@example.com", connection.GetProperty("account").GetString());
        var resources = await Send(HttpMethod.Get, $"/api/data-connections/{id}/resources");
        Assert.Equal("properties/123", resources[0].GetProperty("id").GetString());
        var ready = await Send(HttpMethod.Put, $"/api/data-connections/{id}", new { resource = "properties/123", metrics = new[] { "sessions", "keyEvents" } });
        Assert.Equal("ready", ready.GetProperty("status").GetString()); Assert.Equal(40, ready.GetProperty("lastRows").GetInt32());

        // Search Console: unverified sites aren't offered; average position is a lower-is-better metric.
        var console = await Send(HttpMethod.Post, "/api/data-connections/google", new { kind = "search-console" });
        var state = QueryHelpers.ParseQuery(new Uri(console.GetProperty("authorizationUrl").GetString()!).Query)["state"]!;
        using (var callback = await client.GetAsync($"/api/data-connections/google/callback?code=good&state={Uri.EscapeDataString(state!)}")) Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        var consoleId = console.GetProperty("id").GetString()!;
        Assert.Single((await Send(HttpMethod.Get, $"/api/data-connections/{consoleId}/resources")).EnumerateArray());
        Assert.Equal("ready", (await Send(HttpMethod.Put, $"/api/data-connections/{consoleId}", new { resource = "sc-domain:acme.com" })).GetProperty("status").GetString());

        // Plausible: a wrong key is refused and leaves nothing behind; the right one syncs.
        using (var wrong = await client.PostAsJsonAsync("/api/data-connections/plausible", new { siteId = "acme.com", apiKey = "wrong-key-0123456789" }))
            Assert.False(wrong.IsSuccessStatusCode);
        var plausible = await Send(HttpMethod.Post, "/api/data-connections/plausible", new { siteId = "acme.com", apiKey = "plausible-key-0123456789" });
        Assert.Equal("ready", plausible.GetProperty("status").GetString());
        Assert.Equal(3, (await Send(HttpMethod.Get, "/api/data-connections")).GetProperty("connections").GetArrayLength());

        var scorecard = factory.Services.GetRequiredService<Scorecard>().Ledger();
        var metrics = scorecard.Metrics.ToDictionary(item => item.Name);
        Assert.Equal("Google Analytics", metrics["Sessions"].Source); Assert.True(metrics.ContainsKey("Key events"));
        Assert.Equal("down", metrics["Average search position"].Good); Assert.Equal("%", metrics["Search CTR (%)"].Unit); Assert.Equal(3, scorecard.Observations.Where(item => item.Metric == metrics["Search CTR (%)"].Key).Select(item => item.Value).Distinct().Single());
        Assert.Equal("down", metrics["Bounce rate (%)"].Good);
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        Assert.DoesNotContain(scorecard.Observations, item => item.Date == today);
        // Search Console runs three days behind; its last complete day is kept, nothing newer.
        Assert.DoesNotContain(scorecard.Observations, item => item.Metric == metrics["Search clicks"].Key && string.CompareOrdinal(item.Date, DateTime.UtcNow.AddDays(-3).ToString("yyyy-MM-dd")) > 0);

        // A later sync re-reads the last ten days (late data) and replaces those days rather than adding to them.
        var before = factory.Services.GetRequiredService<Scorecard>().Ledger().Observations.Select(item => (item.Metric, item.Date, item.Value)).ToHashSet();
        Assert.Equal(20, (await Send(HttpMethod.Post, $"/api/data-connections/{id}/sync", new { })).GetProperty("lastRows").GetInt32());
        Assert.True(before.SetEquals(factory.Services.GetRequiredService<Scorecard>().Ledger().Observations.Select(item => (item.Metric, item.Date, item.Value))));

        // Secrets never leave the vault: not in the view, the export or the settings store.
        Assert.Equal(3, vault.Entries.Count);
        foreach (var path in new[] { "/api/data-connections", "/api/export" })
        {
            var text = await client.GetStringAsync(path);
            Assert.DoesNotContain("refresh-secret", text); Assert.DoesNotContain("plausible-key-0123456789", text); Assert.DoesNotContain("client-secret", text);
        }
        await Send(HttpMethod.Delete, $"/api/data-connections/{id}", new { });
        Assert.Equal(2, vault.Entries.Count);
        Assert.Equal(2, (await Send(HttpMethod.Get, "/api/data-connections")).GetProperty("connections").GetArrayLength());
    }
}
