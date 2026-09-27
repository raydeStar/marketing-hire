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

/// <summary>A Facebook Page, its Instagram account and Threads, through the owner's own Meta app: connecting with Meta's token tools,
/// publishing an approved draft, and reading back how it did.</summary>
public sealed class MetaPublishingTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "meta-publishing-" + Guid.NewGuid().ToString("N"));
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

    const string Explorer = "EAAexplorer-short-token-0123456789", AppSecret = "app-secret-0123456789abcdef", ThreadsShort = "THshort-token-0123456789abcdef";
    const string Graph = "https://graph.facebook.com/v23.0", Threads = "https://graph.threads.net";

    /// <summary>Meta's Graph API (token exchange, Pages, Page posts, Instagram media) and the Threads API, as far as publishing needs them.</summary>
    sealed class FakeMeta : HttpMessageHandler
    {
        public List<(string Url, string Body, string? Bearer)> Calls { get; } = [];
        int instagramChecks;
        static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        static HttpResponseMessage Error(string message) => Json(new { error = new { message, type = "OAuthException", code = 190 } }, HttpStatusCode.BadRequest);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            var body = request.Content == null ? "" : Uri.UnescapeDataString((await request.Content.ReadAsStringAsync(cancellationToken)).Replace('+', ' '));
            var bearer = request.Headers.Authorization?.Parameter;
            Calls.Add((url, body, bearer));
            if (url.StartsWith(Graph + "/oauth/access_token?grant_type=fb_exchange_token&client_id=1234567890&client_secret=", StringComparison.Ordinal))
                return url.Contains("client_secret=" + AppSecret) && url.Contains("fb_exchange_token=" + Explorer) ? Json(new { access_token = "user-long", token_type = "bearer" }) : Error("Error validating client secret.");
            if (url.StartsWith(Graph + "/me/accounts?", StringComparison.Ordinal))
                return bearer == "user-long" ? Json(new { data = new object[] {
                    new { id = "111", name = "HireZero", access_token = "page-token", tasks = new[] { "ANALYZE", "CREATE_CONTENT", "MANAGE" }, instagram_business_account = new { id = "178", username = "hirezero.app" } },
                    new { id = "222", name = "Side Project", access_token = "page-2", tasks = new[] { "ANALYZE" } } } }) : Error("Invalid OAuth access token.");
            switch (url)
            {
                case Graph + "/111/feed": return bearer == "page-token" ? Json(new { id = "111_999" }) : Error("Invalid OAuth access token.");
                case Graph + "/111_999?fields=permalink_url": return Json(new { permalink_url = "https://www.facebook.com/111/posts/999", id = "111_999" });
                case Graph + "/178/media": return bearer == "page-token" && body.Contains("image_url=https://hirezero.app/launch.jpg") ? Json(new { id = "c1" }) : Error("Only photo or video can be accepted as media type.");
                case Graph + "/c1?fields=status_code": return Json(new { status_code = ++instagramChecks < 2 ? "IN_PROGRESS" : "FINISHED", id = "c1" });
                case Graph + "/178/media_publish": return body.Contains("creation_id=c1") ? Json(new { id = "m1" }) : Error("Media ID is not available.");
                case Graph + "/m1?fields=permalink": return Json(new { permalink = "https://www.instagram.com/p/Cabc123/", id = "m1" });
                case Threads + "/access_token?grant_type=th_exchange_token&client_secret=" + AppSecret + "&access_token=" + ThreadsShort: return Json(new { access_token = "th-long", token_type = "bearer", expires_in = 5184000 });
                case Threads + "/v1.0/me?fields=id,username": return bearer == "th-long" ? Json(new { id = "th1", username = "hirezero" }) : Error("Invalid OAuth access token.");
                case Threads + "/v1.0/th1/threads": return bearer == "th-long" && body.Contains("media_type=TEXT") ? Json(new { id = "tc1" }) : Error("Invalid parameter.");
                case Threads + "/v1.0/tc1?fields=status": return Json(new { status = "FINISHED", id = "tc1" });
                case Threads + "/v1.0/th1/threads_publish": return body.Contains("creation_id=tc1") ? Json(new { id = "tp1" }) : Error("Invalid parameter.");
                case Threads + "/v1.0/tp1?fields=permalink": return Json(new { permalink = "https://www.threads.net/@hirezero/post/DAbc", id = "tp1" });
                // How they did.
                case Graph + "/111_999?fields=shares,reactions.summary(total_count).limit(0),comments.summary(total_count).limit(0)":
                    return Json(new { shares = new { count = 2 }, reactions = new { data = Array.Empty<object>(), summary = new { total_count = 7 } }, comments = new { data = Array.Empty<object>(), summary = new { total_count = 3 } }, id = "111_999" });
                case Graph + "/m1?fields=like_count,comments_count": return Json(new { like_count = 5, comments_count = 1, id = "m1" });
                case Threads + "/v1.0/tp1/insights?metric=views,likes,replies,reposts,quotes":
                    return Json(new { data = new object[] { new { name = "views", period = "lifetime", values = new[] { new { value = 120 } } }, new { name = "likes", period = "lifetime", values = new[] { new { value = 9 } } }, new { name = "replies", total_value = new { value = 2 } } } });
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    [Fact] public async Task APageItsInstagramAndThreadsConnectPublishAndReportBack()
    {
        var meta = new FakeMeta();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => { services.AddSingleton<ICredentialVault>(vault); services.AddSingleton<IStartupFilter, Loopback>(); });
        });
        var publishing = factory.Services.GetRequiredService<Publishing>();
        publishing.Handler = () => meta;
        publishing.Pause = (_, _) => Task.CompletedTask;
        var drafts = new Dictionary<int, (string Channel, string Content)>
        {
            [1] = ("Facebook", "HireZero drafts your marketing and asks before anything goes out. Sign up for the beta: https://hirezero.app/#launch"),
            [2] = ("Instagram", "[Image: https://hirezero.app/launch.jpg]\nYour AI marketing employee asks first. Beta sign-up: link in bio."),
            [3] = ("Threads", "Four hours, eight cycles, nothing posted without us. https://hirezero.app/blog/"),
            [4] = ("Threads", new string('t', 501)),
        };
        publishing.Draft = (id, _) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(new { id, channel = drafts[id].Channel, destination = drafts[id].Channel switch { "Facebook" => "https://www.facebook.com/111", "Instagram" => "https://www.instagram.com/hirezero.app", _ => "https://www.threads.net/@hirezero" }, content = drafts[id].Content, status = "approved", digest = "digest-" + id }));
        publishing.MarkPosted = (_, _, _) => Task.FromResult<string?>(null);
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var owner = factory.Services.GetRequiredService<Security>().Issue(context, "Owner", true);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        async Task<(bool Ok, string Text)> Post(string path, object body) { using var response = await client.PostAsJsonAsync(path, body); return (response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); }
        JsonElement Parse(string text) => JsonDocument.Parse(text).RootElement.Clone();

        // A Graph API Explorer token with the app's ID and secret: exchanged, then the Page chosen by name.
        var wrong = await Post("/api/publishing/connect/facebook", new { secret = Explorer, appId = "1234567890", appSecret = "not-the-secret-0123456789" });
        Assert.Contains("didn't exchange the token", wrong.Text);
        var several = await Post("/api/publishing/connect/facebook", new { secret = Explorer, appId = "1234567890", appSecret = AppSecret });
        Assert.Contains("“HireZero”, “Side Project”", several.Text);
        Assert.Contains("can't post", (await Post("/api/publishing/connect/facebook", new { secret = Explorer, appId = "1234567890", appSecret = AppSecret, account = "Side Project" })).Text);
        var page = await Post("/api/publishing/connect/facebook", new { secret = Explorer, appId = "1234567890", appSecret = AppSecret, account = "hirezero" });
        Assert.True(page.Ok, page.Text);
        Assert.Equal(("HireZero", "https://www.facebook.com/111"), (Parse(page.Text).GetProperty("account").GetString(), Parse(page.Text).GetProperty("address").GetString()));

        // Instagram is the Page's linked professional account; its default photo has to be a JPEG.
        Assert.Contains("JPEG only", (await Post("/api/publishing/connect/instagram", new { secret = Explorer, appId = "1234567890", appSecret = AppSecret, account = "HireZero", image = "https://hirezero.app/social.png" })).Text);
        var instagram = await Post("/api/publishing/connect/instagram", new { secret = Explorer, appId = "1234567890", appSecret = AppSecret, account = "HireZero", image = "https://hirezero.app/social.jpg" });
        Assert.True(instagram.Ok, instagram.Text);
        Assert.Equal("@hirezero.app", Parse(instagram.Text).GetProperty("account").GetString());

        // Threads: the dashboard's token, exchanged with the app secret for one that lasts 60 days.
        var threads = await Post("/api/publishing/connect/threads", new { secret = ThreadsShort, appSecret = AppSecret });
        Assert.True(threads.Ok, threads.Text);
        Assert.Equal("@hirezero", Parse(threads.Text).GetProperty("account").GetString());
        Assert.InRange(Parse(threads.Text).GetProperty("expiresAt").GetDateTimeOffset(), DateTimeOffset.UtcNow.AddDays(59), DateTimeOffset.UtcNow.AddDays(61));
        // The Page token is what's kept, never the one pasted.
        Assert.DoesNotContain(vault.Entries.Values, value => value.Contains(Explorer) || value.Contains(ThreadsShort));

        string Id(string text) => Parse(text).GetProperty("id").GetString()!;
        var fb = await Post("/api/publishing/drafts/1", new { requestId = "r-1", connectionId = Id(page.Text), digest = "digest-1", at = (DateTimeOffset?)null });
        Assert.True(fb.Ok, fb.Text);
        Assert.Equal(("published", "https://www.facebook.com/111/posts/999"), (Parse(fb.Text).GetProperty("status").GetString(), Parse(fb.Text).GetProperty("url").GetString()));
        Assert.Contains(meta.Calls, call => call.Url == Graph + "/111/feed" && call.Body == "message=" + drafts[1].Content);

        // Instagram: the draft's own photo; the image line isn't part of the caption; published once Meta has the photo.
        var ig = await Post("/api/publishing/drafts/2", new { requestId = "r-2", connectionId = Id(instagram.Text), digest = "digest-2", at = (DateTimeOffset?)null });
        Assert.Equal(("published", "https://www.instagram.com/p/Cabc123/"), (Parse(ig.Text).GetProperty("status").GetString(), Parse(ig.Text).GetProperty("url").GetString()));
        var media = Assert.Single(meta.Calls, call => call.Url == Graph + "/178/media");
        Assert.DoesNotContain("[Image", media.Body);
        Assert.Equal(2, meta.Calls.Count(call => call.Url == Graph + "/c1?fields=status_code"));
        Assert.True(meta.Calls.FindIndex(call => call.Url.EndsWith("/media_publish")) > meta.Calls.FindLastIndex(call => call.Url.Contains("status_code")));

        var th = await Post("/api/publishing/drafts/3", new { requestId = "r-3", connectionId = Id(threads.Text), digest = "digest-3", at = (DateTimeOffset?)null });
        Assert.Equal("https://www.threads.net/@hirezero/post/DAbc", Parse(th.Text).GetProperty("url").GetString());
        var tooLong = await Post("/api/publishing/drafts/4", new { requestId = "r-4", connectionId = Id(threads.Text), digest = "digest-4", at = (DateTimeOffset?)null });
        Assert.Contains("Fits Threads (500 characters)", tooLong.Text);

        // An hour later, each post's counts as the network reports them.
        publishing.Clock = () => DateTimeOffset.UtcNow.AddHours(1);
        Assert.Equal(3, await publishing.CheckResults(default));
        var results = publishing.Ledger().Publications.ToDictionary(item => item.Kind, item => item.Results!);
        Assert.Equal((7, 2, 3), (results["facebook"].Likes, results["facebook"].Reposts, results["facebook"].Replies));
        Assert.Equal((5, 1), (results["instagram"].Likes, results["instagram"].Replies));
        Assert.Equal((9, 2, 120), (results["threads"].Likes, results["threads"].Replies, results["threads"].Impressions));
    }

    [Fact] public void AnInstagramPhotoIsAJpegNamedOnTheDraftsImageLine()
    {
        Assert.Equal("https://hirezero.app/launch.jpg", Publishing.DraftPhoto("[Image: the cockpit — https://hirezero.app/launch.jpg]\nCaption"));
        Assert.Null(Publishing.DraftPhoto("[Image: a screenshot of the cockpit]\nCaption"));
        Assert.Null(Publishing.DraftPhoto("[Image: https://hirezero.app/social.png]\nCaption"));   // Instagram takes JPEG only
        Assert.Null(Publishing.DraftPhoto("See https://hirezero.app/launch.jpg"));                  // only the image line names the photo
    }
}
