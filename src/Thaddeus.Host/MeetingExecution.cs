using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Thaddeus.Host;

internal sealed class MeetingPreflightException(string message, Exception inner) : InvalidOperationException(message, inner);
internal sealed class MeetingOutputException(string message, Exception inner) : InvalidOperationException(message, inner);

/// <summary>The pilot can read only recorded public HN discussion URLs. The worker itself has no tools.</summary>
internal static class MeetingSourceReader
{
    private static readonly Regex ItemQuery = new(@"\A\?id=[0-9]{1,12}\z", RegexOptions.Compiled);
    public static bool Allowed(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host == "news.ycombinator.com" && uri.IsDefaultPort &&
        uri.AbsolutePath == "/item" && ItemQuery.IsMatch(uri.Query) && uri.UserInfo.Length == 0 &&
        uri.Fragment.Length == 0 && url == uri.AbsoluteUri;

    internal static bool PublicIPv4(IPAddress address)
    {
        // Conservative fixed-host preflight; see IANA's IPv4 special-purpose registry.
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] is > 0 and < 224 && b[0] is not (10 or 127) &&
            !(b[0] == 100 && b[1] is >= 64 and <= 127) &&
            !(b[0] == 169 && b[1] == 254) &&
            !(b[0] == 172 && b[1] is >= 16 and <= 31) &&
            !(b[0] == 192 && b[1] == 0 && b[2] is 0 or 2) &&
            !(b[0] == 192 && b[1] == 88 && b[2] == 99) &&
            !(b[0] == 192 && b[1] == 168) &&
            !(b[0] == 198 && b[1] is 18 or 19) &&
            !(b[0] == 198 && b[1] == 51 && b[2] == 100) &&
            !(b[0] == 203 && b[1] == 0 && b[2] == 113);
    }

    public static async Task<string> Read(string url, CancellationToken cancellation)
    {
        if (!Allowed(url)) throw new InvalidOperationException("The source is outside the restricted public-read scope.");
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseProxy = false,
            ConnectCallback = async (context, token) =>
            {
                if (context.DnsEndPoint.Host != "news.ycombinator.com" || context.DnsEndPoint.Port != 443)
                    throw new IOException("The source attempted to leave its approved host.");
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
                var address = addresses.FirstOrDefault(PublicIPv4) ?? throw new IOException("The source host has no public IPv4 address.");
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, 443), token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            }
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentType?.MediaType != "text/html")
            throw new IOException("The approved source did not return a readable HTML page.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        var buffer = new byte[256_001]; var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellation);
            if (read == 0) break;
            count += read;
        }
        if (count == buffer.Length) throw new IOException("The approved source exceeded the retrieval limit.");
        var html = Encoding.UTF8.GetString(buffer, 0, count);
        var body = Regex.Replace(html, @"(?is)<(script|style)\b[^>]*>.*?</\1>", " ");
        body = Regex.Replace(body, @"(?s)<[^>]+>", " ");
        body = Regex.Replace(WebUtility.HtmlDecode(body), @"\s+", " ").Trim();
        if (body.Length < 100) throw new IOException("The approved source contained too little readable text.");
        return body[..Math.Min(body.Length, 16000)];
    }
}

internal static class MeetingWorkerResult
{
    private static string Value(JsonElement item, string name, int limit)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > limit)
            throw new InvalidOperationException("The worker omitted a bounded " + name + ".");
        return value.GetString()!.Trim();
    }

    private static string Assumptions(JsonElement root)
    {
        if (!root.TryGetProperty("assumptions", out var value))
            throw new InvalidOperationException("The worker omitted bounded assumptions.");
        if (value.ValueKind == JsonValueKind.String) return Value(root, "assumptions", 1000);
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is < 1 or > 5)
            throw new InvalidOperationException("The worker needs one to five bounded assumptions.");
        var items = value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(item.GetString()) && item.GetString()!.Length <= 500
                ? item.GetString()!.Trim()
                : throw new InvalidOperationException("An assumption is missing or too long.")).ToArray();
        var combined = string.Join("; ", items);
        if (combined.Length > 1000)
            throw new InvalidOperationException("The worker's assumptions exceeded their combined limit.");
        return combined;
    }

    public static string Parse(string kind, string reply, string[] allowedUrls, IReadOnlyDictionary<string, string>? sourceTexts = null)
    {
        if (reply.Length > 30000) throw new InvalidOperationException("The worker response exceeded its limit.");
        var clean = reply.Trim();
        if (clean.StartsWith("```"))
        {
            var first = clean.IndexOf('\n'); var last = clean.LastIndexOf("```", StringComparison.Ordinal);
            if (first >= 0 && last > first) clean = clean[(first + 1)..last];
        }
        using var json = JsonDocument.Parse(clean);
        var root = json.RootElement;
        if (kind == "evidence_brief")
        {
            if (!root.TryGetProperty("angles", out var angles) || angles.ValueKind != JsonValueKind.Array || angles.GetArrayLength() != 3)
                throw new InvalidOperationException("The evidence brief needs exactly three angles.");
            var lines = new List<string> { "# Three content angles", "" };
            var used = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            foreach (var angle in angles.EnumerateArray())
            {
                var url = Value(angle, "sourceUrl", 500);
                if (!allowedUrls.Contains(url, StringComparer.Ordinal)) throw new InvalidOperationException("An angle cited an unapproved source.");
                used.Add(url);
                var excerpt = Value(angle, "evidence", 240);
                if (sourceTexts != null && (!sourceTexts.TryGetValue(url, out var text) ||
                    excerpt.Length < 10 || !Regex.Replace(text, @"\s+", " ").Contains(Regex.Replace(excerpt, @"\s+", " "), StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("An angle's source excerpt was not found in the retrieved content.");
                lines.Add($"## {++index}. {Value(angle, "title", 160)}");
                lines.Add($"Source excerpt: {excerpt} ([source]({url}))");
                lines.Add($"Why it may matter: {Value(angle, "whyRelevant", 700)}");
                lines.Add($"Assumption: {Value(angle, "assumption", 700)}");
                lines.Add("");
            }
            if (used.Count != 2) throw new InvalidOperationException("The evidence brief must use both approved sources.");
            if (!root.TryGetProperty("gaps", out var gaps) || gaps.ValueKind != JsonValueKind.Array || gaps.GetArrayLength() is < 1 or > 5)
                throw new InvalidOperationException("The evidence brief needs explicit gaps.");
            lines.Add("## Evidence gaps");
            foreach (var gap in gaps.EnumerateArray())
            {
                if (gap.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(gap.GetString()) || gap.GetString()!.Length > 500)
                    throw new InvalidOperationException("An evidence gap is missing or too long.");
                lines.Add("- " + gap.GetString()!.Trim());
            }
            return string.Join("\n", lines);
        }
        if (kind != "local_draft") throw new InvalidOperationException("Unknown worker result type.");
        return "# Local marketing draft\n\nAudience: " + Value(root, "audience", 400) +
            "\n\nAngle: " + Value(root, "angle", 300) +
            "\n\n" + Value(root, "draft", 7000) +
            "\n\nOwner next action: " + Value(root, "ownerNextAction", 500) +
            "\n\nAssumptions: " + Assumptions(root) +
            "\n\n*Saved for owner review. Nothing was published.*";
    }
}
