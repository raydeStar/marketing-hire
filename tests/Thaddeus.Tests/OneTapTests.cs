using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

/// <summary>One tap after approval: a connected channel is scheduled at its suggested time, anything else is handed to the owner
/// to post, and a reply always is, because it has to go under the post it answers.</summary>
public sealed class OneTapTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "one-tap-" + Guid.NewGuid().ToString("N"));
    readonly Vault vault = new();
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++) { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }
    sealed class Vault : ICredentialVault
    {
        public Dictionary<(string, string), string> Entries = [];
        public Task<string?> Execute(string operation, string scope, string id, string? value, CancellationToken cancellation)
        {
            if (operation == "write") Entries[(scope, id)] = value!;
            if (operation == "forget") Entries.Remove((scope, id));
            return Task.FromResult(operation == "read" ? Entries.GetValueOrDefault((scope, id)) : null);
        }
    }
    sealed class Loopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => { app.Use((context, proceed) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return proceed(); }); next(app); };
    }
    sealed class FakeMastodon : HttpMessageHandler
    {
        public int Posted { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            if (request.RequestUri!.AbsoluteUri == "https://mastodon.example/api/v1/accounts/verify_credentials") return Task.FromResult(Json(new { acct = "hirezero" }));
            if (request.RequestUri.AbsoluteUri == "https://mastodon.example/api/v1/statuses") { Posted++; return Task.FromResult(Json(new { url = "https://mastodon.example/@hirezero/1", id = "1" })); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact] public async Task ApprovedWorkIsScheduledSavedOrHandedOverInOneTap()
    {
        var mastodon = new FakeMastodon();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => { services.AddSingleton<ICredentialVault>(vault); services.AddSingleton<IStartupFilter, Loopback>(); });
        });
        var publishing = factory.Services.GetRequiredService<Publishing>();
        publishing.Handler = () => mastodon;
        var drafts = new Dictionary<int, (string Channel, string Destination)>
        {
            [1] = ("Mastodon", "https://mastodon.example/"),
            [2] = ("LinkedIn", "https://www.linkedin.com/feed/"),
            [3] = ("Mastodon", "https://mastodon.example/@sam/112233445566"),
        };
        publishing.Draft = (id, _) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(new { id, channel = drafts[id].Channel, destination = drafts[id].Destination,
            content = "Four hours, eight cycles, nothing posted without us. https://hirezero.app/blog/", status = "approved", digest = "digest-" + id }));
        publishing.MarkPosted = (_, _, _) => Task.FromResult<string?>(null);
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var owner = factory.Services.GetRequiredService<Security>().Issue(context, "Owner", true);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        async Task<JsonElement> Send(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path) { Content = body == null ? null : JsonContent.Create(body) };
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, path + " → " + (int)response.StatusCode + " " + text);
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        await Send(HttpMethod.Post, "/api/publishing/connect/mastodon", new { address = "https://mastodon.example", secret = "mastodon-token-1234" });

        // A connected channel: scheduled at the suggested time, not posted now.
        var route = await Send(HttpMethod.Get, "/api/publishing/drafts/1/one-tap");
        Assert.Equal("schedule", route.GetProperty("action").GetString());
        var at = route.GetProperty("at").GetDateTimeOffset();
        var scheduled = await Send(HttpMethod.Post, "/api/publishing/drafts/1/one-tap", new { requestId = "t-1", digest = "digest-1" });
        Assert.Equal(("scheduled", at), (scheduled.GetProperty("status").GetString(), scheduled.GetProperty("scheduledFor").GetDateTimeOffset()));
        Assert.True(at > DateTimeOffset.UtcNow.AddMinutes(50));
        Assert.Equal(0, mastodon.Posted);

        // Not connected: handed to the owner, waiting for the link once they post it.
        Assert.Equal(("copy", "Approve & copy"), (await Send(HttpMethod.Get, "/api/publishing/drafts/2/one-tap")) is var copy ? (copy.GetProperty("action").GetString(), copy.GetProperty("label").GetString()) : default);
        Assert.Equal("awaiting_link", (await Send(HttpMethod.Post, "/api/publishing/drafts/2/one-tap", new { requestId = "t-2", digest = "digest-2" })).GetProperty("status").GetString());

        // A reply goes under its post by hand, even on a connected channel.
        Assert.Equal("Approve & reply yourself", (await Send(HttpMethod.Get, "/api/publishing/drafts/3/one-tap")).GetProperty("label").GetString());
        Assert.Equal("awaiting_link", (await Send(HttpMethod.Post, "/api/publishing/drafts/3/one-tap", new { requestId = "t-3", digest = "digest-3" })).GetProperty("status").GetString());
        Assert.Equal(0, mastodon.Posted);
    }
}
