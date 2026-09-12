using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class PublicWebTests
{
    private static readonly PublicWebScope Scope = new(["docs.example.com", "www.example.com"]);
    [Theory]
    [InlineData("0.0.0.0")][InlineData("10.1.2.3")][InlineData("100.100.100.200")][InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")][InlineData("172.31.255.254")][InlineData("192.168.1.1")][InlineData("192.0.0.8")]
    [InlineData("192.0.2.1")][InlineData("192.88.99.1")][InlineData("198.18.0.1")][InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")][InlineData("224.0.0.1")][InlineData("255.255.255.255")]
    [InlineData("::1")][InlineData("::ffff:127.0.0.1")][InlineData("64:ff9b::a00:1")][InlineData("fd00::1")]
    [InlineData("fe80::1")][InlineData("ff02::1")][InlineData("2001:db8::1")][InlineData("2002:7f00:1::1")][InlineData("3fff::1")]
    public void InternalSpecialAndTranslationAddressesAreDenied(string ip) => Assert.False(PublicWebNetwork.IsPublic(IPAddress.Parse(ip)));
    [Theory][InlineData("8.8.8.8")][InlineData("1.1.1.1")][InlineData("2606:4700:4700::1111")][InlineData("2001:4860:4860::8888")]
    public void OrdinaryPublicAddressesAreEligible(string ip) => Assert.True(PublicWebNetwork.IsPublic(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("http://docs.example.com/")][InlineData("https://docs.example.com:5181/")]
    [InlineData("https://user:password@docs.example.com/")][InlineData("https://evil.example.com/")]
    [InlineData("https://docs.example.com.evil.test/")][InlineData("https://127.1/")]
    [InlineData("https://2130706433/")][InlineData("file:///etc/passwd")]
    public void UrlMustMatchTheExactTaskHostAndTransport(string url) => Assert.Throws<ArgumentException>(() => PublicWebNetwork.Destination(url, Scope));

    [Fact] public async Task MixedDnsAnswersAreRefusedBeforeAnySocketOpens()
    {
        var dials = 0;
        await Assert.ThrowsAsync<HttpRequestException>(async () => await PublicWebNetwork.Connect(new("docs.example.com", 443),
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("1.1.1.1"), IPAddress.Loopback }),
            (_, _) => { dials++; return ValueTask.FromResult<Stream>(new MemoryStream()); }, default));
        Assert.Equal(0, dials);
    }
    [Fact] public async Task ValidatedIpIsDialledWithoutASecondDnsResolution()
    {
        var lookups = 0; IPEndPoint? dialled = null;
        await using var stream = await PublicWebNetwork.Connect(new("docs.example.com", 443),
            (_, _) => Task.FromResult(new[] { ++lookups == 1 ? IPAddress.Parse("1.1.1.1") : IPAddress.Loopback }),
            (endpoint, _) => { dialled = endpoint; return ValueTask.FromResult<Stream>(new MemoryStream()); }, default);
        Assert.Equal(1, lookups); Assert.Equal("1.1.1.1", dialled!.Address.ToString()); Assert.Equal(443, dialled.Port);
    }
    [Fact] public void ProductionTransportHasNoAmbientAuthenticationProxyOrRedirects()
    {
        using var handler = PublicWebNetwork.Handler();
        Assert.False(handler.UseCookies); Assert.False(handler.UseProxy); Assert.False(handler.PreAuthenticate);
        Assert.False(handler.AllowAutoRedirect); Assert.Null(handler.Credentials); Assert.NotNull(handler.ConnectCallback);
    }
    [Fact] public async Task HtmlProducesHashedUntrustedTextWithoutFollowingEmbeddedResources()
    {
        const string html = "<title>A public title</title><script>secret script</script><nav>menu</nav><main><h1>Research</h1><p>Observed &amp; attributed.</p><img src='https://127.0.0.1/'><div hidden>invisible</div></main>";
        var handler = new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method); Assert.Equal(HttpVersion.Version11, request.Version);
            Assert.Null(request.Headers.Authorization); Assert.Null(request.Headers.Referrer); Assert.False(request.Headers.Contains("Cookie"));
            return Task.FromResult(Page(html, "text/html"));
        });
        var result = await new PublicWebReader(() => handler).Read("https://docs.example.com/page#section", Scope, default);
        Assert.Null(result.Error); Assert.Equal(1, handler.Calls); Assert.Single(result.Hops);
        var source = result.Source!;
        Assert.Equal("https://docs.example.com/page", source.Url); Assert.Equal("A public title", source.Title);
        Assert.Contains("Observed & attributed.", source.Text); Assert.DoesNotContain("secret script", source.Text); Assert.DoesNotContain("invisible", source.Text);
        Assert.Equal(Wire.Hash(html), source.ResponseSha256); Assert.Equal(Wire.Hash(source.Text), source.TextSha256);
        Assert.Equal("untrusted-source-data", source.Trust); Assert.False(source.Truncated);
    }
    [Theory][InlineData("https://127.0.0.1/private")][InlineData("http://docs.example.com/plain")][InlineData("https://ungranted.example.com/")]
    public async Task RedirectCannotLeaveItsHostGrantOrChangeTransport(string location)
    {
        var handler = new Handler((_, _) => Task.FromResult(Redirect(location)));
        var result = await new PublicWebReader(() => handler).Read("https://docs.example.com/", Scope, default);
        Assert.Null(result.Source); Assert.NotNull(result.Error); Assert.Equal(1, handler.Calls);
    }
    [Fact] public async Task AllowedRedirectRetainsBothUrlsAndFinalSource()
    {
        var handler = new Handler((request, _) => Task.FromResult(request.RequestUri!.Host == "docs.example.com" ? Redirect("https://www.example.com/final") : Page("Final source", "text/plain")));
        var result = await new PublicWebReader(() => handler).Read("https://docs.example.com/", Scope, default);
        Assert.Equal(2, result.Hops.Length); Assert.Equal("https://www.example.com/final", result.Source!.Url);
    }
    [Fact] public async Task RedirectLoopHasARequestCeiling()
    {
        var handler = new Handler((_, _) => Task.FromResult(Redirect("https://docs.example.com/loop")));
        var result = await new PublicWebReader(() => handler).Read("https://docs.example.com/", Scope, default);
        Assert.Null(result.Source); Assert.Equal(PublicWebReader.MaxRedirects + 1, handler.Calls);
    }
    [Fact] public async Task ChunkedOversizeAndUnsupportedTypesCannotProduceSources()
    {
        var decoded = new NonSeekableStream(new byte[PublicWebReader.MaxBytes + 1]);
        var oversized = new Handler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(decoded) };
            response.Content.Headers.ContentType = new("text/plain"); response.Content.Headers.ContentLength = null;
            Assert.Null(response.Content.Headers.ContentLength);
            return Task.FromResult(response);
        });
        Assert.Null((await new PublicWebReader(() => oversized).Read("https://docs.example.com/", Scope, default)).Source);
        Assert.Equal(PublicWebReader.MaxBytes + 1, decoded.BytesRead);
        var binary = new Handler((_, _) => Task.FromResult(Page("binary", "application/pdf")));
        Assert.Null((await new PublicWebReader(() => binary).Read("https://docs.example.com/", Scope, default)).Source);
    }
    [Fact] public async Task TruncationIsExplicitAndRetainsTheCompleteDecodedBodyHash()
    {
        var original = new string('x', PublicWebReader.MaxCharacters + 1);
        var handler = new Handler((_, _) => Task.FromResult(Page(original, "text/plain")));
        var result = await new PublicWebReader(() => handler).Read("https://docs.example.com/", Scope, default);
        Assert.True(result.Source!.Truncated); Assert.Equal(PublicWebReader.MaxCharacters, result.Source.Text.Length);
        Assert.Equal(Wire.Hash(original), result.Source.ResponseSha256);
    }
    [Fact] public async Task CancellationProducesNoSourceOrAutomaticRetry()
    {
        var handler = new Handler((_, _) => throw new OperationCanceledException());
        var result = await new PublicWebReader(() => handler).Read("https://docs.example.com/", Scope, default);
        Assert.Null(result.Source); Assert.NotNull(result.Error); Assert.Equal(1, handler.Calls);
    }

    private static HttpResponseMessage Page(string text, string media) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, media) };
    private static HttpResponseMessage Redirect(string location)
    { var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new(location); return response; }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) { Calls++; return send(request, cancellation); }
    }
    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes);
        public int BytesRead;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { var read = inner.Read(buffer, offset, count); BytesRead += read; return read; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellation = default)
        { cancellation.ThrowIfCancellationRequested(); var read = inner.Read(buffer.Span); BytesRead += read; return ValueTask.FromResult(read); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}

