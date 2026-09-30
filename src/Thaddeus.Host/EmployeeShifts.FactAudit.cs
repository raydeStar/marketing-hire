using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Thaddeus.Host;

public sealed partial class EmployeeShifts
{
    const string AuditFormat = "List every statement in body that a customer could check about the product or the business: what it is or is made of, how it works, " +
        "sizes, specs, features, price, offers, stock or batch size, dates, shipping, returns, warranty, history, awards, reviews and results. Leave out opinions, " +
        "what the reader feels or needs, and bracketed blanks such as [price]. For each give text, the statement's exact words in body. " +
        "Return ONLY JSON: {\"claims\":[{\"text\":\"...\"}]}.";
    const string BlankFormat = "For each passage in unsupported, say what replaces it in body: a bracketed blank naming the missing fact, such as [price] or " +
        "[material], or \"\" to cut it. Return ONLY JSON: {\"changes\":[{\"text\":\"the passage, exactly as in unsupported\",\"with\":\"[blank] or empty\"}]}.";
    /// <summary>What an unconfirmed claim is marked with when the repair left it in: a blank, so publishing refuses it until the owner decides.</summary>
    public const string Unconfirmed = "[unconfirmed]";

    /// <summary>Everything the worker was given as fact: the brief, the assignment in the owner's words, the sources it read.</summary>
    internal static string FactsGiven(JsonElement created)
    {
        var parts = new List<string>();
        // The brief is what the owner said, as saved; a line that calls itself an assumption or a guess isn't a fact they gave.
        if (created.TryGetProperty("brief", out var brief) && brief.ValueKind == JsonValueKind.Object)
            foreach (var field in brief.EnumerateObject().Where(field => field.Value.ValueKind == JsonValueKind.String))
                parts.AddRange(field.Value.GetString()!.Split(['.', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(line => !Guessed(line)));
        if (created.TryGetProperty("task", out var task)) parts.Add(Str(task, "title") + ". " + Str(task, "next_action"));
        if (created.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array)
            parts.AddRange(sources.EnumerateArray().Select(source => Str(source, "title") + " " + Str(source, "text") + " " + Str(source, "evidenceText")));
        if (created.TryGetProperty("redraft", out var redraft) && redraft.ValueKind == JsonValueKind.Object) parts.Add(redraft.GetRawText());
        return string.Join("\n", parts);
    }

    static readonly string[] Hedges = ["assum", "guess", "unless told", "probably", "likely", "tbd", "to be confirmed"];
    /// <summary>A saved line that says it isn't known: "assume it's sold on their site unless told otherwise".</summary>
    internal static bool Guessed(string line) { var plain = Plain(line); return Hedges.Any(hedge => plain.Contains(hedge, StringComparison.Ordinal)); }

    /// <summary>Lowercased, every run of anything but letters and digits one space: a quote matches however it was punctuated.</summary>
    internal static string Plain(string text)
    {
        var built = new StringBuilder(text.Length);
        foreach (var character in text.ToLowerInvariant())
            if (char.IsLetterOrDigit(character)) built.Append(character);
            else if (built.Length > 0 && built[^1] != ' ') built.Append(' ');
        return built.ToString().Trim();
    }

    /// <summary>The digit runs in a text: "68–118 cm" states 68 and 118.</summary>
    internal static IEnumerable<string> Numbers(string text)
    {
        var run = new StringBuilder();
        foreach (var character in text + " ")
            if (char.IsDigit(character)) run.Append(character);
            else if (run.Length > 0) { yield return run.ToString(); run.Clear(); }
    }

    /// <summary>Words that never carry a fact on their own: the claim's facts are in its other words.</summary>
    static readonly HashSet<string> Filler = new(StringComparer.Ordinal) { "with", "from", "that", "this", "these", "those", "your", "yours", "their", "they", "them",
        "have", "will", "into", "onto", "only", "more", "most", "than", "just", "what", "when", "where", "which", "also", "each", "every", "been", "being",
        "about", "ours", "here", "there", "today", "now", "then", "very", "really", "some", "like", "make", "made", "makes", "time", "thing", "things", "want",
        "need", "look", "take", "come", "comes", "meet", "introducing", "finally",
        // Ordinary verbs: "it costs $24" states its price in the number, and "it comes with" states its fact in what follows.
        "costs", "cost", "coming", "gets", "goes", "going", "includes", "include", "including", "keeps", "keep", "lets", "help", "helps", "gives", "give",
        "takes", "find", "finds", "looks", "looking", "ready", "would", "could", "should", "aren", "isn", "doesn", "didn", "wasn", "weren", "wont", "cant",
        // An email's own scaffolding.
        "subject", "preview" };

    /// <summary>A blank a repair may put in place of a claim: brackets around a short name, and nothing that could state a fact of its own.</summary>
    internal static bool IsBlank(string text) => text.Length is >= 3 and <= 42 && text[0] == '[' && text[^1] == ']' &&
        text[1..^1].All(character => char.IsLetter(character) || character is ' ' or '-' or '/') && text[1..^1].Trim().Length > 0;

    /// <summary>Applies a repair: each unsupported passage becomes the named blank it was given, or is cut. Nothing else in the body changes,
    /// and a replacement that isn't a blank is ignored, so a repair can't add a claim of its own.</summary>
    internal static string ApplyBlanks(string body, JsonElement repair, IReadOnlyCollection<string> unsupported)
    {
        if (!repair.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) return body;
        foreach (var change in changes.EnumerateArray().Where(change => change.ValueKind == JsonValueKind.Object))
        {
            var text = Str(change, "text").Trim(); var with = Str(change, "with").Trim();
            if (text.Length == 0 || !unsupported.Contains(text) || !(with.Length == 0 || IsBlank(with)) || !body.Contains(text, StringComparison.Ordinal)) continue;
            body = body.Replace(text, with, StringComparison.Ordinal);
        }
        // A cut can leave "  " or " ." behind.
        while (body.Contains("  ", StringComparison.Ordinal)) body = body.Replace("  ", " ", StringComparison.Ordinal);
        return body.Replace(" .", ".", StringComparison.Ordinal).Replace(" ,", ",", StringComparison.Ordinal);
    }

    /// <summary>Whether every word of the claim that could carry a fact (four letters or more, not filler) begins a word the owner gave:
    /// "our new walnut desk" gives "the walnut desk is here" but not "a solid walnut top" or "our standing frame".</summary>
    internal static bool Given(string claim, string facts)
    {
        var known = Plain(facts).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.Length >= 4).Select(word => word[..4]).ToHashSet(StringComparer.Ordinal);
        return Plain(claim).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 4 && !Filler.Contains(word) && !word.All(char.IsDigit)).All(word => known.Contains(word[..4]));
    }

