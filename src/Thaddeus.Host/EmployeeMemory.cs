using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record FeedbackEntry(string Key, string Title, string Verdict, string Note, string By, DateTimeOffset At, int? MinutesSaved = null);
public record FeedbackRequest(string Key, string? Title, string Verdict, string? Note, int? MinutesSaved = null);
public record QualityEntry(DateTimeOffset At, string Title, string Type, string Channel, Dictionary<string, int> Scores, int Passes, double First, string[]? Keys = null, string[]? Issues = null, string? Assignment = null);
public record NotebookState(string[] Known, string[] Decided, string[] OpenQuestions, string[] Worked, string[] DidNotWork, string? WikiId, int WikiVersion, DateTimeOffset UpdatedAt);

/// <summary>What the employee learns from the owner and from its own shifts: the owner's verdicts on its work,
/// and a Marketing notebook it keeps (what's known, decided, open, what worked and what didn't).</summary>
public sealed class EmployeeMemory(Store store, CompanyWiki wiki, WorkspaceLibrary library)
{
    private const string FeedbackKey = "employee-feedback-v1", NotebookKey = "employee-notebook-v1", QualityKey = "employee-quality-v1";

    public QualityEntry[] Quality() { lock (store) return store.Setting(QualityKey) is { } json ? Wire.Unpack<QualityEntry[]>(json) : []; }

    /// <summary>One deliverable's final self-review: its score per rubric item, how many passes it took, and where it started.</summary>
    /// <summary>Which items a grade belongs to ("draft:12", "wiki:…"), once the work is saved: the latest ungraded entry with this title.</summary>
    public void KeyQuality(string title, string[] keys)
    {
        if (keys.Length == 0) return;
        title = title.Length > 160 ? title[..160] : title;
        lock (store)
        {
            var entries = Quality();
            var at = Array.FindLastIndex(entries, entry => entry.Title == title && entry.Keys == null);
            if (at < 0 || at < entries.Length - 10) return;
            entries[at] = entries[at] with { Keys = keys };
            store.Setting(QualityKey, Wire.Pack(entries));
        }
    }

    const string AssignmentsKey = "work-assignments-v1";

    /// <summary>What each piece of work was asked to be (its task's title and instructions), kept by its key so a later review can
    /// check it against the assignment even after the task's own text has moved on.</summary>
    public void RecordAssignment(IEnumerable<string> keys, string assignment)
    {
        if (string.IsNullOrWhiteSpace(assignment)) return;
        assignment = assignment.Length > 1200 ? assignment[..1200] : assignment;
        lock (store)
        {
            var all = store.Setting(AssignmentsKey) is { } json ? Wire.Unpack<Dictionary<string, string>>(json) : [];
            foreach (var key in keys.Where(key => key.Length is > 0 and <= 120)) { all.Remove(key); all[key] = assignment; }
            store.Setting(AssignmentsKey, Wire.Pack(all.Count > 300 ? all.Skip(all.Count - 300).ToDictionary() : all));
        }
    }

    public string? Assignment(string key) { lock (store) return store.Setting(AssignmentsKey) is { } json && Wire.Unpack<Dictionary<string, string>>(json).TryGetValue(key, out var text) ? text : null; }

    public void RecordQuality(string title, string type, string channel, Dictionary<string, int> scores, int passes, double first, string[]? issues = null, string? assignment = null)
    {
        lock (store)
        {
            var entries = Quality().TakeLast(299).Append(new QualityEntry(DateTimeOffset.UtcNow, title.Length > 160 ? title[..160] : title, type, channel, scores, passes, Math.Round(first, 2), null, issues is { Length: > 0 } ? issues : null, assignment is { Length: > 0 } asked ? (asked.Length > 600 ? asked[..600] : asked) : null)).ToArray();
            store.Setting(QualityKey, Wire.Pack(entries));
        }
    }

    /// <summary>The last twenty reviews: the average, the three weakest rubric items, and how much revising lifted the score.</summary>
    public object QualitySummary(int take = 20)
    {
        var recent = Quality().TakeLast(take).ToArray();
        if (recent.Length == 0) return new { reviewed = 0 };
        var items = recent.SelectMany(entry => entry.Scores).GroupBy(pair => pair.Key).Select(group => new { item = group.Key, average = Math.Round(group.Average(pair => pair.Value), 1) }).OrderBy(item => item.average).ToArray();
        return new
        {
            reviewed = recent.Length,
            average = Math.Round(recent.Average(entry => entry.Scores.Values.Average()), 2),
            firstDraft = Math.Round(recent.Average(entry => entry.First), 2),
            weakest = items.Take(3), strongest = items.Reverse().Take(2)
        };
    }
    const string Author = "Marketing employee (shift)";
    public static readonly NotebookState EmptyNotebook = new([], [], [], [], [], null, 0, DateTimeOffset.MinValue);

