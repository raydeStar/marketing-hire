using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Thaddeus.Core;

namespace Thaddeus.Host;

public record DataTokenStart(string Token, string? AccountId, string[]? Metrics)
{
    public override string ToString() => "Token connection (token omitted)";
}
public record CountRow(string Name, double Count, double Value);
public record CrmDeal(string Name, string Stage, double? Amount, string Created, string? Source);
/// <summary>The CRM right now: where the last four weeks' leads came from, the open pipeline, what was won, and the newest deals.</summary>
public record CrmSnapshot(DateTimeOffset At, string Account, string Currency, CountRow[] LeadsBySource, int OpenDeals, double OpenValue, int WonDeals, double WonValue, CrmDeal[] RecentDeals);
public record AdCampaign(string Name, double Spend, double Clicks, double Impressions, double Leads);
/// <summary>Each ad campaign's last seven days: spend, clicks and leads.</summary>
public record AdsSnapshot(DateTimeOffset At, string Account, string Currency, AdCampaign[] Campaigns);

/// <summary>Business data beside the analytics: the CRM (HubSpot) for leads, deals and pipeline, and ad spend (Meta Ads) for
/// what paid channels cost and bring. Both are read-only: a token with read scopes, stored in the credential vault; nothing
/// here can change a contact, a deal, a budget or an ad.</summary>
public sealed partial class DataConnections
{
    public const string HubSpotApi = "https://api.hubapi.com", MetaGraph = "https://graph.facebook.com/v23.0";
    const string CrmKey = "crm-snapshot-v1", AdsKey = "ads-snapshot-v1";
    /// <summary>Every data connection, with its status; the secrets stay in the vault.</summary>
    public DataConnection[] Connected() => Ledger().Connections;
    public CrmSnapshot? Crm() => store.Setting(CrmKey) is { } json ? Wire.Unpack<CrmSnapshot>(json) : null;
    public AdsSnapshot? Ads() => store.Setting(AdsKey) is { } json ? Wire.Unpack<AdsSnapshot>(json) : null;

    // ---------- Connecting with a token ----------
    public async Task<DataConnection> ConnectHubSpot(DataTokenStart start, CancellationToken cancellation)
    {
        if ((start.Token ?? "").Trim() is not { Length: >= 20 and <= 600 } token || token.Any(char.IsWhiteSpace))
            throw new ArgumentException("Paste the HubSpot private app's access token (Settings → Integrations → Private apps).");
        using var details = await Json(HttpMethod.Get, HubSpotApi + "/account-info/v3/details", token, null, cancellation);
        var portal = details.RootElement.TryGetProperty("portalId", out var id) ? id.GetRawText().Trim('"') : throw new InvalidOperationException("HubSpot didn't say which account this token belongs to.");
        var currency = details.RootElement.TryGetProperty("companyCurrency", out var money) && money.ValueKind == JsonValueKind.String ? money.GetString()! : "USD";
        return await AddTokenConnection("hubspot", token, portal, $"HubSpot {portal}", currency, start.Metrics, cancellation);
    }

    public async Task<DataConnection> ConnectMetaAds(DataTokenStart start, CancellationToken cancellation)
    {
        if ((start.Token ?? "").Trim() is not { Length: >= 20 and <= 1000 } token || token.Any(char.IsWhiteSpace))
            throw new ArgumentException("Paste a Meta access token with the ads_read permission (a system user token from Business Settings lasts longest).");
        var account = (start.AccountId ?? "").Trim();
        if (account.StartsWith("act_", StringComparison.Ordinal)) account = account[4..];
        if (account.Length is < 5 or > 20 || !account.All(char.IsDigit)) throw new ArgumentException("Enter the ad account ID, the number shown in Ads Manager (act_ is optional).");
        using var details = await Json(HttpMethod.Get, $"{MetaGraph}/act_{account}?fields=name,currency", token, null, cancellation);
        var name = details.RootElement.TryGetProperty("name", out var named) ? named.GetString() ?? account : account;
        var currency = details.RootElement.TryGetProperty("currency", out var money) ? money.GetString() ?? "USD" : "USD";
        return await AddTokenConnection("meta-ads", token, "act_" + account, name, currency, start.Metrics, cancellation);
    }

