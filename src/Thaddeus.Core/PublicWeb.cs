namespace Thaddeus.Core;

// A task receives an explicit list of public hosts. Neither source text nor experimental policy can expand it.
public record PublicWebScope(string[] Hosts, int MaxFetches = 4);
public record PublicWebHop(string Url, int? Status = null);
public record PublicWebSource(string Url, string Title, DateTimeOffset Retrieved, string MediaType,
    int ResponseBytes, string ResponseSha256, string Text, string TextSha256, bool Truncated,
    string Authority = "retrieved-public-content", string Trust = "untrusted-source-data");
public record PublicWebResult(PublicWebSource? Source, PublicWebHop[] Hops, string? Error = null);
public interface IPublicWebReader
{
    Task<PublicWebResult> Read(string url, PublicWebScope scope, CancellationToken cancellation);
}
