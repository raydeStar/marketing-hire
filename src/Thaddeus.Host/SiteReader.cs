using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Thaddeus.Host;

/// <summary>Reads public pages on the owner's research allowlist (their own site, named competitors).
/// HTTPS only, public IPv4 only, redirects only within the allowlist, 512 KB, text only.</summary>
public static partial class SiteReader
{
    [GeneratedRegex(@"^(?=.{3,120}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,24}$")] private static partial Regex HostName();

    /// <summary>"https://www.acme.com/pricing" or "acme.com" → "acme.com"; anything else is refused.</summary>
    public static string? NormalizeSite(string? value)
    {
        var text = (value ?? "").Trim().ToLowerInvariant();
        if (text.Length == 0) return null;
        if (!text.Contains("://")) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https") return null;
        var host = uri.Host.StartsWith("www.", StringComparison.Ordinal) ? uri.Host[4..] : uri.Host;
        return HostName().IsMatch(host) ? host : null;
    }

    public static bool Allowed(Uri uri, IReadOnlyCollection<string> sites) =>
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        sites.Any(site => uri.Host == site || uri.Host.EndsWith("." + site, StringComparison.Ordinal));

    public static async Task<(string Url, string Title, string Text)> Read(string url, IReadOnlyCollection<string> sites, CancellationToken cancellation)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var target) || !Allowed(target, sites))
            throw new InvalidOperationException("That page is not on the research allowlist.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false,
            ConnectCallback = async (context, token) =>
            {
                if (context.DnsEndPoint.Port != 443 || !sites.Any(site => context.DnsEndPoint.Host == site || context.DnsEndPoint.Host.EndsWith("." + site, StringComparison.Ordinal)))
                    throw new IOException("The page tried to leave the allowlist.");
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
                var address = addresses.FirstOrDefault(MeetingSourceReader.PublicIPv4) ?? throw new IOException("The site has no public address.");
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(new IPEndPoint(address, 443), token); return new NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); throw; }
            } };
        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; FirstEmployeeResearch/1.0)");
        for (var hop = 0; hop < 4; hop++)
        {
            using var response = await client.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next)
            {
                target = next.IsAbsoluteUri ? next : new Uri(target, next);
                if (!Allowed(target, sites)) throw new IOException("The page redirected outside the allowlist.");
                continue;
            }
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentType?.MediaType is not ("text/html" or "text/plain"))
                throw new IOException($"The page could not be read ({(int)response.StatusCode}).");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[524_289]; var count = 0;
            while (count < buffer.Length) { var read = await stream.ReadAsync(buffer.AsMemory(count), timeout.Token); if (read == 0) break; count += read; }
            var html = Encoding.UTF8.GetString(buffer, 0, Math.Min(count, buffer.Length - 1));
            var title = Regex.Match(html, @"(?is)<title[^>]*>(.*?)</title>").Groups[1].Value;
            var body = Regex.Replace(html, @"(?is)<(script|style|noscript|svg|nav|footer)\b[^>]*>.*?</\1>", " ");
            body = Regex.Replace(body, @"(?s)<[^>]+>", " ");
            body = Regex.Replace(WebUtility.HtmlDecode(body), @"\s+", " ").Trim();
            if (body.Length < 80) throw new IOException("The page had too little readable text (it may need JavaScript).");
            title = Regex.Replace(WebUtility.HtmlDecode(title), @"\s+", " ").Trim();
            return (target.AbsoluteUri, title.Length is > 0 and <= 200 ? title : target.Host, body[..Math.Min(body.Length, 3000)]);
        }
        throw new IOException("The page redirected too many times.");
    }
}