    async Task<DataConnection> AddTokenConnection(string kind, string token, string resource, string name, string? currency, string[]? metrics, CancellationToken cancellation, string? baseUrl = null)
    {
        var id = Guid.NewGuid().ToString("N");
        lock (store)
        {
            var ledger = Read();
            if (ledger.Connections.Length >= 10) throw new InvalidOperationException("Remove a data connection before adding another.");
            if (ledger.Connections.Any(item => item.Kind == kind && item.Resource == resource)) throw new InvalidOperationException("That account is already connected.");
            Write(ledger with { Connections = [.. ledger.Connections, new DataConnection(id, kind, "ready", currency, resource, name, Metrics(kind, metrics), DateTimeOffset.UtcNow, null, null, null, baseUrl)] });
        }
        await SaveSecret(id, token, cancellation);
        var synced = await Sync(id, cancellation);
        if (synced.Status == "error") { await Forget(id, cancellation); throw new InvalidOperationException($"{Kinds[kind].Name} refused the connection: " + synced.LastError); }
        return synced;
    }

    static long Millis(DateOnly day) => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeMilliseconds();
    static string? Prop(JsonElement item, string name) => item.TryGetProperty("properties", out var props) && props.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    static double Number(string? text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
    static string? DayOf(string? stamp) => stamp is { Length: >= 10 } ? (DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? Day(DateOnly.FromDateTime(at.UtcDateTime))
        : long.TryParse(stamp, out var millis) ? Day(DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime)) : null) : null;

    /// <summary>Every record a HubSpot CRM search matches, up to 5,000, with the named properties.</summary>
    async Task<List<JsonElement>> Search(string token, string objectType, object[] filters, string[] properties, CancellationToken cancellation, object[]? sorts = null, int max = 5000)
    {
        var found = new List<JsonElement>(); string? after = null;
        while (found.Count < max)
        {
            using var page = await Json(HttpMethod.Post, $"{HubSpotApi}/crm/v3/objects/{objectType}/search", token,
                new { filterGroups = new[] { new { filters } }, properties, limit = Math.Min(200, max - found.Count), sorts = sorts ?? [], after }, cancellation);
            if (page.RootElement.TryGetProperty("results", out var results)) found.AddRange(results.EnumerateArray().Select(item => item.Clone()));
            after = page.RootElement.TryGetProperty("paging", out var paging) && paging.TryGetProperty("next", out var next) && next.TryGetProperty("after", out var cursor) ? cursor.GetString() : null;
            if (after == null) break;
        }
        return found;
    }

    static object Between(string property, DateOnly start, DateOnly end) => new { propertyName = property, @operator = "BETWEEN", value = Millis(start).ToString(CultureInfo.InvariantCulture), highValue = (Millis(end.AddDays(1)) - 1).ToString(CultureInfo.InvariantCulture) };

