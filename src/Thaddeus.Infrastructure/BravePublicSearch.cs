using System.Net;
using System.Text;
using System.Text.Json;
using AngleSharp.Html.Parser;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed class BravePublicSearch(IPublicSearchCredentials credentials, Func<HttpMessageHandler>? handlers = null) : IPublicSearch
{
    public const string Endpoint = "https://api.search.brave.com/res/v1/web/search";
    public const int MaxBytes = 1_000_000, MaxResults = 5;
    public async Task Check(PublicSearchGrant grant, CancellationToken cancellation)
    { PublicSearchAccess.Validate(grant); _ = await credentials.Read(grant, cancellation); }
    public async Task<PublicSearchResult> Search(string query, PublicSearchGrant grant, CancellationToken cancellation)
    {
        PublicSearchAccess.Validate(grant); query = PublicSearchAccess.Query(query);
        // Resolve the frozen key outside the worker. No provider auto-detection, ambient key, redirect or automatic retry.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var key = await credentials.Read(grant, deadline.Token);
        if (string.IsNullOrWhiteSpace(key) || key.Length > 2048 || key.Any(c => c is < '!' or > '~')) throw new InvalidOperationException("The saved search key is invalid.");
        using var client = new HttpClient((handlers ?? PublicWebNetwork.Handler)()) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint + "?q=" + Uri.EscapeDataString(query) + "&count=5&text_decorations=false")
        { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        request.Headers.Add("X-Subscription-Token", key); request.Headers.Accept.ParseAdd("application/json");
        int? status = null; var bytesRead = 0; string? hash = null;
        PublicSearchResult Failure(string error, bool unknown = false) => new(grant.Provider, query, DateTimeOffset.UtcNow, [], status, bytesRead, hash, error, unknown);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            status = (int)response.StatusCode;
            if (response.StatusCode != HttpStatusCode.OK) return Failure("Search did not return HTTP 200. Check the saved key, provider quota and service status. This request will not retry automatically.");
            if (response.Content.Headers.ContentType?.MediaType != "application/json") return Failure("Search returned an unsupported content type.");
            if (response.Content.Headers.ContentLength > MaxBytes) return Failure("Search response exceeds its byte limit.");
            await using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream(); var chunk = new byte[8192];
            while (true)
            {
                var count = await input.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, MaxBytes + 1 - bytesRead)), deadline.Token);
                if (count == 0) break;
                buffer.Write(chunk, 0, count); bytesRead += count;
                if (bytesRead > MaxBytes) return Failure("Search response exceeds its decoded byte limit.");
            }
            var body = buffer.ToArray(); hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(body));
            using var document = JsonDocument.Parse(body, new() { MaxDepth = 32 });
            if (!document.RootElement.TryGetProperty("web", out var web) || !web.TryGetProperty("results", out var results))
                return new(grant.Provider, query, DateTimeOffset.UtcNow, [], status, bytesRead, hash);
            if (results.ValueKind != JsonValueKind.Array) return Failure("Search returned invalid result data.");
            var hits = new List<PublicSearchHit>();
            foreach (var result in results.EnumerateArray())
            {
                if (hits.Count == MaxResults) break;
                if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("url", out var address) || address.ValueKind != JsonValueKind.String) continue;
                Uri url;
                try { url = PublicSearchAccess.ResultUrl(address.GetString()!); } catch (ArgumentException) { continue; }
                if (hits.Any(hit => hit.Url == url.AbsoluteUri)) continue;
                hits.Add(new(url.AbsoluteUri, Plain(result, "title", 200), Plain(result, "description", 1000)));
            }
            return new(grant.Provider, query, DateTimeOffset.UtcNow, hits.ToArray(), status, bytesRead, hash);
        }
        catch (OperationCanceledException) { return Failure("Search was interrupted. Its provider outcome is unknown; no automatic retry.", true); }
        catch (HttpRequestException) { return Failure("Search could not be completed. Its provider outcome is unknown; no automatic retry.", true); }
        catch (IOException) { return Failure("Search response was interrupted. Its provider outcome is unknown; no automatic retry.", true); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or DecoderFallbackException)
        { return Failure("Search returned unreadable result data."); }
    }
    private static string Plain(JsonElement result, string field, int limit)
    {
        if (!result.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String) return "";
        var original = value.GetString()!;
        using var document = new HtmlParser(new() { IsScripting = false }).ParseDocument(original[..Math.Min(original.Length, 12000)]);
        foreach (var element in document.QuerySelectorAll("script,style,iframe,object,template")) element.Remove();
        var text = document.Body?.TextContent.Trim() ?? "";
        return text[..Math.Min(text.Length, limit)];
    }
}
