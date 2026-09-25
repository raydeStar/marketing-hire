using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class PublishingTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "publishing-" + Guid.NewGuid().ToString("N"));
    private readonly Vault vault = new();
    private WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++)
        {
            try { Directory.Delete(root, true); }
            catch (IOException) when (attempt < 10) { await Task.Delay(200); }
        }
    }

    private sealed class Vault : ICredentialVault
    {
        public Dictionary<(string, string), string> Entries = [];
        public Task<string?> Execute(string operation, string scope, string id, string? value, CancellationToken cancellation)
        {
            if (operation == "write") Entries[(scope, id)] = value!;
            if (operation == "forget") Entries.Remove((scope, id));
            return Task.FromResult(operation == "read" ? Entries.GetValueOrDefault((scope, id)) : null);
        }
    }
    private sealed class Loopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, proceed) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return proceed(); }); next(app);
        };
    }

    /// <summary>Bluesky, Mastodon, WordPress, LinkedIn and X, as far as publishing needs them.</summary>
    private sealed class FakeChannels : HttpMessageHandler
    {
        public List<(string Url, string Body, HttpRequestMessage Request)> Posts { get; } = [];
        public bool MastodonTimesOut { get; set; }
        static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var bearer = request.Headers.Authorization?.Parameter;
            switch (url)
            {
                case "https://bsky.social/xrpc/com.atproto.server.createSession":
                    return body.Contains("\"password\":\"app-pass-1234\"") ? Json(new { accessJwt = "jwt", did = "did:plc:abc", handle = "mark.bsky.social" }) : Json(new { error = "AuthenticationRequired", message = "Invalid identifier or password" }, HttpStatusCode.Unauthorized);
                case "https://bsky.social/xrpc/com.atproto.repo.createRecord":
                    Posts.Add((url, body, request));
                    return Json(new { uri = "at://did:plc:abc/app.bsky.feed.post/3kxyz", cid = "c" });
                case "https://mastodon.example/api/v1/accounts/verify_credentials":
                    return bearer == "mastodon-token" ? Json(new { acct = "mark" }) : Json(new { error = "The access token is invalid" }, HttpStatusCode.Unauthorized);
                case "https://mastodon.example/api/v1/statuses":
                    Posts.Add((url, body, request));
                    if (MastodonTimesOut) throw new TaskCanceledException("timed out");
                    return Json(new { url = "https://mastodon.example/@mark/1" });
                case "https://blog.example/wp-json/wp/v2/users/me?context=edit":
                    return Json(new { name = "Mark" });
                case "https://blog.example/wp-json/wp/v2/posts":
                    Posts.Add((url, body, request));
                    return Json(new { link = "https://blog.example/why-shifts/" }, HttpStatusCode.Created);
                case "https://www.linkedin.com/oauth/v2/accessToken":
                    return body.Contains("client_secret=li-secret") ? Json(new { access_token = "li-access", expires_in = 5184000 }) : Json(new { error = "invalid_client" }, HttpStatusCode.Unauthorized);
                case "https://api.linkedin.com/v2/userinfo":
                    return Json(new { sub = "abc123", name = "Mark Hall" });
                case "https://api.linkedin.com/rest/posts":
                {
                    Posts.Add((url, body, request));
                    var created = new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("") };
                    created.Headers.Add("x-restli-id", "urn:li:share:777");
                    return created;
                }
                case "https://api.x.com/2/oauth2/token":
                    return body.Contains("grant_type=authorization_code") && body.Contains("code_verifier=")
                        ? Json(new { access_token = "x-first", refresh_token = "x-refresh", expires_in = 60, token_type = "bearer" })
                        : body.Contains("refresh_token=x-refresh") ? Json(new { access_token = "x-renewed", refresh_token = "x-refresh-2", expires_in = 7200 }) : Json(new { error = "invalid_request" }, HttpStatusCode.BadRequest);
                case "https://oauth2.googleapis.com/token":
                    return body.Contains("grant_type=authorization_code") && body.Contains("code_verifier=") && body.Contains("client_secret=g-secret")
                        ? Json(new { access_token = "g-first", refresh_token = "g-refresh", expires_in = 3599, scope = "openid https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/gmail.compose" })
                        : body.Contains("refresh_token=g-refresh") ? Json(new { access_token = "g-fresh", expires_in = 3599 }) : Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest);
                case "https://openidconnect.googleapis.com/v1/userinfo":
                    return Json(new { email = "marketing@acme.example" });
                case "https://gmail.googleapis.com/gmail/v1/users/me/drafts":
                    Posts.Add((url, body, request));
                    return bearer == "g-fresh" ? Json(new { id = "r-1", message = new { id = "18f00abc" } }) : Json(new { error = new { message = "Invalid Credentials" } }, HttpStatusCode.Unauthorized);
                case "https://public.api.bsky.app/xrpc/app.bsky.feed.getPosts?uris=at%3A%2F%2Fdid%3Aplc%3Aabc%2Fapp.bsky.feed.post%2F3kxyz":
                    return Json(new { posts = new[] { new { uri = "at://did:plc:abc/app.bsky.feed.post/3kxyz", likeCount = 12, repostCount = 3, replyCount = 2, quoteCount = 1 } } });
                case "https://api.x.com/2/tweets/999?tweet.fields=public_metrics":
                    return bearer == "x-renewed" ? Json(new { data = new { id = "999", public_metrics = new { like_count = 5, retweet_count = 1, reply_count = 0, quote_count = 0, impression_count = 340 } } }) : Json(new { detail = "Unauthorized" }, HttpStatusCode.Unauthorized);
                case "https://public.api.bsky.app/xrpc/com.atproto.identity.resolveHandle?handle=owner.bsky.social":
                    return Json(new { did = "did:plc:owner" });
                case "https://public.api.bsky.app/xrpc/app.bsky.feed.getPosts?uris=at%3A%2F%2Fdid%3Aplc%3Aowner%2Fapp.bsky.feed.post%2F3kself":
                    return Json(new { posts = new[] { new { likeCount = 4, repostCount = 1, replyCount = 0, quoteCount = 0 } } });
                case "https://api.x.com/2/users/me":
                    return Json(new { data = new { id = "1", username = "markhall" } });
                case "https://api.x.com/2/tweets":
                    Posts.Add((url, body, request));
                    return bearer == "x-renewed" ? Json(new { data = new { id = "999", text = "t" } }, HttpStatusCode.Created) : Json(new { detail = "Unauthorized" }, HttpStatusCode.Unauthorized);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    /// <summary>Buttondown as far as drafts need it; <see cref="Keeps"/> is the status its reply reports.</summary>
    private sealed class FakeButtondown : HttpMessageHandler
    {
        public List<(string Body, string? Auth)> Emails { get; } = [];
        public string Keeps { get; set; } = "draft";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var auth = request.Headers.Authorization is { } header ? header.Scheme + " " + header.Parameter : null;
            switch (request.RequestUri!.AbsoluteUri)
            {
                case "https://api.buttondown.com/v1/ping":
                    return new(auth == "Token bd-key-123456" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
                case "https://api.buttondown.com/v1/emails":
                    Emails.Add((await request.Content!.ReadAsStringAsync(cancellationToken), auth));
                    return new(HttpStatusCode.Created) { Content = new StringContent(JsonSerializer.Serialize(new { id = "em_1", status = Keeps }), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    [Fact] public async Task NewslettersAreOnlyEverSavedAsButtondownDrafts()
    {
        var buttondown = new FakeButtondown();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => { services.AddSingleton<ICredentialVault>(vault); services.AddSingleton<IStartupFilter, Loopback>(); });
        });
        var publishing = factory.Services.GetRequiredService<Publishing>();
        publishing.Handler = () => buttondown;
        var content = new Dictionary<int, string> { [1] = "Subject: Week 3 — the employee asks first\n\nHi,\n\nThis week the employee started working shifts. https://example.com/?utm_source=newsletter&utm_campaign=week3", [2] = "Subject: Week 4\n\nMore. https://example.com/?utm_source=newsletter&utm_campaign=week4" };
        publishing.Draft = (id, _) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(new { id, channel = "Newsletter", destination = "https://buttondown.com/", content = content[id], status = "approved", digest = "digest-" + id }));
        publishing.MarkPosted = (_, _, _) => Task.FromResult<string?>(null);
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var owner = factory.Services.GetRequiredService<Security>().Issue(context, "Owner", true);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        async Task<(bool Ok, string Text)> Post(string path, object body) { using var response = await client.PostAsJsonAsync(path, body); return (response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); }

        var wrong = await Post("/api/publishing/connect/buttondown", new { secret = "not-the-key" });
        Assert.False(wrong.Ok); Assert.Contains("didn't accept that API key", wrong.Text);
        var connected = await Post("/api/publishing/connect/buttondown", new { secret = "bd-key-123456" });
        Assert.True(connected.Ok, connected.Text);
        var connection = JsonDocument.Parse(connected.Text).RootElement.GetProperty("id").GetString()!;

        // Drafts only: a schedule is refused before anything reaches Buttondown.
        var scheduled = await Post("/api/publishing/drafts/1", new { requestId = "r-s", connectionId = connection, digest = "digest-1", at = DateTimeOffset.UtcNow.AddDays(1) });
        Assert.False(scheduled.Ok); Assert.Contains("only saves a draft", scheduled.Text);
        Assert.Empty(buttondown.Emails);

        var saved = await Post("/api/publishing/drafts/1", new { requestId = "r-1", connectionId = connection, digest = "digest-1", at = (DateTimeOffset?)null });
        Assert.True(saved.Ok, saved.Text);
        var sent = Assert.Single(buttondown.Emails);
        Assert.Equal("Token bd-key-123456", sent.Auth);
        using (var body = JsonDocument.Parse(sent.Body))
        {
            Assert.Equal("draft", body.RootElement.GetProperty("status").GetString());
            Assert.Equal("Week 3 — the employee asks first", body.RootElement.GetProperty("subject").GetString());
            Assert.StartsWith("Hi,", body.RootElement.GetProperty("body").GetString());
        }
        Assert.Equal("published", JsonDocument.Parse(saved.Text).RootElement.GetProperty("status").GetString());

        // If Buttondown ever reports anything but a draft, that is an alarm, not a success.
        buttondown.Keeps = "about_to_send";
        var alarm = await Post("/api/publishing/drafts/2", new { requestId = "r-2", connectionId = connection, digest = "digest-2", at = (DateTimeOffset?)null });
        var state = JsonDocument.Parse(alarm.Text).RootElement;
        Assert.Equal("failed", state.GetProperty("status").GetString());
        Assert.Contains("did not keep this as a draft", state.GetProperty("error").GetString());
    }

    [Fact] public async Task ApprovedDraftsPublishOnlyAsTheOwnerChoseAndNeverTwice()
    {
        var channels = new FakeChannels();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:ShiftPump", "off");
            builder.ConfigureServices(services => { services.AddSingleton<ICredentialVault>(vault); services.AddSingleton<IStartupFilter, Loopback>(); });
        });
        var publishing = factory.Services.GetRequiredService<Publishing>();
        publishing.Handler = () => channels;
        var drafts = new Dictionary<int, (string Channel, string Content, string Status)>
        {
            [1] = ("Bluesky", "Shifts, not prompts 🚀 See how it works: https://example.com/shifts?utm_source=bluesky&utm_campaign=launch", "approved"),
            [2] = ("Mastodon", "The employee works a shift and asks before acting. https://example.com/?utm_source=mastodon&utm_campaign=launch", "approved"),
            [3] = ("Blog", "# Why shifts\n\nMarketing needs daily work. https://example.com/?utm_source=blog&utm_campaign=launch", "approved"),
            [4] = ("LinkedIn", "Built in the open (week 3) #buildinpublic: an AI employee that asks first. https://example.com/?utm_source=linkedin&utm_campaign=launch", "approved"),
            [5] = ("X", "An AI employee that works shifts and asks before acting. https://example.com/?utm_source=x&utm_campaign=launch", "approved"),
            [6] = ("Mastodon", string.Join(" ", Enumerable.Repeat("shift", 90)), "approved"),
            [7] = ("Bluesky", "Waiting for review https://example.com/?utm_source=bluesky&utm_campaign=launch", "pending"),
            [8] = ("Email", "Subject: Launch week — what’s new\nTo: pat@example.com, sam@example.com\n\nHi,\n\nThe employee now works shifts. https://example.com/?utm_source=email&utm_campaign=launch", "approved"),
            [9] = ("Email", "To: not-an-address\n\nHello", "approved"),
            [10] = ("Bluesky", "Morning post https://example.com/?utm_source=bluesky&utm_campaign=morning", "approved"),
            [11] = ("Threads", "A thread about shifts https://example.com/?utm_source=threads&utm_campaign=assist", "approved"),
            [12] = ("Bluesky", "Posted by hand https://example.com/?utm_source=bluesky&utm_campaign=assist", "approved"),
        };
        var posted = new Dictionary<int, string>();
        publishing.Draft = (id, _) => Task.FromResult<JsonElement?>(drafts.TryGetValue(id, out var d)
            ? JsonSerializer.SerializeToElement(new { id, channel = d.Channel, destination = "https://example.com/", content = d.Content, status = posted.ContainsKey(id) ? "posted" : d.Status, digest = "digest-" + id })
            : null);
        publishing.MarkPosted = (id, url, _) => { posted[id] = url; return Task.FromResult<string?>(null); };
        publishing.GoogleClient = _ => Task.FromResult<(string, string)?>(("g-client.apps.googleusercontent.com", "g-secret"));

        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var owner = factory.Services.GetRequiredService<Security>().Issue(context, "Owner", true);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        async Task<JsonElement> Send(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body ?? new { }) };
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, path + " → " + (int)response.StatusCode + " " + text);
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        async Task<string> Refused(string path, object body)
        {
            using var response = await client.PostAsJsonAsync(path, body);
            Assert.False(response.IsSuccessStatusCode, path + " should have been refused");
            return await response.Content.ReadAsStringAsync();
        }
        object Publish(string connection, int draft, string? request = null, DateTimeOffset? at = null) => new { requestId = request ?? Guid.NewGuid().ToString("N"), connectionId = connection, digest = "digest-" + draft, at };

        // Connecting checks the credentials with the service first.
        Assert.Contains("Invalid identifier or password", await Refused("/api/publishing/connect/bluesky", new { account = "mark.bsky.social", secret = "wrong-password" }));
        var bluesky = (await Send(HttpMethod.Post, "/api/publishing/connect/bluesky", new { account = "@mark.bsky.social", secret = "app-pass-1234" })).GetProperty("id").GetString()!;
        var mastodon = (await Send(HttpMethod.Post, "/api/publishing/connect/mastodon", new { address = "https://mastodon.example", secret = "mastodon-token" })).GetProperty("id").GetString()!;
        var wordpress = (await Send(HttpMethod.Post, "/api/publishing/connect/wordpress", new { address = "https://blog.example", account = "mark", secret = "abcd efgh ijkl mnop" })).GetProperty("id").GetString()!;
        Assert.Equal(3, (await Send(HttpMethod.Get, "/api/publishing")).GetProperty("connections").GetArrayLength());

        // Bluesky: the exact approved text, with the link as a byte-offset facet (the emoji is four bytes).
        var first = await Send(HttpMethod.Post, "/api/publishing/drafts/1", Publish(bluesky, 1, "req-1"));
        Assert.Equal("published", first.GetProperty("status").GetString());
        Assert.Equal("https://bsky.app/profile/mark.bsky.social/post/3kxyz", first.GetProperty("url").GetString());
        Assert.Equal(first.GetProperty("url").GetString(), posted[1]);
        using (var record = JsonDocument.Parse(channels.Posts[0].Body))
        {
            var text = drafts[1].Content;
            var facet = record.RootElement.GetProperty("record").GetProperty("facets")[0];
            Assert.Equal(Encoding.UTF8.GetByteCount(text[..text.IndexOf("https", StringComparison.Ordinal)]), facet.GetProperty("index").GetProperty("byteStart").GetInt32());
            Assert.Equal(text, record.RootElement.GetProperty("record").GetProperty("text").GetString());
        }
        // The same request is a replay; a new request for the same draft is refused.
        Assert.Equal(first.GetProperty("id").GetString(), (await Send(HttpMethod.Post, "/api/publishing/drafts/1", Publish(bluesky, 1, "req-1"))).GetProperty("id").GetString());
        Assert.Single(channels.Posts);
        Assert.Contains("Only an approved draft", await Refused("/api/publishing/drafts/1", Publish(bluesky, 1)));
        // What wasn't approved, changed since review, belongs to another channel, or doesn't fit, is refused before anything is sent.
        Assert.Contains("Only an approved draft", await Refused("/api/publishing/drafts/7", Publish(bluesky, 7)));
        Assert.Contains("changed since you reviewed it", await Refused("/api/publishing/drafts/2", new { requestId = "r-stale", connectionId = mastodon, digest = "old" }));
        Assert.Contains("publish it to a Mastodon channel", await Refused("/api/publishing/drafts/2", Publish(bluesky, 2)));
        Assert.Contains("Mastodon allows 500 characters; this is 539", await Refused("/api/publishing/drafts/6", Publish(mastodon, 6)));
        Assert.Single(channels.Posts);

        // Mastodon: sent with an idempotency key; a timeout is "unknown", never retried, until the owner says what happened.
        channels.MastodonTimesOut = true;
        var uncertain = await Send(HttpMethod.Post, "/api/publishing/drafts/2", Publish(mastodon, 2, "req-2"));
        Assert.Equal("unknown", uncertain.GetProperty("status").GetString());
        Assert.Equal("req-2", channels.Posts[1].Request.Headers.GetValues("Idempotency-Key").Single());
        Assert.Contains("already published or scheduled", await Refused("/api/publishing/drafts/2", Publish(mastodon, 2)));
        var resolved = await Send(HttpMethod.Post, $"/api/publishing/publications/{uncertain.GetProperty("id").GetString()}/resolve", new { outcome = "posted", url = "https://mastodon.example/@mark/1" });
        Assert.Equal("published", resolved.GetProperty("status").GetString()); Assert.Equal("https://mastodon.example/@mark/1", posted[2]);

        // WordPress, scheduled: nothing goes out early; at its time the pump publishes it with the heading as title.
        var at = DateTimeOffset.UtcNow.AddHours(2);
        var scheduled = await Send(HttpMethod.Post, "/api/publishing/drafts/3", Publish(wordpress, 3, "req-3", at));
        Assert.Equal("scheduled", scheduled.GetProperty("status").GetString());
        Assert.Equal(0, await publishing.PublishDue(CancellationToken.None));
        using (var busy = await client.DeleteAsync($"/api/publishing/connections/{wordpress}")) Assert.False(busy.IsSuccessStatusCode);
        publishing.Clock = () => at.AddMinutes(1);
        Assert.Equal(1, await publishing.PublishDue(CancellationToken.None));
        Assert.Equal("https://blog.example/why-shifts/", posted[3]);
        using (var post = JsonDocument.Parse(channels.Posts[^1].Body)) { Assert.Equal("Why shifts", post.RootElement.GetProperty("title").GetString()); Assert.Equal("publish", post.RootElement.GetProperty("status").GetString()); }
        publishing.Clock = () => DateTimeOffset.UtcNow;

        // LinkedIn: the owner's own app; post text escapes LinkedIn's markup characters.
        var begin = await Send(HttpMethod.Post, "/api/publishing/oauth/linkedin", new { clientId = "li-client", clientSecret = "li-secret" });
        var query = QueryHelpers.ParseQuery(new Uri(begin.GetProperty("authorizationUrl").GetString()!).Query);
        Assert.Equal("http://127.0.0.1:5179/api/publishing/oauth/callback", query["redirect_uri"]); Assert.Contains("w_member_social", query["scope"].ToString());
        using (var callback = await client.GetAsync($"/api/publishing/oauth/callback?code=c&state={Uri.EscapeDataString(query["state"]!)}"))
            Assert.Contains("LinkedIn is connected as Mark Hall", await callback.Content.ReadAsStringAsync());
        using (var replay = await client.GetAsync($"/api/publishing/oauth/callback?code=c&state={Uri.EscapeDataString(query["state"]!)}")) Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        var linkedin = (await Send(HttpMethod.Get, "/api/publishing")).GetProperty("connections").EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "linkedin").GetProperty("id").GetString()!;
        Assert.Equal("https://www.linkedin.com/feed/update/urn:li:share:777/", (await Send(HttpMethod.Post, "/api/publishing/drafts/4", Publish(linkedin, 4))).GetProperty("url").GetString());
        using (var post = JsonDocument.Parse(channels.Posts[^1].Body))
        {
            Assert.Equal("urn:li:person:abc123", post.RootElement.GetProperty("author").GetString());
            Assert.StartsWith("Built in the open \\(week 3\\) \\#buildinpublic", post.RootElement.GetProperty("commentary").GetString());
        }
        Assert.Equal("202607", channels.Posts[^1].Request.Headers.GetValues("LinkedIn-Version").Single());

        // X: PKCE sign-in; an access token about to expire is refreshed before posting.
        var x = await Send(HttpMethod.Post, "/api/publishing/oauth/x", new { clientId = "x-client" });
        var xQuery = QueryHelpers.ParseQuery(new Uri(x.GetProperty("authorizationUrl").GetString()!).Query);
        Assert.Equal("S256", xQuery["code_challenge_method"]);
        using (var callback = await client.GetAsync($"/api/publishing/oauth/callback?code=c&state={Uri.EscapeDataString(xQuery["state"]!)}"))
            Assert.Contains("X is connected as @markhall", await callback.Content.ReadAsStringAsync());
        var xId = (await Send(HttpMethod.Get, "/api/publishing")).GetProperty("connections").EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "x").GetProperty("id").GetString()!;
        Assert.Equal("https://x.com/markhall/status/999", (await Send(HttpMethod.Post, "/api/publishing/drafts/5", Publish(xId, 5))).GetProperty("url").GetString());
        Assert.Equal(drafts[5].Content.IndexOf("https", StringComparison.Ordinal) + 23, Publishing.Length("x", drafts[5].Content)); // the link counts as 23

        // Email: the owner's Google app, then a Gmail draft (never a send); the owner sends it from Gmail.
        var mail = await Send(HttpMethod.Post, "/api/publishing/oauth/email", new { clientId = "" });
        var mailQuery = QueryHelpers.ParseQuery(new Uri(mail.GetProperty("authorizationUrl").GetString()!).Query);
        Assert.Contains("https://www.googleapis.com/auth/gmail.compose", mailQuery["scope"].ToString()); Assert.Equal("offline", mailQuery["access_type"]);
        using (var callback = await client.GetAsync($"/api/publishing/oauth/callback?code=c&state={Uri.EscapeDataString(mailQuery["state"]!)}"))
            Assert.Contains("is connected as marketing@acme.example", await callback.Content.ReadAsStringAsync());
        var email = (await Send(HttpMethod.Get, "/api/publishing")).GetProperty("connections").EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "email").GetProperty("id").GetString()!;
        Assert.Contains("isn't an email address", await Refused("/api/publishing/drafts/9", Publish(email, 9)));
        Assert.Equal("https://mail.google.com/mail/u/0/#drafts?compose=18f00abc", (await Send(HttpMethod.Post, "/api/publishing/drafts/8", Publish(email, 8))).GetProperty("url").GetString());
        Assert.DoesNotContain(channels.Posts, post => post.Url.EndsWith("/send", StringComparison.Ordinal));
        using (var draft = JsonDocument.Parse(channels.Posts[^1].Body))
        {
            var raw = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(draft.RootElement.GetProperty("message").GetProperty("raw").GetString()!));
            Assert.Contains("From: marketing@acme.example\r\n", raw); Assert.Contains("To: pat@example.com, sam@example.com\r\n", raw);
            Assert.Contains("Subject: =?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes("Launch week — what’s new")) + "?=", raw);
            var body = raw[(raw.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..].Replace("\r\n", "");
            Assert.StartsWith("Hi,\r\n\r\nThe employee now works shifts.", Encoding.UTF8.GetString(Convert.FromBase64String(body)));
        }

        // Scheduled for 7:00: if the workspace was off then, it's held for the owner, not posted late; it can be rescheduled.
        var seven = DateTimeOffset.UtcNow.Date.AddDays(1).AddHours(7);
        var morning = await Send(HttpMethod.Post, "/api/publishing/drafts/10", Publish(bluesky, 10, "req-10", seven));
        Assert.Equal("scheduled", morning.GetProperty("status").GetString());
        publishing.Clock = () => seven.AddHours(3);
        await publishing.PublishDue(CancellationToken.None);
        var missed = (await Send(HttpMethod.Get, "/api/publishing")).GetProperty("publications").EnumerateArray().Single(item => item.GetProperty("id").GetString() == morning.GetProperty("id").GetString());
        Assert.Equal("missed", missed.GetProperty("status").GetString()); Assert.DoesNotContain(10, posted.Keys);
        publishing.Clock = () => DateTimeOffset.UtcNow;
        Assert.Equal("scheduled", (await Send(HttpMethod.Post, "/api/publishing/drafts/10", Publish(bluesky, 10, "req-10b", seven.AddDays(1)))).GetProperty("status").GetString());
        await Send(HttpMethod.Post, $"/api/publishing/publications/{(await Send(HttpMethod.Get, "/api/publishing")).GetProperty("publications").EnumerateArray().First(item => item.GetProperty("status").GetString() == "scheduled").GetProperty("id").GetString()}/cancel");

        // Results: about an hour after posting the host reads each channel's own counts, plus visits from the tracking link.
        var visitsAsked = new List<(string, string)>();
        publishing.Visits = (source, campaign, since, _) => { visitsAsked.Add((source, campaign)); return Task.FromResult<int?>(source == "bluesky" ? 17 : 4); };
        Assert.Equal(0, await publishing.CheckResults(CancellationToken.None)); // too soon
        publishing.Clock = () => DateTimeOffset.UtcNow.AddHours(2);
        Assert.True(await publishing.CheckResults(CancellationToken.None) >= 3);
        var results = (await Send(HttpMethod.Get, "/api/publishing")).GetProperty("publications").EnumerateArray().Where(item => item.GetProperty("status").GetString() == "published")
            .ToDictionary(item => item.GetProperty("kind").GetString()!, item => item.GetProperty("results"));
        Assert.Equal(12, results["bluesky"].GetProperty("likes").GetInt32()); Assert.Equal(3, results["bluesky"].GetProperty("reposts").GetInt32()); Assert.Equal(17, results["bluesky"].GetProperty("visits").GetInt32());
        Assert.Equal(340, results["x"].GetProperty("impressions").GetInt32()); Assert.Equal(5, results["x"].GetProperty("likes").GetInt32());
        Assert.Contains("doesn't share personal-post analytics", results["linkedin"].GetProperty("note").GetString());
        Assert.Contains(("bluesky", "launch"), visitsAsked);
        Assert.Equal(JsonValueKind.Null, results["email"].ValueKind); // a Gmail draft has no audience
        Assert.Equal(0, await publishing.CheckResults(CancellationToken.None)); // checked; the next check is hours away
        Assert.Contains(publishing.RecentPosts(30).Select(post => JsonSerializer.Serialize(post)), post => post.Contains("\"likes\":12"));
        publishing.Clock = () => DateTimeOffset.UtcNow;

        // Assisted posting: the owner posts through the network's own composer and pastes the link back.
        var posts = channels.Posts.Count;
        var assisted = await Send(HttpMethod.Post, "/api/publishing/drafts/11/assist", new { requestId = "as-11", digest = "digest-11" });
        Assert.Equal("awaiting_link", assisted.GetProperty("status").GetString()); Assert.Equal("", assisted.GetProperty("connectionId").GetString());
        Assert.Contains("already published or scheduled", await Refused("/api/publishing/drafts/11/assist", new { requestId = "as-11b", digest = "digest-11" }));
        Assert.Contains("https address", await Refused($"/api/publishing/publications/{assisted.GetProperty("id").GetString()}/link", new { url = "http://threads.net/x" }));
        var linked = await Send(HttpMethod.Post, $"/api/publishing/publications/{assisted.GetProperty("id").GetString()}/link", new { url = "https://www.threads.net/@mark/post/abc" });
        Assert.Equal("published", linked.GetProperty("status").GetString()); Assert.Equal("https://www.threads.net/@mark/post/abc", posted[11]);
        // A reminder at 7:00 turns "due" at its time; the host never posts it.
        var reminder = await Send(HttpMethod.Post, "/api/publishing/drafts/12/assist", new { requestId = "as-12", digest = "digest-12", at = seven.AddDays(2) });
        Assert.Equal("scheduled", reminder.GetProperty("status").GetString());
        publishing.Clock = () => seven.AddDays(2).AddMinutes(1);
        await publishing.PublishDue(CancellationToken.None);
        publishing.Clock = () => DateTimeOffset.UtcNow;
        Assert.Equal(posts, channels.Posts.Count);
        var due = (await Send(HttpMethod.Get, "/api/publishing")).GetProperty("publications").EnumerateArray().Single(item => item.GetProperty("id").GetString() == reminder.GetProperty("id").GetString());
        Assert.Equal("due", due.GetProperty("status").GetString());
        // The pasted Bluesky link is resolved to its post, so its public counts come back with no account connected.
        var byHand = await Send(HttpMethod.Post, $"/api/publishing/publications/{reminder.GetProperty("id").GetString()}/link", new { url = "https://bsky.app/profile/owner.bsky.social/post/3kself" });
        Assert.Equal("at://did:plc:owner/app.bsky.feed.post/3kself", byHand.GetProperty("remoteId").GetString());
        publishing.Clock = () => DateTimeOffset.UtcNow.AddHours(2);
        await publishing.CheckResults(CancellationToken.None);
        publishing.Clock = () => DateTimeOffset.UtcNow;
        var counted = (await Send(HttpMethod.Get, "/api/publishing")).GetProperty("publications").EnumerateArray().Single(item => item.GetProperty("id").GetString() == reminder.GetProperty("id").GetString());
        Assert.Equal(4, counted.GetProperty("results").GetProperty("likes").GetInt32());

        // Secrets stay in the vault; disconnecting removes them.
        foreach (var path in new[] { "/api/publishing", "/api/export" })
        {
            var text = await client.GetStringAsync(path);
            foreach (var secret in new[] { "app-pass-1234", "mastodon-token", "abcd efgh", "li-secret", "li-access", "x-renewed", "x-refresh", "g-refresh", "g-secret" }) Assert.DoesNotContain(secret, text);
        }
        var before = vault.Entries.Count;
        await Send(HttpMethod.Delete, $"/api/publishing/connections/{bluesky}");
        Assert.Equal(before - 1, vault.Entries.Count);
    }

    [Fact] public void ChannelRulesMatchTheServices()
    {
        var (to, cc, subject, body) = Publishing.Email("Welcome aboard\n\nThanks for joining.");
        Assert.Empty(to); Assert.Empty(cc); Assert.Equal("Welcome aboard", subject); Assert.Equal("Thanks for joining.", body);
        Assert.Throws<InvalidOperationException>(() => Publishing.Email("To: a@example.com\n\n"));
        Assert.True(Publishing.Serves("email", "Newsletter"));
        Assert.True(Publishing.Serves("x", "Twitter")); Assert.True(Publishing.Serves("wordpress", "Blog")); Assert.False(Publishing.Serves("bluesky", "LinkedIn"));
        Assert.Equal(1, Publishing.Length("bluesky", "👩‍👩‍👧"));
        Assert.Equal("a\\_b \\@c \\(d\\)", Publishing.LinkedInText("a_b @c (d)"));
        var facets = JsonSerializer.SerializeToElement(Publishing.BlueskyLinks("é https://a.example/x."));
        Assert.Equal(3, facets[0].GetProperty("index").GetProperty("byteStart").GetInt32());
        Assert.Equal("https://a.example/x", facets[0].GetProperty("features")[0].GetProperty("uri").GetString());
    }
}
