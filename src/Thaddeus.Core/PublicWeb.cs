using System.Text.Json.Serialization;

namespace Thaddeus.Core;

// A task receives an explicit list of public hosts. Neither source text nor experimental policy can expand it.
public record PublicWebScope(string[] Hosts, int MaxFetches = 4,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PublicSearchGrant? Search = null);
public record PublicSearchGrant(string Provider, string CredentialId, int MaxQueries = 3, bool OpenResults = false);
public record PublicSearchHit(string Url, string Title, string Description);
public record PublicSearchResult(string Provider, string Query, DateTimeOffset Retrieved, PublicSearchHit[] Results,
    int? HttpStatus, int ResponseBytes, string? ResponseSha256, string? Error = null, bool OutcomeUnknown = false,
    string Trust = "untrusted-discovery-data");
public interface IPublicSearch
{
    Task Check(PublicSearchGrant grant, CancellationToken cancellation);
    Task<PublicSearchResult> Search(string query, PublicSearchGrant grant, CancellationToken cancellation);
}
public interface IPublicSearchCredentials
{
    Task<string> Read(PublicSearchGrant grant, CancellationToken cancellation);
}
public record PublicWebHop(string Url, int? Status = null);
public record PublicWebSource(string Url, string Title, DateTimeOffset Retrieved, string MediaType,
    int ResponseBytes, string ResponseSha256, string Text, string TextSha256, bool Truncated,
    string Authority = "retrieved-public-content", string Trust = "untrusted-source-data");
public record PublicWebResult(PublicWebSource? Source, PublicWebHop[] Hops, string? Error = null);
public interface IPublicWebReader
{
    Task<PublicWebResult> Read(string url, PublicWebScope scope, CancellationToken cancellation);
}
