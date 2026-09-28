namespace Thaddeus.Host;

/// <param name="Unfinished">It still lacks some of what was asked, so it is opened and sent back rather than approved in one tap.</param>
public record AttentionItem(string Id, string Kind, string Title, string Detail, string Target, bool Unfinished = false);

/// <summary>What waits on the owner beyond drafts and tasks: page copy to decide, experiments the employee proposed, documents
/// it sent for review, and a shift that has stalled (paused, or failing to reach the model). The Inbox, its badge and the desktop
/// notice read this beside the drafts, so nothing the employee sends the owner goes unseen.</summary>
public sealed class OwnerAttention(PageProposals pages, Scorecard scorecard, CompanyWiki wiki, EmployeeShifts shifts)
{
    static string Clip(string text, int length = 140) { text = text.ReplaceLineEndings(" ").Trim(); return text.Length > length ? text[..(length - 1)].TrimEnd() + "…" : text; }

    public AttentionItem[] Items()
    {
        var items = new List<AttentionItem>();
        var history = shifts.History();
        // What a shift made for a task is decided through that task, which the Inbox already shows.
        var viaTask = history.TakeLast(10).SelectMany(shift => shift.Handled).Where(item => item.StartsWith("link:", StringComparison.Ordinal)).Select(item => item.Split(':'))
            .Where(parts => parts.Length == 4).Select(parts => (parts[1] == "expstart" ? "exp" : parts[1]) + ":" + parts[2]).ToHashSet();
        if (history.LastOrDefault() is { } last)
        {
            if (last.Status == "paused")
                items.Add(new("shift:" + last.Id, "shift", "The shift is paused", last.StopReason is { Length: > 0 } why ? Clip(why) : "Resume it, or stop it and the next working day starts fresh.", "section:shifts"));
            else if (last.Status == "running" && Stalled(last) is { } error)
                items.Add(new("shift:" + last.Id + ":stalled", "shift", "The employee's last two turns failed", Clip(error), "section:shifts"));
        }
        foreach (var proposal in pages.List().Where(item => item.Status == "pending" && !viaTask.Contains("pagecopy:" + item.Id)).OrderByDescending(item => item.CreatedAt).Take(10))
            items.Add(new("pagecopy:" + proposal.Id, "page", "New copy for " + PageWatch.Short(proposal.Url), Clip(proposal.Rationale is { Length: > 0 } rationale ? rationale : proposal.After), "pagecopy:" + proposal.Id));
        // Approved copy still has to reach the site; it stays on the list until the owner marks it applied (for three weeks).
        foreach (var proposal in pages.List().Where(item => item.Status == "approved" && (item.DecidedAt ?? item.CreatedAt) > DateTimeOffset.UtcNow.AddDays(-21)).Take(5))
            items.Add(new("pagecopy:" + proposal.Id + ":apply", "page", "Put the approved copy on " + PageWatch.Short(proposal.Url), "Approved. Copy it onto the page (or save it as a draft on a connected site), then mark it applied.", "pagecopy:" + proposal.Id));
        foreach (var experiment in scorecard.Ledger().Experiments.Where(item => item.Status == "proposed" && !viaTask.Contains("exp:" + item.Id)).TakeLast(10))
            items.Add(new("exp:" + experiment.Id, "experiment", "Proposed experiment: " + experiment.Title, Clip(experiment.Hypothesis), "section:scorecard"));
        // Documents a shift sent for review that are still drafts: the owner publishes or archives each in the Library.
        var drafts = wiki.List().Where(page => page.Status == "draft").ToDictionary(page => page.Id);
        var asked = history.TakeLast(10).SelectMany(shift => shift.Decisions)
            .Where(key => key.StartsWith("wiki:", StringComparison.Ordinal) && key.Contains(" Review: ", StringComparison.Ordinal))
            .Select(key => key[5..].Split(' ', 2)[0]).Distinct();
        foreach (var id in asked.Where(id => drafts.ContainsKey(id) && !viaTask.Contains("wiki:" + id)).TakeLast(10))
            items.Add(new("wiki:" + id, "document", "Review: " + drafts[id].Title, "Read it, then publish it in the Library or send it back with a note.", "wiki:" + id));
        return [.. items];
    }

    /// <summary>The last two model stages both failed on reaching the model: the owner should know before a whole day goes by.</summary>
    static string? Stalled(EmployeeShift shift)
    {
        var model = shift.Cycles.SelectMany(cycle => cycle.Stages).Where(stage => stage.Stage is "prioritize" or "create").TakeLast(2).ToArray();
        return model.Length == 2 && model.All(stage => stage.Status == "failed") && model.Any(stage => !stage.Summary.StartsWith("The plan was rejected", StringComparison.Ordinal))
            ? model[^1].Summary : null;
    }
}