    /// <summary>Daily counts from HubSpot: contacts created, deals created, deals won and their value, and the open pipeline today.
    /// A day with none is a zero, not a gap.</summary>
    async Task<List<(string Date, string Metric, double Value)>> HubSpot(DataConnection connection, DateOnly start, DateOnly end, CancellationToken cancellation)
    {
        var token = await Secret(connection.Id, cancellation);
        var rows = new List<(string, string, double)>();
        var days = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(offset => Day(start.AddDays(offset))).ToArray();
        void Daily(string metric, IEnumerable<(string? Day, double Value)> items)
        {
            if (!connection.Metrics.Contains(metric)) return;
            var totals = items.Where(item => item.Day != null).GroupBy(item => item.Day!).ToDictionary(group => group.Key, group => group.Sum(item => item.Value));
            rows.AddRange(days.Select(day => (day, metric, totals.GetValueOrDefault(day))));
        }
        if (connection.Metrics.Contains("new_contacts"))
            Daily("new_contacts", (await Search(token, "contacts", [Between("createdate", start, end)], ["createdate"], cancellation)).Select(item => (DayOf(Prop(item, "createdate")), 1.0)));
        if (connection.Metrics.Contains("new_deals"))
            Daily("new_deals", (await Search(token, "deals", [Between("createdate", start, end)], ["createdate"], cancellation)).Select(item => (DayOf(Prop(item, "createdate")), 1.0)));
        if (connection.Metrics.Contains("deals_won") || connection.Metrics.Contains("revenue_won"))
        {
            var won = await Search(token, "deals", [Between("closedate", start, end), new { propertyName = "hs_is_closed_won", @operator = "EQ", value = "true" }], ["closedate", "amount"], cancellation);
            Daily("deals_won", won.Select(item => (DayOf(Prop(item, "closedate")), 1.0)));
            Daily("revenue_won", won.Select(item => (DayOf(Prop(item, "closedate")), Number(Prop(item, "amount")))));
        }
        if (connection.Metrics.Contains("open_pipeline"))
        {
            var open = await Search(token, "deals", [new { propertyName = "hs_is_closed", @operator = "EQ", value = "false" }], ["amount"], cancellation);
            rows.Add((Day(end), "open_pipeline", open.Sum(item => Number(Prop(item, "amount")))));
        }
        return rows;
    }

    static readonly Dictionary<string, string> Sources = new()
    {
        ["ORGANIC_SEARCH"] = "Organic search", ["PAID_SEARCH"] = "Paid search", ["PAID_SOCIAL"] = "Paid social", ["SOCIAL_MEDIA"] = "Organic social", ["EMAIL_MARKETING"] = "Email",
        ["REFERRALS"] = "Referrals", ["DIRECT_TRAFFIC"] = "Direct", ["OTHER_CAMPAIGNS"] = "Other campaigns", ["OFFLINE"] = "Offline or imported", ["AI_REFERRALS"] = "AI referrals"
    };

    /// <summary>The CRM right now, for the brief, the shifts and chat: the last four weeks' leads by source, the open pipeline, what was won, the newest deals.</summary>
    async Task SyncCrm(DataConnection connection, DateOnly end, CancellationToken cancellation)
    {
        var token = await Secret(connection.Id, cancellation);
        var from = end.AddDays(-27);
        var leads = await Search(token, "contacts", [Between("createdate", from, end)], ["createdate", "hs_analytics_source"], cancellation);
        var bySource = leads.GroupBy(item => Prop(item, "hs_analytics_source") is { Length: > 0 } source ? Sources.GetValueOrDefault(source, source.Replace('_', ' ').ToLowerInvariant()) : "Unknown")
            .Select(group => new CountRow(group.Key, group.Count(), 0)).OrderByDescending(row => row.Count).Take(8).ToArray();
        var open = await Search(token, "deals", [new { propertyName = "hs_is_closed", @operator = "EQ", value = "false" }], ["amount"], cancellation);
        var won = await Search(token, "deals", [Between("closedate", from, end), new { propertyName = "hs_is_closed_won", @operator = "EQ", value = "true" }], ["amount"], cancellation);
        // Stage names as the owner set them up, not HubSpot's internal ids.
        var stages = new Dictionary<string, string>();
        try
        {
            using var pipelines = await Json(HttpMethod.Get, HubSpotApi + "/crm/v3/pipelines/deals", token, null, cancellation);
            if (pipelines.RootElement.TryGetProperty("results", out var list))
                foreach (var stage in list.EnumerateArray().SelectMany(pipeline => pipeline.TryGetProperty("stages", out var s) ? s.EnumerateArray() : Enumerable.Empty<JsonElement>()))
                    stages[stage.GetProperty("id").GetString()!] = stage.TryGetProperty("label", out var label) ? label.GetString() ?? "" : "";
        }
        catch (InvalidOperationException) { }
        var recent = await Search(token, "deals", [Between("createdate", end.AddDays(-89), end)], ["dealname", "dealstage", "amount", "createdate", "hs_analytics_source"], cancellation,
            [new { propertyName = "createdate", direction = "DESCENDING" }], 8);
        var deals = recent.Select(item => new CrmDeal(Prop(item, "dealname") ?? "Untitled deal", Prop(item, "dealstage") is { } stage ? stages.GetValueOrDefault(stage, stage) : "",
            Prop(item, "amount") is { Length: > 0 } amount ? Number(amount) : null, DayOf(Prop(item, "createdate")) ?? "", Prop(item, "hs_analytics_source") is { Length: > 0 } source ? Sources.GetValueOrDefault(source, source) : null)).ToArray();
        store.Setting(CrmKey, Wire.Pack(new CrmSnapshot(DateTimeOffset.UtcNow, connection.ResourceName ?? "HubSpot", connection.Account ?? "USD", bySource,
            open.Count, open.Sum(item => Number(Prop(item, "amount"))), won.Count, won.Sum(item => Number(Prop(item, "amount"))), deals)));
    }

