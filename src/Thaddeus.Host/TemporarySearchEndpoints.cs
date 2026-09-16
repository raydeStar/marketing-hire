using Thaddeus.Core;
using Thaddeus.Infrastructure;
namespace Thaddeus.Host;

public record TemporarySearchRequest(string Query);
public sealed class TemporaryPublicSearch(SearchConnections connections, Func<HttpMessageHandler>? handlers=null)
{
    private sealed class Credentials(SearchConnections source):IPublicSearchCredentials
    { public Task<string> Read(PublicSearchGrant grant,CancellationToken cancellation)=>source.ReadTemporary(grant,cancellation); }
    public IPublicSearch Create()=>new BravePublicSearch(new Credentials(connections),handlers);
}
public static class TemporarySearchEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/search/temporary",async (TemporarySearchRequest request,HttpContext context,Store store,SearchConnections connections,TemporaryPublicSearch provider)=>
        {
            var query=PublicSearchAccess.Query(request.Query);
            var grant=new PublicSearchGrant("brave",connections.SelectedId??throw new InvalidOperationException("Save a Brave key in Settings → Connections first."),1);
            var search=provider.Create();
            await search.Check(grant,context.RequestAborted);
            store.ReserveTemporarySearch();
            var result=await search.Search(query,grant,context.RequestAborted);
            // No query, result URL, snippet, or body hash is written into the ledger.
            context.Response.Headers.CacheControl="no-store";
            return Results.Ok(new {result.Results,result.Error,remaining=store.SearchBudget().Remaining});
        });
    }
}
