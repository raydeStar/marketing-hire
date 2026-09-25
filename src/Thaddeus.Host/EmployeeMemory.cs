using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record FeedbackEntry(string Key, string Title, string Verdict, string Note, string By, DateTimeOffset At);
public record FeedbackRequest(string Key, string? Title, string Verdict, string? Note);
public record NotebookState(string[] Known, string[] Decided, string[] OpenQuestions, string[] Worked, string[] DidNotWork, string? WikiId, int WikiVersion, DateTimeOffset UpdatedAt);

/// <summary>What the employee learns from the owner and from its own shifts: the owner's verdicts on its work,
/// and a Marketing notebook it keeps (what's known, decided, open, what worked and what didn't).</summary>
public sealed class EmployeeMemory(Store store, CompanyWiki wiki, WorkspaceLibrary library)
{
    private const string FeedbackKey = "employee-feedback-v1", NotebookKey = "employee-notebook-v1";
    const string Author = "Marketing employee (shift)";
    public static readonly NotebookState EmptyNotebook = new([], [], [], [], [], null, 0, DateTimeOffset.MinValue);

    public FeedbackEntry[] Feedback() { lock (store) return store.Setting(FeedbackKey) is { } json ? Wire.Unpack<FeedbackEntry[]>(json) : []; }

    public FeedbackEntry Record(FeedbackRequest request, string author)
    {
        if (request.Key is not { Length: > 0 and <= 140 } key || !System.Text.RegularExpressions.Regex.IsMatch(key, "^(wiki|draft|page|task):[A-Za-z0-9_.-]{1,100}$"))
            throw new ArgumentException("Say which item the feedback is about.");
        if (request.Verdict is not ("useful" or "not_useful" or "approved" or "rejected")) throw new ArgumentException("Choose useful or not useful.");
        var note = (request.Note ?? "").Trim();
        if (note.Length > 600) throw new ArgumentException("Keep the note under 600 characters.");
        var title = (request.Title ?? "").Trim();
        var entry = new FeedbackEntry(key, title.Length > 160 ? title[..160] : title, request.Verdict, note, author, DateTimeOffset.UtcNow);
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

    /// <summary>Fold one shift's notes into the notebook and publish it to the Library.</summary>
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
                Known = Merge(state.Known, known), Decided = Merge(state.Decided, decided),
                OpenQuestions = Merge(state.OpenQuestions.Where(item => !closed.Contains(item)).ToArray(), open),
                Worked = Merge(state.Worked, worked), DidNotWork = Merge(state.DidNotWork, didNot), UpdatedAt = DateTimeOffset.UtcNow
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
            feedback = Feedback().OrderByDescending(item => item.At).Take(12).Select(item => new { item.Title, verdict = item.Verdict.Replace('_', ' '), item.Note, when = item.At.ToString("yyyy-MM-dd") }),
            notebook = text.Length > 3000 ? text[..3000] : text
        };
    }
}