public sealed class PublicWebCapabilityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-public-capability-" + Guid.NewGuid().ToString("N"));
    private Store store;
    private Runtime runtime;
    private readonly Reader reader = new();
    public PublicWebCapabilityTests() { store = new(root); runtime = NewRuntime(); }
    private Runtime NewRuntime() => new(store, _ => throw new Exception("No model dispatch"), new PlanValidator(), new EvidencePolicy(), reader);
    private Run Granted(int fetches = 2)
    {
        var run = CapabilityTests.CreateWorkerRun(store);
        run.Goal = run.Goal with { Web = new(["docs.example.com"], fetches) };
        store.Save(run, "fixture.public-grant", new { }); return run;
    }
    private Task<CapabilityResult> Fetch(Run run, string id = "fetch-1", string url = "https://docs.example.com/page") =>
        runtime.Call(run.Id, new(id, "thaddeus_fetch_public_page", JsonSerializer.SerializeToElement(new { url })), default);
    [Fact] public async Task NoTaskGrantMeansNoAdvertisedToolAndNoNetworkDispatch()
    {
        var run = CapabilityTests.CreateWorkerRun(store);
        Assert.DoesNotContain(runtime.ToolsFor(run.Id), tool => tool.Name == "thaddeus_fetch_public_page");
        Assert.True((await Fetch(run)).IsError); Assert.Equal(0, reader.Calls);
    }
    [Fact] public async Task ExplicitGrantAdvertisesToolAndDurableReplayDoesNotRetrieveAgain()
    {
        var run = Granted();
        Assert.Contains(runtime.ToolsFor(run.Id), tool => tool.Name == "thaddeus_fetch_public_page");
        reader.Before = () => Assert.Contains(store.Get(run.Id)!.Capabilities, receipt => receipt.OperationId == "fetch-1" && receipt.Authority == "broker-reserved");
        var first = await Fetch(run);
        store.Dispose(); store = new(root); runtime = NewRuntime(); runtime.Recover();
        var again = await Fetch(run);
        Assert.True(JsonElement.DeepEquals(first.Value, again.Value)); Assert.False(first.IsError);
        Assert.Equal(1, reader.Calls); Assert.Equal(1, store.Get(run.Id)!.ToolCalls);
        Assert.Null(store.Get(run.Id)!.Validation);
    }
    [Fact] public async Task OutOfScopeAndExhaustedFetchesNeverReachTheNetwork()
    {
        var run = Granted(1);
        Assert.False((await Fetch(run)).IsError);
        Assert.True((await Fetch(run, "fetch-2")).IsError);
        Assert.True((await Fetch(run, "outside", "https://private.example.com/")).IsError);
        Assert.Equal(1, reader.Calls);
    }
    [Fact] public async Task AProcessCrashAfterIntentRemainsUnknownAndCannotBeReplayed()
    {
        var run = Granted(); reader.Before = () => throw new ApplicationException("fixture process stopped after durable intent");
        await Assert.ThrowsAsync<ApplicationException>(() => Fetch(run));
        store.Dispose(); store = new(root); runtime = NewRuntime(); runtime.Recover();
        Assert.True((await Fetch(run)).IsError); Assert.Equal(1, reader.Calls);
        Assert.Contains("retrieval-outcome-unknown", store.Get(run.Id)!.Capabilities.Single().Result.GetRawText());
    }
    private sealed class Reader : IPublicWebReader
    {
        public int Calls; public Action? Before;
        public Task<PublicWebResult> Read(string url, PublicWebScope scope, CancellationToken cancellation)
        {
            Calls++; Before?.Invoke();
            return Task.FromResult(new PublicWebResult(new(url, "Fictional public source", DateTimeOffset.UtcNow, "text/plain", 7,
                Wire.Hash("source"), "source", Wire.Hash("source"), false), [new(url, 200)]));
        }
    }
    public void Dispose() { store.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
}
