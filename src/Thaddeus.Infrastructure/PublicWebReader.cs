using System.Net;
using System.Security.Cryptography;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed class PublicWebReader : IPublicWebReader
{
    public const int MaxBytes = 1_000_000, MaxCharacters = 12_000, MaxRedirects = 2;
    private readonly Func<HttpMessageHandler> handlers;
    public PublicWebReader() : this(PublicWebNetwork.Handler) { }
    // Dependency injection is for deterministic transport tests; there is no setting that bypasses network validation.
    public PublicWebReader(Func<HttpMessageHandler> handlers) { this.handlers = handlers; }

    public async Task<PublicWebResult> Read(string url, PublicWebScope scope, CancellationToken cancellation)
    {
        var current = PublicWebNetwork.Destination(url, scope);
        var hops = new List<PublicWebHop>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var client = new HttpClient(handlers()) { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            for (var redirect = 0; ; redirect++)
            {
                hops.Add(new(current.AbsoluteUri));
                using var request = new HttpRequestMessage(HttpMethod.Get, current)
                { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
                request.Headers.UserAgent.ParseAdd("Thaddeus/0.1 (public research)");
                request.Headers.Accept.ParseAdd("text/html, text/plain, text/markdown");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                hops[^1] = hops[^1] with { Status = (int)response.StatusCode };
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    if (redirect >= MaxRedirects || response.Headers.Location == null)
                        return new(null, hops.ToArray(), "Redirect limit reached or redirect location missing.");
                    current = PublicWebNetwork.Destination(new Uri(current, response.Headers.Location).AbsoluteUri, scope);
                    continue;
                }
                if (response.StatusCode != HttpStatusCode.OK) return new(null, hops.ToArray(), "The public page did not return HTTP 200.");
                var media = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
                var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"').ToLowerInvariant();
                if (media is not ("text/html" or "text/plain" or "text/markdown") || charset is not (null or "utf-8" or "utf8" or "us-ascii"))
                    return new(null, hops.ToArray(), "Only UTF-8 HTML, plain text and Markdown pages are supported.");
                if (response.Content.Headers.ContentLength > MaxBytes) return new(null, hops.ToArray(), "Public page exceeds the response byte limit.");
                await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                using var buffer = new MemoryStream(); var chunk = new byte[8192];
                while (true)
                {
                    var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, MaxBytes + 1 - (int)buffer.Length)), deadline.Token);
                    if (read == 0) break;
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > MaxBytes) return new(null, hops.ToArray(), "Public page exceeds the decoded response byte limit.");
                }
                var bytes = buffer.ToArray();
                var decoded = new UTF8Encoding(false, true).GetString(bytes);
                var title = current.IdnHost;
                string text;
                if (media == "text/html")
                {
                    // A parser only: no browser, script engine, loader, cookies or subresource requests.
                    using var document = await new HtmlParser(new() { IsScripting = false }).ParseDocumentAsync(decoded, deadline.Token);
                    title = string.IsNullOrWhiteSpace(document.Title) ? title : document.Title.Trim();
                    foreach (var node in document.QuerySelectorAll("script,style,template,noscript,svg,canvas,iframe,object,embed,form,nav,footer,[hidden],[aria-hidden='true']")) node.Remove();
                    text = Extract(document.QuerySelector("main") ?? document.QuerySelector("article") ?? (INode?)document.Body ?? document);
                }
                else text = decoded;
                var truncated = text.Length > MaxCharacters;
                text = text[..Math.Min(text.Length, MaxCharacters)].Trim();
                if (string.IsNullOrWhiteSpace(text)) return new(null, hops.ToArray(), "No readable source text was found.");
                deadline.Token.ThrowIfCancellationRequested();
                var source = new PublicWebSource(current.AbsoluteUri, title[..Math.Min(title.Length, 200)], DateTimeOffset.UtcNow,
                    media, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), text, Wire.Hash(text), truncated);
                return new(source, hops.ToArray());
            }
        }
        catch (OperationCanceledException) { return new(null, hops.ToArray(), "Public retrieval stopped or exceeded its time limit. No automatic retry."); }
        catch (Exception error) when (error is HttpRequestException or IOException or ArgumentException or UriFormatException)
        { return new(null, hops.ToArray(), "Public retrieval failed its network, redirect or text contract. No automatic retry."); }
    }

    private static string Extract(INode root)
    {
        var text = new StringBuilder(); var nodes = new Stack<(INode Node, bool End)>(); nodes.Push((root, false));
        while (nodes.TryPop(out var item) && text.Length <= MaxCharacters)
        {
            if (item.End) { text.Append('\n'); continue; }
            if (item.Node is IText value) { text.Append(value.Data); continue; }
            var block = item.Node is IElement element && element.LocalName is
                "p" or "div" or "br" or "hr" or "li" or "pre" or "blockquote" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "section" or "tr" or "dt" or "dd";
            if (block) { text.Append('\n'); nodes.Push((item.Node, true)); }
            for (var child = item.Node.LastChild; child != null; child = child.PreviousSibling) nodes.Push((child, false));
        }
        if (nodes.Count > 0 && text.Length <= MaxCharacters) text.Append(' ', MaxCharacters + 1 - text.Length);
        return text.ToString();
    }
}
