using System.Text.Json;
using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

/// <summary>Discovery grants can open recorded result pages, never an invented address or a broader host.</summary>
public static class PublicSearchAccess
{
    public static void Validate(PublicSearchGrant grant)
    {
        if (grant.Provider != "brave" || !Regex.IsMatch(grant.CredentialId ?? "", "\\A[a-f0-9]{32}\\z") || grant.MaxQueries is < 1 or > 4)
            throw new ArgumentException("Choose a saved Brave connection and one to four search requests.");
    }
    public static string Query(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 400 || query.Any(char.IsControl))
            throw new ArgumentException("Use a search query of 1–400 characters without control characters.");
        return query.Trim();
    }
    public static Uri ResultUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)) throw new ArgumentException("Invalid public result URL.");
        return PublicWebNetwork.Destination(value, new([parsed.IdnHost]));
    }
    public static PublicWebScope RetrievalScope(Run run, string url)
    {
        var scope = run.Goal.Web ?? throw new InvalidOperationException("Public research is not granted to this task.");
        PublicWebNetwork.ValidateScope(scope);
        var destination = ResultUrl(url);
        if (scope.Hosts.Contains(destination.IdnHost, StringComparer.OrdinalIgnoreCase)) return scope;
        if (scope.Search is not { OpenResults: true } grant) throw new ArgumentException("This URL is outside the task's public website grant.");
        foreach (var receipt in run.Capabilities.Where(receipt => receipt.Name == "thaddeus_search_public_web" && !receipt.IsError && receipt.Authority == "broker-observed"))
        {
            var result = receipt.Result.Deserialize<PublicSearchResult>(Wire.Json);
            if (result is { Error: null, OutcomeUnknown: false } && result.Provider == grant.Provider &&
                result.Results.Any(hit => hit.Url == destination.AbsoluteUri))
                // A result permits this exact initial URL. Its redirects stay on this host; each connection is rechecked.
                return new([destination.IdnHost], scope.MaxFetches);
        }
        throw new ArgumentException("Search first, then open an exact URL returned in this task's recorded results. Other pages require an explicit website grant.");
    }
}
