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

    /// <summary>The page as a person sees it: the served HTML first; when that is a JavaScript shell with little text,
    /// the page rendered by <see cref="PageRenderer"/> (if a browser is available).</summary>
    public static async Task<(string Url, string Title, string Text)> Read(string url, IReadOnlyCollection<string> sites, CancellationToken cancellation)
    {
        var (target, html) = await Get(url, sites, ["text/html", "text/plain"], 2_000_000, cancellation);
        var (title, body) = Extract(html);
        // A JavaScript shell has little text; a pricing page whose served HTML shows no price draws its table in the browser.
        var pricingPage = Regex.IsMatch(target.AbsolutePath, @"(?i)pric|plan") && !Regex.IsMatch(body, @"[$€£]\s?\d");
        if (body.Length < 600 || pricingPage)
        {
            try
            {
                if (await PageRenderer.Render(target, cancellation) is { } rendered && Extract(rendered) is { Body.Length: > 0 } seen && seen.Body.Length > body.Length)
                    (title, body) = (seen.Title.Length > 0 ? seen.Title : title, seen.Body);
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        if (body.Length < 80) throw new IOException("The page had too little readable text (it may need JavaScript).");
        return (target.AbsoluteUri, title.Length is > 0 and <= 200 ? title : target.Host, Excerpt(body));
    }

    public static (string Title, string Body) Extract(string html)
    {
        var title = Regex.Match(html, @"(?is)<title[^>]*>(.*?)</title>").Groups[1].Value;
        var body = Regex.Replace(html, @"(?is)<(script|style|noscript|svg|nav|footer|template)\b[^>]*>.*?</\1>", " ");
        body = Regex.Replace(body, @"(?s)<[^>]+>", " ");
        body = Regex.Replace(WebUtility.HtmlDecode(body), @"\s+", " ").Trim();
        return (Regex.Replace(WebUtility.HtmlDecode(title), @"\s+", " ").Trim(), body);
    }

    /// <summary>3,000 characters, starting near the first price when the page's own menus push prices further down.</summary>
    public static string Excerpt(string body)
    {
        var price = Regex.Match(body, @"[$€£]\s?\d");
        var from = price.Success && price.Index > 1500 ? Math.Max(0, price.Index - 400) : 0;
        return body.Substring(from, Math.Min(body.Length - from, 3000));
    }

    /// <summary>An RSS or Atom feed the owner follows: the same guards, confined to the feed's own site.</summary>
    public static async Task<string> FetchFeed(string url, CancellationToken cancellation)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var feed) || NormalizeSite(feed.Host) is not { } site) throw new InvalidOperationException("That isn't a feed address.");
        var (_, xml) = await Get(url, [site], ["application/rss+xml", "application/atom+xml", "application/xml", "text/xml", "application/rdf+xml", "application/feed+json", "text/plain"], 2_000_000, cancellation);
        return xml;
    }

    /// <summary>A public data file (CSV or JSON) from a fixed source such as BLS or the SEC: the same guards, confined to that source.</summary>
    public static async Task<string> FetchData(string url, IReadOnlyCollection<string> sites, CancellationToken cancellation, string? userAgent = null) =>
        (await Get(url, sites, ["text/csv", "application/json", "text/plain", "application/octet-stream"], 3_000_000, cancellation, userAgent)).Body;

    static async Task<(Uri Url, string Body)> Get(string url, IReadOnlyCollection<string> sites, string[] types, int limit, CancellationToken cancellation, string? userAgent = null)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var target) || !Allowed(target, sites))
            throw new InvalidOperationException("That page is not on the research allowlist.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var client = Guarded(sites, userAgent);
        for (var hop = 0; hop < 4; hop++)
        {
            using var response = await client.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next)
            {
                target = next.IsAbsoluteUri ? next : new Uri(target, next);
                if (!Allowed(target, sites)) throw new IOException("The page redirected outside the allowlist.");
                continue;
            }
            if (response.StatusCode != HttpStatusCode.OK || !types.Contains(response.Content.Headers.ContentType?.MediaType ?? ""))
                throw new IOException(response.StatusCode == HttpStatusCode.OK ? $"Unexpected content ({response.Content.Headers.ContentType?.MediaType ?? "none"})." : $"It could not be read ({(int)response.StatusCode}).");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[limit + 1]; var count = 0;
            while (count < buffer.Length) { var read = await stream.ReadAsync(buffer.AsMemory(count), timeout.Token); if (read == 0) break; count += read; }
            if (count > limit && types[0] != "text/html") throw new IOException("It is larger than the reading limit.");
            return (target, Encoding.UTF8.GetString(buffer, 0, Math.Min(count, limit)));
        }
        throw new IOException("The page redirected too many times.");
    }

    /// <summary>An HTTPS client that connects only to allowlisted hosts on port 443 at a public IPv4 address, never follows redirects itself, and uses no proxy.</summary>
    static HttpClient Guarded(IReadOnlyCollection<string> sites, string? userAgent)
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false,
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
        var client = new HttpClient(handler, disposeHandler: true);
        if (userAgent != null) client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        else client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; FirstEmployeeResearch/1.0)");
        return client;
    }

    /// <summary>One page of the owner's own site, as a crawler sees it: the status (a broken link is an answer, not an error), where it ended up
    /// after redirects within the site, and the text of HTML, XML or plain-text responses (2 MB at most). Same guards as every other read.</summary>
    public static async Task<(int Status, Uri Url, string MediaType, string Body, int Redirects)> Probe(string url, IReadOnlyCollection<string> sites, CancellationToken cancellation)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var target) || !Allowed(target, sites))
            throw new InvalidOperationException("That page is not on the research allowlist.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var client = Guarded(sites, "Mozilla/5.0 (compatible; FirstEmployeeSiteCheck/1.0)");
        for (var hop = 0; hop < 5; hop++)
        {
            using var response = await client.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next)
            {
                var moved = next.IsAbsoluteUri ? next : new Uri(target, next);
                if (!Allowed(moved, sites)) return ((int)response.StatusCode, moved, "", "", hop + 1);
                target = moved;
                continue;
            }
            var media = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!(media.StartsWith("text/", StringComparison.Ordinal) || media.EndsWith("xml", StringComparison.Ordinal)) || response.StatusCode != HttpStatusCode.OK)
                return ((int)response.StatusCode, target, media, "", hop);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[2_000_000]; var count = 0;
            while (count < buffer.Length) { var read = await stream.ReadAsync(buffer.AsMemory(count), timeout.Token); if (read == 0) break; count += read; }
            return (200, target, media, Encoding.UTF8.GetString(buffer, 0, count), hop);
        }
        return (310, target, "", "", 5);
    }
}
