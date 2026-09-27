using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Thaddeus.Host;

/// <summary>Meta's networks, through the owner's own free Meta developer app: a Facebook Page and its linked Instagram professional
/// account (Graph API, with a Page token), and Threads (Threads API). Standard access covers accounts the owner manages, with no
/// App Review. Tokens come from Meta's own token tools; with the app's secret they are exchanged for lasting ones.</summary>
public sealed partial class Publishing
{
    public const string ThreadsGraph = "https://graph.threads.net/v1.0";
    static string Graph => DataConnections.MetaGraph;
    /// <summary>The pause between checks while Meta fetches an Instagram photo or prepares a Threads post; tests make it instant.</summary>
    public Func<TimeSpan, CancellationToken, Task> Pause { get; set; } = Task.Delay;

    async Task<JsonDocument> Meta(HttpClient http, HttpMethod method, string url, string token, Dictionary<string, string>? form, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(method, url) { Content = form == null ? null : new FormUrlEncodedContent(form) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Read(http, request, cancellation);
    }

    async Task<PublishingConnection> ConnectMeta(string kind, PublishingConnect request, CancellationToken cancellation)
    {
        var token = (request.Secret ?? "").Trim();
        if (token.Length is < 20 or > 2048 || token.Any(char.IsWhiteSpace)) throw new ArgumentException("Paste the access token as Meta showed it.");
        var appId = (request.AppId ?? "").Trim(); var appSecret = (request.AppSecret ?? "").Trim();
        if (appSecret.Length > 0 && (appSecret.Length is < 16 or > 128 || appSecret.Any(char.IsWhiteSpace))) throw new ArgumentException("Paste the app secret from the app's settings.");
        using var http = Client();
        string account, subject, address; DateTimeOffset? expires = null; string? image = null;
        if (kind == "threads")
        {
            if (appSecret.Length > 0)
                // A short-lived token from the app dashboard becomes one that lasts 60 days (and renews itself); an already lasting one is kept as it is.
                try
                {
                    using var longer = await Meta(http, HttpMethod.Get, $"https://graph.threads.net/access_token?grant_type=th_exchange_token&client_secret={Uri.EscapeDataString(appSecret)}&access_token={Uri.EscapeDataString(token)}", token, null, cancellation);
                    token = longer.RootElement.GetProperty("access_token").GetString()!;
                    expires = DateTimeOffset.UtcNow.AddSeconds(longer.RootElement.TryGetProperty("expires_in", out var seconds) && seconds.TryGetInt32(out var value) ? value : 5_184_000);
                }
                catch (InvalidOperationException) { }
            // A token that is already lasting (the dashboard's generator makes those) isn't exchanged again; it lasts 60 days and renews itself from here.
            expires ??= DateTimeOffset.UtcNow.AddDays(60);
            using var me = await Meta(http, HttpMethod.Get, ThreadsGraph + "/me?fields=id,username", token, null, cancellation);
            subject = me.RootElement.GetProperty("id").GetString()!;
            var username = me.RootElement.GetProperty("username").GetString()!;
            (account, address) = ("@" + username, "https://www.threads.net/@" + username);
        }
        else
        {
            if (appId.Length > 0 && appSecret.Length > 0)
            {
                if (appId.Length > 30 || !appId.All(char.IsDigit)) throw new ArgumentException("The app ID is the number at the top of the app's dashboard.");
                // A Graph API Explorer token lasts about an hour; exchanged, the Page tokens it gives don't expire.
                try
                {
                    using var longer = await Meta(http, HttpMethod.Get, $"{Graph}/oauth/access_token?grant_type=fb_exchange_token&client_id={appId}&client_secret={Uri.EscapeDataString(appSecret)}&fb_exchange_token={Uri.EscapeDataString(token)}", token, null, cancellation);
                    token = longer.RootElement.GetProperty("access_token").GetString()!;
                }
                catch (InvalidOperationException refused) { throw new InvalidOperationException("Meta didn't exchange the token with that app ID and secret (" + refused.Message + "). Check both in App settings → Basic, or leave them empty for a system user's token."); }
            }
            using var pages = await Meta(http, HttpMethod.Get, Graph + "/me/accounts?fields=id,name,access_token,tasks,instagram_business_account%7Bid,username%7D&limit=100", token, null, cancellation);
            var list = pages.RootElement.TryGetProperty("data", out var data) ? data.EnumerateArray().Where(page => page.TryGetProperty("access_token", out _)).ToArray() : [];
            if (list.Length == 0) throw new InvalidOperationException("That token manages no Facebook Pages. Create it with pages_show_list, as an admin of the Page.");
            var wanted = (request.Account ?? "").Trim();
            var matches = wanted.Length == 0 ? list : [.. list.Where(page => Str(page, "id") == wanted || string.Equals(Str(page, "name"), wanted, StringComparison.OrdinalIgnoreCase))];
            if (matches.Length != 1)
                throw new ArgumentException((wanted.Length == 0 ? "This token manages several Pages" : $"No Page called “{wanted}”") + ": enter one of " + string.Join(", ", list.Select(page => $"“{Str(page, "name")}”")) + ".");
            var page = matches[0];
            token = Str(page, "access_token");
            if (kind == "facebook")
            {
                if (page.TryGetProperty("tasks", out var tasks) && tasks.ValueKind == JsonValueKind.Array && !tasks.EnumerateArray().Any(task => task.GetString() == "CREATE_CONTENT"))
                    throw new InvalidOperationException($"Your role on {Str(page, "name")} can't post. Ask for full control of the Page, or connect a Page you manage.");
                (subject, account, address) = (Str(page, "id"), Str(page, "name"), "https://www.facebook.com/" + Str(page, "id"));
            }
            else
            {
                if (!page.TryGetProperty("instagram_business_account", out var instagram) || instagram.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException($"{Str(page, "name")} has no Instagram professional account linked. In Instagram, switch to a professional account and link it to the Page, then connect again.");
                var username = Str(instagram, "username");
                (subject, account, address) = (Str(instagram, "id"), "@" + username, "https://www.instagram.com/" + username);
                if ((request.Image ?? "").Trim() is { Length: > 0 } photo) image = Jpeg(photo) ?? throw new ArgumentException("The default photo must be a public https link to a .jpg file (Instagram takes JPEG only).");
            }
        }
        var connection = Add(new PublishingConnection(Guid.NewGuid().ToString("N"), kind, "ready", account, address, DateTimeOffset.UtcNow, expires, null, false, image));
        await SaveSecret(connection.Id, new Secret(token, null, appId.Length > 0 ? appId : null, appSecret.Length > 0 ? appSecret : null, subject, expires), cancellation);
        return connection;
    }

    /// <summary>A public https link to a JPEG, or null.</summary>
    static string? Jpeg(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 && Regex.IsMatch(uri.AbsolutePath, @"\.jpe?g$", RegexOptions.IgnoreCase) ? uri.AbsoluteUri : null;

    /// <summary>The photo a draft names on its image line ("[Image: https://…/photo.jpg]"), which is left out of the caption.</summary>
    public static string? DraftPhoto(string content) =>
        Regex.Matches(content, @"^\s*\[(?:image|visual|graphic)\s*:[^\]\n]*?(https://[^\s\]]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline).Select(match => Jpeg(match.Groups[1].Value)).FirstOrDefault(link => link != null);

    async Task<(string Url, string? Remote)> PostMeta(PublishingConnection connection, Secret secret, HttpClient http, Publication item, string content, CancellationToken cancellation)
    {
        switch (connection.Kind)
        {
            case "facebook":
            {
                using var made = await Meta(http, HttpMethod.Post, $"{Graph}/{secret.Subject}/feed", secret.Token, new() { ["message"] = content }, cancellation);
                var id = made.RootElement.GetProperty("id").GetString()!;
                return (await Permalink(http, $"{Graph}/{id}?fields=permalink_url", "permalink_url", secret.Token, cancellation) ?? "https://www.facebook.com/" + id, id);
            }
            case "instagram":
            {
                var photo = (await DraftContent(item, cancellation) is { } raw ? DraftPhoto(raw) : null) ?? connection.Image
                    ?? throw new Refused("An Instagram post needs a photo: add a line “[Image: https://…/photo.jpg]” to the draft, or connect Instagram again with a default photo.");
                using var container = await Meta(http, HttpMethod.Post, $"{Graph}/{secret.Subject}/media", secret.Token, new() { ["image_url"] = photo, ["caption"] = content }, cancellation);
                var creation = container.RootElement.GetProperty("id").GetString()!;
                await Ready(http, $"{Graph}/{creation}?fields=status_code", "status_code", secret.Token, "Instagram couldn't use the photo; it must be a public JPEG link", cancellation);
                using var published = await Meta(http, HttpMethod.Post, $"{Graph}/{secret.Subject}/media_publish", secret.Token, new() { ["creation_id"] = creation }, cancellation);
                var id = published.RootElement.GetProperty("id").GetString()!;
                return (await Permalink(http, $"{Graph}/{id}?fields=permalink", "permalink", secret.Token, cancellation) ?? connection.Address!, id);
            }
            default:
            {
                var token = await ThreadsToken(connection, secret, http, cancellation);
                using var container = await Meta(http, HttpMethod.Post, $"{ThreadsGraph}/{secret.Subject}/threads", token, new() { ["media_type"] = "TEXT", ["text"] = content }, cancellation);
                var creation = container.RootElement.GetProperty("id").GetString()!;
                await Ready(http, $"{ThreadsGraph}/{creation}?fields=status", "status", token, "Threads couldn't prepare the post", cancellation);
                using var published = await Meta(http, HttpMethod.Post, $"{ThreadsGraph}/{secret.Subject}/threads_publish", token, new() { ["creation_id"] = creation }, cancellation);
                var id = published.RootElement.GetProperty("id").GetString()!;
                return (await Permalink(http, $"{ThreadsGraph}/{id}?fields=permalink", "permalink", token, cancellation) ?? connection.Address!, id);
            }
        }
    }

    /// <summary>Waits (up to about half a minute) for Meta to finish preparing a post before it is published. Nothing is public yet,
    /// so a failure here is a plain refusal.</summary>
    async Task Ready(HttpClient http, string url, string field, string token, string failure, CancellationToken cancellation)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var status = await Meta(http, HttpMethod.Get, url, token, null, cancellation);
            var code = Str(status.RootElement, field);
            if (code is "FINISHED" or "") return;
            if (code is "ERROR" or "EXPIRED" || attempt >= 10) throw new Refused($"{failure} ({(code == "IN_PROGRESS" ? "still preparing after 30 seconds" : code.ToLowerInvariant())}). Nothing was posted.");
            await Pause(TimeSpan.FromSeconds(3), cancellation);
        }
    }

    /// <summary>The post's public link; after the post is live, a missing link is never a failure.</summary>
    async Task<string?> Permalink(HttpClient http, string url, string field, string token, CancellationToken cancellation)
    {
        try { using var found = await Meta(http, HttpMethod.Get, url, token, null, cancellation); return Str(found.RootElement, field) is { Length: > 0 } link && link.StartsWith("https://", StringComparison.Ordinal) ? link : null; }
        catch (Exception error) when (error is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException) { return null; }
    }

    /// <summary>Threads access lasts 60 days and renews for another 60 once it is a day old: renewed when fewer than 50 days are left.</summary>
    async Task<string> ThreadsToken(PublishingConnection connection, Secret secret, HttpClient http, CancellationToken cancellation)
    {
        if (secret.ExpiresAt is not { } expires) return secret.Token;
        if (expires < DateTimeOffset.UtcNow) throw new Refused("Threads access has expired. Disconnect and connect Threads again.");
        if (expires - DateTimeOffset.UtcNow > TimeSpan.FromDays(50)) return secret.Token;
        try
        {
            using var renewed = await Meta(http, HttpMethod.Get, $"https://graph.threads.net/refresh_access_token?grant_type=th_refresh_token&access_token={Uri.EscapeDataString(secret.Token)}", secret.Token, null, cancellation);
            var token = renewed.RootElement.GetProperty("access_token").GetString()!;
            var until = DateTimeOffset.UtcNow.AddSeconds(renewed.RootElement.TryGetProperty("expires_in", out var seconds) && seconds.TryGetInt32(out var value) ? value : 5_184_000);
            await SaveSecret(connection.Id, secret with { Token = token, ExpiresAt = until }, cancellation);
            Change(ledger => (ledger with { Connections = [.. ledger.Connections.Select(entry => entry.Id == connection.Id ? entry with { ExpiresAt = until } : entry)] }, 0));
            return token;
        }
        catch (Exception error) when (error is InvalidOperationException or JsonException or KeyNotFoundException) { return secret.Token; }
    }

    async Task<PostResults> MetaCounts(Publication item, PublishingConnection connection, HttpClient http, CancellationToken cancellation)
    {
        var secret = await ReadSecret(connection.Id, cancellation);
        var now = Clock();
        static int? Count(JsonElement element, params string[] path)
        {
            foreach (var name in path) { if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element)) return null; }
            return element.TryGetInt32(out var number) ? number : null;
        }
        switch (item.Kind)
        {
            case "facebook":
            {
                using var post = await Meta(http, HttpMethod.Get, $"{Graph}/{item.RemoteId}?fields=shares,reactions.summary(total_count).limit(0),comments.summary(total_count).limit(0)", secret.Token, null, cancellation);
                var root = post.RootElement;
                return new(Count(root, "reactions", "summary", "total_count"), Count(root, "shares", "count") ?? 0, Count(root, "comments", "summary", "total_count"), null, null, null, now, null);
            }
            case "instagram":
            {
                using var media = await Meta(http, HttpMethod.Get, $"{Graph}/{item.RemoteId}?fields=like_count,comments_count", secret.Token, null, cancellation);
                return new(Count(media.RootElement, "like_count"), null, Count(media.RootElement, "comments_count"), null, null, null, now, null);
            }
            default:
            {
                var token = await ThreadsToken(connection, secret, http, cancellation);
                using var insights = await Meta(http, HttpMethod.Get, $"{ThreadsGraph}/{item.RemoteId}/insights?metric=views,likes,replies,reposts,quotes", token, null, cancellation);
                var values = new Dictionary<string, int>();
                if (insights.RootElement.TryGetProperty("data", out var data))
                    foreach (var metric in data.EnumerateArray())
                        if ((metric.TryGetProperty("values", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0 ? Count(list[0], "value") : Count(metric, "total_value", "value")) is { } number)
                            values[Str(metric, "name")] = number;
                int? Value(string name) => values.TryGetValue(name, out var number) ? number : null;
                return new(Value("likes"), Value("reposts"), Value("replies"), Value("quotes"), Value("views"), null, now, null);
            }
        }
    }
}
