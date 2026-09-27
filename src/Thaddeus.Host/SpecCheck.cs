using System.Globalization;
using System.Text.RegularExpressions;

namespace Thaddeus.Host;

public record SpecResult(string Requirement, bool Met, string Detail);

/// <summary>What the assignment asked for that can be measured, measured on the work: how many questions, posts, ideas or
/// emails; a word limit or range; a Subject line; citations. The self-review gets what's unmet as must-fix, and the result is
/// part of the review's summary, so "eight questions" can't quietly become eleven.</summary>
public static partial class SpecCheck
{
    static readonly Dictionary<string, int> Numbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12
    };
    static int? Count(string word) => int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : Numbers.TryGetValue(word, out var named) ? named : null;

    [GeneratedRegex(@"\b(\d{1,2}|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\s+(?:[\w-]+\s+){0,2}?(questions|posts|emails|ideas|tips|steps|headlines|subject lines|reasons|objections|drafts)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Counted();
    [GeneratedRegex(@"\b(?:under|at most|no more than|up to|maximum of)\s+(\d{1,2},\d{3}|\d{2,5})\s+words\b", RegexOptions.IgnoreCase)]
    private static partial Regex WordLimit();
    [GeneratedRegex(@"\b(\d{1,2},\d{3}|\d{2,5})\s*(?:–|-|to)\s*(\d{1,2},\d{3}|\d{2,5})\s+words\b", RegexOptions.IgnoreCase)]
    private static partial Regex WordRange();

    static int Whole(string text) => int.Parse(text.Replace(",", ""), CultureInfo.InvariantCulture);
    public static int Words(string text) => Regex.Matches(Regex.Replace(text, @"\[(\d{1,2})\]|[#*_`>|]", " "), @"[\p{L}\p{N}][\p{L}\p{N}'’-]*").Count;

    /// <summary>How many of a thing the work holds: questions as headings or lines ending in "?", posts or emails as series parts,
    /// ideas, tips and steps as list items or numbered headings.</summary>
    static int Found(string thing, string body, int parts)
    {
        var lines = body.Replace("\r", "").Split('\n').Select(line => line.Trim()).ToArray();
        switch (thing.ToLowerInvariant())
        {
            case "questions":
            case "objections":
                var headed = lines.Count(line => line.StartsWith('#') && line.TrimEnd().EndsWith('?'));
                // Questions as headings; else bold questions ("**Is it safe?** Yes.") or list lines that end in "?".
                return headed > 0 ? headed : lines.Count(line => Regex.IsMatch(line, @"^([-*]\s+|\d+[.)]\s+)?\*\*[^*]+\?\*\*") || Regex.IsMatch(line, @"^([-*]|\d+[.)])\s+.*\?$"));
            case "posts":
            case "emails":
            case "drafts":
                return parts;
            default:
                var headings = lines.Count(line => Regex.IsMatch(line, @"^#{2,4}\s+\S"));
                var items = lines.Count(line => Regex.IsMatch(line, @"^([-*]|\d+[.)])\s+\S"));
                return Math.Max(items, headings);
        }
    }

    /// <summary>Every measurable requirement in the assignment, checked on the work. <paramref name="parts"/> is how many posts or
    /// emails a series holds (1 for a single piece).</summary>
    public static SpecResult[] Check(string assignment, string body, int parts = 1, int sources = 0)
    {
        var results = new List<SpecResult>();
        foreach (Match match in Counted().Matches(assignment))
        {
            if (Count(match.Groups[1].Value) is not { } wanted || wanted < 2) continue;
            var thing = match.Groups[2].Value.ToLowerInvariant();
            if (results.Any(result => result.Requirement.EndsWith(" " + thing, StringComparison.Ordinal))) continue;
            // A series is judged by its parts only when it is one; a single long piece asked for "three posts" is judged on items.
            var found = thing is "posts" or "emails" or "drafts" && parts < 2 ? Found("ideas", body, parts) : Found(thing, body, parts);
            results.Add(new($"{wanted} {thing}", found == wanted, found == wanted ? $"{found}" : $"found {found}"));
        }
        var words = Words(body);
        if (WordLimit().Match(assignment) is { Success: true } limit && Whole(limit.Groups[1].Value) is var most)
            results.Add(new($"under {most} words", words <= most, $"{words} words"));
        if (WordRange().Match(assignment) is { Success: true } range && Whole(range.Groups[1].Value) is var low && Whole(range.Groups[2].Value) is var high && high > low)
            results.Add(new($"{low}–{high} words", words >= low * 0.9 && words <= high * 1.1, $"{words} words"));
        if (Regex.IsMatch(assignment, @"\bsubject:? line\b", RegexOptions.IgnoreCase))
            results.Add(new("a Subject: line", Regex.IsMatch(body.TrimStart(), @"^\**subject\**\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline), Regex.IsMatch(body, @"^\**subject\**\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline) ? "present" : "missing"));
        if (Regex.IsMatch(assignment, @"\bcite\b|\bcitations?\b|\bsources?\b", RegexOptions.IgnoreCase) && sources > 0)
            results.Add(new("sources cited", Regex.IsMatch(body, @"\[\d{1,2}\]"), Regex.Matches(body, @"\[\d{1,2}\]").Count + " citation(s)"));
        return [.. results];
    }

    /// <summary>The owner's notes on an earlier version, one ask each: numbered points ("1) … 2) …") when they're numbered,
    /// otherwise sentences. Each is checked on the new version, so a note can't be answered in general and missed in particular.</summary>
    public static string[] OwnerAsks(string feedback)
    {
        feedback = (feedback ?? "").Trim();
        // "Guidance:" starts how to go about it (explore angles, ask at most one question): advice for the writer, not requirements of the work.
        if (Regex.Match(feedback, @"(?:^|\s)Guidance:", RegexOptions.IgnoreCase) is { Success: true } guidance) feedback = feedback[..guidance.Index].Trim();
        if (Regex.Match(feedback, @"^Deliver:\s*", RegexOptions.IgnoreCase) is { Success: true } deliver) feedback = feedback[deliver.Length..];
        var found = Regex.Matches(feedback, @"(?:^|(?<=\s))(\d{1,2})[).]\s+");
        var marks = new List<Match>();
        foreach (Match mark in found) if (int.Parse(mark.Groups[1].Value, CultureInfo.InvariantCulture) == marks.Count + 1) marks.Add(mark);
        string[] asks = marks.Count >= 2
            ? [.. marks.Select((mark, index) => feedback[(mark.Index + mark.Length)..(index + 1 < marks.Count ? marks[index + 1].Index : feedback.Length)].Trim())]
            : Regex.Split(feedback, @"(?<=[.!?])\s+");
        return [.. asks.Select(ask => ask.Trim().TrimEnd(';')).Where(ask => ask.Length >= 8).Take(8).Select(ask => ask.Length > 300 ? ask[..300] : ask)];
    }

    /// <summary>What an ask measures, when the host can measure it: a length in characters or words, a video's running time,
    /// how many links, a Subject line. Such an ask is done when the host's own check of that finds nothing wrong.</summary>
    public static string? Dimension(string ask) =>
        Regex.IsMatch(ask, @"\bcharacters?\b", RegexOptions.IgnoreCase) ? "characters"
        : Regex.IsMatch(ask, @"\d[\d,]*\s*words\b|\bword (limit|count)\b", RegexOptions.IgnoreCase) ? "words"
        : Regex.IsMatch(ask, @"\bseconds?\b", RegexOptions.IgnoreCase) ? "seconds"
        : Regex.IsMatch(ask, @"\b(one|only one|a single|only the)\s+link\b|\blinks? only\b|\bone link only\b", RegexOptions.IgnoreCase) ? "link"
        : Regex.IsMatch(ask, @"\bsubject:? line\b", RegexOptions.IgnoreCase) ? "subject" : null;

    /// <summary>A video's running time against the assignment's "60+ second" or "at least 60 seconds".</summary>
    public static SpecResult[] Duration(string assignment, double seconds)
    {
        var asked = Regex.Match(assignment, @"\b(\d{1,3})\s*\+\s*-?\s*seconds?\b|\bat least (\d{1,3})\s*seconds?\b|\b(\d{1,3})\s*seconds? or (?:more|longer)\b", RegexOptions.IgnoreCase);
        if (!asked.Success) return [];
        var least = int.Parse(new[] { asked.Groups[1], asked.Groups[2], asked.Groups[3] }.First(group => group.Success).Value, CultureInfo.InvariantCulture);
        return [new($"at least {least} seconds", seconds >= least, $"{seconds:0} seconds")];
    }

    /// <summary>Whether a passage the reviewer quoted is really in the work (case, spacing and markup aside).</summary>
    public static bool Quotes(string body, string? quote)
    {
        static string Plain(string text) => Regex.Replace(Regex.Replace(text.ToLowerInvariant(), @"[*_`#>\\""“”‘’']", ""), @"\s+", " ").Trim();
        var passage = Plain(quote ?? "");
        return passage.Length >= 12 && Plain(body).Contains(passage, StringComparison.Ordinal);
    }

    /// <summary>What every social post must meet whatever the assignment says, checked post by post: its network's own length
    /// limit (counted the network's way), one link, and in a series, an opening of its own.</summary>
    /// <summary>A number the work gives the list that follows it ("five real assignments:") against the items it actually lists:
    /// one per line, or short sentences in one paragraph. Only a mismatch is reported; prose that isn't a list is left alone.</summary>
    public static SpecResult[] Tallies(string body)
    {
        var results = new List<SpecResult>();
        var lines = body.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var intro = lines[index].Trim();
            if (!intro.EndsWith(':') || intro.Length > 160) continue;
            var numbers = Regex.Matches(intro, @"(?<![\w-])(\d{1,2}|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)(?![\w-])", RegexOptions.IgnoreCase);
            if (numbers.Count != 1 || Count(numbers[0].Value) is not { } said) continue;
            var next = index + 1;
            while (next < lines.Length && lines[next].Trim().Length == 0) next++;
            var block = new List<string>();
            for (; next < lines.Length && lines[next].Trim().Length > 0; next++) block.Add(lines[next].Trim());
            var items = block.Count >= 2 ? block
                : block.Count == 1 ? [.. Regex.Split(block[0], @"(?<=[.!?])\s+").Where(item => item.Length > 0)] : [];
            if (items.Count < 2 || items.Any(item => item.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 12) || items.Count == said) continue;
            var claim = intro[numbers[0].Index..].TrimEnd(':');
            results.Add(new($"the list after “{(claim.Length > 60 ? claim[..60] + "…" : claim)}” has {said}", false, $"{items.Count} listed"));
        }
        return [.. results];
    }

    /// <summary>A redraft that swaps a specific link for a less specific one on the same site (the blog index for the post it
    /// shared): the page the original pointed at is what the reader was promised.</summary>
    public static SpecResult[] Narrowed(string original, string body)
    {
        static Uri[] Links(string text) => [.. Regex.Matches(text, @"https?://[^\s)\]""'<>]+").Select(match => match.Value.TrimEnd('.', ',', ';', ':', '!', '?'))
            .Select(link => Uri.TryCreate(link, UriKind.Absolute, out var uri) ? uri : null).OfType<Uri>()];
        string Path(Uri uri) => uri.AbsolutePath.TrimEnd('/');
        var before = Links(original); var after = Links(body);
        var results = new List<SpecResult>();
        foreach (var link in after)
            // The home page (a sign-up link) is a different page, not a less specific one.
            if (Path(link).Length > 0 && before.FirstOrDefault(earlier => earlier.Host == link.Host && Path(earlier).StartsWith(Path(link) + "/", StringComparison.OrdinalIgnoreCase)
                && !after.Any(kept => kept.Host == earlier.Host && Path(kept) == Path(earlier))) is { } specific)
                results.Add(new($"the link to {specific.AbsoluteUri}", false, $"now {link.AbsoluteUri}, a less specific page"));
        return [.. results];
    }

    /// <summary>A post that says the same thing over: a phrase of five or more words three times, or of eight or more twice
    /// ("runs on your own computer today" in three sentences of one short post). Links don't count.</summary>
    public static SpecResult[] Repeats(string body)
    {
        var words = Regex.Replace(body, @"https?://\S+", " ").ToLowerInvariant().Split((char[])[' ', '\n', '\r', '\t', ',', '.', ':', ';', '!', '?', '—', '–', '“', '”', '"', '(', ')'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var (size, most) in new[] { (8, 1), (5, 2) })
        {
            var seen = new Dictionary<string, int>();
            for (var start = 0; start + size <= words.Length; start++)
            {
                var phrase = string.Join(' ', words[start..(start + size)]);
                if ((seen[phrase] = seen.GetValueOrDefault(phrase) + 1) > most)
                    return [new($"no phrase said {most + 1} times", false, $"“{phrase}” {seen[phrase]} times")];
            }
        }
        return [];
    }

    /// <summary>New work that copies the owner's voice examples word for word: a sentence of six or more words that is already one
    /// of their posts. Examples are how they sound, not copy to reuse. A quoted "Before" (the current line, shown to be replaced) is fine.</summary>
    public static SpecResult[] Copied(string body, IEnumerable<string> examples)
    {
        static string Plain(string text) => Regex.Replace(Regex.Replace(text.ToLowerInvariant(), @"[*_`>#\[\]]", ""), @"\s+", " ").Trim();
        var known = examples.Select(Plain).Where(text => text.Length > 0).ToArray();
        if (known.Length == 0) return [];
        // Lines that present the current version, to be replaced, are quoting it on purpose.
        var fresh = string.Join("\n", body.Replace("\r\n", "\n").Split('\n').Where(line => !Regex.IsMatch(line, @"^\s*(>|\**(before|current|now|original)\b\**\s*[:\-—])", RegexOptions.IgnoreCase)));
        // Whole lines too: a headline of two short sentences is still a copied line.
        foreach (var sentence in Regex.Split(fresh, @"(?<=[.!?])\s+|\n+").Concat(fresh.Split('\n')).Select(Plain).Where(sentence => sentence.Split(' ').Length >= 6).Distinct())
            if (known.FirstOrDefault(example => example.Contains(sentence, StringComparison.Ordinal)) is { })
                return [new("new wording, not a copy of your own posts", false, $"“{(sentence.Length > 80 ? sentence[..80] + "…" : sentence)}” is already one of them")];
        return [];
    }

    /// <summary>A document for the owner that ends on the public call to action ("decide whether to sign up for the beta"): it
    /// should end on the owner's decision. The end is the last section before any grade or sources footer.</summary>
    public static SpecResult[] OwnerDocumentCta(string body, string? ctaUrl)
    {
        if (string.IsNullOrWhiteSpace(ctaUrl)) return [];
        var main = Regex.Split(body, @"\n-{3,}\s*\n|\n## Sources\b")[0].TrimEnd();
        var last = main[Math.Max(0, main.LastIndexOf("\n#", StringComparison.Ordinal))..];
        if (last.Length > 600) last = last[^600..];
        return last.Contains(ctaUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            ? [new("a document for you ends on your decision", false, "it ends on the public call to action")] : [];
    }

    public static SpecResult[] Posts(IReadOnlyList<(string Channel, string Body)> posts)
    {
        var results = new List<SpecResult>();
        string? Kind(string channel) => Publishing.Kinds.FirstOrDefault(item => item.Value.Channels.Contains(channel.Trim().ToLowerInvariant())).Key;
        foreach (var (channel, body) in posts)
        {
            if (Kind(channel) is not { } kind || Publishing.DraftsOnly(kind) || kind == "wordpress") continue;
            if (Publishing.Kinds[kind].Limit is { } limit && Publishing.Length(kind, body) is var length && length > limit)
                results.Add(new($"the {channel} post within {limit:N0} characters", false, $"{length:N0} characters"));
            var links = Regex.Matches(body, @"https?://[^\s)\]]+").Select(match => match.Value.TrimEnd('.', ',', ';')).Distinct().Count();
            if (links > 1) results.Add(new($"one link in the {channel} post", false, $"{links} links"));
        }
        // The first line of each post in a series is its own: two that share most of their words are the same opening.
        static HashSet<string> Opening(string body) => [.. Regex.Matches(body.Trim().Split('\n')[0].ToLowerInvariant(), @"[a-z0-9']{3,}").Select(match => match.Value)];
        for (var first = 0; first < posts.Count; first++)
            for (var second = first + 1; second < posts.Count; second++)
            {
                var a = Opening(posts[first].Body); var b = Opening(posts[second].Body);
                if (a.Count > 0 && b.Count > 0 && a.Intersect(b).Count() / (double)a.Union(b).Count() >= 0.6)
                {
                    results.Add(new("each post opens its own way", false, $"the {posts[first].Channel} and {posts[second].Channel} posts open the same way"));
                    first = posts.Count; break;
                }
            }
        return [.. results];
    }

    /// <summary>"8 questions ✓, under 150 words ✗ (163 words)".</summary>
    public static string Line(SpecResult[] results) =>
        string.Join(", ", results.Select(result => $"{result.Requirement} {(result.Met ? "✓" : "✗")}{(result.Met ? "" : $" ({result.Detail})")}"));
}