    /// <summary>Leads in Meta's actions: the "lead" total where Meta reports one, else the pixel and on-Facebook lead events.</summary>
    static double Leads(JsonElement row)
    {
        if (!row.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array) return 0;
        var byType = actions.EnumerateArray().Where(item => item.TryGetProperty("action_type", out _)).GroupBy(item => item.GetProperty("action_type").GetString()!)
            .ToDictionary(group => group.Key, group => group.Sum(item => Number(item.TryGetProperty("value", out var value) ? value.GetString() : null)));
        return byType.TryGetValue("lead", out var total) ? total : byType.GetValueOrDefault("offsite_conversion.fb_pixel_lead") + byType.GetValueOrDefault("onsite_conversion.lead_grouped");
    }

    /// <summary>Every page of a Meta insights read; only Meta's own paging links are followed.</summary>
    async Task<List<JsonElement>> Insights(string token, string url, CancellationToken cancellation)
    {
        var rows = new List<JsonElement>();
        for (var page = 0; page < 20 && url.Length > 0; page++)
        {
            using var result = await Json(HttpMethod.Get, url, token, null, cancellation);
            if (result.RootElement.TryGetProperty("data", out var data)) rows.AddRange(data.EnumerateArray().Select(item => item.Clone()));
            url = result.RootElement.TryGetProperty("paging", out var paging) && paging.TryGetProperty("next", out var next) && next.GetString() is { } link && link.StartsWith("https://graph.facebook.com/", StringComparison.Ordinal) ? link : "";
        }
        return rows;
    }