    public FeedbackEntry[] Feedback() { lock (store) return store.Setting(FeedbackKey) is { } json ? Wire.Unpack<FeedbackEntry[]>(json) : []; }

    public FeedbackEntry Record(FeedbackRequest request, string author)
    {
        if (request.Key is not { Length: > 0 and <= 140 } key || !System.Text.RegularExpressions.Regex.IsMatch(key, "^(wiki|draft|page|pagecopy|task):[A-Za-z0-9_.-]{1,100}$"))
            throw new ArgumentException("Say which item the feedback is about.");
        if (request.Verdict is not ("useful" or "not_useful" or "approved" or "rejected" or "redraft")) throw new ArgumentException("Choose useful or not useful.");
        var note = (request.Note ?? "").Trim();
        if (note.Length > 600) throw new ArgumentException("Keep the note under 600 characters.");
        var title = (request.Title ?? "").Trim();
        if (request.MinutesSaved is < 0 or > 1440) throw new ArgumentException("Reported time saved must be between 0 and 1,440 minutes.");
        var entry = new FeedbackEntry(key, title.Length > 160 ? title[..160] : title, request.Verdict, note, author, DateTimeOffset.UtcNow, request.Verdict == "useful" ? request.MinutesSaved : null);
        lock (store)
        {
            // The latest verdict on an item replaces the earlier one.
            var entries = Feedback().Where(item => item.Key != key).Append(entry).TakeLast(200).ToArray();
            store.Setting(FeedbackKey, Wire.Pack(entries));
        }
        return entry;
    }

    public NotebookState Notebook() { lock (store) return store.Setting(NotebookKey) is { } json ? Wire.Unpack<NotebookState>(json) : EmptyNotebook; }

    static string[] Merge(string[] current, IEnumerable<string>? additions, int cap = 15)
    {
        var list = current.ToList();
        foreach (var raw in additions ?? [])
        {
            var item = raw.Trim();
            if (item.Length is < 3 or > 300 || list.Any(existing => string.Equals(existing, item, StringComparison.OrdinalIgnoreCase))) continue;
            list.Add(item);
        }
        return [.. list.TakeLast(cap)];
    }

