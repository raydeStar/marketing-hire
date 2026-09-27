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
    /// <summary>Two posts whose openings (the first paragraph, heading aside) share a run of six or more words: the same post twice.
    /// The live kit opened two of its three posts with "…you do not have to wait until you are ready for therapy".</summary>
    public static string? SharedOpening(IReadOnlyList<string> posts)
    {
        static string[] Opening(string post)
        {
            var text = Regex.Replace(post.Replace("\r", ""), @"^#{1,4}[^\n]*\n+", "").Trim();
            var first = text.Split("\n\n")[0];
            return Regex.Matches(first.ToLowerInvariant().Replace('’', '\''), @"[a-z0-9']+").Select(match => match.Value).ToArray();
        }
        var openings = posts.Select(Opening).ToArray();
        for (var a = 0; a < openings.Length; a++)
            for (var b = a + 1; b < openings.Length; b++)
                for (var start = 0; start + 6 <= openings[a].Length; start++)
                {
                    var phrase = string.Join(' ', openings[a][start..(start + 6)]);
                    if ((" " + string.Join(' ', openings[b]) + " ").Contains(" " + phrase + " ", StringComparison.Ordinal))
                        return $"posts {a + 1} and {b + 1} both open with “{phrase}…”";
                }
        return null;
    }

    /// <summary>An ask that is a count of the series itself ("five posts for this week as a series, in the order to post them"),
    /// which the host measures: how many of what. Null when the ask says more than that (a comma-free clause of its own).</summary>
    public static (int Many, string Thing)? SeriesCount(string ask) =>
        Regex.Match(ask.Trim().TrimEnd('.'), @"^(\d{1,2}|one|two|three|four|five|six|seven|eight|nine|ten)\s+(posts|emails|drafts)\b(?:\s+for\s+(?:this|the|next)\s+week)?(?:\s+as\s+a\s+series)?(?:,\s*in\s+the\s+order\s+to\s+(?:post|send)\s+them)?(?:,?\s*(?:across|on)\s+[^;:]+)?$", RegexOptions.IgnoreCase) is { Success: true } match
        && Count(match.Groups[1].Value) is { } many ? (many, match.Groups[2].Value.ToLowerInvariant()) : null;

    /// <summary>"Posts 1 to 3" or "posts 4 and 5" in an ask: which posts it is about (1-based, inclusive).</summary>
    public static (int From, int To)? PostRange(string ask) =>
        Regex.Match(ask, @"^\s*posts?\s+(\d)(?:\s*(?:to|–|-|and|through)\s*(\d))?\b|\bposts?\s+(\d)\s*(?:to|–|-|and|through)\s*(\d)\b", RegexOptions.IgnoreCase) is { Success: true } range
        && int.Parse(range.Groups[1].Success ? range.Groups[1].Value : range.Groups[3].Value, CultureInfo.InvariantCulture) is var from
        && (range.Groups[2].Success ? int.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture) : range.Groups[4].Success ? int.Parse(range.Groups[4].Value, CultureInfo.InvariantCulture) : from) is var to && from >= 1 && to >= from ? (from, to) : null;

    /// <summary>A week's mix, as the assignment sets it ("posts 1 to 3 teach … no link; posts 4 and 5 promote … the call to action"),
    /// measured by where the links are: a teaching post carries none, a promoting post carries the call to action.</summary>
    public static SpecResult[] Mix(string assignment, IReadOnlyList<string> parts, string? ctaUrl)
    {
        var results = new List<SpecResult>();
        static bool Linked(string text) => Regex.IsMatch(text, @"https?://");
        foreach (Match clause in Regex.Matches(assignment, @"\bposts?\s+\d\s*(?:to|–|-|and|through)\s*\d\b[^.;]*", RegexOptions.IgnoreCase))
        {
            if (PostRange(clause.Value) is not { } range || range.To > parts.Count) continue;
            var which = Enumerable.Range(range.From, range.To - range.From + 1).ToArray();
            if (Regex.IsMatch(clause.Value, @"\bno link\b|\bwithout (?:a|the) link\b", RegexOptions.IgnoreCase) && which.Where(n => Linked(parts[n - 1])).ToArray() is { Length: > 0 } linked)
                results.Add(new($"posts {range.From} to {range.To} teach, without a link", false, $"post {string.Join(" and ", linked)} {(linked.Length == 1 ? "has" : "have")} a link"));
            if (Regex.IsMatch(clause.Value, @"\bcall to action\b", RegexOptions.IgnoreCase) && which.Where(n => !(string.IsNullOrWhiteSpace(ctaUrl) ? Linked(parts[n - 1]) : parts[n - 1].Contains(ctaUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))).ToArray() is { Length: > 0 } missing)
                results.Add(new($"posts {range.From} and {range.To} carry the call to action", false, $"post {string.Join(" and ", missing)} {(missing.Length == 1 ? "doesn't" : "don't")}"));
        }
        return [.. results];
    }

    /// <summary>An ask about some of a series' posts ("Three teach one idea each", "at most two promote the seminar"): how many,
    /// and whether that's a ceiling. Null when it's about every post (five of five) or names no number.</summary>
    public static (int Many, bool AtMost)? Counted(string ask, int posts) =>
        Regex.Match(ask, @"^\s*(at most |no more than |up to )?(\d{1,2}|one|two|three|four|five|six|seven|eight|nine|ten)\b", RegexOptions.IgnoreCase) is { Success: true } counted
        && Count(counted.Groups[2].Value) is { } many && many < posts ? (many, counted.Groups[1].Success) : null;
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

    /// <summary>How many headings or bold labels name the thing ("## Post 2: the reminder" is a post), or null when none do.</summary>
    static int? Labelled(string thing, string body)
    {
        var one = thing.TrimEnd('s');
        var count = body.Replace("\r", "").Split('\n').Count(line => Regex.IsMatch(line.Trim(), $@"^(#{{1,4}}\s+|\*\*)[^\n]*\b{one}\b(?!s)", RegexOptions.IgnoreCase));
        return count > 0 ? count : null;
    }

    /// <summary>The body's sections under a heading that names the thing ("## Post 3 — final promotion"), each with its text.</summary>
    public static string[] Sections(string body, string thing) =>
        [.. Regex.Split(body.Replace("\r", ""), @"\n(?=#{1,4}\s)").Where(part => Regex.IsMatch(part.Split('\n')[0], $@"^#{{1,4}}\s[^\n]*\b{thing}s?\b", RegexOptions.IgnoreCase))];

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
            // A series is judged by its parts only when it is one; a single long piece asked for "three posts" is judged by the
            // sections named for them ("## Post 1", "**Reminder email**") when it has them (a kit holds page copy and emails too), else on items.
            var found = thing is "posts" or "emails" or "drafts" && parts < 2 ? Labelled(thing, body) ?? Found("ideas", body, parts) : Found(thing, body, parts);
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
        // Quotes and apostrophes of either style count as the same: the owner typed "done," and the post printed “done,”.
        static string Plain(string text) => Regex.Replace(Regex.Replace(text.ToLowerInvariant(), @"[*_`>#\[\]""“”‘’']", ""), @"\s+", " ").Trim();
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
        // The footer is what follows the last --- line when it is the grade or the sources; a kit's --- lines between its pieces stay.
        var main = Regex.Split(body, @"\n## Sources\b")[0];
        var rule = Regex.Matches(main, @"\n-{3,}\s*\n").LastOrDefault();
        if (rule != null && Regex.IsMatch(main[(rule.Index + rule.Length)..].TrimStart(), @"^(_|\*|Sources\b|\[\d)")) main = main[..rule.Index];
        main = main.TrimEnd();
        // Only the document's own last paragraph: proposed public copy above it keeps its call to action.
        var last = Regex.Split(main, @"\n\s*\n").Select(part => part.Trim()).LastOrDefault(part => part.Length > 0) ?? "";
        return last.Contains(ctaUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            ? [new("a document for you ends on your decision", false, "it ends on the public call to action")] : [];
    }

    /// <summary>A before/after whose After repeats the Before: a sentence of five or more words from the Before, punctuation aside,
    /// is still in the After, so nothing was improved.</summary>
    public static SpecResult[] BeforeAfter(string body)
    {
        static string Plain(string text) => Regex.Replace(Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{N}\s]", " "), @"\s+", " ").Trim();
        var before = Regex.Match(body, @"(?:^|\n)\s*(?:#+\s*|\*\*)?Before\b[^\n]*\n?(.*?)(?=\n\s*(?:#+\s*|\*\*)?After\b)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        // The After runs to the next section of the memo (Why, Evidence, the decision…), past any headline of its own.
        var after = Regex.Match(body, @"(?:^|\n)\s*(?:#+\s*|\*\*)?After\b[^\n]*\n?(.*?)(?=\n\s*(?:#{1,3}\s*|\*\*)?(?:Why|Evidence|Next|Owner|Decision|Sources|Limits|Recommendation|What)\b|\n-{3,}|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!before.Success || !after.Success) return [];
        var improved = Plain(string.Join("\n", after.Groups[1].Value.Split('\n').Where(line => !Regex.IsMatch(line, @"\((?:unchanged|kept|kept as is|stays)\)|\b(?:unchanged|stays as it is)\b", RegexOptions.IgnoreCase))));
        foreach (var sentence in Regex.Split(before.Groups[1].Value, @"(?<=[.!?])\s+|\n+").Concat(before.Groups[1].Value.Split('\n')).Select(Plain).Where(sentence => sentence.Split(' ').Length >= 5).Distinct())
            if (improved.Contains(sentence, StringComparison.Ordinal))
                return [new("an After that changes the Before", false, $"the After repeats “{(sentence.Length > 70 ? sentence[..70] + "…" : sentence)}”")];
        return [];
    }

    /// <summary>What a kind of work must meet that code can measure: a pitch's length, subject and links; a reply's length and a first
    /// message without a link; an event's date, time and sign-up link in every piece; a Google Business Profile description's
    /// limits; a nurture email's subject and timing; ad copy limits, a budget and a stop rule; a pricing test with its measure.
    /// <paramref name="parts"/> are the pieces (a series' parts, or the one body).</summary>
    public static SpecResult[] ForKind(string kind, string assignment, IReadOnlyList<string> parts, string? ctaUrl)
    {
        var results = new List<SpecResult>();
        var body = string.Join("\n\n", parts);
        static int Links(string text) => Regex.Matches(text, @"https?://[^\s)\]]+").Select(match => match.Value.TrimEnd('.', ',', ';')).Distinct().Count();
        static string? Subject(string text) => Regex.Match(text, @"^\W*subject\W*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline) is { Success: true } line ? line.Groups[1].Value.Trim().Trim('*').Trim() : null;
        // The message itself, without the Subject line or labels a writer puts above it.
        static string Message(string text) => Regex.Replace(text, @"^\W*(subject|to|channel|send|when|segment|trigger)\W*:.*$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        string Which(int index) => parts.Count > 1 ? $" (piece {index + 1})" : "";
        switch (kind)
        {
            case "pitch":
                for (var index = 0; index < parts.Count; index++)
                {
                    var words = Words(Message(parts[index]));
                    if (words > 150) results.Add(new($"a pitch under 150 words{Which(index)}", false, $"{words} words"));
                    if (Subject(parts[index]) is not { } subject) { if (!Regex.IsMatch(assignment, @"\b(dm|direct message|linkedin message)\b", RegexOptions.IgnoreCase)) results.Add(new($"a Subject line{Which(index)}", false, "missing")); }
                    else if (subject.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 8) results.Add(new($"a subject of eight words or fewer{Which(index)}", false, $"“{subject}”"));
                    if (Links(parts[index]) > 1) results.Add(new($"one link at most{Which(index)}", false, $"{Links(parts[index])} links"));
                }
                break;
            case "community":
                if (Regex.IsMatch(assignment, @"\b(repl(?:y|ies)|dms?|direct messages?)\b", RegexOptions.IgnoreCase))
                    for (var index = 0; index < parts.Count; index++)
                        foreach (var reply in Regex.Split(parts[index], @"\n\s*\n").Where(block => !Regex.IsMatch(block.Trim(), @"^(#|\*\*[^*]+\*\*\s*$|>)")).Select(Message))
                            if (Words(reply) > 80) { results.Add(new($"a reply under 80 words{Which(index)}", false, $"{Words(reply)} words")); break; }
                if (Regex.IsMatch(assignment, @"\b(dms?|direct messages?|first message)\b", RegexOptions.IgnoreCase) && Links(body) > 0)
                    results.Add(new("a first message without a link", false, $"{Links(body)} link(s)"));
                break;
            case "event":
                var months = @"(?:jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)[a-z]*\.?\s+\d{1,2}\b|\b\d{1,2}(?:st|nd|rd|th)?\s+(?:of\s+)?(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*|\b\d{4}-\d{2}-\d{2}\b|\b\d{1,2}/\d{1,2}\b|\b(?:mon|tues|wednes|thurs|fri|satur|sun)day\b";
                for (var index = 0; index < parts.Count; index++)
                    if (!Regex.IsMatch(parts[index], months, RegexOptions.IgnoreCase)) results.Add(new($"the event's date{Which(index)}", false, "no date"));
                if (!Regex.IsMatch(body, @"\b\d{1,2}(?::\d{2})?\s*(?:am|pm|a\.m\.|p\.m\.)|\b\d{1,2}:\d{2}\b", RegexOptions.IgnoreCase)) results.Add(new("the event's time", false, "no time given"));
                if (!string.IsNullOrWhiteSpace(ctaUrl) && !body.Contains(ctaUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) results.Add(new("the sign-up link", false, $"{ctaUrl} is missing"));
                // The kit's posts go out whenever the owner posts them: a date as the date (a day-before email may say "tomorrow").
                // Every piece the assignment names has its own heading: the live kit left out its follow-up and was passed as complete.
                var named = new List<(string Piece, string Asked, string Heading)> { ("the registration page", @"registration|sign-?up page|landing page", @"registration|sign-?up page|landing page"),
                    ("the reminder email", @"reminder email", @"reminder[^\n]*email|email[^\n]*reminder"), ("the follow-up email", @"follow-?\s?up", @"follow-?\s?up") };
                // "Post 1 … Post 2 … Post 3": each post the assignment names is a post of its own (the live kit merged post 3 into a "Reminder post").
                foreach (Match post in Regex.Matches(assignment, @"\bPost (\d)\b")) named.Add(($"Post {post.Groups[1].Value}", $@"\bPost {post.Groups[1].Value}\b", $@"\bPost {post.Groups[1].Value}\b"));
                foreach (var (piece, asked, heading) in named.DistinctBy(item => item.Piece))
                    if (Regex.IsMatch(assignment, asked, RegexOptions.IgnoreCase) && !Regex.IsMatch(body, $@"(?:^|\n)#{{1,4}}\s[^\n]*(?:{heading})", RegexOptions.IgnoreCase))
                        results.Add(new($"{piece}, under its own heading", false, "missing"));
                var kitPosts = Sections(body, "post");
                foreach (var post in kitPosts)
                    if (RelativeDays(post) is [var relativeDay]) { results.Add(relativeDay with { Requirement = "dates written as dates in the posts" }); break; }
                if (SharedOpening(kitPosts) is { } shared) results.Add(new("each post opens its own way", false, shared));
                // A follow-up thanks the people who came and offers the next step; signing them up again for the seminar they attended isn't one.
                if (!string.IsNullOrWhiteSpace(ctaUrl) && Sections(body, "follow-up").Concat(Sections(body, "follow up")).FirstOrDefault() is { } followUp && followUp.Contains(ctaUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                    results.Add(new("a follow-up with its own next step", false, "it sends attendees to the seminar's sign-up again"));
                break;
            case "local":
                if (Regex.Match(body, @"(?:^|\n)\s*(?:#+\s*|\*\*)?(?:(?:google )?business profile )?description\b[^\n]*\n+(.*?)(?=\n\s*(?:#|\*\*[A-Z])|\n\s*\n\s*\n|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline) is { Success: true } section)
                {
                    var description = Regex.Replace(section.Groups[1].Value, @"^\s*\(?\d+ characters\)?\s*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase).Trim();
                    if (description.Length > 750) results.Add(new("a profile description within 750 characters", false, $"{description.Length} characters"));
                    if (Links(description) > 0 || Regex.IsMatch(description, @"\(?\d{3}\)?[\s.-]\d{3}[\s.-]\d{4}")) results.Add(new("a profile description with no link or phone number", false, "Google removes those"));
                }
                break;
            case "nurture":
                for (var index = 0; index < parts.Count; index++)
                {
                    if (Subject(parts[index]) is not { } subject) results.Add(new($"a Subject line{Which(index)}", false, "missing"));
                    else if (subject.Length > 50) results.Add(new($"a subject under 50 characters{Which(index)}", false, $"{subject.Length} characters"));
                    if (!Regex.IsMatch(parts[index], @"\b(send|sent|day \d+|after|when|trigger|immediately|right away|\d+ (?:hours?|days?|weeks?) (?:later|after))\b", RegexOptions.IgnoreCase))
                        results.Add(new($"when it's sent{Which(index)}", false, "no timing"));
                }
                break;
            case "paid":
                foreach (Match headline in Regex.Matches(body, @"^\W*headline(?:\s*\d+)?\W*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline))
                    if (Regex.IsMatch(body, @"\bgoogle\b", RegexOptions.IgnoreCase) && headline.Groups[1].Value.Trim().Trim('*', '"', '“', '”').Length > 30)
                    { results.Add(new("Google headlines within 30 characters", false, $"“{headline.Groups[1].Value.Trim()}” is {headline.Groups[1].Value.Trim().Trim('*', '"', '“', '”').Length}")); break; }
                if (!Regex.IsMatch(body, @"budget[^\n]{0,80}[$€£]\s?\d|[$€£]\s?\d[\d,.]*[^\n]{0,40}\b(budget|total|a day|per day|daily|a month|per month|monthly)\b", RegexOptions.IgnoreCase))
                    results.Add(new("a budget with a figure", false, "none given"));
                if (!Regex.IsMatch(body, @"\b(pause|stop|cut|kill|turn off|shift|move)\b[^\n.]{0,120}\b(if|when|once|after|above|below|under|over)\b", RegexOptions.IgnoreCase))
                    results.Add(new("a rule for when to stop or shift money", false, "none given"));
                break;
            case "pricing":
                if (!Regex.IsMatch(body, @"(?:^|\n)\s*(?:#+\s*|\*\*|[-*]\s+)?(?:one )?(?:change to )?test(?: first)?\b", RegexOptions.IgnoreCase))
                    results.Add(new("one change to test first", false, "no test section"));
                else if (!Regex.IsMatch(body, @"\b(for|over|run(?:s)? for|until)\s+(?:\d+|one|two|three|four|six|eight|a)\s+(?:days?|weeks?|months?|sign-?ups|customers|sales)\b", RegexOptions.IgnoreCase))
                    results.Add(new("how long the test runs", false, "no length or sample"));
                break;
        }
        return [.. results];
    }

    /// <summary>A bracketed request to the owner for one of their own stories when the writer was given them: live kit run 7 wrote
    /// "[Owner: insert the exact belief from “Something we believe”]" and passed, since an owner-only fact costs no points.</summary>
    public static SpecResult[] AskedForGiven(string body, string? stories)
    {
        if (string.IsNullOrWhiteSpace(stories)) return [];
        var headings = Regex.Matches(stories, @"^##\s+(.+)$", RegexOptions.Multiline).Select(match => match.Groups[1].Value.Trim()).Where(heading => heading.Length > 3).ToArray();
        foreach (Match request in Regex.Matches(body, @"\[(?:Owner|owner)[^\]]*\]"))
            if (headings.Any(heading => request.Value.Contains(heading, StringComparison.OrdinalIgnoreCase)) || Regex.IsMatch(request.Value, @"\b(story|stories|belief|believes?|how (?:you|they|we) started)\b", RegexOptions.IgnoreCase))
                return [new("the owner's story told, not asked for", false, $"{(request.Value.Length > 80 ? request.Value[..80] + "…]" : request.Value)} though their stories were given")];
        return [];
    }

    /// <summary>The proposed copy (the After) in a memo, when it carries the call to action, ends on it: one ask, last. The live site
    /// fix's After ended "register here: <link>. … you can also ask about a free 15-minute consult", two asks with the second last.</summary>
    public static SpecResult[] AfterEndsOnCta(string body, string? ctaUrl)
    {
        if (string.IsNullOrWhiteSpace(ctaUrl)) return [];
        var after = Regex.Match(body, @"(?:^|\n)\s*(?:#+\s*|\*\*)?After\b[^\n]*\n?(.*?)(?=\n\s*(?:#{1,3}\s*|\*\*)?(?:Why|Evidence|Next|Owner|Decision|Sources|Limits|Recommendation|What)\b|\n-{3,}|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!after.Success) return [];
        var copy = after.Groups[1].Value.Trim();
        var link = ctaUrl.TrimEnd('/');
        if (!copy.Contains(link, StringComparison.OrdinalIgnoreCase)) return [];
        // The last sentence that isn't a bracketed note for the owner.
        var last = Regex.Split(Regex.Replace(copy, @"\[(?:Owner|owner)[^\]]*\]", ""), @"(?<=[.!?])\s+|\n+").Select(item => item.Trim()).LastOrDefault(item => item.Length > 0) ?? "";
        return last.Contains(link, StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(last, @"^\W*https?://") ? []
            : [new("the After ends on its one call to action", false, $"it ends on “{(last.Length > 80 ? last[..80] + "…" : last)}”")];
    }

    /// <summary>A playbook's guardrails that code can see. A practice (therapist, coach, consultant) promises no outcomes and tells no
    /// client's story without their consent.</summary>
    public static SpecResult[] Guardrails(string body, string? playbook)
    {
        if (playbook != "practice") return [];
        var results = new List<SpecResult>();
        if (Regex.Match(body, @"\b(guarantee[sd]?|cure[sd]?|will (?:fix|heal|cure|solve)|permanently (?:fix|solve|heal)|100% (?:effective|results|success))\b", RegexOptions.IgnoreCase) is { Success: true } promise)
            results.Add(new("no promised outcomes", false, $"it says “{promise.Value}”"));
        if (Regex.Match(body, @"\b(?:my|a|one|our) (?:client|patient)s?\b[^.!?\n]{0,90}\b(?:told|said|came|was|were|had|felt|struggled|shared|called)\b", RegexOptions.IgnoreCase) is { Success: true } story
            && !Regex.IsMatch(body, @"\b(consent|permission|shared with (?:their|his|her) (?:ok|okay|blessing))\b", RegexOptions.IgnoreCase))
            results.Add(new("a client's story only with their consent", false, $"“{(story.Value.Length > 70 ? story.Value[..70] + "…" : story.Value)}” with no consent noted"));
        return [.. results];
    }

    /// <summary>A post that names a day relative to when it's written ("Thursday night", "tomorrow", "this week"): the owner
    /// approves and posts it later, so the day is wrong by then. A weekday followed by its date ("Thursday, October 16") is fine.</summary>
    public static SpecResult[] RelativeDays(string body)
    {
        var month = @"(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?\s+\d{1,2}|\d{1,2}/\d{1,2}";
        var day = @"(?:mon|tues|wednes|thurs|fri|satur|sun)day";
        var dated = $@"(?!s\b|,?\s+(?:the\s+)?(?:{month}))";
        // A day said as "this Thursday", "on Thursday" or "Thursday night"; "every Sunday night" is a habit, not a date.
        // Only a day said about something happening then: "What would make this week more manageable?" is true whenever it's read.
        var happening = @"\b(join|running|hosting|holding|starts?|opens?|doors|register|registration|seats?|sign up|see you|meet|meetup|meet-up|walk|hike|seminar|workshop|webinar|event|class|session|launch|sale|live)\b|\d{1,2}(?::\d{2})?\s*(?:am|pm)";
        foreach (var sentence in Regex.Split(body, @"(?<=[.!?])\s+|\n+"))
            if (Regex.Match(sentence, $@"\b(?:tonight|tomorrow|this (?:week|weekend)|next week|(?:this|next|on|until|by) {day}{dated}|(?<!every ){day}{dated}\s+(?:night|evening|morning|afternoon)(?!s))\b", RegexOptions.IgnoreCase) is { Success: true } relative
                && Regex.IsMatch(sentence, happening, RegexOptions.IgnoreCase))
                return [new("dates written as dates", false, $"“{relative.Value}” without its date")];
        return [];
    }

    /// <summary>A sentence of eight or more words in two posts of one series: the live community week used the owner's line
    /// "Getting outside with little kids isn't about the hike…" in two of its five posts. The event's details (a date, a time) may repeat.</summary>
    public static string? RepeatedAcross(IReadOnlyList<string> posts)
    {
        static string Plain(string text) => Regex.Replace(Regex.Replace(text.ToLowerInvariant().Replace('’', '\''), @"[^\p{L}\p{N}'\s]", " "), @"\s+", " ").Trim();
        var seen = new Dictionary<string, int>();
        for (var index = 0; index < posts.Count; index++)
            foreach (var sentence in Regex.Split(Regex.Replace(posts[index], @"https?://\S+", ""), @"(?<=[.!?])\s+|\n+").Select(Plain).Where(sentence => sentence.Split(' ').Length >= 8).Distinct())
            {
                if (Regex.IsMatch(sentence, @"\b\d{1,2}(?: \d{2})? ?(?:am|pm)\b|\b(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]* \d{1,2}\b")) continue;
                if (seen.TryGetValue(sentence, out var first) && first != index) return $"posts {first + 1} and {index + 1} both say “{(sentence.Length > 70 ? sentence[..70] + "…" : sentence)}”";
                seen.TryAdd(sentence, index);
            }
        return null;
    }

    public static SpecResult[] Posts(IReadOnlyList<(string Channel, string Body)> posts)
    {
        var results = new List<SpecResult>();
        if (RepeatedAcross([.. posts.Select(post => post.Body)]) is { } repeated) results.Add(new("each post in its own words", false, repeated));
        foreach (var (_, body) in posts) if (RelativeDays(body) is [var relativeDay]) { results.Add(relativeDay); break; }
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
