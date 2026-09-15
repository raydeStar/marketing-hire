using System.Text.Json;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Runtime
{
    private async Task<PublicSearchResult> SearchPublicWeb(Run run, CapabilityCall call, string requestHash, CancellationToken cancellation)
    {
        Fields(call.Arguments, "query"); var query = PublicSearchAccess.Query(Text(call.Arguments, "query", 400));
        if (publicSearch == null || run.Goal.Kind != "research" || run.Goal.Web?.Search is not { } grant)
            throw new InvalidOperationException("Public search is not granted to this task.");
        PublicSearchAccess.Validate(grant);
        if (run.Capabilities.Count(receipt => receipt.Name == call.Name) >= grant.MaxQueries)
            throw new InvalidOperationException("The task's search allowance is exhausted.");
        var pending = new CapabilityReceipt(call.OperationId, requestHash, call.Name, "broker-reserved", DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new { status = "search-outcome-unknown", query, provider = grant.Provider,
                instruction = "Search intent was saved. This operation remains charged and must not be replayed automatically." }), true);
        store.ReservePublicSearch(run, pending);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(RemainingExecutionTime(run)); cancellations[run.Id] = deadline;
        try { return await publicSearch.Search(query, grant, deadline.Token); }
        catch (Exception error) when (error is IOException or HttpRequestException or OperationCanceledException)
        { throw new InvalidOperationException("Search outcome is unknown. This operation remains charged; no automatic retry."); }
        finally { cancellations.TryRemove(run.Id, out _); }
    }
}
