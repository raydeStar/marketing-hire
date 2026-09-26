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
    DateTimeOffset? ScheduledFor, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt, string? Url, string? Error, string By,
    string? RemoteId = null, string? Excerpt = null, string? Channel = null, PostResults? Results = null);
/// <summary>How a post did, as the channel reports it; visits come from the post's tracking link in Google Analytics.</summary>
public record PostResults(int? Likes, int? Reposts, int? Replies, int? Quotes, int? Impressions, int? Visits, DateTimeOffset CheckedAt, string? Note);
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
/// <summary>Assisted posting: the owner posts through the network's own composer; the host keeps the record and the reminder.</summary>
public record AssistRequest(string RequestId, string Digest, DateTimeOffset? At);
public record PostedLink(string Url);
public record PublicationResolve(string Outcome, string? Url);

/// <summary>Publishing an approved draft to a channel the owner connected: Bluesky, Mastodon, WordPress, LinkedIn or X.
/// Approval never publishes; the owner publishes or schedules the exact approved text as a separate act. A post whose
/// outcome is uncertain is never retried automatically, so a network failure can't post twice.</summary>
public sealed class Publishing(Store store, ICredentialVault vault, MarketingBackend marketing, McpConnections google, DataConnections data, ILogger<Publishing> logger, string localOrigin)
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
    /// <summary>A scheduled post more than this late (the workspace was off) is held for the owner instead of going out late.</summary>
    public static readonly TimeSpan Lateness = TimeSpan.FromHours(2);
    public Func<CancellationToken, Task<(string ClientId, string ClientSecret)?>> GoogleClient { get; set; } =
        async cancellation => await google.SavedGoogleClient(cancellation) is { } client ? (client.ClientId, client.ClientSecret) : null;
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;
    /// <summary>Visits from a post's tracking link (utm_source, utm_campaign) since it went out, when Google Analytics is connected.</summary>
    public Func<string, string, DateTimeOffset, CancellationToken, Task<int?>> Visits { get; set; } = (source, campaign, since, cancellation) => data.CampaignVisits(source, campaign, since, cancellation);
    public Uri Redirect => new UriBuilder(new Uri(localOrigin)) { Host = "127.0.0.1", Path = "/api/publishing/oauth/callback" }.Uri;

    public static readonly Dictionary<string, (string Name, string[] Channels, int? Limit)> Kinds = new()
    {
        ["bluesky"] = ("Bluesky", ["bluesky", "bsky"], 300),
        ["mastodon"] = ("Mastodon", ["mastodon", "fediverse"], 500),
        ["wordpress"] = ("WordPress", ["blog", "wordpress", "website", "site"], null),
        ["linkedin"] = ("LinkedIn", ["linkedin"], 3000),
        ["x"] = ("X", ["x", "twitter", "x (twitter)", "x/twitter"], 280),
        ["email"] = ("Email (Gmail drafts)", ["email", "e-mail", "newsletter", "gmail"], null),
        ["buttondown"] = ("Buttondown (newsletter drafts)", ["newsletter", "buttondown"], null),
        ["hirezero"] = ("HireZero site (drafts)", ["blog", "website", "site", "hirezero"], null),
    };
    /// <summary>Kinds that only ever create a draft in the service; the owner sends from there, so there is no schedule and no results to read.</summary>
    public static bool DraftsOnly(string kind) => kind is "email" or "buttondown" or "hirezero";
    /// <summary>A destination that is one specific public post (X, Bluesky, Hacker News, Reddit, Threads, LinkedIn, Mastodon): the draft is a reply to it.</summary>
    public static bool IsReply(string destination) => Regex.IsMatch(destination.Trim(),
        @"^https://((www\.)?(x|twitter)\.com/[^/]+/status/\d+|bsky\.app/profile/[^/]+/post/\w+|news\.ycombinator\.com/item\?id=\d+|((www|old)\.)?reddit\.com/r/[^/]+/comments/|(www\.)?threads\.(net|com)/@[^/]+/post/|(www\.)?linkedin\.com/(feed/update/|posts/)|[^/]+/@[\w.-]+/\d{6,}$)");
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
        connections = ledger.Connections, publications = ledger.Publications.OrderByDescending(item => item.CreatedAt).Take(100),
        suggested = SuggestedChannels.ToDictionary(channel => channel.ToLowerInvariant(), channel => SuggestedTime(channel) is var (at, why) ? new { at, why } : null) }; }

    static readonly string[] SuggestedChannels = ["LinkedIn", "X", "Bluesky", "Mastodon", "Threads", "Facebook", "Instagram", "Hacker News", "Reddit", "Blog", "Newsletter"];
    static readonly DayOfWeek[] Weekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    /// <summary>When to post next on a channel, in this machine's time zone: the weekday and hour this channel's own posts did best
    /// once five or more have results (replies count most, then reposts, then likes); until then a common starting point for the network,
    /// said to be one. Always at least an hour away.</summary>
    public (DateTimeOffset At, string Why) SuggestedTime(string channel, DateTimeOffset? from = null)
    {
        var now = from ?? Clock();
        var zone = TimeZoneInfo.Local;
        var posts = Ledger().Publications.Where(item => item.Status == "published" && item.PublishedAt != null && item.Results != null &&
            string.Equals(item.Channel ?? (Kinds.TryGetValue(item.Kind, out var known) ? known.Name : item.Kind), channel, StringComparison.OrdinalIgnoreCase)).ToArray();
        DayOfWeek[] days; int hour, minute; string why;
        if (posts.Length >= 5)
        {
            var best = posts.GroupBy(item => { var local = TimeZoneInfo.ConvertTime(item.PublishedAt!.Value, zone); return (local.DayOfWeek, local.Hour); })
                .Select(group => (group.Key, Score: group.Average(item => (item.Results!.Likes ?? 0) + 2.0 * (item.Results.Reposts ?? 0) + 3.0 * (item.Results.Replies ?? 0)), Count: group.Count()))
                .OrderByDescending(item => item.Score).ThenByDescending(item => item.Count).First();
            (days, hour, minute) = ([best.Key.DayOfWeek], best.Key.Hour, 0);
            why = $"Your {channel} posts did best on {best.Key.DayOfWeek}s around {new DateTime(2000, 1, 1, best.Key.Hour, 0, 0):h tt} ({best.Count} of {posts.Length} posts with results).";
        }
        else
        {
            (days, hour, minute) = channel.Trim().ToLowerInvariant() switch
            {
                "linkedin" => ([DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday], 8, 30),
                "x" or "twitter" => (Weekdays, 9, 0),
                "hacker news" or "hn" => ([DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday], 8, 0),
                "newsletter" or "email" => ([DayOfWeek.Tuesday, DayOfWeek.Thursday], 9, 0),
                "reddit" => (Weekdays, 8, 0),
                _ => (Weekdays, 10, 0)
            };
            why = $"A common starting point for {channel} (weekday mornings), not measured for you yet: after five posts with results it uses your own best time.";
        }
        var start = TimeZoneInfo.ConvertTime(now.AddHours(1), zone);
        for (var offset = 0; offset < 14; offset++)
        {
            var day = start.Date.AddDays(offset);
            if (!days.Contains(day.DayOfWeek)) continue;
            var local = new DateTime(day.Year, day.Month, day.Day, hour, minute, 0, DateTimeKind.Unspecified);
            var at = new DateTimeOffset(local, zone.GetUtcOffset(local));
            if (at >= now.AddHours(1)) return (at, why);
        }
        return (now.AddDays(1), why);
    }

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
            case "hirezero":
            {
                // The site's own CMS, over MCP with its drafts-only agent key: check the key and that the tools are there.
                address = SiteAddress(request.Address);
                using var tools = await SiteCall(http, address, secret, "tools/list", new { }, cancellation);
                var names = tools.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).ToHashSet();
                if (!names.Contains("save_post_draft") || !names.Contains("save_landing_draft"))
                    throw new InvalidOperationException("That address answers MCP but isn't a HireZero site (no draft tools).");
                if (names.Any(name => name?.Contains("publish", StringComparison.OrdinalIgnoreCase) == true))
                    throw new InvalidOperationException("That key can publish; use a drafts-only agent key from the site's Settings.");
                account = new Uri(address).Host;
                break;
            }
            case "buttondown":
            {
                address = Buttondown;
                using var check = new HttpRequestMessage(HttpMethod.Get, Buttondown + "/ping");
                check.Headers.Authorization = new AuthenticationHeaderValue("Token", secret);
                using var response = await http.SendAsync(check, cancellation);
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Buttondown didn't accept that API key ({(int)response.StatusCode}).");
                account = "Newsletter";
                break;
            }
            default: throw new ArgumentException("Choose Bluesky, Mastodon, WordPress or Buttondown here; LinkedIn and X sign in.");
        }
        var connection = Add(new PublishingConnection(Guid.NewGuid().ToString("N"), kind, "ready", account, address, DateTimeOffset.UtcNow, null, null, kind == "wordpress" && request.SaveAsDraft == true));
        await SaveSecret(connection.Id, new Secret(secret, null, null, null, kind == "bluesky" ? request.Account!.Trim().TrimStart('@') : null, null), cancellation);
        return connection;
    }

    static AuthenticationHeaderValue Basic(string user, string password) => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password)));

    /// <summary>LinkedIn and X: the owner's own developer app, OAuth in the browser, returning to this computer.</summary>
    public async Task<object> BeginOAuth(string kind, PublishingOAuthStart start, CancellationToken cancellation)
    {
        if (kind is not ("linkedin" or "x" or "email")) throw new ArgumentException("Sign-in is for LinkedIn, X and email.");
        var saved = kind == "email" ? await GoogleClient(cancellation) ?? throw new InvalidOperationException("Set up the Google app once first: Settings → Google app.") : default;
        var clientId = kind == "email" ? saved.ClientId : (start.ClientId ?? "").Trim();
        if (clientId.Length is < 4 or > 200) throw new ArgumentException("Paste the app's client ID.");
        var clientSecret = kind == "email" ? saved.ClientSecret : string.IsNullOrWhiteSpace(start.ClientSecret) ? null : start.ClientSecret.Trim();
        if (kind == "linkedin" && clientSecret == null) throw new ArgumentException("LinkedIn needs the app's client secret too.");
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        lock (attempts)
        {
            foreach (var old in attempts.Where(item => item.Value.At < DateTimeOffset.UtcNow.AddMinutes(-15)).Select(item => item.Key).ToArray()) attempts.Remove(old);
            attempts[state] = (kind, verifier, clientId, clientSecret, DateTimeOffset.UtcNow);
        }
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var url = kind == "email"
            ? QueryHelpers.AddQueryString("https://accounts.google.com/o/oauth2/v2/auth", new Dictionary<string, string?> { ["response_type"] = "code", ["client_id"] = clientId,
                ["redirect_uri"] = Redirect.AbsoluteUri, ["scope"] = "openid email " + GmailScope, ["access_type"] = "offline", ["prompt"] = "consent",
                ["state"] = state, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256" })
            : kind == "linkedin"
            ? QueryHelpers.AddQueryString("https://www.linkedin.com/oauth/v2/authorization", new Dictionary<string, string?> { ["response_type"] = "code", ["client_id"] = clientId,
                ["redirect_uri"] = Redirect.AbsoluteUri, ["scope"] = "openid profile w_member_social", ["state"] = state })
            : QueryHelpers.AddQueryString("https://x.com/i/oauth2/authorize", new Dictionary<string, string?> { ["response_type"] = "code", ["client_id"] = clientId,
                ["redirect_uri"] = Redirect.AbsoluteUri, ["scope"] = "tweet.read tweet.write users.read offline.access", ["state"] = state, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256" });
        return new { authorizationUrl = url, redirectUri = Redirect.AbsoluteUri };
    }

    /// <summary>Gmail's narrowest scope that can create drafts. It would also allow sending; this host only ever creates drafts.</summary>
    const string GmailScope = "https://www.googleapis.com/auth/gmail.compose";
    const string Buttondown = "https://api.buttondown.com/v1";

    public async Task<string> CompleteOAuth(string? code, string? state, string? error, CancellationToken cancellation)
    {
        (string Kind, string Verifier, string ClientId, string? ClientSecret, DateTimeOffset At) attempt;
        lock (attempts) { if (state == null || !attempts.Remove(state, out attempt)) throw new ArgumentException("This sign-in response is unknown, expired, or already used."); }
        var name = Kinds[attempt.Kind].Name;
        if (!string.IsNullOrEmpty(error) || string.IsNullOrWhiteSpace(code)) return $"{name} did not authorize the connection.";
        using var http = Client();
        var form = new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = Redirect.AbsoluteUri, ["client_id"] = attempt.ClientId };
        using var exchange = new HttpRequestMessage(HttpMethod.Post, attempt.Kind switch { "linkedin" => "https://www.linkedin.com/oauth/v2/accessToken", "email" => GoogleToken, _ => "https://api.x.com/2/oauth2/token" });
        if (attempt.Kind == "linkedin") form["client_secret"] = attempt.ClientSecret!;
        else if (attempt.Kind == "email") { form["client_secret"] = attempt.ClientSecret!; form["code_verifier"] = attempt.Verifier; }
        else { form["code_verifier"] = attempt.Verifier; if (attempt.ClientSecret != null) exchange.Headers.Authorization = Basic(attempt.ClientId, attempt.ClientSecret); }
        exchange.Content = new FormUrlEncodedContent(form);
        using var token = await Read(http, exchange, cancellation);
        var access = token.RootElement.GetProperty("access_token").GetString()!;
        var refresh = token.RootElement.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        DateTimeOffset? expires = token.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? DateTimeOffset.UtcNow.AddSeconds(seconds) : null;
        if (attempt.Kind == "email")
        {
            var granted = token.RootElement.TryGetProperty("scope", out var scope) ? scope.GetString() ?? "" : "";
            if (!granted.Split(' ').Contains(GmailScope)) return "Permission to create Gmail drafts wasn't granted. Try again and tick it on Google's consent screen.";
            if (refresh == null) return "Google didn't provide lasting access. Try again and approve access on the consent screen.";
        }
        string account, subject;
        using (var me = new HttpRequestMessage(HttpMethod.Get, attempt.Kind switch { "linkedin" => "https://api.linkedin.com/v2/userinfo", "email" => "https://openidconnect.googleapis.com/v1/userinfo", _ => "https://api.x.com/2/users/me" }))
        {
            me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            using var who = await Read(http, me, cancellation);
            if (attempt.Kind == "email") { subject = who.RootElement.GetProperty("email").GetString()!; account = subject; }
            else if (attempt.Kind == "linkedin") { subject = who.RootElement.GetProperty("sub").GetString()!; account = who.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "LinkedIn member" : "LinkedIn member"; }
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
        if (IsReply(Str(draft, "destination"))) throw new ArgumentException("This draft replies to a specific post; post it yourself from that post so it lands as a reply, not a new post.");
        var content = Str(draft, "content");
        var qa = CampaignQa.Check(Str(draft, "channel"), Str(draft, "destination"), content);
        if (qa.Status == "blocked") throw new InvalidOperationException("Launch QA blocks this draft: " + string.Join("; ", qa.Checks.Where(check => check.Result == "fail").Select(check => check.Label + " (" + check.Detail + ")")));
        if (connection.Kind is "email" or "buttondown") Email(content); // a missing subject or a bad address is refused before anything is created
        if (DraftsOnly(connection.Kind) && request.At != null) throw new ArgumentException($"{Kinds[connection.Kind].Name} only saves a draft; schedule the send there.");
        if (Kinds[connection.Kind].Limit is { } limit && Length(connection.Kind, content) > limit)
            throw new InvalidOperationException($"{Kinds[connection.Kind].Name} allows {limit} characters; this is {Length(connection.Kind, content)}.");
        var now = Clock();
        var when = request.At is { } at && at > now.AddMinutes(1) ? at : (DateTimeOffset?)null;
        if (when > now.AddDays(60)) throw new ArgumentException("Schedule within the next 60 days.");
        var publication = Change(ledger =>
        {
            if (ledger.Publications.FirstOrDefault(item => item.RequestId == request.RequestId) is { } again) return (ledger, again);
            if (ledger.Publications.Any(item => item.DraftId == draftId && Active(item.Status)))
                throw new InvalidOperationException("This draft is already published or scheduled.");
            var made = new Publication(Guid.NewGuid().ToString("N"), request.RequestId, draftId, request.Digest, connection.Id, connection.Kind, when == null ? "publishing" : "scheduled", when, DateTimeOffset.UtcNow, null, null, null, by,
                null, Regex.Replace(content, @"\s+", " ").Trim() is var flat && flat.Length > 160 ? flat[..160] + "…" : flat, Str(draft, "channel"));
            return (ledger with { Publications = [.. ledger.Publications.TakeLast(499), made] }, made);
        });
        return publication.Status == "publishing" ? await Execute(publication.Id, content, cancellation) : publication;
    }

    static bool Active(string status) => status is "scheduled" or "publishing" or "published" or "unknown" or "awaiting_link" or "due";
    /// <summary>The channel's kind for an assisted post, when it is one the host knows.</summary>
    static string KindOf(string channel) => Kinds.FirstOrDefault(item => Serves(item.Key, channel)).Key ?? (channel.Trim().ToLowerInvariant() is "threads" ? "threads" : "other");

    /// <summary>The owner will post it themselves, now (the cockpit opens the network's composer) or at a time (a reminder).
    /// The same checks as publishing apply: approved, unchanged since review, launch QA, the channel's length rule.</summary>
    public async Task<Publication> Assist(int draftId, AssistRequest request, string by, CancellationToken cancellation)
    {
        if (request.RequestId is not { Length: > 0 and <= 120 }) throw new ArgumentException("A request ID is required.");
        if (Ledger().Publications.FirstOrDefault(item => item.RequestId == request.RequestId) is { } replay) return replay;
        var draft = await Draft(draftId, cancellation) ?? throw new KeyNotFoundException("Draft not found.");
        if (Str(draft, "status") != "approved") throw new InvalidOperationException("Only an approved draft can be posted.");
        if (Str(draft, "digest") != request.Digest) throw new InvalidOperationException("The draft changed since you reviewed it. Refresh and review it again.");
        var content = Str(draft, "content"); var channel = Str(draft, "channel"); var kind = KindOf(channel);
        var qa = CampaignQa.Check(channel, Str(draft, "destination"), content);
        if (qa.Status == "blocked") throw new InvalidOperationException("Launch QA blocks this draft: " + string.Join("; ", qa.Checks.Where(check => check.Result == "fail").Select(check => check.Label + " (" + check.Detail + ")")));
        if (Kinds.TryGetValue(kind, out var info) && info.Limit is { } limit && Length(kind, content) > limit)
            throw new InvalidOperationException($"{info.Name} allows {limit} characters; this is {Length(kind, content)}.");
        var now = Clock();
        var when = request.At is { } at && at > now.AddMinutes(1) ? at : (DateTimeOffset?)null;
        if (when > now.AddDays(60)) throw new ArgumentException("Schedule within the next 60 days.");
        return Change(ledger =>
        {
            if (ledger.Publications.FirstOrDefault(item => item.RequestId == request.RequestId) is { } again) return (ledger, again);
            if (ledger.Publications.Any(item => item.DraftId == draftId && Active(item.Status))) throw new InvalidOperationException("This draft is already published or scheduled.");
            var made = new Publication(Guid.NewGuid().ToString("N"), request.RequestId, draftId, request.Digest, "", kind, when == null ? "awaiting_link" : "scheduled", when, now, null, null, null, by,
                null, Regex.Replace(content, @"\s+", " ").Trim() is var flat && flat.Length > 160 ? flat[..160] + "…" : flat, channel);
            return (ledger with { Publications = [.. ledger.Publications.TakeLast(499), made] }, made);
        });
    }

    /// <summary>The owner posted it: the live link makes it a published post, with results read back like any other.</summary>
    public async Task<Publication> RecordLink(string id, PostedLink link, CancellationToken cancellation)
    {
        var item = Ledger().Publications.FirstOrDefault(entry => entry.Id == id) ?? throw new KeyNotFoundException("Post not found.");
        if (item.Status is not ("awaiting_link" or "due" or "scheduled" or "unknown")) throw new InvalidOperationException("This post already has its link.");
        var url = Https(link.Url, "The post's link");
        var remote = await RemoteFor(item.Kind, url, cancellation);
        var next = Set(id, current => current with { Status = "published", Url = url, RemoteId = remote ?? current.RemoteId, PublishedAt = Clock(), Error = null });
        if (await MarkPosted(item.DraftId, url, cancellation) is { } error) logger.LogWarning("Posted draft {Draft} could not be marked posted: {Error}", item.DraftId, error);
        marketing.InvalidateState();
        return next;
    }

    /// <summary>Where a pasted link's counts can be read without an account: Bluesky's public AppView (after resolving the
    /// handle), a Mastodon server's public status API; an X post's ID when X is connected.</summary>
    async Task<string?> RemoteFor(string kind, string url, CancellationToken cancellation)
    {
        var uri = new Uri(url);
        try
        {
            if (kind == "bluesky" && Regex.Match(uri.AbsolutePath, @"^/profile/([^/]+)/post/([A-Za-z0-9]+)$") is { Success: true } bsky)
            {
                var actor = bsky.Groups[1].Value;
                if (!actor.StartsWith("did:", StringComparison.Ordinal))
                {
                    using var http = Client();
                    using var request = new HttpRequestMessage(HttpMethod.Get, "https://public.api.bsky.app/xrpc/com.atproto.identity.resolveHandle?handle=" + Uri.EscapeDataString(actor));
                    using var resolved = await Read(http, request, cancellation);
                    actor = resolved.RootElement.GetProperty("did").GetString()!;
                }
                return $"at://{actor}/app.bsky.feed.post/{bsky.Groups[2].Value}";
            }
            if (kind == "mastodon" && Regex.Match(uri.AbsolutePath, @"/(\d{5,24})/?$") is { Success: true } status)
                return $"https://{uri.Host}/api/v1/statuses/{status.Groups[1].Value}";
            if (kind == "x" && Regex.Match(uri.AbsolutePath, @"/status/(\d{5,24})") is { Success: true } tweet) return tweet.Groups[1].Value;
        }
        catch (Exception error) when (error is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException) { }
        return null;
    }

    /// <summary>Scheduled posts whose time has come; called by the pump.</summary>
    public async Task<int> PublishDue(CancellationToken cancellation)
    {
        var due = Ledger().Publications.Where(item => item.Status == "scheduled" && item.ScheduledFor <= Clock()).ToArray();
        foreach (var item in due)
        {
            // An assisted post is the owner's to make: its time turns into a reminder, never a post.
            if (item.ConnectionId.Length == 0) { Set(item.Id, current => current with { Status = "due" }); continue; }
            if (item.ScheduledFor < Clock() - Lateness)
            {
                Set(item.Id, current => current with { Status = "missed", Error = $"The workspace wasn't running at {item.ScheduledFor:u}, so this wasn't posted late. Pick a new time or publish it now." });
                continue;
            }
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
        if (item.Status is not ("scheduled" or "awaiting_link" or "due")) throw new InvalidOperationException("Only a scheduled post can be cancelled.");
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
            string url; string? remote;
            try { (url, remote) = await Post(connection, item, content, cancellation); }
            // A definite refusal, or a problem before anything was sent: nothing was posted.
            catch (InvalidOperationException refused) { return Set(id, current => current with { Status = "failed", Error = refused.Message }); }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or KeyNotFoundException or JsonException)
            {
                // The request may have reached the channel: never retry on our own.
                return Set(id, current => current with { Status = "unknown", Error = error is KeyNotFoundException or JsonException
                    ? "The channel accepted the request but its answer couldn't be read, so the post is probably live. Check the channel, then record what happened."
                    : "The connection failed after the post was sent, so it may or may not have been published. Check the channel, then record what happened." });
            }
            var done = Set(id, current => current with { Status = "published", Url = url, RemoteId = remote, PublishedAt = DateTimeOffset.UtcNow, Error = null });
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

    /// <summary>A blog draft's title and body: a leading “# Heading” (or the first line) is the title.</summary>
    static (string Title, string Body) BlogParts(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var heading = lines.FirstOrDefault(line => line.Trim().Length > 0)?.Trim() ?? "";
        var title = heading.StartsWith('#') ? heading.TrimStart('#').Trim() : heading.Length <= 120 ? heading : heading[..120];
        var body = heading.StartsWith('#') ? string.Join("\n", lines.SkipWhile(line => line.Trim() != heading).Skip(1)).Trim() : content;
        return (title, body.Length > 0 ? body : content);
    }

    /// <summary>The site's origin: https, or plain http only on this machine (for testing against a local copy).</summary>
    static string SiteAddress(string? value)
    {
        var text = (value ?? "").Trim().TrimEnd('/');
        if (Uri.TryCreate(text, UriKind.Absolute, out var local) && local.Scheme == "http" && local.IsLoopback && local.UserInfo.Length == 0) return local.GetLeftPart(UriPartial.Authority);
        return new Uri(Https(value, "The site")).GetLeftPart(UriPartial.Authority);
    }

    async Task<JsonDocument> SiteCall(HttpClient http, string address, string token, string method, object parameters, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, address + "/mcp") { Content = Json(new { jsonrpc = "2.0", id = 1, method, @params = parameters }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        var reply = await Read(http, request, cancellation);
        if (reply.RootElement.TryGetProperty("error", out var error))
        {
            reply.Dispose();
            throw new Refused("The site refused it: " + (error.TryGetProperty("message", out var message) ? message.GetString() : "unknown error") + ".");
        }
        return reply;
    }

    /// <summary>One of the site's tools; its JSON answer, or a refusal carrying the site's own message.</summary>
    async Task<JsonElement> SiteTool(HttpClient http, string address, string token, string name, object arguments, CancellationToken cancellation)
    {
        using var reply = await SiteCall(http, address, token, "tools/call", new { name, arguments }, cancellation);
        var result = reply.RootElement.GetProperty("result");
        var text = result.TryGetProperty("content", out var parts) && parts.GetArrayLength() > 0 ? parts[0].GetProperty("text").GetString() ?? "" : "";
        if (result.TryGetProperty("isError", out var failed) && failed.GetBoolean()) throw new Refused("The site refused it: " + text);
        try { using var parsed = JsonDocument.Parse(text); return parsed.RootElement.Clone(); }
        catch (JsonException) { throw new Refused("The site answered in an unexpected format."); }
    }

    /// <summary>The HireZero connection for a site: the one at that address, or the only one there is (a local copy under test).</summary>
    PublishingConnection? SiteConnection(string? site)
    {
        var sites = Ledger().Connections.Where(item => item.Kind == "hirezero" && item.Status == "ready").ToArray();
        return sites.FirstOrDefault(item => Uri.TryCreate(item.Address, UriKind.Absolute, out var address) && SiteReader.NormalizeSite(address.Host) == site) ?? (sites.Length == 1 ? sites[0] : null);
    }

    /// <summary>For a shift writing the landing page of a connected HireZero site: its current sections and the section types it accepts.</summary>
    public async Task<(JsonElement Current, JsonElement Types)?> SiteLanding(string site, CancellationToken cancellation)
    {
        if (SiteConnection(site) is not { } connection) return null;
        var secret = await ReadSecret(connection.Id, cancellation);
        using var http = Client();
        var landing = await SiteTool(http, connection.Address!, secret.Token, "get_landing", new { }, cancellation);
        var types = await SiteTool(http, connection.Address!, secret.Token, "landing_section_types", new { }, cancellation);
        var current = landing.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.Object ? draft.GetProperty("body") : landing;
        return (current, types);
    }

    /// <summary>Approved landing-page copy (sections) as a draft on the HireZero site, with a review request. Returns the admin link.</summary>
    public async Task<string> SiteLandingDraft(string connectionId, JsonElement page, string note, CancellationToken cancellation)
    {
        var connection = Ledger().Connections.FirstOrDefault(item => item.Id == connectionId && item.Kind == "hirezero" && item.Status == "ready") ?? throw new ArgumentException("Choose a connected HireZero site.");
        var secret = await ReadSecret(connection.Id, cancellation);
        using var http = Client();
        await SiteTool(http, connection.Address!, secret.Token, "save_landing_draft", new
        {
            title = page.TryGetProperty("title", out var title) ? title.GetString() : null,
            description = page.TryGetProperty("description", out var description) ? description.GetString() : null,
            sections = page.GetProperty("sections"), note = note.Length > 480 ? note[..480] : note,
        }, cancellation);
        await SiteTool(http, connection.Address!, secret.Token, "request_review", new { kind = "landing", note = "Approved in the cockpit; ready for you to publish." }, cancellation);
        return $"{connection.Address}/admin/#landing";
    }

    /// <summary>Approved page copy as a new WordPress page in draft status: the live page is untouched, and the owner reviews
    /// and swaps it in WordPress. Returns the draft's edit link.</summary>
    public async Task<string> WordPressDraftPage(string connectionId, string title, string content, CancellationToken cancellation)
    {
        var connection = Ledger().Connections.FirstOrDefault(item => item.Id == connectionId && item.Kind == "wordpress" && item.Status == "ready") ?? throw new ArgumentException("Choose a connected WordPress site.");
        var secret = await ReadSecret(connection.Id, cancellation);
        using var http = Client();
        var colon = secret.Token.IndexOf(':');
        using var request = new HttpRequestMessage(HttpMethod.Post, connection.Address + "/wp-json/wp/v2/pages") { Content = Json(new { title = "Draft: " + title, content, status = "draft" }) };
        request.Headers.Authorization = Basic(secret.Token[..colon], secret.Token[(colon + 1)..]);
        using var made = await Read(http, request, cancellation);
        if (made.RootElement.TryGetProperty("status", out var status) && status.GetString() != "draft") throw new InvalidOperationException($"WordPress saved the page as “{status.GetString()}”, not a draft. Check the site now.");
        var id = made.RootElement.GetProperty("id").GetRawText();
        return $"{connection.Address}/wp-admin/post.php?post={id}&action=edit";
    }

    async Task<(string Url, string? Remote)> Post(PublishingConnection connection, Publication item, string content, CancellationToken cancellation)
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
                var uri = made.RootElement.GetProperty("uri").GetString()!;
                return ($"https://bsky.app/profile/{auth.Handle}/post/{uri.Split('/')[^1]}", uri);
            }
            case "mastodon":
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, connection.Address + "/api/v1/statuses") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["status"] = content, ["visibility"] = "public" }) };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret.Token);
                request.Headers.Add("Idempotency-Key", item.RequestId);
                using var made = await Read(http, request, cancellation);
                return (made.RootElement.GetProperty("url").GetString()!, made.RootElement.TryGetProperty("id", out var status) ? status.GetString() : null);
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
                return (made.RootElement.GetProperty("link").GetString()!, made.RootElement.TryGetProperty("id", out var post) ? post.GetRawText() : null);
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
                return ($"https://www.linkedin.com/feed/update/{urn}/", urn);
            }
            case "x":
            {
                var token = await XToken(connection, secret, http, cancellation);
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.com/2/tweets") { Content = Json(new { text = content }) };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var made = await Read(http, request, cancellation);
                var tweet = made.RootElement.GetProperty("data").GetProperty("id").GetString()!;
                return ($"https://x.com/{secret.Subject}/status/{tweet}", tweet);
            }
        }
        if (connection.Kind == "hirezero")
        {
            // A post draft on the site, then a review request: the owner publishes it from the site's admin.
            var (title, body) = BlogParts(content);
            var saved = await SiteTool(http, connection.Address!, secret.Token, "save_post_draft",
                new { title, body, note = $"From the marketing employee: approved draft #{item.DraftId} in the cockpit." }, cancellation);
            var slug = saved.GetProperty("slug").GetString()!;
            await SiteTool(http, connection.Address!, secret.Token, "request_review", new { kind = "post", slug, note = "Approved in the cockpit; ready for you to publish." }, cancellation);
            return ($"{connection.Address}/admin/#blog/{slug}", slug);
        }
        if (connection.Kind == "buttondown")
        {
            // Buttondown sends an email the moment it is created unless it is created as a draft; a reply that isn't a draft is an alarm, not a success.
            var (_, _, subjectLine, body) = Email(content);
            using var request = new HttpRequestMessage(HttpMethod.Post, Buttondown + "/emails") { Content = Json(new { subject = subjectLine, body, status = "draft" }) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Token", secret.Token);
            request.Headers.Add("Idempotency-Key", item.RequestId);
            using var made = await Read(http, request, cancellation);
            var status = made.RootElement.TryGetProperty("status", out var given) ? given.GetString() : null;
            var id = made.RootElement.TryGetProperty("id", out var email) ? email.GetString() : null;
            if (status != "draft") throw new Refused($"Buttondown did not keep this as a draft (it reports “{status ?? "no status"}”). Check Buttondown now.");
            return ("https://buttondown.com/emails", id);
        }
        if (connection.Kind == "email")
        {
            var (to, cc, subjectLine, body) = Email(content);
            return (await GmailDraft(secret, http, to, cc, subjectLine, body, cancellation), null);
        }
        throw new Refused("This channel type isn't supported.");
    }

    async Task<string> GmailDraft(Secret secret, HttpClient http, string[] to, string[] cc, string subject, string body, CancellationToken cancellation)
    {
        using var refresh = new HttpRequestMessage(HttpMethod.Post, GoogleToken) { Content = new FormUrlEncodedContent(new Dictionary<string, string>
            { ["grant_type"] = "refresh_token", ["refresh_token"] = secret.Refresh ?? "", ["client_id"] = secret.ClientId!, ["client_secret"] = secret.ClientSecret! }) };
        using var renewed = await Read(http, refresh, cancellation);
        var raw = Mime(secret.Subject!, to, cc, subject, body);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/drafts") { Content = Json(new { message = new { raw } }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", renewed.RootElement.GetProperty("access_token").GetString());
        using var made = await Read(http, request, cancellation);
        return "https://mail.google.com/mail/u/0/#drafts?compose=" + made.RootElement.GetProperty("message").GetProperty("id").GetString();
    }

    /// <summary>A Gmail draft from the workspace itself (the weekly update), in the connected mailbox. Null when no mailbox is connected.</summary>
    public async Task<string?> EmailDraft(string subject, string body, CancellationToken cancellation)
    {
        var connection = Ledger().Connections.FirstOrDefault(item => item.Kind == "email" && item.Status == "ready");
        if (connection == null) return null;
        using var http = Client();
        return await GmailDraft(await ReadSecret(connection.Id, cancellation), http, [], [], subject, body, cancellation);
    }

    async Task<string> XToken(PublishingConnection connection, Secret secret, HttpClient http, CancellationToken cancellation)
    {
        if (!(secret.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(2))) return secret.Token;
        if (secret.Refresh == null) throw new Refused("X access has expired. Disconnect and sign in again.");
        using var refresh = new HttpRequestMessage(HttpMethod.Post, "https://api.x.com/2/oauth2/token") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = secret.Refresh, ["client_id"] = secret.ClientId! }) };
        if (secret.ClientSecret != null) refresh.Headers.Authorization = Basic(secret.ClientId!, secret.ClientSecret);
        using var renewed = await Read(http, refresh, cancellation);
        var token = renewed.RootElement.GetProperty("access_token").GetString()!;
        var expires = renewed.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? DateTimeOffset.UtcNow.AddSeconds(seconds) : DateTimeOffset.UtcNow.AddHours(2);
        await SaveSecret(connection.Id, secret with { Token = token, Refresh = renewed.RootElement.TryGetProperty("refresh_token", out var r) ? r.GetString() : secret.Refresh, ExpiresAt = expires }, cancellation);
        return token;
    }

    // ---------- Results: how published posts did ----------
    /// <summary>Checks settle over a week: about an hour after posting, then every 6 hours for three days, then daily until day 8.</summary>
    static bool ResultsDue(Publication item, DateTimeOffset now)
    {
        if (item.Status != "published" || item.PublishedAt is not { } at || now - at > TimeSpan.FromDays(8) || DraftsOnly(item.Kind)) return false;
        var age = now - at;
        if (item.Results is not { } last) return age >= TimeSpan.FromMinutes(50);
        var gap = age < TimeSpan.FromDays(1) ? TimeSpan.FromHours(6) : age < TimeSpan.FromDays(3) ? TimeSpan.FromHours(12) : TimeSpan.FromHours(24);
        return now - last.CheckedAt >= gap;
    }

    static (string Source, string Campaign)? Tracking(string? text)
    {
        var link = Regex.Match(text ?? "", @"https?://\S*utm_[^\s]*").Value;
        if (link.Length == 0 || !Uri.TryCreate(link.TrimEnd('.', ',', ')', '…'), UriKind.Absolute, out var uri)) return null;
        var query = QueryHelpers.ParseQuery(uri.Query);
        return query.TryGetValue("utm_campaign", out var campaign) && query.TryGetValue("utm_source", out var source) && campaign.ToString().Length > 0 && source.ToString().Length > 0
            ? (source.ToString(), campaign.ToString()) : null;
    }

    /// <summary>Reads the counts for every post whose next check is due. Returns how many posts were checked.</summary>
    public async Task<int> CheckResults(CancellationToken cancellation)
    {
        var now = Clock();
        var due = Ledger().Publications.Where(item => ResultsDue(item, now)).Take(20).ToArray();
        foreach (var item in due)
        {
            var connection = Ledger().Connections.FirstOrDefault(entry => entry.Id == item.ConnectionId)
                ?? (item.ConnectionId.Length == 0 && item.Kind == "x" ? Ledger().Connections.FirstOrDefault(entry => entry.Kind == "x" && entry.Status == "ready") : null);
            PostResults counts;
            try { counts = await Counts(item, connection, cancellation); }
            catch (Exception error) when (error is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
            { counts = (item.Results ?? new PostResults(null, null, null, null, null, null, now, null)) with { CheckedAt = now, Note = "The channel didn't answer: " + error.Message }; }
            if (Tracking(await DraftContent(item, cancellation) ?? item.Excerpt) is { } tracking)
                try { counts = counts with { Visits = await Visits(tracking.Source, tracking.Campaign, item.PublishedAt!.Value, cancellation) ?? counts.Visits }; }
                catch (Exception error) when (error is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException) { }
            Set(item.Id, current => current with { Results = counts with { CheckedAt = now } });
        }
        return due.Length;
    }

    async Task<string?> DraftContent(Publication item, CancellationToken cancellation)
    {
        try { return await Draft(item.DraftId, cancellation) is { } draft ? Str(draft, "content") : null; }
        catch (Exception error) when (error is InvalidOperationException or IOException) { return null; }
    }

    async Task<PostResults> Counts(Publication item, PublishingConnection? connection, CancellationToken cancellation)
    {
        var now = Clock();
        static int? Int(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
        using var http = Client();
        switch (item.Kind)
        {
            case "bluesky" when item.RemoteId != null:
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://public.api.bsky.app/xrpc/app.bsky.feed.getPosts?uris=" + Uri.EscapeDataString(item.RemoteId));
                using var posts = await Read(http, request, cancellation);
                var post = posts.RootElement.GetProperty("posts").EnumerateArray().FirstOrDefault();
                if (post.ValueKind != JsonValueKind.Object) return new(null, null, null, null, null, null, now, "The post is no longer on Bluesky.");
                return new(Int(post, "likeCount"), Int(post, "repostCount"), Int(post, "replyCount"), Int(post, "quoteCount"), null, null, now, null);
            }
            case "mastodon" when item.RemoteId is { } api && api.StartsWith("https://", StringComparison.Ordinal):
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, api);
                using var status = await Read(http, request, cancellation);
                return new(Int(status.RootElement, "favourites_count"), Int(status.RootElement, "reblogs_count"), Int(status.RootElement, "replies_count"), null, null, null, now, null);
            }
            case "mastodon" when item.RemoteId != null && connection != null:
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, connection.Address + "/api/v1/statuses/" + Uri.EscapeDataString(item.RemoteId));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", (await ReadSecret(connection.Id, cancellation)).Token);
                using var status = await Read(http, request, cancellation);
                return new(Int(status.RootElement, "favourites_count"), Int(status.RootElement, "reblogs_count"), Int(status.RootElement, "replies_count"), null, null, null, now, null);
            }
            case "x" when item.RemoteId != null && connection != null:
            {
                var token = await XToken(connection, await ReadSecret(connection.Id, cancellation), http, cancellation);
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.x.com/2/tweets/{Uri.EscapeDataString(item.RemoteId)}?tweet.fields=public_metrics");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var tweet = await Read(http, request, cancellation);
                var metrics = tweet.RootElement.GetProperty("data").GetProperty("public_metrics");
                return new(Int(metrics, "like_count"), Int(metrics, "retweet_count"), Int(metrics, "reply_count"), Int(metrics, "quote_count"), Int(metrics, "impression_count"), null, now, null);
            }
            case "linkedin":
                return new(null, null, null, null, null, null, now, "LinkedIn doesn't share personal-post analytics with self-serve apps; visits come from the tracking link.");
            case "x" when connection == null:
                return new(null, null, null, null, null, null, now, "Connect X to read its counts (X bills API reads); visits come from the tracking link.");
            default:
                return new(null, null, null, null, null, null, now, null);
        }
    }

    /// <summary>Posts from the last few weeks with how they did, for planning, the notebook and the weekly update.</summary>
    public object[] RecentPosts(int days) => [.. Ledger().Publications.Where(item => item.Status == "published" && item.PublishedAt > Clock().AddDays(-days))
        .OrderByDescending(item => item.PublishedAt).Take(12).Select(item => (object)new { channel = item.Channel ?? (Kinds.TryGetValue(item.Kind, out var known) ? known.Name : item.Kind), published = item.PublishedAt!.Value.ToString("yyyy-MM-dd"), text = item.Excerpt,
            likes = item.Results?.Likes, reposts = item.Results?.Reposts, replies = item.Results?.Replies, visits = item.Results?.Visits })];

    const string GoogleToken = "https://oauth2.googleapis.com/token";

    /// <summary>An email draft is written as header lines (Subject:, optional To: and Cc:), a blank line, then the body.
    /// Without a Subject: line the first line is the subject.</summary>
    public static (string[] To, string[] Cc, string Subject, string Body) Email(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n').ToList();
        string? subject = null; var to = new List<string>(); var cc = new List<string>();
        while (lines.Count > 0 && Regex.Match(lines[0], @"^(subject|to|cc)\s*:\s*(.*)$", RegexOptions.IgnoreCase) is { Success: true } header)
        {
            var value = header.Groups[2].Value.Trim();
            switch (header.Groups[1].Value.ToLowerInvariant())
            {
                case "subject": subject = value; break;
                case "to": to.AddRange(Addresses(value)); break;
                default: cc.AddRange(Addresses(value)); break;
            }
            lines.RemoveAt(0);
        }
        if (subject == null)
        {
            while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
            subject = lines.Count > 0 ? lines[0].Trim().TrimStart('#').Trim() : "";
            if (lines.Count > 0) lines.RemoveAt(0);
        }
        if (subject.Length is 0 or > 200) throw new InvalidOperationException("An email needs a subject: start the draft with a line like “Subject: …”.");
        return ([.. to.Distinct()], [.. cc.Distinct()], subject, string.Join("\n", lines).Trim());
    }

    static IEnumerable<string> Addresses(string value)
    {
        foreach (var part in value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!System.Net.Mail.MailAddress.TryCreate(part, out var address) || address.Address.Contains('\n') || address.Address.Contains('\r'))
                throw new InvalidOperationException($"“{part}” isn't an email address.");
            yield return address.Address;
        }
    }

    /// <summary>A plain-text RFC 5322 message, UTF-8 throughout, as Gmail's base64url "raw".</summary>
    public static string Mime(string from, string[] to, string[] cc, string subject, string body)
    {
        static string Header(string text) => text.All(ch => ch is >= ' ' and <= '~') ? text : "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) + "?=";
        var message = new StringBuilder();
        message.Append("From: ").Append(from).Append("\r\n");
        if (to.Length > 0) message.Append("To: ").Append(string.Join(", ", to)).Append("\r\n");
        if (cc.Length > 0) message.Append("Cc: ").Append(string.Join(", ", cc)).Append("\r\n");
        message.Append("Subject: ").Append(Header(Regex.Replace(subject, @"[\r\n]+", " "))).Append("\r\n");
        message.Append("MIME-Version: 1.0\r\nContent-Type: text/plain; charset=UTF-8\r\nContent-Transfer-Encoding: base64\r\n\r\n");
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(body.Replace("\r\n", "\n").Replace("\n", "\r\n")));
        for (var index = 0; index < encoded.Length; index += 76) message.Append(encoded, index, Math.Min(76, encoded.Length - index)).Append("\r\n");
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(message.ToString()));
    }
}
