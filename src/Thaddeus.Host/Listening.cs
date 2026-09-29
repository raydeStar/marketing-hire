using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record Mention(string Id, string Topic, string Source, string Title, string Snippet, string Url, DateTimeOffset PublishedAt, DateTimeOffset SeenAt, string Sentiment);
public record ListeningLedger(Mention[] Mentions, Dictionary<string, DateTimeOffset> Tracked, DateTimeOffset? LastScanAt, string[] Errors);
public record ListeningScan(int Topics, int Feeds, int New, string[] Errors, DateTimeOffset At);
public record FeedItem(string Title, string Url, string Summary, DateTimeOffset? PublishedAt);

/// <summary>Continuous listening: public mentions of the owner's watch topics (Hacker News, Google News, Bluesky, and
/// Reddit when credentials are set) and new posts on the feeds they follow. It runs in code between and during shifts,
/// keeps 30 days of history, and turns only material changes (a spike, a negative turn) into signals for a shift.</summary>
public sealed class MarketListening(Store store, MarketingBackend marketing, CompanyObjectives objectives, PageWatch watch)
{
    private const string Key = "listening-v1";
    public const string Sources = "hackernews,reddit,news,bluesky";
    private readonly SemaphoreSlim scanGate = new(1, 1);
    public Func<string, CancellationToken, Task<ResearchSource[]?>> Pulse { get; set; } = (topic, cancellation) => marketing.PulseResearch(topic, cancellation, Sources);
    public Func<string, CancellationToken, Task<string>> FetchFeed { get; set; } = SiteReader.FetchFeed;
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    ListeningLedger Read() => store.Setting(Key) is { } json ? Wire.Unpack<ListeningLedger>(json) : new([], [], null, []);
    public ListeningLedger Ledger() { lock (store) return Read(); }
    (string[] Topics, string[] Feeds) Config() { var content = objectives.Current().Content; return (content.WatchTopics ?? [], content.Feeds ?? []); }
    static string Id(string topic, string url) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(topic.ToLowerInvariant() + "\n" + url)))[..20].ToLowerInvariant();
    public static string FeedTopic(string feed) => "feed:" + (Uri.TryCreate(feed, UriKind.Absolute, out var uri) ? uri.Host : feed);

    /// <summary>One pass over every watch topic and feed. Safe to call often; a pass already running is not repeated.</summary>
    /// <summary>What the page watch has read on competitors' pages, and the changes it saw.</summary>
    public PageWatchLedger Watched() => watch.Ledger();

    public async Task<ListeningScan> Scan(CancellationToken cancellation)
    {
        var (topics, feeds) = Config();
        var now = Clock();
        if (!await scanGate.WaitAsync(0, cancellation)) return new(0, 0, 0, ["A listening pass is already running."], now);
        try
        {
            // Watched competitor pages: each read at most once a day.
            try { await watch.Check(cancellation); }
            catch (Exception error) when (error is IOException or InvalidOperationException) { }
            if (topics.Length == 0 && feeds.Length == 0) return new(0, 0, 0, [], now);
            var found = new List<Mention>(); var errors = new List<string>();
            foreach (var topic in topics.Where(topic => !Generic(topic)))
            {
                ResearchSource[]? items = null;
                try { items = await Pulse(topic, cancellation); }
                catch (Exception error) when (error is IOException or HttpRequestException or InvalidOperationException) { }
                if (items == null) { if (!errors.Any(item => item.StartsWith("Community search", StringComparison.Ordinal))) errors.Add("Community search is unavailable: the employee's container isn't reachable."); continue; }
                found.AddRange(items.Select(item => new Mention(Id(topic, item.Url), topic, item.Via, item.Title, item.Excerpt, item.Url, item.PublishedAt, now, SentimentLexicon.Of(item.Title + " " + item.Excerpt))));
            }
            foreach (var feed in feeds)
            {
                var topic = FeedTopic(feed);
                try
                {
                    foreach (var item in ParseFeed(await FetchFeed(feed, cancellation)).Take(25))
                        found.Add(new Mention(Id(topic, item.Url), topic, topic[5..], item.Title, item.Summary, item.Url, item.PublishedAt ?? now, now, SentimentLexicon.Of(item.Title + " " + item.Summary)));
                }
                catch (Exception error) when (error is IOException or HttpRequestException or InvalidOperationException or XmlException or OperationCanceledException && !cancellation.IsCancellationRequested)
                { errors.Add($"{topic[5..]}: {error.Message}"); }
            }
            lock (store)
            {
                var ledger = Read();
                var known = ledger.Mentions.Select(item => item.Id).ToHashSet();
                var cutoff = now.AddDays(-30);
                bool Kept(Mention item) => item.PublishedAt >= cutoff && item.PublishedAt <= now.AddHours(1) && Relevant(item);
                // Only what is kept counts as new: an off-topic or stale post found again next pass isn't news twice.
                var added = found.Where(item => known.Add(item.Id) && Kept(item)).ToArray();
                var tracked = new Dictionary<string, DateTimeOffset>(ledger.Tracked);
                foreach (var topic in topics.Concat(feeds.Select(FeedTopic))) tracked.TryAdd(topic.ToLowerInvariant(), now);
                var mentions = ledger.Mentions.Concat(added).Where(Kept)
                    .OrderBy(item => item.PublishedAt).TakeLast(4000).ToArray();
                store.Setting(Key, Wire.Pack(new ListeningLedger(mentions, tracked, now, [.. errors])));
                return new(topics.Length, feeds.Length, added.Length, [.. errors], now);
            }
        }
        finally { scanGate.Release(); }
    }

    /// <summary>Spikes and negative turns on watch topics, against the prior week. A topic needs history first:
    /// tracked for three days, or mentions on three of the prior seven, so a first backfill never reads as a spike.</summary>
    public List<ShiftSignal> Signals()
    {
        var now = Clock(); var ledger = Ledger(); var signals = new List<ShiftSignal>();
        foreach (var topic in Config().Topics.Where(topic => !Generic(topic)))
        {
            var (last, prior, priorDays) = Window(ledger, topic, now);
            var tracked = ledger.Tracked.TryGetValue(topic.ToLowerInvariant(), out var since) && since <= now.AddDays(-3);
            if (!tracked && priorDays < 3 || last.Length < 5) continue;
            var perDay = prior.Length / 7.0;
            var day = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var bySource = string.Join(", ", last.GroupBy(item => item.Source).Select(group => $"{group.Count()} {group.Key}"));
            if (last.Length >= 3 * Math.Max(perDay, 0.5))
                signals.Add(new ShiftSignal("mention_spike", last.Length >= 5 * Math.Max(perDay, 0.5) ? "high" : "medium", $"Mentions of “{topic}” up: {last.Length} in 24 hours",
                    $"{last.Length} public mentions of “{topic}” in the last 24 hours ({bySource}), against about {perDay.ToString("0.#", CultureInfo.InvariantCulture)} a day over the prior week.",
                    $"listen:spike:{topic}:{day}", topic));
            var negative = last.Count(item => item.Sentiment == "negative") / (double)last.Length;
            var baseline = prior.Length >= 5 ? prior.Count(item => item.Sentiment == "negative") / (double)prior.Length : 0.15;
            if (negative >= 0.4 && negative >= baseline + 0.2)
                signals.Add(new ShiftSignal("sentiment_drop", "high", $"Negative turn on “{topic}”",
                    $"{Math.Round(negative * 100)}% of the last day's {last.Length} mentions of “{topic}” read as negative, against {Math.Round(baseline * 100)}% the prior week. Word-list sentiment: read the mentions before concluding.",
                    $"listen:negative:{topic}:{day}", topic));
        }
        // A question someone asked in public on a watch topic: worth a reply for the owner to post under it. Two a day at most.
        // Only one that names the topic itself, on a topic specific enough to be about the owner's market (not "General").
        foreach (var asked in ledger.Mentions.Where(item => item.PublishedAt > now.AddHours(-24) && Asks(item) && Relevant(item) && !Generic(item.Topic)).OrderByDescending(item => item.PublishedAt).Take(2))
            signals.Add(new ShiftSignal("public_question", "medium", $"A question on {asked.Source} about “{asked.Topic}”",
                $"Someone asked on {asked.Source}: “{Flat(asked.Title.Length > 0 && asked.Snippet.Length > 0 && !asked.Snippet.StartsWith(asked.Title, StringComparison.Ordinal) ? asked.Title + " — " + asked.Snippet : asked.Snippet.Length > 0 ? asked.Snippet : asked.Title, 320)}”. Answer it with a reply draft: channel {asked.Source}, destination {asked.Url}.",
                $"listen:question:{asked.Id}", asked.Url));
        signals.AddRange(watch.Signals());
        return signals;
    }

    static string Flat(string text, int length) { var flat = Regex.Replace(text, @"\s+", " ").Trim(); return flat.Length > length ? flat[..(length - 1)].TrimEnd() + "…" : flat; }

    /// <summary>A post that asks something: on Bluesky, Reddit or Hacker News (not the news or a followed feed), with a question
    /// mark and a question's words ("how", "anyone", "recommend"…).</summary>
    public static bool Asks(Mention item) => item.Source is "Bluesky" or "Reddit" or "Hacker News"
        && (item.Title + " " + item.Snippet).Contains('?')
        && Regex.IsMatch(item.Title + " " + item.Snippet, @"\b(how|what|which|who|why|anyone|is there|are there|recommend|should i|should we|best way|does anyone|do you|can i|worth it)\b", RegexOptions.IgnoreCase);

    /// <summary>A mention as a source the reply can cite: the post itself.</summary>
    public ResearchSource? MentionSource(string url) =>
        Ledger().Mentions.LastOrDefault(item => item.Url == url) is { } found ? new ResearchSource(found.Url, found.Title.Length > 0 ? found.Title : Flat(found.Snippet, 120), found.Snippet, null, found.PublishedAt, found.Source) : null;

    /// <summary>A watched page as a citable source: what changed and what it says now.</summary>
    public ResearchSource? PageSource(string url) => watch.SourceFor(url);

    static (Mention[] Last, Mention[] Prior, int PriorDays) Window(ListeningLedger ledger, string topic, DateTimeOffset now)
    {
        var mine = ledger.Mentions.Where(item => string.Equals(item.Topic, topic, StringComparison.OrdinalIgnoreCase)).ToArray();
        var last = mine.Where(item => item.PublishedAt > now.AddHours(-24)).ToArray();
        var prior = mine.Where(item => item.PublishedAt <= now.AddHours(-24) && item.PublishedAt > now.AddDays(-8)).ToArray();
        return (last, prior, prior.Select(item => item.PublishedAt.UtcDateTime.Date).Distinct().Count());
    }

    /// <summary>A compact summary for planning: each topic's last day against its week, and new feed posts.</summary>
    /// <summary>Social search (Bluesky, Reddit) matches a topic's words anywhere in a post, so "AI employee" finds a story about an
    /// employee next to one about AI. A post counts only when it names the topic as a phrase; news and followed feeds are kept as they come.</summary>
    /// Hacker News search matches words in the comments too: a question there counts only when its own text names the topic.
    public static bool Relevant(Mention mention) =>
        mention.Topic.StartsWith("feed:", StringComparison.Ordinal) || mention.Source is not ("Bluesky" or "Reddit" or "Hacker News") || OnTopic(mention.Topic, mention.Title + " " + mention.Snippet);

    /// <summary>Too broad to listen for: one common word ("General", "Marketing", "AI"), or a placeholder a brief left in
    /// ("specific competitor not identified"). Listening for these fills the cockpit with posts about anything.</summary>
    public static bool Generic(string topic)
    {
        var text = topic.Trim().ToLowerInvariant();
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"not identified|unidentified|unknown|various|n/a|\bnone\b|\btbd\b|placeholder|\bexample\b")) return true;
        var words = System.Text.RegularExpressions.Regex.Matches(text, @"[\p{L}\p{N}]+").Select(match => match.Value).ToArray();
        return words.Length == 0 || words.Length == 1 && (words[0].Length < 3 || GenericWords.Contains(words[0]));
    }
    static readonly HashSet<string> GenericWords = ["general", "marketing", "ai", "social", "tools", "software", "business", "news", "tech", "technology", "startup", "startups",
        "saas", "apps", "app", "online", "digital", "content", "media", "services", "service", "products", "product", "sales", "growth", "manual", "other", "others", "misc"];

    /// <summary>The topic's words in order, next to each other (a hyphen or plural is fine).</summary>
    public static bool OnTopic(string topic, string text)
    {
        var words = System.Text.RegularExpressions.Regex.Matches(topic.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(match => System.Text.RegularExpressions.Regex.Escape(match.Value)).ToArray();
        if (words.Length == 0) return true;
        var pattern = @"(?<![\p{L}\p{N}])" + string.Join(@"(?:s|es)?[\s\-_./]+", words) + @"(?:s|es)?(?![\p{L}\p{N}])";
        return System.Text.RegularExpressions.Regex.IsMatch(text.ToLowerInvariant(), pattern);
    }

    public object Digest()
    {
        var now = Clock(); var ledger = Ledger(); var (topics, feeds) = Config();
        return new
        {
            topics = topics.Select(topic => { var (last, prior, _) = Window(ledger, topic, now); return new { topic, last24h = last.Length, perDayPriorWeek = Math.Round(prior.Length / 7.0, 1),
                negativeShare = last.Length == 0 ? 0 : Math.Round(last.Count(item => item.Sentiment == "negative") / (double)last.Length, 2) }; }),
            newFeedPosts = ledger.Mentions.Where(item => item.Topic.StartsWith("feed:", StringComparison.Ordinal) && item.PublishedAt > now.AddHours(-48))
                .OrderByDescending(item => item.PublishedAt).Take(8).Select(item => new { feed = item.Source, title = item.Title, url = item.Url, published = item.PublishedAt.ToString("yyyy-MM-dd") })
        };
    }

    /// <summary>The latest mentions of a topic, as sources a deliverable can cite.</summary>
    public ResearchSource[] SourcesFor(string topic, int count) => [.. Ledger().Mentions.Where(item => string.Equals(item.Topic, topic, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(item => item.PublishedAt).Take(count).Select(item => new ResearchSource(item.Url, item.Title, item.Snippet, null, item.PublishedAt, item.Source))];

    public object View()
    {
        var now = Clock(); var ledger = Ledger(); var (topics, feeds) = Config();
        int[] Daily(IEnumerable<Mention> mentions) { var list = mentions.ToArray(); return [.. Enumerable.Range(0, 14).Reverse().Select(back => list.Count(item => item.PublishedAt <= now.AddDays(-back) && item.PublishedAt > now.AddDays(-back - 1)))]; }
        var signals = Signals();
        return new
        {
            topics, feeds, lastScanAt = ledger.LastScanAt, errors = ledger.Errors,
            stats = topics.Select(topic => { var (last, prior, _) = Window(ledger, topic, now); var mine = ledger.Mentions.Where(item => string.Equals(item.Topic, topic, StringComparison.OrdinalIgnoreCase));
                return new { topic, last24h = last.Length, perDayPriorWeek = Math.Round(prior.Length / 7.0, 1),
                    negativeShare = last.Length == 0 ? 0 : Math.Round(last.Count(item => item.Sentiment == "negative") / (double)last.Length, 2),
                    daily = Daily(mine), flag = signals.FirstOrDefault(signal => string.Equals(signal.MetricName, topic, StringComparison.OrdinalIgnoreCase))?.Kind }; }),
            mentions = ledger.Mentions.OrderByDescending(item => item.PublishedAt).Take(40),
            watch = watch.View()
        };
    }

    /// <summary>RSS 2.0 or Atom, read without DTDs or external entities.</summary>
    public static FeedItem[] ParseFeed(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4_000_000 });
        var document = XDocument.Load(reader);
        static string Text(XElement? element) => element == null ? "" : Regex.Replace(System.Net.WebUtility.HtmlDecode(Regex.Replace(element.Value, @"(?s)<[^>]+>", " ")), @"\s+", " ").Trim();
        static DateTimeOffset? When(XElement? element) => element != null && DateTimeOffset.TryParse(element.Value.Replace("GMT", "+00:00").Replace("UTC", "+00:00"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;
        static string Clip(string text, int limit) => text.Length > limit ? text[..limit] : text;
        var items = new List<FeedItem>();
        foreach (var item in document.Descendants().Where(element => element.Name.LocalName is "item" or "entry"))
        {
            XElement? Child(string name) => item.Elements().FirstOrDefault(element => element.Name.LocalName == name);
            var link = Child("link") is { } found ? (found.Attribute("href")?.Value ?? found.Value).Trim() : "";
            if (!Uri.TryCreate(link, UriKind.Absolute, out var url) || url.Scheme is not ("https" or "http")) continue;
            var title = Clip(Text(Child("title")), 200);
            if (title.Length == 0) continue;
            items.Add(new FeedItem(title, url.AbsoluteUri, Clip(Text(Child("description") ?? Child("summary") ?? Child("content")), 500), When(Child("pubDate") ?? Child("published") ?? Child("updated"))));
        }
        return [.. items];
    }
}

/// <summary>A small word-list sentiment for public mentions: cheap and explainable, not a judgment. Shifts treat it as a
/// reason to read the mentions, never as the conclusion.</summary>
public static class SentimentLexicon
{
    static readonly HashSet<string> Positive = [.. "good nice love loved loving great excellent amazing awesome fantastic helpful useful recommend recommended best better easy fast reliable impressive happy glad thanks thank brilliant solid favorite wins win winning works worked smooth delightful clear powerful elegant".Split(' ')];
    static readonly HashSet<string> Negative = [.. "hate hated terrible awful bad worse worst broken buggy bug bugs slow scam spam fraud lawsuit outage down fail failed failing failure disappointing disappointed useless waste expensive overpriced refund complaint complaints angry annoying confusing crash crashed leak leaked breach hacked problem problems issue issues risk risky concern concerns layoffs lawsuit misleading".Split(' ')];
    static readonly HashSet<string> Negations = ["not", "no", "never", "isn't", "wasn't", "don't", "doesn't", "didn't", "can't", "won't", "hardly"];

    public static string Of(string text)
    {
        var words = Regex.Matches(text.ToLowerInvariant(), @"[a-z']+").Select(match => match.Value).ToArray();
        var score = 0;
        for (var index = 0; index < words.Length; index++)
        {
            var value = Positive.Contains(words[index]) ? 1 : Negative.Contains(words[index]) ? -1 : 0;
            if (value != 0 && index > 0 && Negations.Contains(words[index - 1])) value = -value;
            score += value;
        }
        return score > 0 ? "positive" : score < 0 ? "negative" : "neutral";
    }
}
