using System.Text.Json;

namespace Thaddeus.Host;

public record CheckItem(int DraftId, string? Asked, int? OriginalId = null);
public record CheckRequest(CheckItem[] Items);

/// <summary>A second opinion on a post, from a reviewer that didn't write it and never sees its grade: would a demanding editor
/// publish it as written? Every problem it names must quote the post, or it is set aside, so a check can't invent faults.
/// Run on demand inside a shift's metered allowance, to measure what it catches and what it costs before it joins the review.</summary>
public sealed partial class EmployeeShifts
{
    const string CheckFormat =
        "You are an independent editor checking one post before the owner publishes it. You did not write it and have not seen anyone's grade for it. " +
        "Judge it as a demanding marketing editor would: would you publish it exactly as written? Check every factual claim against facts and voice.stories (a number, an event or a claim that isn't there, or is said differently, is a problem); " +
        "who did what (a mistake the product or its AI employee made is its own; when the business speaks of itself it uses the voice guide's person, while describing the product in the third person, \"HireZero works shifts\", is fine); sentences a reader could misread or that read awkwardly; saying the same thing twice; " +
        "an opening that doesn't earn the next line; and asked, when given (the assignment or the owner's notes): every part of it done, where original, when given, is the version the owner sent back (so a note to keep something is checked against it). Matters of taste you would publish anyway are not problems. " +
        "Answer ONE JSON object: {\"verdict\":\"A\"|\"not A\",\"problems\":[{\"quote\":\"the exact words from the post\",\"problem\":\"what is wrong, in one sentence\"}],\"missing\":[\"something asked for that the post lacks\"]}. " +
        "verdict is A only when problems and missing are both empty.";

    public async Task<object[]> IndependentCheck(string shiftId, CheckRequest request, CancellationToken cancellation)
    {
        var shift = Find(shiftId) ?? throw new KeyNotFoundException("That shift doesn't exist.");
        if (shift.Status != "running") throw new InvalidOperationException("A check runs inside a running shift's allowance.");
        if (request.Items is not { Length: > 0 and <= 12 } items) throw new ArgumentException("Check 1 to 12 drafts.");
        var work = (await marketing.ShiftHire(null, "snapshot")).Value ?? throw new InvalidOperationException("The work ledger didn't answer.");
        var results = new List<object>();
        foreach (var item in items)
        {
            var draft = work.GetProperty("drafts").EnumerateArray().FirstOrDefault(entry => Num(entry, "id") == item.DraftId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (draft.ValueKind != JsonValueKind.Object) { results.Add(new { item.DraftId, error = "not found" }); continue; }
            var body = WithoutImageLine(Str(draft, "content"));
            var data = JsonSerializer.SerializeToElement(new
            {
                post = new { channel = Str(draft, "channel"), destination = Str(draft, "destination"), body },
                asked = item.Asked is { Length: > 0 } asked ? asked : null,
                original = item.OriginalId is { } before && work.GetProperty("drafts").EnumerateArray().FirstOrDefault(entry => Num(entry, "id") == before.ToString(System.Globalization.CultureInfo.InvariantCulture)) is { ValueKind: JsonValueKind.Object } sent ? WithoutImageLine(Str(sent, "content")) : null,
                facts = CompanyFacts(), voice = Voice(work, Str(draft, "channel")), callToAction = objectives.Current().Content.CallToAction
            });
            var turn = await Model(shiftId, 0, "check", data, CheckFormat, cancellation, keep: ["post"]);
            if (turn.Json is not { } json) { results.Add(new { item.DraftId, error = turn.Busy ? "busy" : turn.Error, tokens = turn.Tokens }); continue; }
            var problems = json.TryGetProperty("problems", out var listed) && listed.ValueKind == JsonValueKind.Array ? listed.EnumerateArray().Where(entry => entry.ValueKind == JsonValueKind.Object).ToArray() : [];
            // A problem has to point at the words it is about; one that quotes nothing in the post is set aside.
            var grounded = problems.Where(entry => SpecCheck.Quotes(body, Str(entry, "quote"))).Select(entry => new { quote = Str(entry, "quote"), problem = Str(entry, "problem") }).ToArray();
            var missing = json.TryGetProperty("missing", out var absent) && absent.ValueKind == JsonValueKind.Array ? absent.EnumerateArray().Where(entry => entry.ValueKind == JsonValueKind.String).Select(entry => entry.GetString()!).ToArray() : [];
            results.Add(new { item.DraftId, channel = Str(draft, "channel"), verdict = grounded.Length + missing.Length == 0 ? "A" : "not A", said = Str(json, "verdict"),
                problems = grounded, missing, setAside = problems.Length - grounded.Length, tokens = turn.Tokens });
        }
        return [.. results];
    }
}
