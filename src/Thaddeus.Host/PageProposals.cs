using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record PageProposal(string Id, string Url, string Title, string Before, string After, string Rationale, string Status, DateTimeOffset CreatedAt,
    string By, DateTimeOffset? DecidedAt = null, string? DecidedBy = null, string? Note = null, string? AppliedUrl = null, DateTimeOffset? AppliedAt = null);
public record PageDecision(string Decision, string? Note);
public record PageApplied(string? Url);
public record PageToWordPress(string ConnectionId);

/// <summary>Proposed new copy for a page on the owner's own site: what the page says now (read from the live page), what the
/// employee proposes, and why. The owner approves or rejects it; an approved proposal is applied by the owner (copied into
/// their site, or saved as a WordPress draft page). Nothing here ever changes the live site.</summary>
public sealed class PageProposals(Store store, CompanyObjectives objectives)
{
    const string Key = "page-proposals-v1";
    PageProposal[] Read() => store.Setting(Key) is { } json ? Wire.Unpack<PageProposal[]>(json) : [];
    void Write(PageProposal[] all) => store.Setting(Key, Wire.Pack(all.OrderByDescending(item => item.CreatedAt).Take(100).ToArray()));
    public PageProposal[] List() { lock (store) return Read(); }
    public PageProposal? Find(string id) => List().FirstOrDefault(item => item.Id == id);

    /// <summary>The owner's own site, as set in Objectives; proposals are only for its pages.</summary>
    public string? OwnSite() => objectives.Current().Content.OwnSite;

    public PageProposal Propose(string url, string title, string before, string after, string rationale, string by)
    {
        var own = OwnSite() ?? throw new InvalidOperationException("Set your own site in Objectives & positioning first; page proposals are only for your site.");
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var page) || !SiteReader.Allowed(page, [own])) throw new InvalidOperationException($"A page proposal must be for a page on {own}.");
        if (after.Trim().Length is < 40 or > 30000) throw new InvalidOperationException("The proposed copy is too short or too long.");
        var proposal = new PageProposal(Guid.NewGuid().ToString("N")[..16], page.AbsoluteUri, title.Trim() is { Length: > 0 and <= 160 } named ? named : page.AbsolutePath,
            before.Length > 4000 ? before[..4000] : before, after.Trim(), rationale.Length > 1500 ? rationale[..1500] : rationale, "pending", DateTimeOffset.UtcNow, by);
        lock (store)
        {
            var all = Read();
            // The same proposal twice (a retried turn) is one proposal.
            if (all.FirstOrDefault(item => item.Status == "pending" && item.Url == proposal.Url && item.After == proposal.After) is { } same) return same;
            Write([.. all, proposal]);
        }
        return proposal;
    }

    public PageProposal Decide(string id, PageDecision decision, string actor)
    {
        if (decision.Decision is not ("approved" or "rejected")) throw new ArgumentException("Approve or reject the proposal.");
        return Change(id, item => item.Status != "pending" ? throw new InvalidOperationException("This proposal was already decided.")
            : item with { Status = decision.Decision, DecidedAt = DateTimeOffset.UtcNow, DecidedBy = actor, Note = decision.Note?.Trim() is { Length: > 0 } note ? (note.Length > 600 ? note[..600] : note) : null });
    }

    public PageProposal MarkApplied(string id, string? url) => Change(id, item => item.Status is not ("approved" or "applied") ? throw new InvalidOperationException("Approve the proposal before applying it.")
        : item with { Status = "applied", AppliedAt = DateTimeOffset.UtcNow, AppliedUrl = url is { Length: > 0 } link && Uri.TryCreate(link, UriKind.Absolute, out var target) && (target.Scheme == "https" || target.Scheme == "http" && target.IsLoopback) ? target.AbsoluteUri : item.AppliedUrl });

    PageProposal Change(string id, Func<PageProposal, PageProposal> change)
    {
        lock (store)
        {
            var all = Read();
            var index = Array.FindIndex(all, item => item.Id == id);
            if (index < 0) throw new KeyNotFoundException("That proposal doesn't exist.");
            all[index] = change(all[index]);
            Write(all);
            return all[index];
        }
    }
}
