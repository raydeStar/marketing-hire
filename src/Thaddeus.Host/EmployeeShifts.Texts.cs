using System.Text.Json;
using System.Text.RegularExpressions;

namespace Thaddeus.Host;

public sealed partial class EmployeeShifts
{
    /// <summary>Texts the owner (a Plow employee's line) with a key it is sent once for. Unset: nobody is texted.</summary>
    public Func<string, string, CancellationToken, Task<bool>>? TextOwner { get; set; }
    /// <summary>Where the owner opens the cockpit, named at the end of a text.</summary>
    public string? CockpitLink { get; set; }

    /// <summary>A named blank the owner fills in ([price], [launch date]); never a citation or a link's text.</summary>
    [GeneratedRegex(@"(?-i:\[([a-z][a-z0-9 ,/&'’-]{1,40})\])(?!\()")] internal static partial Regex FactBlank();

    /// <summary>What a finished run or shift made and asked, as one text the owner can act on without opening the cockpit:
    /// the work itself (a post's words, a document's opening), the blanks to fill in, its questions, and how to reply.</summary>
    public async Task<string?> RunText(EmployeeShift shift)
    {
        var notes = shift.Cycles.SelectMany(cycle => cycle.Stages).Where(stage => stage.Stage == "create").SelectMany(stage => stage.Summary.Split('\n')).ToArray();
        var asked = notes.Select(line => Regex.Match(line, @"^Asked you about (.+?): (.+)$")).Where(match => match.Success)
            .Select(match => (Title: match.Groups[1].Value, Question: match.Groups[2].Value.Trim())).DistinctBy(item => item.Title).ToArray();
        var stuck = notes.Select(line => Regex.Match(line, @"^(.+?) is back with you: (.+?)\.?$")).Where(match => match.Success)
            .Select(match => (Title: match.Groups[1].Value, Why: match.Groups[2].Value)).DistinctBy(item => item.Title).ToArray();
        var made = shift.Created.Select(output => { var space = output.IndexOf(' '); return (Key: space > 0 ? output[..space] : output, Title: space > 0 ? output[(space + 1)..] : output); })
            .Where(item => Regex.IsMatch(item.Key, @"^(draft|wiki|pagecopy|media):[A-Za-z0-9_-]+$")).DistinctBy(item => item.Key).ToArray();
        if (made.Length == 0 && asked.Length == 0 && stuck.Length == 0) return null;

        JsonElement? work = made.Any(item => item.Key.StartsWith("draft:", StringComparison.Ordinal)) ? (await marketing.ShiftHire(null, "snapshot")).Value : null;
        var pieces = new List<(string Label, string Body)>();
        // Posts the owner would put up themselves on X, Bluesky or Threads (nothing connected there, or an X reply): a "post N" reply
        // brings the link that opens it filled in.
        var tappable = new List<(string Id, string Network)>();
        foreach (var (key, title) in made)
        {
            var id = key[(key.IndexOf(':') + 1)..];
            if (key.StartsWith("draft:", StringComparison.Ordinal))
            {
                var draft = work?.GetProperty("drafts").EnumerateArray().FirstOrDefault(item => Num(item, "id") == id);
                if (draft is not { ValueKind: JsonValueKind.Object } found) continue;
                // A draft already decided (approved in the cockpit, sent back) isn't news.
                if (Str(found, "status") != "pending") continue;
                pieces.Add(($"{Str(found, "channel")} post (draft #{id})", WithoutImageLine(Str(found, "content"))));
                if (Publishing.Compose(Str(found, "channel"), Str(found, "destination"), Str(found, "content")) is { } tap && publishing.Route(Str(found, "channel"), Str(found, "destination")).Action == "copy")
                    tappable.Add((id, tap.Network));
            }
            else if (key.StartsWith("wiki:", StringComparison.Ordinal) && wiki.List().FirstOrDefault(page => page.Id == id) is { } page)
                pieces.Add((page.Title, Regex.Replace(page.Body, @"^_[^\n]*_\s*\n+", "")));   // the kept-draft note in italics isn't the work
            else if (key.StartsWith("pagecopy:", StringComparison.Ordinal) && pages.Find(id) is { } proposal)
                pieces.Add(($"New copy for {PageWatch.Short(proposal.Url)}", proposal.After));
            else if (key.StartsWith("media:", StringComparison.Ordinal)) pieces.Add((title, ""));
        }
        if (pieces.Count == 0 && asked.Length == 0 && stuck.Length == 0) return null;

        var blanks = pieces.SelectMany(piece => FactBlank().Matches(piece.Body).Select(match => match.Groups[1].Value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        var tail = new List<string>();
        if (blanks.Length > 0) tail.Add("Fill in before posting: " + string.Join(", ", blanks) + ".");
        tail.AddRange(asked.Select(item => $"Question about “{item.Title}”: {item.Question}"));
        tail.AddRange(stuck.Select(item => $"Couldn't finish “{item.Title}”: {item.Why}."));
        if (tappable.Count > 0)
        {
            // One network is named; posts for several share one line that names none.
            var network = tappable.Select(item => item.Network).Distinct().Count() == 1 ? tappable[0].Network : null;
            var first = tappable[0].Id;
            tail.Add((tappable.Count == 1 ? $"To post #{first} on {network}, reply \"post {first}\"" : $"To post one {(network != null ? "on " + network : "yourself")}, reply \"post {first}\" (or another draft's number)") +
                $" and say yes when I ask: I'll text you a link that opens {network ?? "it"} with {(network != null ? "it" : "the words")} filled in.");
        }
        tail.Add((asked.Length > 0 ? "Reply here with your answer" : pieces.Count > 0 ? "Reply here with any changes" : "Reply here with what you'd like instead") +
            (pieces.Count > 0 && CockpitLink is { Length: > 0 } link ? $", or approve and post from your cockpit: {link}" : "."));
        var head = pieces.Count > 0
            ? (shift.Requests ? "Done with what you asked" : "From this shift") + $": {pieces.Count} {(pieces.Count == 1 ? "piece" : "pieces")} ready for your review."
            : shift.Requests ? "I need you before I can finish what you asked." : "This shift needs you before it can go on.";

        // The work fills what the text has room for: the first pieces in full or nearly, the rest by name.
        var room = OwnerTexts.MaxLength - head.Length - tail.Sum(line => line.Length + 2) - 8;
        var body = new List<string>();
        for (var index = 0; index < pieces.Count; index++)
        {
            var (label, text) = pieces[index];
            var flat = Regex.Replace(text.Trim(), @"\n{3,}", "\n\n");
            var share = Math.Min(600, (room - body.Sum(line => line.Length + 2)) / Math.Max(1, pieces.Count - index) - label.Length - 8);
            if (share < 80 || flat.Length == 0) { body.Add($"{index + 1}) {label}"); continue; }
            body.Add($"{index + 1}) {label}\n{(flat.Length > share ? flat[..share].TrimEnd() + "…" : flat)}");
        }
        return string.Join("\n\n", new[] { head }.Concat(body).Concat(tail));
    }

    /// <summary>Tells the owner, once, what a finished run or shift made. Never when the owner stopped it (they're in the cockpit).</summary>
    async Task TextAbout(EmployeeShift shift)
    {
        if (TextOwner == null || shift.StopReason?.StartsWith("Stopped", StringComparison.Ordinal) == true) return;
        try { if (await RunText(shift) is { } text) await TextOwner("run:" + shift.Id, text, CancellationToken.None); }
        catch (Exception error) when (error is InvalidOperationException or JsonException or IOException or HttpRequestException or KeyNotFoundException)
        { logger.LogWarning("The owner wasn't texted about {Shift}: {Error}", shift.Id, error.Message); }
    }
}
