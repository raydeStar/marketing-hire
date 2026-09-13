using System.Net;
using System.Text;
using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class PublicSearchTransportTests
{
    private static readonly PublicSearchGrant Grant = new("brave", new string('a', 32));
    private sealed class Credentials : IPublicSearchCredentials
    {
        public int Calls;
        public Task<string> Read(PublicSearchGrant grant, CancellationToken cancellation) { Calls++; return Task.FromResult("fictional-search-key"); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        { Calls++; return Task.FromResult(reply(request)); }
    }
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    [Fact] public async Task KeyUsesOnlyTheFixedProviderAndResultsRemainUntrustedDiscovery()
    {
        var keys = new Credentials();
        var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method); Assert.Equal("api.search.brave.com", request.RequestUri!.Host);
            Assert.Equal("/res/v1/web/search", request.RequestUri.AbsolutePath);
            Assert.Contains("q=public%20raven%20%26%20weather", request.RequestUri.Query);
            Assert.Equal("fictional-search-key", request.Headers.GetValues("X-Subscription-Token").Single());
            Assert.Null(request.Headers.Authorization); Assert.Null(request.Headers.Referrer); Assert.False(request.Headers.Contains("Cookie"));
            return Json("""{"web":{"results":[{"url":"https://docs.example.com/a#x","title":"<b>A &amp; B</b>","description":"<script>bad()</script>Public source"},{"url":"http://example.com/"},{"url":"https://127.0.0.1/"},{"url":"https://user:key@example.com/"},{"url":"https://docs.example.com/a"}]}}""");
        });
        var service = new BravePublicSearch(keys, () => handler);
        await service.Check(Grant, default); Assert.Equal(0, handler.Calls);
        var result = await service.Search("public raven & weather", Grant, default);
        Assert.Null(result.Error); Assert.Equal(1, handler.Calls); Assert.Equal(2, keys.Calls);
        var hit = Assert.Single(result.Results); Assert.Equal("https://docs.example.com/a", hit.Url);
        Assert.Equal("A & B", hit.Title); Assert.Equal("Public source", hit.Description);
        Assert.Equal("untrusted-discovery-data", result.Trust); Assert.Equal(64, result.ResponseSha256!.Length);
        Assert.DoesNotContain("fictional-search-key", Wire.Pack(result));
    }
    [Theory] [InlineData(302)] [InlineData(401)] [InlineData(429)] [InlineData(500)]
    public async Task RedirectsErrorsAndRateLimitsNeverRetryOrFollowCredentials(int status)
    {
        var handler = new Handler(_ => new((HttpStatusCode)status) { Headers = { Location = new("https://elsewhere.example/") } });
        var result = await new BravePublicSearch(new Credentials(), () => handler).Search("public query", Grant, default);
        Assert.NotNull(result.Error); Assert.Empty(result.Results); Assert.Equal(status, result.HttpStatus); Assert.Equal(1, handler.Calls);
    }
    [Fact] public async Task NetworkFailureKeepsAnUnknownOutcome()
    {
        var handler = new Handler(_ => throw new HttpRequestException("Do not expose provider internals."));
        var result = await new BravePublicSearch(new Credentials(), () => handler).Search("public query", Grant, default);
        Assert.True(result.OutcomeUnknown); Assert.DoesNotContain("internals", result.Error); Assert.Equal(1, handler.Calls);
    }
    [Fact] public async Task ResultCountAndSnippetSizeAreBounded()
    {
        var body = Wire.Pack(new { web = new { results = Enumerable.Range(0, 20).Select(i => new { url = $"https://example.com/{i}", title = new string('t', 1000), description = new string('s', 3000) }) } });
        var result = await new BravePublicSearch(new Credentials(), () => new Handler(_ => Json(body))).Search("public query", Grant, default);
        Assert.Equal(5, result.Results.Length); Assert.All(result.Results, hit => { Assert.Equal(200, hit.Title.Length); Assert.Equal(1000, hit.Description.Length); });
    }
    [Theory] [InlineData("[]")] [InlineData("{\"web\":{\"results\":{}}}")] [InlineData("not json")]
    public async Task MalformedResponsesDoNotBecomeSuccessfulSearch(string body)
    {
        var result = await new BravePublicSearch(new Credentials(), () => new Handler(_ => Json(body))).Search("public query", Grant, default);
        Assert.NotNull(result.Error); Assert.Empty(result.Results);
    }
    [Fact] public async Task UnknownLengthResponseCannotExceedTheDecodedByteAllowance()
    {
        var content = new UnknownLength(new byte[BravePublicSearch.MaxBytes + 10]); content.Headers.ContentType = new("application/json");
        Assert.Null(content.Headers.ContentLength);
        var result = await new BravePublicSearch(new Credentials(), () => new Handler(_ => new(HttpStatusCode.OK) { Content = content })).Search("public query", Grant, default);
        Assert.Equal(BravePublicSearch.MaxBytes + 1, result.ResponseBytes); Assert.NotNull(result.Error);
    }
    private sealed class UnknownLength(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes));
    }
    [Fact] public async Task InvalidQueriesAndCredentialReferencesFailBeforeProviderAccess()
    {
        var keys = new Credentials(); var service = new BravePublicSearch(keys, () => throw new Exception("No HTTP handler may be created"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.Search("private\nquery", Grant, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.Search("public query", Grant with { CredentialId = "foreign" }, default));
        Assert.Equal(0, keys.Calls);
        const string legacy = "{\"hosts\":[\"docs.example.com\"],\"maxFetches\":4}";
        Assert.Equal(legacy, Wire.Pack(Wire.Unpack<PublicWebScope>(legacy)));
    }
}

public sealed class PublicSearchCapabilityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-search-" + Guid.NewGuid().ToString("N"));
    private Store store; private Runtime runtime; private readonly Searcher search = new(); private readonly Reader reader = new();
    public PublicSearchCapabilityTests() { store = new(root); runtime = NewRuntime(); }
    private Runtime NewRuntime() => new(store, _ => throw new Exception("No model dispatch"), new PlanValidator(), new EvidencePolicy(), reader, publicSearch: search);
    private Run Granted(int queries = 2, bool openResults = true)
    {
        var run = CapabilityTests.CreateWorkerRun(store, 20); run.Goal = run.Goal with { Web = new([], 4, new("brave", new string('a', 32), queries, openResults)) };
        store.Save(run, "fixture.search-grant", new { }); return run;
    }
    private Task<CapabilityResult> Search(Run run, string id = "search-1", string query = "public query") => runtime.Call(run.Id, new(id, "thaddeus_search_public_web", JsonSerializer.SerializeToElement(new { query })), default);
    private Task<CapabilityResult> Fetch(Run run, string url, string id = "fetch-1") => runtime.Call(run.Id, new(id, "thaddeus_fetch_public_page", JsonSerializer.SerializeToElement(new { url })), default);
    [Fact] public async Task NoGrantMeansNoSearchToolAndNoProviderRequest()
    {
        var run = CapabilityTests.CreateWorkerRun(store);
        Assert.DoesNotContain(runtime.ToolsFor(run.Id), tool => tool.Name == "thaddeus_search_public_web");
        Assert.True((await Search(run)).IsError); Assert.Equal(0, search.Calls);
    }
    [Fact] public async Task SearchIntentAndReplaySurviveRestartWithoutRepeatingTheProviderRequest()
    {
        var run = Granted(); Assert.Contains(runtime.ToolsFor(run.Id), tool => tool.Name == "thaddeus_search_public_web");
        search.Before = () => Assert.Equal("broker-reserved", store.Get(run.Id)!.Capabilities.Single().Authority);
        var first = await Search(run); Assert.False(first.IsError);
        store.Dispose(); store = new(root); runtime = NewRuntime(); runtime.Recover();
        Assert.True(JsonElement.DeepEquals(first.Value, (await Search(run)).Value)); Assert.Equal(1, search.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Search(run, query: "changed query"));
        Assert.Equal(1, store.Get(run.Id)!.ToolCalls);
    }
    [Fact] public async Task SearchCannotExceedItsAllowanceOrExpandRetrievalToUnseenPaths()
    {
        var run = Granted(1); Assert.False((await Search(run)).IsError); Assert.True((await Search(run, "search-2")).IsError); Assert.Equal(1, search.Calls);
        Assert.True((await Fetch(run, "https://docs.example.com/other")).IsError);
        Assert.True((await Fetch(run, "https://other.example.com/page", "fetch-2")).IsError);
        Assert.False((await Fetch(run, "https://docs.example.com/page", "fetch-3")).IsError);
        Assert.Equal(1, reader.Calls); Assert.Equal(["docs.example.com"], reader.Scope!.Hosts);
    }
    [Fact] public async Task OpeningResultsRequiresItsOwnGrantAndSearchSnippetsAreNotQuotationEvidence()
    {
        var run = Granted(openResults: false); await Search(run);
        Assert.True((await Fetch(run, "https://docs.example.com/page")).IsError); Assert.Equal(0, reader.Calls);
        var persisted = store.Get(run.Id)!; persisted.Goal = persisted.Goal with { ReadScope = [] };
        var result = new ProposalEvidenceValidator().Assess(persisted, "Discovery hint https://docs.example.com/page", [new("https://docs.example.com/page", Wire.Hash("Discovery hint"), "Discovery hint")]);
        Assert.False(result.Passed);
    }
    [Fact] public async Task CrashedSearchStaysChargedAndCannotGrantAnyPageAccess()
    {
        var run = Granted(1); search.Before = () => throw new ApplicationException("Fixture crash after persisted intent");
        await Assert.ThrowsAsync<ApplicationException>(() => Search(run));
        store.Dispose(); store = new(root); runtime = NewRuntime(); runtime.Recover();
        Assert.True((await Search(run)).IsError); Assert.Equal(1, search.Calls);
        Assert.Throws<ArgumentException>(() => PublicSearchAccess.RetrievalScope(store.Get(run.Id)!, "https://docs.example.com/page"));
    }
    [Fact] public async Task FetchedSearchResultCanSupplyCapturedEvidenceAndSurvivesStoreReload()
    {
        var run = Granted(); await Search(run); await Fetch(run, "https://docs.example.com/page");
        var persisted = store.Get(run.Id)!; persisted.Goal = persisted.Goal with { ReadScope = [] };
        var result = new ProposalEvidenceValidator().Assess(persisted, "Captured quotation https://docs.example.com/page", [new("https://docs.example.com/page", Wire.Hash("Captured quotation"), "Captured quotation")]);
        Assert.True(result.Passed);
        store.Dispose(); store = new(root);
        Assert.Equal("docs.example.com", PublicSearchAccess.RetrievalScope(store.Get(run.Id)!, "https://docs.example.com/page").Hosts.Single());
    }
    private sealed class Searcher : IPublicSearch
    {
        public int Calls; public Action? Before;
        public Task Check(PublicSearchGrant grant, CancellationToken cancellation) => Task.CompletedTask;
        public Task<PublicSearchResult> Search(string query, PublicSearchGrant grant, CancellationToken cancellation)
        { Calls++; Before?.Invoke(); return Task.FromResult(new PublicSearchResult("brave", query, DateTimeOffset.UtcNow, [new("https://docs.example.com/page", "Source", "Discovery hint")], 200, 42, Wire.Hash("fixture"))); }
    }
    private sealed class Reader : IPublicWebReader
    {
        public int Calls; public PublicWebScope? Scope;
        public Task<PublicWebResult> Read(string url, PublicWebScope scope, CancellationToken cancellation)
        { Calls++; Scope = scope; return Task.FromResult(new PublicWebResult(new(url, "Source", DateTimeOffset.UtcNow, "text/plain", 18, Wire.Hash("Captured quotation"), "Captured quotation", Wire.Hash("Captured quotation"), false), [new(url, 200)])); }
    }
    public void Dispose() { store.Dispose(); Directory.Delete(root, true); }
}
