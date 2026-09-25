using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record PublishingConnection(string Id, string Kind, string Status, string Account, string? Address, DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt, string? LastError, bool SaveAsDraft = false);
public record Publication(string Id, string RequestId, int DraftId, string Digest, string ConnectionId, string Kind, string Status,
    DateTimeOffset? ScheduledFor, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt, string? Url, string? Error, string By);
public record PublishingLedger(string VaultScope, PublishingConnection[] Connections, Publication[] Publications);
public record PublishingConnect(string? Address, string? Account, string? Secret, bool? SaveAsDraft)
{
    public override string ToString() => "Publishing connection (secret omitted)";
}
public record PublishingOAuthStart(string ClientId, string? ClientSecret)
{
    public override string ToString() => "Publishing sign-in (client secret omitted)";
}
public record DraftPublishRequest(string RequestId, string ConnectionId, string Digest, DateTimeOffset? At);
public record PublicationResolve(string Outcome, string? Url);

/// <summary>Publishing an approved draft to a channel the owner connected: Bluesky, Mastodon, WordPress, LinkedIn or X.
/// Approval never publishes; the owner publishes or schedules the exact approved text as a separate act. A post whose
/// outcome is uncertain is never retried automatically, so a network failure can't post twice.</summary>
public sealed class Publishing(Store store, ICredentialVault vault, MarketingBackend marketing, ILogger<Publishing> logger, string localOrigin)
{
    private const string Key = "publishing-v1";
    private readonly SemaphoreSlim publishGate = new(1, 1);
    private readonly Dictionary<string, (string Kind, string Verifier, string ClientId, string? ClientSecret, DateTimeOffset At)> attempts = [];
    public Func<HttpMessageHandler>? Handler { get; set; }
    public Func<int, CancellationToken, Task<JsonElement?>> Draft { get; set; } =
        async (id, cancellation) => (await marketing.ShiftHire(null, "draft", "get", "--id", id.ToString(CultureInfo.InvariantCulture))).Value;
    public Func<int, string, CancellationToken, Task<string?>> MarkPosted { get; set; } =
        async (id, url, cancellation) => (await marketing.ShiftHire(null, "draft", "posted", "--id", id.ToString(CultureInfo.InvariantCulture), "--url", url)).Error;
    public string LinkedInVersion { get; set; } = "202607";
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;
    public Uri Redirect => new UriBuilder(new Uri(localOrigin)) { Host = "127.0.0.1", Path = "/api/publishing/oauth/callback" }.Uri;

    public static readonly Dictionary<string, (string Name, string[] Channels, int? Limit)> Kinds = new()
    {
        ["bluesky"] = ("Bluesky", ["bluesky", "bsky"], 300),
        ["mastodon"] = ("Mastodon", ["mastodon", "fediverse"], 500),
        ["wordpress"] = ("WordPress", ["blog", "wordpress", "website", "site"], null),
        ["linkedin"] = ("LinkedIn", ["linkedin"], 3000),
        ["x"] = ("X", ["x", "twitter", "x (twitter)", "x/twitter"], 280),
    };
    public static bool Serves(string kind, string channel) => Kinds.TryGetValue(kind, out var info) && info.Channels.Contains(channel.Trim().ToLowerInvariant());