    /// <summary>What belongs in a notebook: not a log of the shift ("draft #12 was approved", "seven drafts were created during the cycle").</summary>
    static IEnumerable<string>? Knowledge(IEnumerable<string>? items) => items?.Where(item => !System.Text.RegularExpressions.Regex.IsMatch(item,
        @"#\d+|\bdrafts? (was|were) (approved|rejected|created|drafted)|\bduring (the|this) (cycle|shift)\b|\b(this|the) shift (created|produced|drafted|wrote)|\b(was|were) (created|drafted|written)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

    /// <summary>Fold one shift's notes into the notebook and publish it to the Library.</summary>
    /// <summary>A notebook written before it kept to knowledge ("LinkedIn draft #40 was approved") loses those lines,
    /// unless someone other than the employee has edited the page.</summary>
    public bool Tidy()
    {
        lock (store)
        {
            var state = Notebook();
            if (state.WikiId == null || wiki.List().FirstOrDefault(page => page.Id == state.WikiId) is not { } current) return false;
            if (!wiki.History(current.Id).All(revision => revision.Author == Author)) return false;
            state = Parse(current.Body, state);
            var next = state with { Known = [.. Knowledge(state.Known)!], Decided = [.. Knowledge(state.Decided)!], Worked = [.. Knowledge(state.Worked)!], DidNotWork = [.. Knowledge(state.DidNotWork)!] };
            if (next.Known.Length + next.Decided.Length + next.Worked.Length + next.DidNotWork.Length == state.Known.Length + state.Decided.Length + state.Worked.Length + state.DidNotWork.Length) return false;
            var page = wiki.Save(new WikiChange(Guid.NewGuid().ToString("N"), current.Id, current.Version, current.Scope, current.ScopeId, "Marketing notebook", Render(next), "fact", "active"), Author);
            store.Setting(NotebookKey, Wire.Pack(next with { WikiId = page.Id, WikiVersion = page.Version }));
            return true;
        }
    }

    public NotebookState Update(IEnumerable<string>? known, IEnumerable<string>? decided, IEnumerable<string>? open, IEnumerable<string>? worked, IEnumerable<string>? didNot, IEnumerable<string>? resolved)
    {
        lock (store)
        {
            var state = Notebook();
            // The owner may have edited the page: their version of each list is the starting point.
            var current = state.WikiId != null ? wiki.List().FirstOrDefault(page => page.Id == state.WikiId) : null;
            if (current != null && current.Body != Render(state)) state = Parse(current.Body, state);
            var closed = (resolved ?? []).Select(item => item.Trim()).Where(item => item.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var next = state with
            {
                Known = Merge(state.Known, Knowledge(known)), Decided = Merge(state.Decided, Knowledge(decided)),
                OpenQuestions = Merge(state.OpenQuestions.Where(item => !closed.Contains(item)).ToArray(), open),
                Worked = Merge(state.Worked, Knowledge(worked)), DidNotWork = Merge(state.DidNotWork, Knowledge(didNot)), UpdatedAt = DateTimeOffset.UtcNow
            };
            var body = Render(next);
            var existing = next.WikiId != null ? wiki.List().FirstOrDefault(page => page.Id == next.WikiId) : null;
            var page = wiki.Save(new WikiChange(Guid.NewGuid().ToString("N"), existing?.Id, existing?.Version ?? 0, "company", "company", "Marketing notebook", body, "fact", "active"), Author);
            if (existing == null)
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    try { library.SaveEntry("wiki:" + page.Id, new LibraryEntryChange(library.View("").Version, "Company", ["notebook"]), Author, "employee"); break; }
                    catch (InvalidOperationException) when (attempt < 2) { }
                }
            next = next with { WikiId = page.Id, WikiVersion = page.Version };
            store.Setting(NotebookKey, Wire.Pack(next));
            return next;
        }
    }

    static readonly (string Heading, Func<NotebookState, string[], NotebookState> Set)[] Sections =
    [
        ("What we know", (state, items) => state with { Known = items }), ("Decided", (state, items) => state with { Decided = items }),
        ("Open questions", (state, items) => state with { OpenQuestions = items }), ("What worked", (state, items) => state with { Worked = items }),
        ("What didn't", (state, items) => state with { DidNotWork = items })
    ];

    /// <summary>Read the lists back from the page, section by section; a section the owner removed keeps the stored list.</summary>
    public static NotebookState Parse(string body, NotebookState fallback)
    {
        var state = fallback;
        var parts = System.Text.RegularExpressions.Regex.Split(body.Replace("\r\n", "\n"), @"^##\s+", System.Text.RegularExpressions.RegexOptions.Multiline);
        foreach (var part in parts.Skip(1))
        {
            var newline = part.IndexOf('\n');
            var heading = (newline < 0 ? part : part[..newline]).Trim();
            var section = Sections.FirstOrDefault(item => string.Equals(item.Heading, heading, StringComparison.OrdinalIgnoreCase));
            if (section.Set == null) continue;
            var items = (newline < 0 ? "" : part[(newline + 1)..]).Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
                .Select(line => line[2..].Trim()).Where(line => line.Length is >= 3 and <= 300).Take(15).ToArray();
            state = section.Set(state, items);
        }
        return state;
    }

    public static string Render(NotebookState state)
    {
        static string Section(string title, string[] items, string empty) => $"## {title}\n\n" + (items.Length == 0 ? $"_{empty}_\n" : string.Join("\n", items.Select(item => "- " + item)) + "\n");
        return "# Marketing notebook\n\n_Kept by the marketing employee at the end of each shift. Edit it freely; the employee reads it before every piece of work._\n\n" +
            Section("What we know", state.Known, "Nothing confirmed yet.") + "\n" + Section("Decided", state.Decided, "No decisions recorded.") + "\n" +
            Section("Open questions", state.OpenQuestions, "None.") + "\n" + Section("What worked", state.Worked, "Nothing yet.") + "\n" + Section("What didn't", state.DidNotWork, "Nothing yet.");
    }

    /// <summary>The same memory as plain text for chat: the latest verdicts with reasons, and the notebook page.</summary>
    public string ChatText()
    {
        var lines = Feedback().OrderByDescending(item => item.At).Take(6)
            .Select(item => $"- {item.Title}: {item.Verdict.Replace('_', ' ')}{(item.Note.Length > 0 ? " (" + item.Note + ")" : "")}").ToArray();
        var notebook = Notebook();
        var page = notebook.WikiId != null ? wiki.List().FirstOrDefault(item => item.Id == notebook.WikiId) : null;
        var text = (lines.Length > 0 ? "The owner's recent verdicts on your work:\n" + string.Join("\n", lines) + "\n" : "") +
            (page != null ? "Your Marketing notebook (the owner may have edited it):\n" + page.Body.Replace("# Marketing notebook", "").Trim() : "");
        return text.Length > 2000 ? text[..2000] : text;
    }

    /// <summary>The context a shift reads: recent verdicts with reasons, and the notebook (the owner's edits to the page win).</summary>
    public object Context()
    {
        var notebook = Notebook();
        var page = notebook.WikiId != null ? wiki.List().FirstOrDefault(item => item.Id == notebook.WikiId) : null;
        var text = page?.Body ?? Render(notebook);
        return new
        {
            feedback = Feedback().OrderByDescending(item => item.At).Take(12).Select(item => new { item.Key, item.Title, verdict = item.Verdict.Replace('_', ' '), item.Note, when = item.At.ToString("yyyy-MM-dd") }),
            quality = QualitySummary(),
            notebook = text.Length > 3000 ? text[..3000] : text
        };
    }
}