    /// <summary>The model only finds the claims; code decides which stand, from the owner's own words: a claim stands when its words and
    /// its numbers are all in what the worker was given. A listed claim that isn't in the body is ignored.</summary>
    internal static string[] Unsupported(JsonElement audit, string body, string facts)
    {
        if (!audit.TryGetProperty("claims", out var claims) || claims.ValueKind != JsonValueKind.Array) return [];
        var plainBody = Plain(body); var given = Numbers(facts).ToHashSet();
        return [.. claims.EnumerateArray().Where(claim => claim.ValueKind == JsonValueKind.Object).Select(claim => Str(claim, "text").Trim())
            .Where(text => Plain(text).Length >= 3 && plainBody.Contains(Plain(text), StringComparison.Ordinal))
            .Where(text => !Given(text, facts) || Numbers(text).Any(number => !given.Contains(number)))
            .Distinct()];
    }

    /// <summary>Marks each claim still in the body, where it stands, so it can't be published until the owner confirms or removes it.</summary>
    internal static string MarkUnconfirmed(string body, IEnumerable<string> claims)
    {
        foreach (var claim in claims)
        {
            var at = body.IndexOf(claim, StringComparison.Ordinal);
            if (at >= 0 && !body[..at].EndsWith(Unconfirmed + " ", StringComparison.Ordinal)) body = body[..at] + Unconfirmed + " " + body[at..];
        }
        return body;
    }

    /// <summary>Public copy is checked against the facts it was given before the owner sees it: unsupported claims become blanks,
    /// and whatever the repair left in is marked. One listing turn, and one repair turn only when something is unsupported. The repair
    /// answers with replacements only, which code applies: short enough to fit the worker's answer, and unable to add anything.</summary>
    async Task<(JsonElement Reply, int Tokens, string? Note)> AuditFacts(string id, int number, JsonElement reply, JsonElement created, CancellationToken cancellation)
    {
        var body = Str(reply, "body");
        if (Str(reply, "deliverable") != "draft" || body.Trim().Length < 20) return (reply, 0, null);
        var facts = FactsGiven(created);
        var listed = await Model(id, number, "audit", JsonSerializer.SerializeToElement(new { facts, body }), AuditFormat, cancellation);
        if (listed.Json is not { } audit) return (reply, listed.Tokens, "The fact check didn't run" + (listed.Error is { Length: > 0 } why ? $" ({why.TrimEnd('.')})." : "."));
        var unsupported = Unsupported(audit, body, facts);
        if (unsupported.Length == 0) return (reply, listed.Tokens, "Fact check: every claim it could check is in what you gave.");
        var repair = await Model(id, number, "audit", JsonSerializer.SerializeToElement(new { body, unsupported }), BlankFormat, cancellation);
        var revised = repair.Json is { } made ? ApplyBlanks(body, made, unsupported) : body;
        // A claim the repair kept (or all of them, when it didn't answer) is marked where it stands.
        var left = unsupported.Where(claim => Plain(revised).Contains(Plain(claim), StringComparison.Ordinal)).ToArray();
        revised = MarkUnconfirmed(revised, left);
        var node = JsonNode.Parse(reply.GetRawText())!.AsObject();
        node["body"] = revised;
        // A series' parts are what gets saved: re-split them from the checked body.
        if (node["drafts"] is JsonArray parts)
        {
            var bodies = revised.Split(SeriesBreak);
            if (bodies.Length == parts.Count) for (var index = 0; index < parts.Count; index++) if (parts[index] is JsonObject part) part["body"] = bodies[index].Trim();
        }
        return (JsonSerializer.SerializeToElement(node), listed.Tokens + repair.Tokens,
            $"Fact check: {unsupported.Length} claim{(unsupported.Length == 1 ? "" : "s")} weren't in what you gave (" + string.Join("; ", unsupported.Take(4).Select(claim => "“" + (claim.Length > 60 ? claim[..60] + "…" : claim) + "”")) + ")" +
            (left.Length == 0 ? "; they became blanks for you to fill in." : $"; {left.Length} marked {Unconfirmed} for you to confirm or cut."));
    }
}
