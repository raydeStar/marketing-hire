using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record DataConnection(string Id, string Kind, string Status, string? Account, string? Resource, string? ResourceName, string[] Metrics,
    DateTimeOffset CreatedAt, DateTimeOffset? LastSyncAt, string? LastError, int? LastRows, string? BaseUrl = null);
public record DataConnectionLedger(string VaultScope, DataConnection[] Connections);
public record DataResource(string Id, string Name);
public record SearchQuery(string Query, string Page, double Clicks, double Impressions, double Ctr, double Position);
public record SearchQueries(DateTimeOffset At, string Site, string From, string To, SearchQuery[] Rows);
public record DataGoogleStart(string Kind);
public record DataConnectionChoice(string Resource, string? ResourceName, string[]? Metrics);
public record DataPlausibleStart(string? BaseUrl, string SiteId, string ApiKey, string[]? Metrics)
{
    public override string ToString() => "Plausible connection (API key omitted)";
}

/// <summary>Read-only analytics connections that keep the scorecard current: Google Analytics 4 and Search Console
/// through the owner's own Google app, and Plausible (open source) with an API key. Credentials live in the operating
/// system's credential vault; numbers arrive as daily scorecard observations. Nothing here can change the source.</summary>
public sealed class DataConnections(Store store, ICredentialVault vault, McpConnections google, Scorecard scorecard, ILogger<DataConnections> logger, string localOrigin)
{
    private const string Key = "data-connections-v1";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private readonly SemaphoreSlim syncGate = new(1, 1);
    private readonly Dictionary<string, (string Verifier, string ConnectionId, DateTimeOffset At)> attempts = [];
    public Func<HttpMessageHandler>? Handler { get; set; }
    public Func<CancellationToken, Task<(string ClientId, string ClientSecret)?>> GoogleClient { get; set; } =
        async cancellation => await google.SavedGoogleClient(cancellation) is { } client ? (client.ClientId, client.ClientSecret) : null;
    public Uri Redirect => new UriBuilder(new Uri(localOrigin)) { Host = "127.0.0.1", Path = "/api/data-connections/google/callback" }.Uri;

    /// <summary>Metric options per source: (API name, scorecard name). The first entries are the defaults.</summary>
    public static readonly Dictionary<string, (string Name, string Label, string Scope, (string Api, string Metric)[] Metrics, int Defaults)> Kinds = new()
    {
        ["google-analytics"] = ("Google Analytics", "GA4 property", "https://www.googleapis.com/auth/analytics.readonly",
            [("sessions", "Sessions"), ("totalUsers", "Users"), ("newUsers", "New users"), ("keyEvents", "Key events"), ("engagedSessions", "Engaged sessions"), ("screenPageViews", "Page views")], 4),
        ["search-console"] = ("Search Console", "Search Console site", "https://www.googleapis.com/auth/webmasters.readonly",
            [("clicks", "Search clicks"), ("impressions", "Search impressions"), ("ctr", "Search CTR (%)"), ("position", "Average search position")], 4),
        ["plausible"] = ("Plausible", "Plausible site", "",
            [("visitors", "Visitors"), ("visits", "Visits"), ("pageviews", "Page views"), ("bounce_rate", "Bounce rate (%)")], 4)
    };

    private const string QueriesKey = "search-queries-v1";
    /// <summary>The latest four weeks of Search Console queries by page, read with each Search Console sync.</summary>
    public SearchQueries? Queries() => store.Setting(QueriesKey) is { } json ? Wire.Unpack<SearchQueries>(json) : null;

    /// <summary>Queries within reach of page one: shown often, ranked 4-20, so a better title, heading or section could win the clicks.
    /// Ranked by the clicks being missed (impressions not clicked), which favours real demand over rare queries.</summary>
    public static SearchQuery[] Opportunities(SearchQueries? data, int take = 10) =>
        data == null ? [] : [.. data.Rows.Where(row => row.Position is >= 4 and <= 20 && row.Impressions >= 10).OrderByDescending(row => row.Impressions * (1 - row.Ctr / 100)).Take(take)];