    /// <summary>Daily spend, impressions, clicks and leads for the ad account. A day without delivery is a zero.</summary>
    async Task<List<(string Date, string Metric, double Value)>> MetaAds(DataConnection connection, DateOnly start, DateOnly end, CancellationToken cancellation)
    {
        var token = await Secret(connection.Id, cancellation);
        var range = Uri.EscapeDataString(JsonSerializer.Serialize(new { since = Day(start), until = Day(end) }));
        var daily = await Insights(token, $"{MetaGraph}/{connection.Resource}/insights?level=account&time_increment=1&time_range={range}&fields=spend,impressions,clicks,actions&limit=500", cancellation);
        // One row per day; if Meta repeats a day across pages, the last one stands.
        var byDay = daily.Where(row => row.TryGetProperty("date_start", out _)).GroupBy(row => row.GetProperty("date_start").GetString()!).ToDictionary(group => group.Key, group => group.Last());
        var rows = new List<(string, string, double)>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            var found = byDay.TryGetValue(Day(day), out var row);
            double Field(string name) => found && row.TryGetProperty(name, out var value) ? Number(value.GetString()) : 0;
            foreach (var metric in connection.Metrics)
                rows.Add((Day(day), metric, metric switch { "leads" => found ? Leads(row) : 0, _ => Field(metric) }));
        }
        return rows;
    }

    /// <summary>Each campaign's last seven days, for the brief's push-or-pivot call on paid.</summary>
    async Task SyncAds(DataConnection connection, CancellationToken cancellation)
    {
        var token = await Secret(connection.Id, cancellation);
        var rows = await Insights(token, $"{MetaGraph}/{connection.Resource}/insights?level=campaign&date_preset=last_7d&fields=campaign_name,spend,clicks,impressions,actions&limit=200", cancellation);
        var campaigns = rows.Select(row => new AdCampaign(row.TryGetProperty("campaign_name", out var name) ? name.GetString() ?? "Campaign" : "Campaign",
            Number(row.TryGetProperty("spend", out var spend) ? spend.GetString() : null), Number(row.TryGetProperty("clicks", out var clicks) ? clicks.GetString() : null),
            Number(row.TryGetProperty("impressions", out var shown) ? shown.GetString() : null), Leads(row))).OrderByDescending(item => item.Spend).Take(20).ToArray();
        store.Setting(AdsKey, Wire.Pack(new AdsSnapshot(DateTimeOffset.UtcNow, connection.ResourceName ?? "Meta Ads", connection.Account ?? "USD", campaigns)));
    }

    public static string Money(double value, string currency) =>
        (currency == "USD" ? "$" : currency == "EUR" ? "€" : currency == "GBP" ? "£" : "") + value.ToString(value >= 100 ? "N0" : "N2", CultureInfo.InvariantCulture) + (currency is "USD" or "EUR" or "GBP" ? "" : " " + currency);

    /// <summary>The CRM in a few lines: where leads came from, the pipeline, what was won, the newest deals.</summary>
    public static string[] PipelineLines(CrmSnapshot? crm)
    {
        if (crm == null) return [];
        var lines = new List<string>();
        var total = crm.LeadsBySource.Sum(row => row.Count);
        lines.Add(total > 0 ? $"New contacts in the last four weeks: {total:0}, by source: {string.Join(", ", crm.LeadsBySource.Take(5).Select(row => $"{row.Name} {row.Count:0}"))}." : "No new contacts in the last four weeks.");
        lines.Add($"Open pipeline: {crm.OpenDeals} deal(s) worth {Money(crm.OpenValue, crm.Currency)}. Won in the last four weeks: {crm.WonDeals} deal(s), {Money(crm.WonValue, crm.Currency)}.");
        if (crm.RecentDeals.Length > 0) lines.Add("Newest deals: " + string.Join("; ", crm.RecentDeals.Take(5).Select(deal => $"{deal.Name} ({deal.Stage}{(deal.Amount is { } amount ? ", " + Money(amount, crm.Currency) : "")}{(deal.Source != null ? ", from " + deal.Source : "")})")) + ".");
        return [.. lines];
    }

    /// <summary>Paid in a few lines: each campaign's last seven days, with its cost per lead, biggest spenders first.</summary>
    public static string[] PaidLines(AdsSnapshot? ads, int take = 6)
    {
        if (ads == null) return [];
        if (ads.Campaigns.Length == 0) return ["No ad spend in the last seven days."];
        var spend = ads.Campaigns.Sum(item => item.Spend); var leads = ads.Campaigns.Sum(item => item.Leads);
        return [$"Meta Ads, last seven days: {Money(spend, ads.Currency)} spent, {leads:0} lead(s){(leads > 0 ? $", {Money(spend / leads, ads.Currency)} per lead" : "")}.",
            .. ads.Campaigns.Take(take).Select(item => $"{item.Name}: {Money(item.Spend, ads.Currency)}, {item.Clicks:0} clicks, {item.Leads:0} lead(s)" + (item.Leads > 0 ? $" ({Money(item.Spend / item.Leads, ads.Currency)} each)" : item.Spend > 0 ? ", no leads" : ""))];
    }

    /// <summary>Campaigns that spent a real share of the week's budget and brought no leads: the ones to pause or change.</summary>
    public static AdCampaign[] Wasting(AdsSnapshot? ads)
    {
        if (ads == null) return [];
        var spend = ads.Campaigns.Sum(item => item.Spend);
        return [.. ads.Campaigns.Where(item => item.Leads == 0 && item.Spend > 0 && item.Spend >= spend * 0.1)];
    }
}
