using System.Globalization;
using System.Text.Json;

namespace Thaddeus.Host;

public record DataSiteStart(string? Address, string? Token, string[]? Metrics)
{
    public override string ToString() => "HireZero site connection (key omitted)";
}

/// <summary>The owner's own HireZero site: launch-list sign-ups and new accounts per day, read over the site's MCP with its
/// drafts-only agent key. Numbers only; the site never hands over an address.</summary>
public sealed partial class DataConnections
{
    /// <summary>The HireZero site already connected under Publishing (its address and agent key), so the key isn't pasted twice.</summary>
    public Func<CancellationToken, Task<(string Address, string Token)?>>? SiteKey { get; set; }

    public async Task<DataConnection> ConnectSite(DataSiteStart start, CancellationToken cancellation)
    {
        string address, token;
        if (string.IsNullOrWhiteSpace(start.Token))
        {
            if (SiteKey == null || await SiteKey(cancellation) is not { } site)
                throw new ArgumentException("Paste the site's agent key (the site's Settings → Agent keys), or connect the HireZero site under Publishing first.");
            (address, token) = site;
        }
        else
        {
            address = SiteAddress(start.Address);
            token = start.Token.Trim();
            if (token.Length is < 16 or > 600 || token.Any(char.IsWhiteSpace)) throw new ArgumentException("Paste the site's agent key as the site showed it (Settings → Agent keys).");
        }
        var host = new Uri(address).Host;
        return await AddTokenConnection("hirezero-signups", token, host, $"Sign-ups on {host}", null, start.Metrics, cancellation, address);
    }

    /// <summary>The key pasted for sign-ups, so the same site can take approved fixes as drafts without pasting it twice.</summary>
    public async Task<(string Address, string Token)?> SignupSiteKey(CancellationToken cancellation) =>
        Ledger().Connections.FirstOrDefault(item => item.Kind == "hirezero-signups" && item.Status == "ready" && item.BaseUrl != null) is { } site
            ? (site.BaseUrl!, await Secret(site.Id, cancellation)) : null;

    /// <summary>https, or http on this computer for a local copy of the site.</summary>
    static string SiteAddress(string? value)
    {
        var text = (value ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(text, UriKind.Absolute, out var address) || address.UserInfo.Length > 0 || !(address.Scheme == "https" || address.Scheme == "http" && address.IsLoopback))
            throw new ArgumentException("The site's address must be https, e.g. https://hirezero.app.");
        return address.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>Each day's launch-list sign-ups and new accounts, from the site's signups_daily tool (a day with none is a zero), and
    /// the launch list's size at the end of each day: today's size less the sign-ups since, so "100 by October 31" reads the total.</summary>
    async Task<List<(string Date, string Metric, double Value)>> SiteSignups(DataConnection connection, DateOnly start, CancellationToken cancellation)
    {
        var asked = Math.Clamp(DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - start.DayNumber + 1, 1, 400);
        using var reply = await Json(HttpMethod.Post, connection.BaseUrl + "/mcp", await Secret(connection.Id, cancellation),
            new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "signups_daily", arguments = new { days = asked } } }, cancellation);
        if (reply.RootElement.TryGetProperty("error", out var error))
            throw new InvalidOperationException("The site refused it: " + (error.TryGetProperty("message", out var message) ? message.GetString() : "unknown error") + ".");
        var result = reply.RootElement.GetProperty("result");
        var text = result.TryGetProperty("content", out var parts) && parts.GetArrayLength() > 0 ? parts[0].GetProperty("text").GetString() ?? "" : "";
        if (result.TryGetProperty("isError", out var failed) && failed.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException(text.Contains("Unknown tool", StringComparison.OrdinalIgnoreCase)
                ? "The site doesn't report sign-ups yet: deploy the latest version of its CMS." : "The site refused it: " + text);
        using var counts = JsonDocument.Parse(text);
        var rows = new List<(string, string, double)>();
        var days = counts.RootElement.GetProperty("days").EnumerateArray().OrderBy(day => day.GetProperty("date").GetString(), StringComparer.Ordinal).ToArray();
        foreach (var day in days)
            foreach (var metric in connection.Metrics)
                if (day.TryGetProperty(metric, out var value) && value.TryGetDouble(out var count))
                    rows.Add((day.GetProperty("date").GetString()!, metric, count));
        if (connection.Metrics.Contains("launch_list") && counts.RootElement.TryGetProperty("totals", out var totals) && totals.TryGetProperty("launchList", out var size) && size.TryGetDouble(out var total))
            for (var index = days.Length - 1; index >= 0; index--)
            {
                rows.Add((days[index].GetProperty("date").GetString()!, "launch_list", Math.Max(0, total)));
                total -= days[index].TryGetProperty("signups", out var joined) && joined.TryGetDouble(out var count) ? count : 0;
            }
        return rows;
    }
}
