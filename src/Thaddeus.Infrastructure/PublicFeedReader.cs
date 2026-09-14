using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public record FeedFetch(string Url, FeedContent? Feed = null, FeedCandidate[]? Candidates = null,
    bool NotModified = false, string? ETag = null, DateTimeOffset? LastModified = null,
    int RefreshMinutes = 60, string? Error = null);
public interface IPublicFeedReader
{
    Task<FeedFetch> Read(string url, bool discover, string? validatorUrl, string? etag, DateTimeOffset? modified, CancellationToken cancellation);
}

public sealed class PublicFeedReader : IPublicFeedReader
{
    private readonly Func<HttpMessageHandler> handlers;
    public PublicFeedReader() : this(PublicWebNetwork.Handler) { }
    public PublicFeedReader(Func<HttpMessageHandler> handlers) => this.handlers = handlers;

    public async Task<FeedFetch> Read(string url, bool discover, string? validatorUrl, string? etag, DateTimeOffset? modified, CancellationToken cancellation)
    {
        var current = FeedParser.SourceUrl(url);
        var scope = new PublicWebScope([current.IdnHost]);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var client = new HttpClient(handlers()) { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            for (var redirects = 0; ; redirects++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current)
                { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
                request.Headers.UserAgent.ParseAdd("Thaddeus/0.1 (subscribed feeds)");
                request.Headers.Accept.ParseAdd("application/atom+xml, application/rss+xml, application/xml, text/xml, text/html;q=0.5");
                // A validator belongs to one representation, not every page in the estate.
                if (current.AbsoluteUri == validatorUrl)
                {
                    if (etag?.Length <= 512 && EntityTagHeaderValue.TryParse(etag, out var tag)) request.Headers.IfNoneMatch.Add(tag);
                    if (modified != null) request.Headers.IfModifiedSince = modified;
                }
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    if (redirects >= 2 || response.Headers.Location == null) return new(current.AbsoluteUri, Error: "The feed exceeded its redirect limit.");
                    current = PublicWebNetwork.Destination(new Uri(current, response.Headers.Location).AbsoluteUri, scope);
                    continue;
                }
                var interval = 60;
                var cache = response.Headers.CacheControl?.MaxAge;
                var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
                foreach (var delay in new[] { cache, retry })
                    if (delay is { } value) interval = Math.Max(interval, (int)Math.Clamp(Math.Ceiling(value.TotalMinutes), 60, 1440));
                var result = new FeedFetch(current.AbsoluteUri, ETag: response.Headers.ETag?.ToString() is { Length: <= 512 } valueTag ? valueTag : null,
                    LastModified: response.Content.Headers.LastModified, RefreshMinutes: interval);
                if (response.StatusCode == HttpStatusCode.NotModified)
                    return request.Headers.IfNoneMatch.Count > 0 || request.Headers.IfModifiedSince != null
                        ? result with { NotModified = true } : result with { Error = "The source returned an unexpected unchanged response." };
                if (response.StatusCode != HttpStatusCode.OK) return result with { Error = $"The source returned HTTP {(int)response.StatusCode}." };
                var media = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
                if (media is not ("application/atom+xml" or "application/rss+xml" or "application/xml" or "text/xml" or "text/html"))
                    return result with { Error = "Use an RSS 2.0 or Atom feed, or an HTML site with a feed link." };
                if (response.Content.Headers.ContentLength > FeedParser.MaxBytes) return result with { Error = "The source exceeds the one-megabyte download limit." };
                await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                using var buffer = new MemoryStream(); var chunk = new byte[8192];
                while (true)
                {
                    var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, FeedParser.MaxBytes + 1 - (int)buffer.Length)), deadline.Token);
                    if (read == 0) break;
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > FeedParser.MaxBytes) return result with { Error = "The decoded source exceeds the one-megabyte download limit." };
                }
                if (media == "text/html")
                {
                    if (!discover) return result with { Error = "This address now returns a website. Add its RSS or Atom feed address again." };
                    var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"').ToLowerInvariant();
                    if (charset is not (null or "utf-8" or "utf8" or "us-ascii")) return result with { Error = "Website discovery currently supports UTF-8 pages." };
                    var candidates = FeedParser.Discover(new UTF8Encoding(false, true).GetString(buffer.ToArray()), current);
                    return result with { Candidates = candidates, Error = candidates.Length == 0 ? "No RSS or Atom feed link was found. Paste the feed address directly." : null };
                }
                var feed = FeedParser.Parse(buffer.ToArray(), current, deadline.Token);
                return result with { Feed = feed, RefreshMinutes = Math.Max(interval, feed.RefreshMinutes ?? 60) };
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { return new(current.AbsoluteUri, Error: "The source took longer than 15 seconds. It will be checked later."); }
        catch (Exception error) when (error is HttpRequestException or IOException or ArgumentException or UriFormatException or XmlException)
        { return new(current.AbsoluteUri, Error: "The source failed its public-network, redirect or feed-format checks."); }
    }
}
