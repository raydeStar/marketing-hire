using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Thaddeus.Host;

public record VoiceImport(string Handle);
public record VoiceSave(string[] Posts, string? Started, string? Customer, string? Opinion);
public record VoiceSaved(string VoiceId, string? StoriesId, int Posts, int Stories);

/// <summary>Sounds like you: the owner's own past posts (pasted, or read from a public Bluesky or Mastodon profile) become the
/// Voice page's examples, and three answers (how it started, a customer moment, a strong opinion) become true stories on the
/// Stories page. The writer gets the two closest examples and the one closest story for each piece; the reviewer grades against them.</summary>
public sealed class VoiceStudio(CompanyWiki wiki)
{
    public const string VoiceTitle = "Voice: how we sound", StoriesTitle = "Stories: true stories to tell", PostsHeading = "## Posts that sound like us";
    public const int MostPosts = 20;
    public Func<HttpMessageHandler>? Handler { get; set; }
    const string Author = "Owner (voice setup)";

    HttpClient Client() => new(Handler?.Invoke() ?? new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };

    static string Text(string html) => WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, @"<br\s*/?>|</p>\s*<p>", "\n"), "<[^>]+>", "")).Trim();

    /// <summary>The profile's own recent posts, newest first: no replies, no reposts, nothing under 40 characters.
    /// "you.bsky.social" reads Bluesky's public AppView; "@you@mastodon.social" reads that server's public API.</summary>
    public async Task<string[]> Import(VoiceImport request, CancellationToken cancellation)
    {
        var handle = (request.Handle ?? "").Trim();
        using var http = Client();
        List<string> posts = [];
        if (Regex.Match(handle, @"^@?([A-Za-z0-9_.]{1,64})@([A-Za-z0-9.-]+\.[A-Za-z]{2,})$") is { Success: true } mastodon)
        {
            var server = "https://" + mastodon.Groups[2].Value.ToLowerInvariant();
            using var account = JsonDocument.Parse(await Get(http, $"{server}/api/v1/accounts/lookup?acct={Uri.EscapeDataString(mastodon.Groups[1].Value)}", cancellation));
            var id = account.RootElement.GetProperty("id").GetString()!;
            using var statuses = JsonDocument.Parse(await Get(http, $"{server}/api/v1/accounts/{Uri.EscapeDataString(id)}/statuses?exclude_replies=true&exclude_reblogs=true&limit=40", cancellation));
            posts.AddRange(statuses.RootElement.EnumerateArray().Select(status => Text(status.TryGetProperty("content", out var content) ? content.GetString() ?? "" : "")));
        }
        else if (Regex.Match(handle.TrimStart('@'), @"^([A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+)$") is { Success: true } bluesky)
        {
            using var feed = JsonDocument.Parse(await Get(http, $"https://public.api.bsky.app/xrpc/app.bsky.feed.getAuthorFeed?actor={Uri.EscapeDataString(bluesky.Value)}&limit=50&filter=posts_no_replies", cancellation));
            posts.AddRange(feed.RootElement.GetProperty("feed").EnumerateArray()
                .Where(item => !item.TryGetProperty("reason", out _))   // a repost is someone else's words
                .Select(item => item.GetProperty("post").GetProperty("record").TryGetProperty("text", out var text) ? text.GetString() ?? "" : ""));
        }
        else throw new ArgumentException("Enter a Bluesky handle (you.bsky.social) or a Mastodon address (@you@mastodon.social).");
        return [.. posts.Select(post => post.Trim()).Where(post => post.Length >= 40).Distinct().Take(MostPosts)];
    }

    static async Task<string> Get(HttpClient http, string url, CancellationToken cancellation)
    {
        using var response = await http.GetAsync(url, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new KeyNotFoundException("That profile wasn't found. Check the handle.");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"The profile couldn't be read ({(int)response.StatusCode}). Paste a few posts instead.");
        return await response.Content.ReadAsStringAsync(cancellation);
    }

    /// <summary>Posts become the Voice page's examples (replacing any from before; the rest of the page is kept); each answer becomes
    /// a section of the Stories page (replacing the same section from before; other stories are kept).</summary>
    public VoiceSaved Save(VoiceSave request)
    {
        var posts = (request.Posts ?? []).Select(post => Regex.Replace(post ?? "", @"\r\n?", "\n").Trim()).Where(post => post.Length >= 20).Distinct().Take(MostPosts).Select(post => post.Length > 1200 ? post[..1200] : post).ToArray();
        var stories = new (string Heading, string? Text)[] { ("## How we started", request.Started), ("## A customer moment", request.Customer), ("## Something we believe", request.Opinion) }
            .Where(item => (item.Text ?? "").Trim().Length >= 10).Select(item => (item.Heading, Text: item.Text!.Trim().Length > 1500 ? item.Text.Trim()[..1500] : item.Text.Trim())).ToArray();
        if (posts.Length == 0 && stories.Length == 0) throw new ArgumentException("Paste a few of your posts or answer one of the questions.");
        string? voiceId = null, storiesId = null;
        if (posts.Length > 0)
        {
            var section = PostsHeading + "\n\n" + string.Join("\n\n---\n\n", posts);
            voiceId = Upsert(VoiceTitle, "Voice", "policy", body => body == null ? "Your own posts, as examples of how we sound.\n\n" + section : ReplaceSection(body, PostsHeading, section));
        }
        if (stories.Length > 0)
            storiesId = Upsert(StoriesTitle, "Stories", "fact", body => stories.Aggregate(body ?? "True stories the employee can tell. Only these, never invented ones.", (text, story) => ReplaceSection(text, story.Heading, story.Heading + "\n\n" + story.Text)));
        return new(voiceId ?? "", storiesId, posts.Length, stories.Length);
    }

    string Upsert(string title, string starts, string kind, Func<string?, string> body)
    {
        var page = wiki.List().Where(item => item.Status == "active" && item.Title.StartsWith(starts, StringComparison.OrdinalIgnoreCase)).OrderByDescending(item => item.UpdatedAt).FirstOrDefault();
        var text = body(page?.Body);
        if (text.Length > 12_000) text = text[..12_000];
        return wiki.Save(new WikiChange(Guid.NewGuid().ToString("N"), page?.Id, page?.Version ?? 0, page?.Scope ?? "company", page?.ScopeId ?? "company", page?.Title ?? title, text, page?.Kind ?? kind, "active"), Author).Id;
    }

    /// <summary>The page with the "## Heading" section swapped for the new one, or the new one added at the end.</summary>
    public static string ReplaceSection(string body, string heading, string section)
    {
        var at = Regex.Match(body, "^" + Regex.Escape(heading) + @"[ \t]*$", RegexOptions.Multiline);
        if (!at.Success) return body.TrimEnd() + "\n\n" + section + "\n";
        var next = Regex.Match(body[(at.Index + at.Length)..], @"^## ", RegexOptions.Multiline);
        var end = next.Success ? at.Index + at.Length + next.Index : body.Length;
        return body[..at.Index] + section + "\n\n" + body[end..].TrimStart('\n');
    }

    static HashSet<string> Words(string text) => [.. Regex.Matches(text.ToLowerInvariant(), @"[a-z][a-z'-]{3,}").Select(match => match.Value)];

    /// <summary>What the writer gets for one piece: the voice guide without its examples, the two past posts closest to the piece,
    /// and the one story closest to it (the whole Stories page when it isn't split into stories).</summary>
    public static (string? Guide, string[] Posts, string? Story) Closest(string? voice, string? stories, string hint)
    {
        var wanted = Words(hint);
        int Score(string text) { var words = Words(text); return words.Count(wanted.Contains); }
        string? guide = null; string[] posts = [];
        if (voice != null)
        {
            var at = voice.IndexOf(PostsHeading, StringComparison.Ordinal);
            var section = "";
            if (at >= 0)
            {
                var rest = voice[(at + PostsHeading.Length)..];
                var next = Regex.Match(rest, @"^## ", RegexOptions.Multiline);
                section = next.Success ? rest[..next.Index] : rest;
            }
            guide = (at < 0 ? voice : voice[..at] + voice[(at + PostsHeading.Length + section.Length)..]).Trim();
            // Posts are kept apart by --- lines; a hand-written list may use blank lines instead.
            var examples = (section.Contains("\n---", StringComparison.Ordinal) ? Regex.Split(section, @"\n\s*---\s*\n") : Regex.Split(section, @"\n\s*\n"))
                .Select(post => post.Trim()).Where(post => post.Length >= 20).ToArray();
            posts = [.. examples.Select((post, index) => (post, index, score: Score(post))).OrderByDescending(item => item.score).ThenBy(item => item.index).Take(2).Select(item => item.post.Length > 700 ? item.post[..700] + "…" : item.post)];
            if (guide.Length == 0) guide = null; else if (guide.Length > 1200) guide = guide[..1200] + "…";
        }
        string? story = null;
        if (stories != null)
        {
            var sections = Regex.Split(stories, @"(?=^## )", RegexOptions.Multiline).Select(item => item.Trim()).Where(item => item.StartsWith("## ", StringComparison.Ordinal)).ToArray();
            story = sections.Length >= 2 ? sections.Select((item, index) => (item, index, score: Score(item))).OrderByDescending(item => item.score).ThenBy(item => item.index).First().item : stories;
            if (story.Length > 1500) story = story[..1500] + "…";
        }
        return (guide, posts, story);
    }
}