    PublishingLedger Read() => store.Setting(Key) is { } json ? Wire.Unpack<PublishingLedger>(json) : new(Guid.NewGuid().ToString("N"), [], []);
    void Write(PublishingLedger ledger) => store.Setting(Key, Wire.Pack(ledger));
    public PublishingLedger Ledger() { lock (store) { var ledger = Read(); if (store.Setting(Key) == null) Write(ledger); return ledger; } }
    T Change<T>(Func<PublishingLedger, (PublishingLedger Next, T Result)> change) { lock (store) { var (next, result) = change(Read()); Write(next); return result; } }
    Publication Set(string id, Func<Publication, Publication> change) => Change(ledger =>
    {
        var next = change(ledger.Publications.First(item => item.Id == id));
        return (ledger with { Publications = [.. ledger.Publications.Select(item => item.Id == id ? next : item)] }, next);
    });
    HttpClient Client() => new(Handler?.Invoke() ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30) };

    public object View() { var ledger = Ledger(); return new { redirectUri = Redirect.AbsoluteUri, kinds = Kinds.Select(item => new { kind = item.Key, name = item.Value.Name, channels = item.Value.Channels, limit = item.Value.Limit }),
        connections = ledger.Connections, publications = ledger.Publications.OrderByDescending(item => item.CreatedAt).Take(100) }; }

    // ---------- Connecting ----------
    record Secret(string Token, string? Refresh, string? ClientId, string? ClientSecret, string? Subject, DateTimeOffset? ExpiresAt);
    async Task SaveSecret(string id, Secret secret, CancellationToken cancellation)
    {
        var scope = Ledger().VaultScope; var packed = Wire.Pack(secret);
        await vault.Execute("write", scope, id, packed, cancellation);
        if (await vault.Execute("read", scope, id, null, cancellation) != packed) throw new InvalidOperationException("The credential store did not keep the channel's secret.");
    }
    async Task<Secret> ReadSecret(string id, CancellationToken cancellation) =>
        await vault.Execute("read", Ledger().VaultScope, id, null, cancellation) is { } packed ? Wire.Unpack<Secret>(packed) : throw new InvalidOperationException("The saved access is missing. Disconnect and connect the channel again.");

    static string Https(string? value, string what)
    {
        var text = (value ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException($"{what} must be an https address, e.g. https://example.com.");
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    PublishingConnection Add(PublishingConnection connection) => Change(ledger =>
    {
        if (ledger.Connections.Count(item => item.Status == "ready") >= 12) throw new InvalidOperationException("Disconnect a channel before adding another.");
        return (ledger with { Connections = [.. ledger.Connections.Where(item => item.Status != "authorizing" || item.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-15)), connection] }, connection);
    });

    /// <summary>Bluesky (app password), Mastodon (access token) and WordPress (application password): checked against the service before saving.</summary>
    public async Task<PublishingConnection> Connect(string kind, PublishingConnect request, CancellationToken cancellation)
    {
        var secret = (request.Secret ?? "").Trim();
        if (secret.Length is < 8 or > 2048) throw new ArgumentException("Paste the password or token for this channel.");
        using var http = Client();
        string account, address;
        switch (kind)
        {
            case "bluesky":
            {
                address = request.Address is { Length: > 0 } ? Https(request.Address, "The Bluesky server") : "https://bsky.social";
                var handle = (request.Account ?? "").Trim().TrimStart('@');
                if (handle.Length is < 3 or > 253) throw new ArgumentException("Enter the Bluesky handle, e.g. you.bsky.social.");
                var session = await BlueskySession(http, address, handle, secret, cancellation);
                account = "@" + session.Handle;
                break;
            }
            case "mastodon":
            {
                address = Https(request.Address, "The Mastodon server");
                using var check = new HttpRequestMessage(HttpMethod.Get, address + "/api/v1/accounts/verify_credentials");
                check.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                using var me = await Read(http, check, cancellation);
                account = "@" + me.RootElement.GetProperty("acct").GetString() + "@" + new Uri(address).Host;
                break;
            }
            case "wordpress":
            {
                address = Https(request.Address, "The WordPress site");
                var user = (request.Account ?? "").Trim();
                if (user.Length is < 1 or > 120) throw new ArgumentException("Enter the WordPress username.");
                using var check = new HttpRequestMessage(HttpMethod.Get, address + "/wp-json/wp/v2/users/me?context=edit");
                check.Headers.Authorization = Basic(user, secret);
                using var me = await Read(http, check, cancellation);
                account = me.RootElement.TryGetProperty("name", out var name) ? name.GetString() ?? user : user;
                secret = user + ":" + secret;
                break;
            }
            default: throw new ArgumentException("Choose Bluesky, Mastodon or WordPress here; LinkedIn and X sign in.");
        }
        var connection = Add(new PublishingConnection(Guid.NewGuid().ToString("N"), kind, "ready", account, address, DateTimeOffset.UtcNow, null, null, kind == "wordpress" && request.SaveAsDraft == true));
        await SaveSecret(connection.Id, new Secret(secret, null, null, null, kind == "bluesky" ? request.Account!.Trim().TrimStart('@') : null, null), cancellation);
        return connection;
    }

    static AuthenticationHeaderValue Basic(string user, string password) => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password)));

    /// <summary>LinkedIn and X: the owner's own developer app, OAuth in the browser, returning to this computer.</summary>
    public object BeginOAuth(string kind, PublishingOAuthStart start)
    {
        if (kind is not ("linkedin" or "x")) throw new ArgumentException("Sign-in is for LinkedIn and X.");
        var clientId = (start.ClientId ?? "").Trim();
        if (clientId.Length is < 4 or > 200) throw new ArgumentException("Paste the app's client ID.");
        var clientSecret = string.IsNullOrWhiteSpace(start.ClientSecret) ? null : start.ClientSecret.Trim();
        if (kind == "linkedin" && clientSecret == null) throw new ArgumentException("LinkedIn needs the app's client secret too.");
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        lock (attempts)
        {
            foreach (var old in attempts.Where(item => item.Value.At < DateTimeOffset.UtcNow.AddMinutes(-15)).Select(item => item.Key).ToArray()) attempts.Remove(old);
            attempts[state] = (kind, verifier, clientId, clientSecret, DateTimeOffset.UtcNow);
        }
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var url = kind == "linkedin"
            ? QueryHelpers.AddQueryString("https://www.linkedin.com/oauth/v2/authorization", new Dictionary<string, string?> { ["response_type"] = "code", ["client_id"] = clientId,
                ["redirect_uri"] = Redirect.AbsoluteUri, ["scope"] = "openid profile w_member_social", ["state"] = state })
            : QueryHelpers.AddQueryString("https://x.com/i/oauth2/authorize", new Dictionary<string, string?> { ["response_type"] = "code", ["client_id"] = clientId,
                ["redirect_uri"] = Redirect.AbsoluteUri, ["scope"] = "tweet.read tweet.write users.read offline.access", ["state"] = state, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256" });
        return new { authorizationUrl = url, redirectUri = Redirect.AbsoluteUri };
    }

    public async Task<string> CompleteOAuth(string? code, string? state, string? error, CancellationToken cancellation)
    {
        (string Kind, string Verifier, string ClientId, string? ClientSecret, DateTimeOffset At) attempt;
        lock (attempts) { if (state == null || !attempts.Remove(state, out attempt)) throw new ArgumentException("This sign-in response is unknown, expired, or already used."); }
        var name = Kinds[attempt.Kind].Name;
        if (!string.IsNullOrEmpty(error) || string.IsNullOrWhiteSpace(code)) return $"{name} did not authorize the connection.";
        using var http = Client();
        var form = new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = Redirect.AbsoluteUri, ["client_id"] = attempt.ClientId };
        using var exchange = new HttpRequestMessage(HttpMethod.Post, attempt.Kind == "linkedin" ? "https://www.linkedin.com/oauth/v2/accessToken" : "https://api.x.com/2/oauth2/token");
        if (attempt.Kind == "linkedin") form["client_secret"] = attempt.ClientSecret!;
        else { form["code_verifier"] = attempt.Verifier; if (attempt.ClientSecret != null) exchange.Headers.Authorization = Basic(attempt.ClientId, attempt.ClientSecret); }
        exchange.Content = new FormUrlEncodedContent(form);
        using var token = await Read(http, exchange, cancellation);
        var access = token.RootElement.GetProperty("access_token").GetString()!;
        var refresh = token.RootElement.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        DateTimeOffset? expires = token.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? DateTimeOffset.UtcNow.AddSeconds(seconds) : null;
        string account, subject;
        using (var me = new HttpRequestMessage(HttpMethod.Get, attempt.Kind == "linkedin" ? "https://api.linkedin.com/v2/userinfo" : "https://api.x.com/2/users/me"))
        {
            me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            using var who = await Read(http, me, cancellation);
            if (attempt.Kind == "linkedin") { subject = who.RootElement.GetProperty("sub").GetString()!; account = who.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "LinkedIn member" : "LinkedIn member"; }
            else { var data = who.RootElement.GetProperty("data"); subject = data.GetProperty("username").GetString()!; account = "@" + subject; }
        }
        var connection = Add(new PublishingConnection(Guid.NewGuid().ToString("N"), attempt.Kind, "ready", account, null, DateTimeOffset.UtcNow, expires, null));
        await SaveSecret(connection.Id, new Secret(access, refresh, attempt.ClientId, attempt.ClientSecret, subject, expires), cancellation);
        return $"{name} is connected as {account}. Return to the workspace; this window may be closed.";
    }

    public async Task Disconnect(string id, CancellationToken cancellation)
    {
        var ledger = Ledger();
        if (ledger.Connections.All(item => item.Id != id)) throw new KeyNotFoundException("That channel isn't connected.");
        if (ledger.Publications.Any(item => item.ConnectionId == id && item.Status is "scheduled" or "publishing")) throw new InvalidOperationException("Cancel this channel's scheduled posts first.");
        await vault.Execute("forget", ledger.VaultScope, id, null, cancellation);
        Change(current => (current with { Connections = [.. current.Connections.Where(item => item.Id != id)] }, 0));
    }

    // ---------- Publishing ----------
    static string Str(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    /// <summary>How long the text is by the channel's own rule: Bluesky counts graphemes, X counts every link as 23.</summary>
    public static int Length(string kind, string text) => kind switch
    {
        "bluesky" => new StringInfo(text).LengthInTextElements,
        "x" => Regex.Replace(text, @"https?://\S+", new string('x', 23)).Length,
        _ => text.Length
    };

    public async Task<Publication> Publish(int draftId, DraftPublishRequest request, string by, CancellationToken cancellation)
    {
        if (request.RequestId is not { Length: > 0 and <= 120 }) throw new ArgumentException("A request ID is required.");
        if (Ledger().Publications.FirstOrDefault(item => item.RequestId == request.RequestId) is { } replay) return replay;
        var connection = Ledger().Connections.FirstOrDefault(item => item.Id == request.ConnectionId && item.Status == "ready") ?? throw new ArgumentException("Choose a connected channel.");
        var draft = await Draft(draftId, cancellation) ?? throw new KeyNotFoundException("Draft not found.");
        if (Str(draft, "status") != "approved") throw new InvalidOperationException("Only an approved draft can be published.");
        if (Str(draft, "digest") != request.Digest) throw new InvalidOperationException("The draft changed since you reviewed it. Refresh and review it again.");
        if (!Serves(connection.Kind, Str(draft, "channel"))) throw new ArgumentException($"This is a {Str(draft, "channel")} draft; publish it to a {Str(draft, "channel")} channel.");
        var content = Str(draft, "content");
        var qa = CampaignQa.Check(Str(draft, "channel"), Str(draft, "destination"), content);
        if (qa.Status == "blocked") throw new InvalidOperationException("Launch QA blocks this draft: " + string.Join("; ", qa.Checks.Where(check => check.Result == "fail").Select(check => check.Label + " (" + check.Detail + ")")));
        if (Kinds[connection.Kind].Limit is { } limit && Length(connection.Kind, content) > limit)
            throw new InvalidOperationException($"{Kinds[connection.Kind].Name} allows {limit} characters; this is {Length(connection.Kind, content)}.");
        var now = Clock();
        var when = request.At is { } at && at > now.AddMinutes(1) ? at : (DateTimeOffset?)null;
        if (when > now.AddDays(60)) throw new ArgumentException("Schedule within the next 60 days.");
        var publication = Change(ledger =>
        {
            if (ledger.Publications.FirstOrDefault(item => item.RequestId == request.RequestId) is { } again) return (ledger, again);
            if (ledger.Publications.Any(item => item.DraftId == draftId && item.Status is "scheduled" or "publishing" or "published" or "unknown"))
                throw new InvalidOperationException("This draft is already published or scheduled.");
            var made = new Publication(Guid.NewGuid().ToString("N"), request.RequestId, draftId, request.Digest, connection.Id, connection.Kind, when == null ? "publishing" : "scheduled", when, DateTimeOffset.UtcNow, null, null, null, by);
            return (ledger with { Publications = [.. ledger.Publications.TakeLast(499), made] }, made);
        });
        return publication.Status == "publishing" ? await Execute(publication.Id, content, cancellation) : publication;
    }

    /// <summary>Scheduled posts whose time has come; called by the pump.</summary>
    public async Task<int> PublishDue(CancellationToken cancellation)
    {
        var due = Ledger().Publications.Where(item => item.Status == "scheduled" && item.ScheduledFor <= Clock()).ToArray();
        foreach (var item in due)
        {
            var draft = await Draft(item.DraftId, cancellation);
            if (draft is not { } found || Str(found, "status") != "approved" || Str(found, "digest") != item.Digest)
            { Set(item.Id, current => current with { Status = "failed", Error = "The draft changed or is no longer approved, so it wasn't posted." }); continue; }
            Set(item.Id, current => current with { Status = "publishing" });
            await Execute(item.Id, Str(found, "content"), cancellation);
        }
        return due.Length;
    }

    public Publication Cancel(string id) => Change(ledger =>
    {
        var item = ledger.Publications.FirstOrDefault(entry => entry.Id == id) ?? throw new KeyNotFoundException("That post isn't scheduled.");
        if (item.Status != "scheduled") throw new InvalidOperationException("Only a scheduled post can be cancelled.");
        var next = item with { Status = "cancelled" };
        return (ledger with { Publications = [.. ledger.Publications.Select(entry => entry.Id == id ? next : entry)] }, next);
    });

    /// <summary>After an uncertain outcome the owner checks the channel: it was posted (with its link) or it wasn't (retry later).</summary>
    public async Task<Publication> Resolve(string id, PublicationResolve resolve, CancellationToken cancellation)
    {
        var item = Ledger().Publications.FirstOrDefault(entry => entry.Id == id) ?? throw new KeyNotFoundException("Post not found.");
        if (item.Status != "unknown") throw new InvalidOperationException("Only a post with an uncertain outcome needs resolving.");
        if (resolve.Outcome == "posted")
        {
            var url = Https(resolve.Url, "The post's link");
            var next = Set(id, current => current with { Status = "published", Url = url, PublishedAt = DateTimeOffset.UtcNow, Error = null });
            if (await MarkPosted(item.DraftId, url, cancellation) is { } error) logger.LogWarning("Posted draft {Draft} could not be marked posted: {Error}", item.DraftId, error);
            return next;
        }
        if (resolve.Outcome == "not_posted") return Set(id, current => current with { Status = "failed", Error = "Confirmed not posted by the owner." });
        throw new ArgumentException("Say whether it was posted.");
    }

    async Task<Publication> Execute(string id, string content, CancellationToken cancellation)
    {
        await publishGate.WaitAsync(cancellation);
        try
        {
            var item = Ledger().Publications.First(entry => entry.Id == id);
            var connection = Ledger().Connections.FirstOrDefault(entry => entry.Id == item.ConnectionId);
            if (connection == null) return Set(id, current => current with { Status = "failed", Error = "The channel was disconnected." });
            string url;
            try { url = await Post(connection, item, content, cancellation); }
            // A definite refusal, or a problem before anything was sent: nothing was posted.
            catch (InvalidOperationException refused) { return Set(id, current => current with { Status = "failed", Error = refused.Message }); }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or KeyNotFoundException or JsonException)
            {
                // The request may have reached the channel: never retry on our own.
                return Set(id, current => current with { Status = "unknown", Error = error is KeyNotFoundException or JsonException
                    ? "The channel accepted the request but its answer couldn't be read, so the post is probably live. Check the channel, then record what happened."
                    : "The connection failed after the post was sent, so it may or may not have been published. Check the channel, then record what happened." });
            }
            var done = Set(id, current => current with { Status = "published", Url = url, PublishedAt = DateTimeOffset.UtcNow, Error = null });
            if (await MarkPosted(item.DraftId, url, cancellation) is { } markError) logger.LogWarning("Published draft {Draft} could not be marked posted: {Error}", item.DraftId, markError);
            marketing.InvalidateState();
            return done;
        }
        finally { publishGate.Release(); }
    }

    /// <summary>A definite refusal from the channel: nothing was posted, so it is safe to fix and try again.</summary>
    sealed class Refused(string message) : InvalidOperationException(message);

    static async Task<JsonDocument> Read(HttpClient http, HttpRequestMessage request, CancellationToken cancellation)
    {
        using var response = await http.SendAsync(request, cancellation);
        var text = await response.Content.ReadAsStringAsync(cancellation);
        if (!response.IsSuccessStatusCode)
        {
            // The human-readable message first; a bare error code only when there is nothing else.
            var detail = new[] { "message", "error_description", "detail", "error" }
                .Select(key => Regex.Match(text, "\"" + key + "\"\\s*:\\s*\"([^\"]{1,240})").Groups[1].Value).FirstOrDefault(value => value.Length > 0) ?? "";
            throw new Refused($"The channel refused it ({(int)response.StatusCode}{(detail.Length > 0 ? ": " + detail : "")}).");
        }
        try { return JsonDocument.Parse(text.Length == 0 ? "{}" : text); }
        catch (JsonException) { throw new Refused("The channel answered in an unexpected format."); }
    }

    record BlueskyAuth(string Jwt, string Did, string Handle);
    static async Task<BlueskyAuth> BlueskySession(HttpClient http, string server, string handle, string password, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, server + "/xrpc/com.atproto.server.createSession") { Content = Json(new { identifier = handle, password }) };
        using var session = await Read(http, request, cancellation);
        return new(session.RootElement.GetProperty("accessJwt").GetString()!, session.RootElement.GetProperty("did").GetString()!, session.RootElement.GetProperty("handle").GetString()!);
    }
    static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    /// <summary>Links in a Bluesky post, as byte-offset facets so they are clickable.</summary>
    public static object[] BlueskyLinks(string text)
    {
        var facets = new List<object>();
        foreach (Match match in Regex.Matches(text, @"https?://[^\s)\]]+"))
        {
            var uri = match.Value.TrimEnd('.', ',', ';', ':', '!', '?');
            var start = Encoding.UTF8.GetByteCount(text[..match.Index]);
            facets.Add(new Dictionary<string, object> { ["index"] = new { byteStart = start, byteEnd = start + Encoding.UTF8.GetByteCount(uri) },
                ["features"] = new object[] { new Dictionary<string, object> { ["$type"] = "app.bsky.richtext.facet#link", ["uri"] = uri } } });
        }
        return [.. facets];
    }

    /// <summary>LinkedIn post text is "little text": these characters are markup unless escaped.</summary>
    public static string LinkedInText(string text) => Regex.Replace(text, @"[\\|{}@\[\]()<>#*_~]", match => "\\" + match.Value);

    async Task<string> Post(PublishingConnection connection, Publication item, string content, CancellationToken cancellation)
    {
        var secret = await ReadSecret(connection.Id, cancellation);
        using var http = Client();
        switch (connection.Kind)
        {
            case "bluesky":
            {
                var auth = await BlueskySession(http, connection.Address!, secret.Subject!, secret.Token, cancellation);
                var record = new Dictionary<string, object> { ["$type"] = "app.bsky.feed.post", ["text"] = content, ["createdAt"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture), ["langs"] = new[] { "en" } };
                if (BlueskyLinks(content) is { Length: > 0 } facets) record["facets"] = facets;
                using var request = new HttpRequestMessage(HttpMethod.Post, connection.Address + "/xrpc/com.atproto.repo.createRecord") { Content = Json(new { repo = auth.Did, collection = "app.bsky.feed.post", record }) };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Jwt);
                using var made = await Read(http, request, cancellation);
                var rkey = made.RootElement.GetProperty("uri").GetString()!.Split('/')[^1];
                return $"https://bsky.app/profile/{auth.Handle}/post/{rkey}";
            }
            case "mastodon":
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, connection.Address + "/api/v1/statuses") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["status"] = content, ["visibility"] = "public" }) };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret.Token);
                request.Headers.Add("Idempotency-Key", item.RequestId);
                using var made = await Read(http, request, cancellation);
                return made.RootElement.GetProperty("url").GetString()!;
            }
            case "wordpress":
            {
                var lines = content.Replace("\r\n", "\n").Split('\n');
                var heading = lines.FirstOrDefault(line => line.Trim().Length > 0)?.Trim() ?? "";
                var title = heading.StartsWith('#') ? heading.TrimStart('#').Trim() : heading.Length <= 120 ? heading : heading[..120];
                var body = heading.StartsWith('#') ? string.Join("\n", lines.SkipWhile(line => line.Trim() != heading).Skip(1)).Trim() : content;
                var colon = secret.Token.IndexOf(':');
                using var request = new HttpRequestMessage(HttpMethod.Post, connection.Address + "/wp-json/wp/v2/posts") { Content = Json(new { title, content = body, status = connection.SaveAsDraft ? "draft" : "publish" }) };
                request.Headers.Authorization = Basic(secret.Token[..colon], secret.Token[(colon + 1)..]);
                using var made = await Read(http, request, cancellation);
                return made.RootElement.GetProperty("link").GetString()!;
            }
            case "linkedin":
            {
                if (secret.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(5)) throw new Refused("LinkedIn access has expired (it lasts about 60 days). Disconnect and sign in again.");
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.linkedin.com/rest/posts") { Content = Json(new
                {
                    author = "urn:li:person:" + secret.Subject, commentary = LinkedInText(content), visibility = "PUBLIC",
                    distribution = new { feedDistribution = "MAIN_FEED", targetEntities = Array.Empty<object>(), thirdPartyDistributionChannels = Array.Empty<object>() },
                    lifecycleState = "PUBLISHED", isReshareDisabledByAuthor = false
                }) };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret.Token);
                request.Headers.Add("LinkedIn-Version", LinkedInVersion);
                request.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
                using var response = await http.SendAsync(request, cancellation);
                if (!response.IsSuccessStatusCode)
                {
                    var text = await response.Content.ReadAsStringAsync(cancellation);
                    throw new Refused($"LinkedIn refused it ({(int)response.StatusCode}: {Regex.Match(text, "\"message\"\\s*:\\s*\"([^\"]{1,240})").Groups[1].Value}).");
                }
                var urn = response.Headers.TryGetValues("x-restli-id", out var ids) ? ids.First() : throw new KeyNotFoundException("LinkedIn accepted the post but didn't return its ID.");
                return $"https://www.linkedin.com/feed/update/{urn}/";
            }
            case "x":
            {
                var token = secret.Token;
                if (secret.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(2))
                {
                    if (secret.Refresh == null) throw new Refused("X access has expired. Disconnect and sign in again.");
                    using var refresh = new HttpRequestMessage(HttpMethod.Post, "https://api.x.com/2/oauth2/token") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = secret.Refresh, ["client_id"] = secret.ClientId! }) };
                    if (secret.ClientSecret != null) refresh.Headers.Authorization = Basic(secret.ClientId!, secret.ClientSecret);
                    using var renewed = await Read(http, refresh, cancellation);
                    token = renewed.RootElement.GetProperty("access_token").GetString()!;
                    var expires = renewed.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? DateTimeOffset.UtcNow.AddSeconds(seconds) : DateTimeOffset.UtcNow.AddHours(2);
                    await SaveSecret(connection.Id, secret with { Token = token, Refresh = renewed.RootElement.TryGetProperty("refresh_token", out var r) ? r.GetString() : secret.Refresh, ExpiresAt = expires }, cancellation);
                }
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.com/2/tweets") { Content = Json(new { text = content }) };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var made = await Read(http, request, cancellation);
                return $"https://x.com/{secret.Subject}/status/{made.RootElement.GetProperty("data").GetProperty("id").GetString()}";
            }
        }
        throw new Refused("This channel type isn't supported.");
    }
}