    DataConnectionLedger Read() => store.Setting(Key) is { } json ? Wire.Unpack<DataConnectionLedger>(json) : new(Guid.NewGuid().ToString("N"), []);
    void Write(DataConnectionLedger ledger) => store.Setting(Key, Wire.Pack(ledger));
    DataConnectionLedger Ledger() { lock (store) { var ledger = Read(); if (store.Setting(Key) == null) Write(ledger); return ledger; } }
    DataConnection Find(string id) => Ledger().Connections.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException("That data connection doesn't exist.");
    DataConnection Update(string id, Func<DataConnection, DataConnection> change)
    {
        lock (store)
        {
            var ledger = Read();
            var current = ledger.Connections.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException("That data connection doesn't exist.");
            var next = change(current);
            Write(ledger with { Connections = [.. ledger.Connections.Select(item => item.Id == id ? next : item)] });
            return next;
        }
    }
    HttpClient Client() => new(Handler?.Invoke() ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<object> View(CancellationToken cancellation)
    {
        bool googleReady;
        try { googleReady = await GoogleClient(cancellation) != null; } catch (InvalidOperationException) { googleReady = false; }
        return new
        {
            googleReady,
            kinds = Kinds.Select(item => new { kind = item.Key, name = item.Value.Name, label = item.Value.Label, metrics = item.Value.Metrics.Select((metric, index) => new { id = metric.Api, name = metric.Metric, standard = index < item.Value.Defaults }) }),
            connections = Ledger().Connections
        };
    }

    // ---------- Google: PKCE sign-in against the owner's saved Desktop app ----------
    public async Task<object> BeginGoogle(DataGoogleStart start, CancellationToken cancellation)
    {
        if (start.Kind is not ("google-analytics" or "search-console")) throw new ArgumentException("Choose Google Analytics or Search Console.");
        var client = await GoogleClient(cancellation) ?? throw new InvalidOperationException("Set up the Google app once first: Settings → Google app.");
        var id = Guid.NewGuid().ToString("N");
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        lock (attempts)
        {
            foreach (var old in attempts.Where(item => item.Value.At < DateTimeOffset.UtcNow.AddMinutes(-15)).Select(item => item.Key).ToArray()) attempts.Remove(old);
            attempts[state] = (verifier, id, DateTimeOffset.UtcNow);
        }
        lock (store)
        {
            var ledger = Read();
            if (ledger.Connections.Length >= 10) throw new InvalidOperationException("Remove a data connection before adding another.");
            Write(ledger with { Connections = [.. ledger.Connections.Where(item => item.Status != "authorizing" || item.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-15)),
                new DataConnection(id, start.Kind, "authorizing", null, null, null, [], DateTimeOffset.UtcNow, null, null, null)] });
        }
        var url = QueryHelpers.AddQueryString("https://accounts.google.com/o/oauth2/v2/auth", new Dictionary<string, string?>
        {
            ["client_id"] = client.ClientId, ["redirect_uri"] = Redirect.AbsoluteUri, ["response_type"] = "code",
            ["scope"] = "openid email " + Kinds[start.Kind].Scope, ["access_type"] = "offline", ["prompt"] = "consent",
            ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["state"] = state
        });
        return new { id, authorizationUrl = url };
    }

