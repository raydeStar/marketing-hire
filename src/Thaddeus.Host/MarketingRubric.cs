using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record RubricCategory(string Key, string Name, string Asks);
public record RubricSettings(string[] Focus, string UpdatedBy, DateTimeOffset UpdatedAt);
public record RubricChange(string[]? Focus);

/// <summary>The marketing rubric every piece of work is graded on before the owner sees it: eight categories, each A to F.
/// The owner can raise up to three: the reviewer holds them to a higher standard, they count double in the overall grade,
/// and a piece isn't done until they reach a B.</summary>
public sealed class MarketingRubric(Store store)
{
    private const string Key = "marketing-rubric-v1";
    public const int MaxFocus = 3, FocusBar = 4;
    public static readonly RubricCategory[] Categories =
    [
        new("strategy", "Strategy", "Visibly serves the north star or an objective"),
        new("customer", "Audience insight", "Rests on a real customer truth from the brief or sources"),
        new("distinctive", "Distinctive", "Only this company could say it"),
        new("channel", "Channel fit", "Native to its channel, or fit for purpose as a document"),
        new("brand", "Brand voice", "Sounds like the brief's voice"),
        new("action", "Call to action", "One clear next step"),
        new("claims", "Proof", "Every claim defensible from the proof points or sources; nothing invented"),
        new("shareable", "Shareability", "Someone would pass it on")
    ];

    public RubricSettings Current() { lock (store) return store.Setting(Key) is { } json ? Wire.Unpack<RubricSettings>(json) : new([], "", DateTimeOffset.MinValue); }

    public RubricSettings Save(RubricChange change, string author)
    {
        var focus = (change.Focus ?? []).Select(item => item.Trim().ToLowerInvariant()).Distinct().ToArray();
        if (focus.Any(item => Categories.All(category => category.Key != item))) throw new ArgumentException("That isn't a rubric category.");
        if (focus.Length > MaxFocus) throw new ArgumentException($"Raise up to {MaxFocus} categories at a time; focus is what makes it work.");
        var saved = new RubricSettings([.. Categories.Select(category => category.Key).Where(focus.Contains)], author, DateTimeOffset.UtcNow);
        lock (store) store.Setting(Key, Wire.Pack(saved));
        return saved;
    }

    public static string Name(string key) => Categories.FirstOrDefault(category => category.Key == key)?.Name ?? key;
    public static string Grade(double score) => score >= 4.5 ? "A" : score >= 3.5 ? "B" : score >= 2.5 ? "C" : score >= 1.5 ? "D" : "F";

    /// <summary>The overall score: the categories being raised count double.</summary>
    public double Overall(IReadOnlyDictionary<string, int> scores)
    {
        if (scores.Count == 0) return 0;
        var focus = Current().Focus;
        double total = 0, weight = 0;
        foreach (var (key, score) in scores) { var w = focus.Contains(key) ? 2 : 1; total += score * w; weight += w; }
        return total / weight;
    }

    /// <summary>A piece is done when its overall grade meets the bar and every raised category reaches a B.</summary>
    public bool Meets(IReadOnlyDictionary<string, int> scores, double bar) =>
        Overall(scores) >= bar && Current().Focus.All(key => !scores.TryGetValue(key, out var score) || score >= FocusBar);

    /// <summary>"Strategy B, Audience insight A, …" in rubric order.</summary>
    public static string Line(IReadOnlyDictionary<string, int> scores) =>
        string.Join(", ", Categories.Where(category => scores.ContainsKey(category.Key)).Select(category => $"{category.Name} {Grade(scores[category.Key])}"));

    /// <summary>What the reviewer reads about the categories the owner is raising.</summary>
    public string? ReviewerNote() => Current().Focus is { Length: > 0 } focus
        ? $"The owner is raising {string.Join(", ", focus.Select(Name))}: hold them to a higher standard (a 4 there is what a 3 is elsewhere) and revise to lift them first."
        : null;
}
