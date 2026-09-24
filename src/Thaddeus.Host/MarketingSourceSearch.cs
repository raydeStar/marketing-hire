using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Thaddeus.Host;

internal static class MarketingSourceSearch
{
    internal sealed record Candidate(string Url, string Title, long PublishedAt, int? Comments);
    internal static Uri SearchUri(string? query, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length is < 2 or > 120 || query.Any(char.IsControl))
            throw new ArgumentException("Use 2–120 characters of public search terms.");
        return new Uri("https://hn.algolia.com/api/v1/search_by_date?tags=story&hitsPerPage=20&query=" +
            Uri.EscapeDataString(query.Trim()) + "&numericFilters=" +
            Uri.EscapeDataString("created_at_i>" + now.AddDays(-90).ToUnixTimeSeconds()));
    }

    internal static Candidate[] Parse(JsonElement root, DateTimeOffset now)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
            throw new IOException("The discussion search returned an unexpected result.");
        var items = new List<Candidate>();
        foreach (var hit in hits.EnumerateArray().Take(20))
        {
            if (hit.ValueKind != JsonValueKind.Object || !hit.TryGetProperty("objectID", out var id) || id.ValueKind != JsonValueKind.String ||
                !Regex.IsMatch(id.GetString()!, "\\A[0-9]{1,12}\\z") ||
                !hit.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(title.GetString()) ||
                !hit.TryGetProperty("created_at_i", out var date) || date.ValueKind != JsonValueKind.Number || !date.TryGetInt64(out var published) ||
                published < now.AddDays(-90).ToUnixTimeSeconds() || published > now.ToUnixTimeSeconds()) continue;
            int? comments = hit.TryGetProperty("num_comments", out var count) && count.ValueKind == JsonValueKind.Number &&
                count.TryGetInt32(out var n) && n >= 0 ? n : null;
            var text = title.GetString()!;
            items.Add(new("https://news.ycombinator.com/item?id=" + id.GetString(), text[..Math.Min(text.Length, 300)], published, comments));
        }
        return items.DistinctBy(item => item.Url).ToArray();
    }

    public static async Task<IResult> Search(string? query, CancellationToken cancellation)
    {
        var now = DateTimeOffset.UtcNow;
        Uri url;
        try { url = SearchUri(query, now); }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false,
                ConnectCallback = async (context, token) => {
                    if (context.DnsEndPoint.Host != "hn.algolia.com" || context.DnsEndPoint.Port != 443)
                        throw new IOException("Search left its approved host.");
                    var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
                    var address = addresses.FirstOrDefault(MeetingSourceReader.PublicIPv4) ?? throw new IOException("Search host has no public address.");
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    try { await socket.ConnectAsync(new IPEndPoint(address, 443), token); return new NetworkStream(socket, ownsSocket: true); }
                    catch { socket.Dispose(); throw; }
                } };
            using var client = new HttpClient(handler);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentType?.MediaType != "application/json")
                throw new IOException("The public discussion search is unavailable.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var bytes = new byte[262145]; var length = 0;
            while (length < bytes.Length) { var read = await stream.ReadAsync(bytes.AsMemory(length), timeout.Token); if (read == 0) break; length += read; }
            if (length == bytes.Length) throw new IOException("Search results exceeded the retrieval limit.");
            using var document = JsonDocument.Parse(bytes.AsMemory(0, length));
            return Results.Ok(new { candidates = Parse(document.RootElement, now), searchedAt = now, windowDays = 90 });
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or OperationCanceledException)
        { return Results.Json(new { error = "Public discussion search is unavailable. You can still paste two discussion URLs." }, statusCode: 503); }
    }
}
