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

/// <summary>The CRM (HubSpot) and ad spend (Meta Ads), read-only: daily numbers into the scorecard, a snapshot of the pipeline and
/// the campaigns, and a morning brief that calls push or pivot on paid.</summary>
public sealed class BusinessDataTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "business-data-" + Guid.NewGuid().ToString("N"));
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

    const string HubToken = "pat-na1-hubspot-token-0123456789", MetaToken = "EAAmeta-token-0123456789abcdef";
    static string Ago(int days) => DateTime.UtcNow.Date.AddDays(-days).ToString("yyyy-MM-dd");
    static string Stamp(int days) => DateTime.UtcNow.Date.AddDays(-days).AddHours(15).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    /// <summary>Stands in for HubSpot's account, CRM search and pipelines APIs, and for Meta's Graph ad account and insights.</summary>
    sealed class FakeApis : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];
        static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        static object Contact(int days, string source) => new { id = Guid.NewGuid().ToString("N"), properties = new { createdate = Stamp(days), hs_analytics_source = source } };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            var bearer = request.Headers.Authorization?.Parameter;
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add(request.Method + " " + url);
            if (url.StartsWith(DataConnections.HubSpotApi, StringComparison.Ordinal))
            {
                if (bearer != HubToken) return Json(new { status = "error", message = "Authentication credentials not found." }, HttpStatusCode.Unauthorized);
                if (url.EndsWith("/account-info/v3/details", StringComparison.Ordinal)) return Json(new { portalId = 4242, companyCurrency = "USD" });
                if (url.EndsWith("/crm/v3/pipelines/deals", StringComparison.Ordinal)) return Json(new { results = new[] { new { id = "default", stages = new[] { new { id = "appointmentscheduled", label = "Meeting booked" } } } } });
                using var search = JsonDocument.Parse(body);
                var after = search.RootElement.TryGetProperty("after", out var cursor) && cursor.ValueKind == JsonValueKind.String ? cursor.GetString() : null;
                if (url.EndsWith("/contacts/search", StringComparison.Ordinal))
                    // Two pages: the host follows HubSpot's cursor.
                    return after == null
                        ? Json(new { total = 4, results = new[] { Contact(1, "PAID_SOCIAL"), Contact(1, "PAID_SOCIAL") }, paging = new { next = new { after = "p2" } } })
                        : Json(new { total = 4, results = new[] { Contact(1, "ORGANIC_SEARCH"), Contact(2, "DIRECT_TRAFFIC") } });
                if (body.Contains("hs_is_closed_won"))
                    return Json(new { results = new[] { new { id = "d9", properties = new { closedate = Stamp(1), amount = "5000" } } } });
                if (body.Contains("hs_is_closed"))
                    return Json(new { results = new[] { new { id = "d1", properties = new { amount = "12000" } }, new { id = "d2", properties = new { amount = "8000" } } } });
                if (body.Contains("DESCENDING"))
                    return Json(new { results = new[] { new { id = "d1", properties = new { dealname = "Acme pilot", dealstage = "appointmentscheduled", amount = "12000", createdate = Stamp(1), hs_analytics_source = "PAID_SOCIAL" } } } });
                return Json(new { results = new[] { new { id = "d1", properties = new { createdate = Stamp(1) } }, new { id = "d2", properties = new { createdate = Stamp(1) } } } });
            }
            if (url.StartsWith("https://graph.facebook.com/", StringComparison.Ordinal))
            {
                if (bearer != MetaToken) return Json(new { error = new { message = "Invalid OAuth access token." } }, HttpStatusCode.BadRequest);
                if (url.StartsWith(DataConnections.MetaGraph + "/act_12345678?fields=name,currency", StringComparison.Ordinal)) return Json(new { name = "Acme Ads", currency = "USD", id = "act_12345678" });
                if (url.Contains("level=campaign"))
                    return Json(new { data = new object[] {
                        new { campaign_name = "Founders — lead form", spend = "60.00", clicks = "150", impressions = "4000", actions = new[] { new { action_type = "lead", value = "3" } } },
                        new { campaign_name = "Broad retargeting", spend = "40.00", clicks = "90", impressions = "5000", actions = Array.Empty<object>() } } });
                if (url.Contains("level=account2"))
                    return Json(new { data = new object[] { new { date_start = Ago(2), spend = "30.00", impressions = "2000", clicks = "80" } }, paging = new { next = "https://evil.example/steal" } });
                if (url.Contains("level=account"))
                    return Json(new { data = new object[] {
                        new { date_start = Ago(1), spend = "42.50", impressions = "3000", clicks = "120", actions = new[] { new { action_type = "link_click", value = "120" }, new { action_type = "lead", value = "3" } } } },
                        // Meta's next page is followed; a link to anywhere else would not be.
                        paging = new { next = DataConnections.MetaGraph + "/act_12345678/insights?after=p2&level=account2" } });
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    [Fact] public void AnAdPlatformExportImportsAsIs()
    {
        // Google Ads and LinkedIn exports start with a title and a date range, then the header; totals rows have no date.
        var (rows, layout) = Scorecard.ParseCsv("Campaign performance\n\"September 1, 2026 - September 7, 2026\"\nDay,Cost,Clicks,Conversions\n2026-09-01,\"$1,212.50\",40,2\n2026-09-02,$10.00,35,1\nTotal: Account,\"$1,222.50\",75,3\n");
        Assert.Equal("wide", layout);
        Assert.Equal(6, rows.Count);
        Assert.Contains(rows, row => row.Name == "Cost" && row.Date == "2026-09-01" && row.Value == 1212.5);
    }

    [Fact] public async Task TheCrmAndAdSpendReadIntoTheScorecardTheBriefAndChat()
    {
        var apis = new FakeApis();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host")); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => { services.AddSingleton<ICredentialVault>(vault); services.AddSingleton<IStartupFilter, Loopback>(); });
        });
        factory.Services.GetRequiredService<DataConnections>().Handler = () => apis;
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var owner = factory.Services.GetRequiredService<Security>().Issue(context, "Owner", true);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        async Task<(HttpStatusCode Status, string Text)> Call(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path) { Content = body == null ? null : JsonContent.Create(body) };
            using var response = await client.SendAsync(request);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        async Task<JsonElement> Send(HttpMethod method, string path, object? body = null)
        {
            var (status, text) = await Call(method, path, body);
            Assert.True((int)status < 300, path + " → " + (int)status + " " + text);
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        // A wrong token is refused and leaves nothing behind.
        Assert.False((int)(await Call(HttpMethod.Post, "/api/data-connections/hubspot", new { token = "pat-na1-wrong-token-000000000000" })).Status < 300);
        Assert.Equal(0, (await Send(HttpMethod.Get, "/api/data-connections")).GetProperty("connections").GetArrayLength());

        var hub = await Send(HttpMethod.Post, "/api/data-connections/hubspot", new { token = HubToken });
        Assert.Equal(("ready", "HubSpot 4242"), (hub.GetProperty("status").GetString(), hub.GetProperty("resourceName").GetString()));
        var meta = await Send(HttpMethod.Post, "/api/data-connections/meta-ads", new { token = MetaToken, accountId = "act_12345678" });
        Assert.Equal("Acme Ads", meta.GetProperty("resourceName").GetString());
        // The same ad account twice is refused.
        Assert.Equal(HttpStatusCode.Conflict, (await Call(HttpMethod.Post, "/api/data-connections/meta-ads", new { token = MetaToken, accountId = "12345678" })).Status);
        Assert.DoesNotContain(apis.Calls, call => call.Contains("evil.example"));

        // Daily numbers in the scorecard; a day with nothing is a zero, not a gap.
        var ledger = factory.Services.GetRequiredService<Scorecard>().Ledger();
        var metrics = ledger.Metrics.ToDictionary(item => item.Name);
        double On(string name, int daysAgo) => ledger.Observations.Single(item => item.Metric == metrics[name].Key && item.Date == Ago(daysAgo)).Value;
        Assert.Equal((3, 1, 0), (On("New contacts", 1), On("New contacts", 2), On("New contacts", 5)));
        Assert.Equal((2, 1, 5000), (On("New deals", 1), On("Deals won", 1), On("Revenue won", 1)));
        Assert.Equal(20000, On("Open pipeline", 1));
        Assert.Equal((42.5, 30, 0, 3), (On("Ad spend (Meta)", 1), On("Ad spend (Meta)", 2), On("Ad spend (Meta)", 4), On("Ad leads (Meta)", 1)));
        Assert.Equal("down", metrics["Ad spend (Meta)"].Good);
        Assert.DoesNotContain(ledger.Observations, item => item.Date == DateTime.UtcNow.ToString("yyyy-MM-dd"));

        // The snapshots: leads by source, the pipeline with the owner's stage names, each campaign's week.
        var business = await Send(HttpMethod.Get, "/api/data-connections/business");
        Assert.Equal("Paid social", business.GetProperty("crm").GetProperty("leadsBySource")[0].GetProperty("name").GetString());
        Assert.Equal("Meeting booked", business.GetProperty("crm").GetProperty("recentDeals")[0].GetProperty("stage").GetString());
        Assert.Contains("Open pipeline: 2 deal(s) worth $20,000. Won in the last four weeks: 1 deal(s), $5,000.", business.GetProperty("pipeline").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("Broad retargeting: $40.00, 90 clicks, 0 lead(s), no leads", business.GetProperty("paid")[2].GetString());

        // The morning brief: pipeline and paid sections, and the call on paid names the campaign to change.
        var brief = await Send(HttpMethod.Post, "/api/weekly/brief", new { });
        var body = factory.Services.GetRequiredService<CompanyWiki>().List().Single(page => page.Id == brief.GetProperty("wikiId").GetString()).Body;
        Assert.Contains("## Pipeline", body); Assert.Contains("## Paid (last seven days)", body);
        Assert.Contains("- **Pivot: paid.** “Broad retargeting” spent $40.00 this week with no leads.", body);
        Assert.Contains("- Paid: Founders — lead form brought 3 lead(s) at $20.00 each", body);
        Assert.Contains("- Paid: Broad retargeting spent $40.00 in seven days with no leads", body);
        Assert.Contains("Most new contacts in four weeks came from Paid social (2)", body);

        // Chat and the shifts see the same numbers.
        var chat = await factory.Services.GetRequiredService<EmployeeShifts>().ChatContext(CancellationToken.None);
        Assert.Contains("CRM: Open pipeline: 2 deal(s) worth $20,000.", chat);
        Assert.Contains("Paid: Meta Ads, last seven days: $100 spent, 3 lead(s), $33.33 per lead.", chat);

        // Tokens stay in the vault: never in the view.
        var view = (await Call(HttpMethod.Get, "/api/data-connections")).Text;
        Assert.DoesNotContain(HubToken, view); Assert.DoesNotContain(MetaToken, view);
        Assert.Contains(vault.Entries.Values, value => value == HubToken);

        // Settings → Connections: the store works (a throwaway round trip, removed after), and lists what's stored by name only.
        var before = vault.Entries.Count;
        var overview = await Call(HttpMethod.Get, "/api/vault?check");
        Assert.True((int)overview.Status < 300, overview.Text);
        using var shown = JsonDocument.Parse(overview.Text);
        Assert.True(shown.RootElement.GetProperty("health").GetProperty("working").GetBoolean());
        Assert.Equal(before, vault.Entries.Count);
        Assert.Equal(["HubSpot", "Meta Ads"], shown.RootElement.GetProperty("keys").EnumerateArray().Select(key => key.GetProperty("service").GetString()).Order());
        Assert.DoesNotContain(HubToken, overview.Text); Assert.DoesNotContain(MetaToken, overview.Text);
    }
}
