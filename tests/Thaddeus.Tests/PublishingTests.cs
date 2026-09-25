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
                case "https://api.x.com/2/users/me":
                    return Json(new { data = new { id = "1", username = "markhall" } });
                case "https://api.x.com/2/tweets":
                    Posts.Add((url, body, request));
                    return bearer == "x-renewed" ? Json(new { data = new { id = "999", text = "t" } }, HttpStatusCode.Created) : Json(new { detail = "Unauthorized" }, HttpStatusCode.Unauthorized);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
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