    public async Task CompleteGoogle(string? code, string? state, string? error, CancellationToken cancellation)
    {
        (string Verifier, string ConnectionId, DateTimeOffset At) attempt;
        lock (attempts)
        {
            if (state == null || !attempts.Remove(state, out attempt)) throw new ArgumentException("This Google sign-in response is unknown, expired, or already used.");
        }
        if (!string.IsNullOrEmpty(error) || string.IsNullOrWhiteSpace(code))
        {
            Update(attempt.ConnectionId, item => item with { Status = "error", LastError = "Google did not authorize the connection." });
            return;
        }
        try
        {
            var client = await GoogleClient(cancellation) ?? throw new InvalidOperationException("The Google app setup was removed.");
            using var http = Client();
            using var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code, ["client_id"] = client.ClientId, ["client_secret"] = client.ClientSecret, ["redirect_uri"] = Redirect.AbsoluteUri,
                ["grant_type"] = "authorization_code", ["code_verifier"] = attempt.Verifier
            }), cancellation);
            using var token = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            if (!response.IsSuccessStatusCode || !token.RootElement.TryGetProperty("refresh_token", out var refresh) || refresh.GetString() is not { Length: > 0 } refreshToken)
                throw new InvalidOperationException("Google did not provide lasting read access. Try again and approve access on the consent screen.");
            var kind = Find(attempt.ConnectionId).Kind;
            var granted = token.RootElement.TryGetProperty("scope", out var scope) ? scope.GetString() ?? "" : "";
            if (!granted.Split(' ').Contains(Kinds[kind].Scope)) throw new InvalidOperationException("Read access to " + Kinds[kind].Name + " wasn't granted. Try again and tick the permission.");
            var access = token.RootElement.GetProperty("access_token").GetString()!;
            string? account = null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
                using var info = await http.SendAsync(request, cancellation);
                if (info.IsSuccessStatusCode) { using var body = JsonDocument.Parse(await info.Content.ReadAsStringAsync(cancellation)); account = body.RootElement.TryGetProperty("email", out var email) ? email.GetString() : null; }
            }
            catch (Exception failure) when (failure is HttpRequestException or JsonException) { }
            await SaveSecret(attempt.ConnectionId, refreshToken, cancellation);
            Update(attempt.ConnectionId, item => item with { Status = "choose", Account = account, LastError = null });
        }
        catch (Exception failure) when (failure is InvalidOperationException or HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException)
        {
            Update(attempt.ConnectionId, item => item with { Status = "error", LastError = failure.Message });
        }
    }

    async Task SaveSecret(string id, string secret, CancellationToken cancellation)
    {
        var scope = Ledger().VaultScope;
        await vault.Execute("write", scope, id, secret, cancellation);
        if (await vault.Execute("read", scope, id, null, cancellation) != secret) throw new InvalidOperationException("The credential store did not keep the connection's secret.");
    }

    async Task<string> Secret(string id, CancellationToken cancellation) =>
        await vault.Execute("read", Ledger().VaultScope, id, null, cancellation) ?? throw new InvalidOperationException("The saved credential is missing. Disconnect and connect again.");

    async Task<string> GoogleAccess(DataConnection connection, CancellationToken cancellation)
    {
        var client = await GoogleClient(cancellation) ?? throw new InvalidOperationException("The Google app setup was removed.");
        using var http = Client();
        using var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = client.ClientId, ["client_secret"] = client.ClientSecret, ["refresh_token"] = await Secret(connection.Id, cancellation), ["grant_type"] = "refresh_token"
        }), cancellation);
        using var token = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        if (!response.IsSuccessStatusCode || !token.RootElement.TryGetProperty("access_token", out var access))
            throw new InvalidOperationException("Google refused the saved access (it may have been revoked or expired). Disconnect and connect again.");
        return access.GetString()!;
    }

    async Task<JsonDocument> Json(HttpMethod method, string url, string? bearer, object? body, CancellationToken cancellation)
    {
        using var http = Client();
        using var request = new HttpRequestMessage(method, url) { Content = body == null ? null : JsonContent(body) };
        if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await http.SendAsync(request, cancellation);
        var text = await response.Content.ReadAsStringAsync(cancellation);
        if (!response.IsSuccessStatusCode)
        {
            var detail = "";
            try { using var error = JsonDocument.Parse(text); detail = error.RootElement.TryGetProperty("error", out var e) ? (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m) ? m.GetString() : e.ToString()) ?? "" : ""; }
            catch (JsonException) { }
            throw new InvalidOperationException($"The source refused the request ({(int)response.StatusCode}){(detail.Length > 0 ? ": " + (detail.Length > 240 ? detail[..240] : detail) : ".")}");
        }
        return JsonDocument.Parse(text.Length == 0 ? "{}" : text);
    }
    static StringContent JsonContent(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    /// <summary>Properties or sites the signed-in account can read, to choose from.</summary>
    public async Task<DataResource[]> Resources(string id, CancellationToken cancellation)
    {
        var connection = Find(id);
        if (connection.Kind == "plausible") return connection.Resource is { } site ? [new(site, site)] : [];
        var access = await GoogleAccess(connection, cancellation);
        if (connection.Kind == "google-analytics")
        {
            using var result = await Json(HttpMethod.Get, "https://analyticsadmin.googleapis.com/v1beta/accountSummaries?pageSize=200", access, null, cancellation);
            return result.RootElement.TryGetProperty("accountSummaries", out var accounts)
                ? [.. accounts.EnumerateArray().SelectMany(account => account.TryGetProperty("propertySummaries", out var properties) ? properties.EnumerateArray()
                    .Select(property => new DataResource(property.GetProperty("property").GetString()!, $"{property.GetProperty("displayName").GetString()} ({account.GetProperty("displayName").GetString()})")) : [])]
                : [];
        }
        using var sites = await Json(HttpMethod.Get, "https://www.googleapis.com/webmasters/v3/sites", access, null, cancellation);
        return sites.RootElement.TryGetProperty("siteEntry", out var entries)
            ? [.. entries.EnumerateArray().Where(site => site.GetProperty("permissionLevel").GetString() != "siteUnverifiedUser").Select(site => new DataResource(site.GetProperty("siteUrl").GetString()!, site.GetProperty("siteUrl").GetString()!))]
            : [];
    }

    string[] Metrics(string kind, string[]? chosen)
    {
        var options = Kinds[kind].Metrics;
        var picked = (chosen ?? []).Where(metric => options.Any(option => option.Api == metric)).Distinct().ToArray();
        return picked.Length > 0 ? picked : [.. options.Take(Kinds[kind].Defaults).Select(option => option.Api)];
    }

    public async Task<DataConnection> Choose(string id, DataConnectionChoice choice, CancellationToken cancellation)
    {
        var connection = Find(id);
        if (connection.Status is not ("choose" or "ready" or "error") || connection.Kind == "plausible") throw new InvalidOperationException("This connection isn't waiting for a choice.");
        var available = await Resources(id, cancellation);
        var resource = available.FirstOrDefault(item => item.Id == choice.Resource) ?? throw new ArgumentException("Choose one of the properties or sites this account can read.");
        Update(id, item => item with { Status = "ready", Resource = resource.Id, ResourceName = resource.Name, Metrics = Metrics(item.Kind, choice.Metrics), LastError = null });
        return await Sync(id, cancellation);
    }

    // ---------- Plausible (open source): an API key the owner creates in their Plausible settings ----------
    public async Task<DataConnection> ConnectPlausible(DataPlausibleStart start, CancellationToken cancellation)
    {
        var baseUrl = (start.BaseUrl is { Length: > 0 } given ? given : "https://plausible.io").Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var base_) || base_.Scheme != "https" || base_.UserInfo.Length > 0 || base_.Query.Length > 0)
            throw new ArgumentException("Plausible's address must be https, e.g. https://plausible.io or your own server.");
        var site = (start.SiteId ?? "").Trim();
        if (site.Length is < 3 or > 200 || site.Contains('/')) throw new ArgumentException("Enter the site's domain as it appears in Plausible, e.g. example.com.");
        if ((start.ApiKey ?? "").Trim() is not { Length: >= 16 and <= 512 } key) throw new ArgumentException("Paste the Plausible API key (Settings → API keys).");
        var id = Guid.NewGuid().ToString("N");
        lock (store)
        {
            var ledger = Read();
            if (ledger.Connections.Length >= 10) throw new InvalidOperationException("Remove a data connection before adding another.");
            Write(ledger with { Connections = [.. ledger.Connections, new DataConnection(id, "plausible", "ready", null, site, site, Metrics("plausible", start.Metrics), DateTimeOffset.UtcNow, null, null, null, base_.AbsoluteUri.TrimEnd('/'))] });
        }
        await SaveSecret(id, key, cancellation);
        var synced = await Sync(id, cancellation);
        if (synced.Status == "error") { await Forget(id, cancellation); throw new InvalidOperationException("Plausible refused the connection: " + synced.LastError); }
        return synced;
    }

    public async Task Forget(string id, CancellationToken cancellation)
    {
        Find(id);
        await vault.Execute("forget", Ledger().VaultScope, id, null, cancellation);
        lock (store) { var ledger = Read(); Write(ledger with { Connections = [.. ledger.Connections.Where(item => item.Id != id)] }); }
    }

    // ---------- Sync: daily values into the scorecard ----------
    /// <summary>Complete days only: today is partial for every source, and Search Console runs about three days behind,
    /// so including them would read as a drop.</summary>
    public async Task<DataConnection> Sync(string id, CancellationToken cancellation)
    {
        await syncGate.WaitAsync(cancellation);
        try
        {
            var connection = Find(id);
            if (connection.Status != "ready" && !(connection.Status == "error" && connection.Resource != null)) throw new InvalidOperationException("Choose what to read before syncing.");
            try
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var end = connection.Kind == "search-console" ? today.AddDays(-3) : today.AddDays(-1);
                var start = connection.LastSyncAt == null ? end.AddDays(-89) : end.AddDays(-9);
                var rows = connection.Kind switch
                {
                    "google-analytics" => await Analytics(connection, start, end, cancellation),
                    "search-console" => await SearchConsole(connection, start, end, cancellation),
                    _ => await Plausible(connection, start, end, cancellation)
                };
                // Only complete days in the window are kept, whatever the source returns.
                rows = [.. rows.Where(row => string.CompareOrdinal(row.Date, Day(start)) >= 0 && string.CompareOrdinal(row.Date, Day(end)) <= 0)];
                var names = Kinds[connection.Kind].Metrics.ToDictionary(item => item.Api, item => item.Metric);
                var csv = new StringBuilder("date,metric,value\n");
                foreach (var (date, metric, value) in rows)
                    csv.Append(CultureInfo.InvariantCulture, $"{date},\"{names[metric]}\",{value.ToString("0.####", CultureInfo.InvariantCulture)}\n");
                var count = 0;
                if (rows.Count > 0)
                {
                    var text = csv.ToString();
                    count = scorecard.Import(new ScoreImportRequest($"sync-{id}-{Wire.Hash(text)[..16]}", null, null, Kinds[connection.Kind].Name), text, "Data connection").Rows;
                }
                if (connection.Kind == "search-console")
                {
                    // Queries and pages are a separate read; the daily totals above stand even if this one fails.
                    try { await SyncQueries(connection, end, cancellation); }
                    catch (Exception failure) when (failure is InvalidOperationException or HttpRequestException or JsonException or TaskCanceledException) { logger.LogWarning("Search Console queries weren't read: {Error}", failure.Message); }
                }
                return Update(id, item => item with { Status = "ready", LastSyncAt = DateTimeOffset.UtcNow, LastError = null, LastRows = count });
            }
            catch (Exception failure) when (failure is InvalidOperationException or HttpRequestException or JsonException or ArgumentException or TaskCanceledException or KeyNotFoundException)
            {
                logger.LogWarning("Data connection {Kind} failed to sync: {Error}", connection.Kind, failure.Message);
                return Update(id, item => item with { Status = "error", LastError = failure.Message });
            }
        }
        finally { syncGate.Release(); }
    }

    /// <summary>Every connection not synced in the last six hours; called by the pump and at the start of a shift.</summary>
    public async Task<int> SyncDue(CancellationToken cancellation)
    {
        var due = Ledger().Connections.Where(item => item.Resource != null && item.Status is "ready" or "error" && !(item.LastSyncAt > DateTimeOffset.UtcNow.AddHours(-6))).ToArray();
        foreach (var connection in due) await Sync(connection.Id, cancellation);
        return due.Length;
    }

    static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Sessions that arrived through a tracking link (utm_source + utm_campaign) since a date, from the first
    /// ready Google Analytics connection. Null when none is connected.</summary>
    public async Task<int?> CampaignVisits(string source, string campaign, DateTimeOffset since, CancellationToken cancellation)
    {
        var connection = Ledger().Connections.FirstOrDefault(item => item.Kind == "google-analytics" && item.Status == "ready" && item.Resource != null);
        if (connection == null) return null;
        var access = await GoogleAccess(connection, cancellation);
        object Exact(string field, string value) => new { filter = new { fieldName = field, stringFilter = new { matchType = "EXACT", value, caseSensitive = false } } };
        using var report = await Json(HttpMethod.Post, $"https://analyticsdata.googleapis.com/v1beta/{connection.Resource}:runReport", access, new
        {
            dateRanges = new[] { new { startDate = Day(DateOnly.FromDateTime(since.UtcDateTime)), endDate = "today" } },
            metrics = new[] { new { name = "sessions" } },
            dimensionFilter = new { andGroup = new { expressions = new[] { Exact("sessionSource", source), Exact("sessionCampaignName", campaign) } } }
        }, cancellation);
        if (!report.RootElement.TryGetProperty("rows", out var rows)) return 0;
        return rows.EnumerateArray().Sum(row => int.TryParse(row.GetProperty("metricValues")[0].GetProperty("value").GetString(), out var value) ? value : 0);
    }

    async Task<List<(string Date, string Metric, double Value)>> Analytics(DataConnection connection, DateOnly start, DateOnly end, CancellationToken cancellation)
    {
        var access = await GoogleAccess(connection, cancellation);
        using var report = await Json(HttpMethod.Post, $"https://analyticsdata.googleapis.com/v1beta/{connection.Resource}:runReport", access, new
        {
            dateRanges = new[] { new { startDate = Day(start), endDate = Day(end) } }, dimensions = new[] { new { name = "date" } },
            metrics = connection.Metrics.Select(metric => new { name = metric }), limit = 1000
        }, cancellation);
        var rows = new List<(string, string, double)>();
        if (!report.RootElement.TryGetProperty("rows", out var list)) return rows;
        foreach (var row in list.EnumerateArray())
        {
            var raw = row.GetProperty("dimensionValues")[0].GetProperty("value").GetString()!;
            if (!DateOnly.TryParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            var values = row.GetProperty("metricValues").EnumerateArray().ToArray();
            for (var index = 0; index < connection.Metrics.Length && index < values.Length; index++)
                if (double.TryParse(values[index].GetProperty("value").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) rows.Add((Day(date), connection.Metrics[index], value));
        }
        return rows;
    }

    async Task<List<(string Date, string Metric, double Value)>> SearchConsole(DataConnection connection, DateOnly start, DateOnly end, CancellationToken cancellation)
    {
        var access = await GoogleAccess(connection, cancellation);
        using var report = await Json(HttpMethod.Post, $"https://www.googleapis.com/webmasters/v3/sites/{Uri.EscapeDataString(connection.Resource!)}/searchAnalytics/query", access,
            new { startDate = Day(start), endDate = Day(end), dimensions = new[] { "date" }, rowLimit = 1000 }, cancellation);
        var rows = new List<(string, string, double)>();
        if (!report.RootElement.TryGetProperty("rows", out var list)) return rows;
        foreach (var row in list.EnumerateArray())
        {
            var date = row.GetProperty("keys")[0].GetString()!;
            foreach (var metric in connection.Metrics)
                if (row.TryGetProperty(metric, out var value) && value.TryGetDouble(out var number)) rows.Add((date, metric, metric == "ctr" ? number * 100 : number));
        }
        return rows;
    }

    async Task SyncQueries(DataConnection connection, DateOnly end, CancellationToken cancellation)
    {
        var access = await GoogleAccess(connection, cancellation);
        var start = end.AddDays(-27);
        using var report = await Json(HttpMethod.Post, $"https://www.googleapis.com/webmasters/v3/sites/{Uri.EscapeDataString(connection.Resource!)}/searchAnalytics/query", access,
            new { startDate = Day(start), endDate = Day(end), dimensions = new[] { "query", "page" }, rowLimit = 250 }, cancellation);
        var rows = new List<SearchQuery>();
        if (report.RootElement.TryGetProperty("rows", out var list))
            foreach (var row in list.EnumerateArray())
            {
                var keys = row.GetProperty("keys").EnumerateArray().Select(key => key.GetString() ?? "").ToArray();
                if (keys.Length < 2 || keys[0].Length is 0 or > 200 || keys[1].Length > 500) continue;
                double Get(string name) => row.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : 0;
                rows.Add(new SearchQuery(keys[0], keys[1], Get("clicks"), Get("impressions"), Math.Round(Get("ctr") * 100, 2), Math.Round(Get("position"), 1)));
            }
        store.Setting(QueriesKey, Wire.Pack(new SearchQueries(DateTimeOffset.UtcNow, connection.Resource!, Day(start), Day(end), [.. rows])));
    }

    async Task<List<(string Date, string Metric, double Value)>> Plausible(DataConnection connection, DateOnly start, DateOnly end, CancellationToken cancellation)
    {
        using var report = await Json(HttpMethod.Post, (connection.BaseUrl ?? "https://plausible.io") + "/api/v2/query", await Secret(connection.Id, cancellation),
            new { site_id = connection.Resource, metrics = connection.Metrics, date_range = new[] { Day(start), Day(end) }, dimensions = new[] { "time:day" } }, cancellation);
        var rows = new List<(string, string, double)>();
        if (!report.RootElement.TryGetProperty("results", out var list)) return rows;
        foreach (var row in list.EnumerateArray())
        {
            var date = row.GetProperty("dimensions")[0].GetString()![..10];
            var values = row.GetProperty("metrics").EnumerateArray().ToArray();
            for (var index = 0; index < connection.Metrics.Length && index < values.Length; index++)
                if (values[index].ValueKind == JsonValueKind.Number) rows.Add((date, connection.Metrics[index], values[index].GetDouble()));
        }
        return rows;
    }
}
