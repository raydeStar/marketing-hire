using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class VmTransportTests
{
    private const string Id = "0123456789abcdef0123456789abcdef";
    private const string Allowed = "/worker/" + Id + "/mcp";
    private static JsonElement Request(string path = Allowed, string method = "POST", string body = "e30=", object? headers = null) =>
        JsonSerializer.SerializeToElement(new { path, method, body, headers = headers ?? new { authorization = "Bearer fixture", accept = "application/json" } });
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Interlocked.Increment(ref Calls); return send(request, token); }
    }
    private static Handler Successful() => new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("fixture") }));

    [Theory]
    [InlineData("/api/state")] [InlineData("/worker/another/mcp")] [InlineData("http://127.0.0.1:5179/api/state")]
    [InlineData(Allowed + "/../v1/chat/completions")] [InlineData(Allowed + "?url=http://example.invalid")]
    [InlineData(Allowed + "/")] [InlineData("//127.0.0.1:5179/api/state")]
    public async Task GuestCannotChooseAnotherHostDestination(string path)
    {
        var handler = Successful(); using var proxy = new VmBrokerProxy(new(Id, 5123), handler);
        await Assert.ThrowsAsync<IOException>(() => proxy.Forward(Request(path), default)); Assert.Equal(0, handler.Calls);
    }
    [Theory] [InlineData("PUT")] [InlineData("CONNECT")] [InlineData("TRACE")] [InlineData("HEAD")]
    public async Task UnlistedMethodsNeverReachHttp(string method)
    {
        var handler = Successful(); using var proxy = new VmBrokerProxy(new(Id, 5123), handler);
        await Assert.ThrowsAsync<IOException>(() => proxy.Forward(Request(method: method), default)); Assert.Equal(0, handler.Calls);
    }
    [Theory] [InlineData("large-body")] [InlineData("invalid-base64")] [InlineData("get-body")]
    [InlineData("large-headers")] [InlineData("large-header")] [InlineData("header-newline")]
    public async Task MalformedOrOversizedRequestsAreRejectedBeforeDispatch(string defect)
    {
        var message = defect switch
        {
            "large-body" => Request(body: Convert.ToBase64String(new byte[150001])),
            "invalid-base64" => Request(body: "??"),
            "get-body" => Request(method: "GET"),
            "large-headers" => Request(headers: new { ignored = new string('x', 16001) }),
            "large-header" => Request(headers: new { authorization = new string('x', 8193) }),
            _ => Request(headers: new { authorization = "Bearer fixture\r\nHost: other" })
        };
        var handler = Successful(); using var proxy = new VmBrokerProxy(new(Id, 5123), handler);
        var error = await Record.ExceptionAsync(() => proxy.Forward(message, default));
        Assert.True(error is IOException or FormatException); Assert.Equal(0, handler.Calls);
    }
    [Fact] public async Task ExactBoundaryBodyAndAllowedHeadersArriveWithoutAmbientGuestAuthority()
    {
        var bytes = Enumerable.Range(0, 150000).Select(i => (byte)(i % 251)).ToArray();
        var handler = new Handler(async (request, token) =>
        {
            Assert.Equal("http://127.0.0.1:5123" + Allowed, request.RequestUri!.AbsoluteUri);
            Assert.Equal(bytes, await request.Content!.ReadAsByteArrayAsync(token));
            Assert.Equal("Bearer fixture", request.Headers.Authorization!.ToString());
            Assert.False(request.Headers.Contains("cookie")); Assert.False(request.Headers.Contains("x-csrf"));
            Assert.Null(request.Headers.Host);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });
        using var proxy = new VmBrokerProxy(new(Id, 5123), handler);
        var reply = await proxy.Forward(Request(body: Convert.ToBase64String(bytes), headers: new Dictionary<string, string>
        { ["authorization"] = "Bearer fixture", ["host"] = "untrusted.invalid", ["cookie"] = "owner=fixture", ["x-csrf"] = "fixture" }), default);
        Assert.Equal(200, reply.status); Assert.Equal(bytes, Convert.FromBase64String(reply.body)); Assert.Equal(1, handler.Calls);
    }
    [Theory] [InlineData(1500000, true)] [InlineData(1500001, false)]
    public async Task BrokerResponseMustFitTheHostBound(int length, bool accepted)
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[length]) }));
        using var proxy = new VmBrokerProxy(new(Id, 5123), handler);
        if (accepted) Assert.Equal(length, Convert.FromBase64String((await proxy.Forward(Request(), default)).body).Length);
        else await Assert.ThrowsAsync<IOException>(() => proxy.Forward(Request(), default));
    }
    [Fact] public async Task HostCancellationReachesAnInFlightBrokerRequest()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); });
        using var proxy = new VmBrokerProxy(new(Id, 5123), handler); using var stop = new CancellationTokenSource();
        var request = proxy.Forward(Request(), stop.Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await stop.CancelAsync(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }
    [Fact] public async Task RealLoopbackProxyNeitherFollowsRedirectsNorRetainsResponseCookies()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build(); var reachedRedirect = 0; var calls = 0;
        app.MapPost(Allowed, (HttpContext context) =>
        {
            Assert.False(context.Request.Headers.ContainsKey("Cookie"));
            context.Response.Headers.SetCookie = "guest-cookie=fixture; Path=/";
            if (Interlocked.Increment(ref calls) == 1) return Results.Redirect("/forbidden");
            return Results.Json(new { ok = true });
        });
        app.MapGet("/forbidden", () => { Interlocked.Increment(ref reachedRedirect); return "must not reach"; });
        await app.StartAsync();
        using var proxy = new VmBrokerProxy(new(Id, new Uri(app.Urls.Single()).Port));
        Assert.Equal(302, (await proxy.Forward(Request(), default)).status);
        Assert.Equal(200, (await proxy.Forward(Request(), default)).status);
        Assert.Equal(0, reachedRedirect); Assert.Equal(2, calls); await app.StopAsync();
    }

    [Fact] public void BrokerQuotaBoundsConcurrencyAndDoesNotReuseOldRequestIdentities()
    {
        var quota = new VmBrokerQuota(); var held = Enumerable.Range(1, 8).Select(i => quota.Admit("http-" + i)).ToArray();
        Assert.Throws<IOException>(() => quota.Admit("http-9")); held[0].Dispose(); held[0].Dispose();
        using var next = quota.Admit("http-9"); Assert.Throws<IOException>(() => quota.Admit("http-10"));
        Assert.Throws<IOException>(() => quota.Admit("http-1")); foreach (var lease in held) lease.Dispose();
    }
    [Fact] public void CompletedRequestsStillCountTowardThePerBootLimit()
    {
        var quota = new VmBrokerQuota(); for (var i = 1; i <= 200; i++) quota.Admit("http-" + i).Dispose();
        Assert.Throws<IOException>(() => quota.Admit("http-201")); Assert.Throws<IOException>(() => quota.Admit("http-1"));
    }
    [Theory] [InlineData("http-0")] [InlineData("http-01")] [InlineData("http-1000000")] [InlineData("other-1")] [InlineData("http-1\n")]
    public void BrokerIdentityHasOneBoundedCanonicalShape(string id) => Assert.Throws<IOException>(() => new VmBrokerQuota().Admit(id));

    private sealed class Chunks(byte[] bytes, int size) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => base.ReadAsync(buffer[..Math.Min(size, buffer.Length)], token);
    }
    [Theory] [InlineData(1)] [InlineData(3)] [InlineData(16384)]
    public async Task FramesPreserveFragmentedUtf8AndMultipleMessages(int chunk)
    {
        using var stream = new Chunks(Encoding.UTF8.GetBytes("{\"text\":\"Café 🪶\"}\n{\"text\":\"second\"}\n"), chunk);
        var messages = new List<string>(); await VmJsonFrames.Read(stream, json => messages.Add(json.GetProperty("text").GetString()!), 2200000, default);
        Assert.Equal(new[] { "Café 🪶", "second" }, messages);
    }
    [Theory] [InlineData("oversize")] [InlineData("unfinished")] [InlineData("json")]
    [InlineData("depth")] [InlineData("count")]
    public async Task InvalidFramesNeverReachTheReceiverBeyondTheBound(string defect)
    {
        var input = defect switch
        {
            "oversize" => "{\"text\":\"" + new string('x', 2200000) + "\"}\n",
            "unfinished" => "{}",
            "json" => "{invalid}\n",
            "depth" => new string('[', 33) + "0" + new string(']', 33) + "\n",
            _ => string.Concat(Enumerable.Repeat("{}\n", 5001))
        };
        using var stream = new Chunks(Encoding.UTF8.GetBytes(input), 16384); var delivered = 0;
        var error = await Record.ExceptionAsync(() => VmJsonFrames.Read(stream, _ => delivered++, 2200000, default));
        Assert.True(error is IOException or JsonException); Assert.Equal(defect == "count" ? 5000 : 0, delivered);
    }
}
