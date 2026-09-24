using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record WikiRevision(string Id, int Version, string Scope, string ScopeId, string Title, string Body,
    string Kind, string Status, string Digest, string Author, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public record WikiChange(string RequestId, string? Id, int Version, string Scope, string ScopeId,
    string Title, string Body, string Kind, string Status);
public record WikiReceipt(string RequestId, string Digest, string Id, int Version);
public record WikiLedger(WikiRevision[] Revisions, WikiReceipt[] Receipts);
public record WikiContextPage(string Id, int Version, string Digest, string Title, string Kind, string Scope, string ScopeId);
public record WikiContextSnapshot(string MemberId, string Role, DateTimeOffset CapturedAt, WikiContextPage[] Pages);

/// <summary>Owner-published pages are pinned by revision before an agent can read them.</summary>
public sealed class CompanyWiki(Store store, OrganizationDirectory directory)
{
    private const string Key = "company-wiki-v1";
    private WikiLedger Read() => store.Setting(Key) is { } json ? Wire.Unpack<WikiLedger>(json) : new([], []);
    private void Write(WikiLedger value) => store.Setting(Key, Wire.Pack(value));
    public WikiRevision[] List()
    {
        lock (store) return Read().Revisions.GroupBy(page => page.Id).Select(group => group.MaxBy(page => page.Version)!)
            .OrderBy(page => page.Scope).ThenBy(page => page.ScopeId).ThenBy(page => page.Title).ToArray();
    }
    public WikiRevision[] History(string id)
    {
        lock (store) return Read().Revisions.Where(page => page.Id == id).OrderByDescending(page => page.Version).ToArray();
    }
    public WikiRevision Save(WikiChange change, string author)
    {
        lock (store)
        {
            if (string.IsNullOrWhiteSpace(change.RequestId) || change.RequestId.Length > 120) throw new ArgumentException("A request ID is required.");
            var ledger = Read(); var digest = Wire.Hash(Wire.Pack(change));
            if (ledger.Receipts.FirstOrDefault(receipt => receipt.RequestId == change.RequestId) is { } replay)
            {
                if (replay.Digest != digest) throw new ArgumentException("That request ID belongs to another wiki change.");
                return ledger.Revisions.Single(page => page.Id == replay.Id && page.Version == replay.Version);
            }
            if (change.Scope is not ("company" or "department" or "member") ||
                change.Kind is not ("fact" or "policy" or "hypothesis" or "question") ||
                change.Status is not ("draft" or "active" or "archived") ||
                string.IsNullOrWhiteSpace(change.Title) || change.Title.Length > 160 ||
                string.IsNullOrWhiteSpace(change.Body) || change.Body.Length > 12000)
                throw new ArgumentException("A wiki page needs a valid layer, type, status, title, and body.");
            var team = directory.Read();
            if (change.Scope == "company" && change.ScopeId != "company" ||
                change.Scope == "department" && !team.Departments.Any(item => item.Id == change.ScopeId) ||
                change.Scope == "member" && change.ScopeId != "ceo" && !team.Agents.Any(item => item.Id == change.ScopeId))
                throw new ArgumentException("The wiki page must belong to an existing company, department, or member.");
            var previous = change.Id == null ? null : ledger.Revisions.Where(page => page.Id == change.Id).MaxBy(page => page.Version);
            if (change.Id != null && previous == null) throw new ArgumentException("Wiki page not found.");
            if (change.Version != (previous?.Version ?? 0)) throw new InvalidOperationException("The wiki page changed. Refresh before saving.");
            if (previous != null && (previous.Scope != change.Scope || previous.ScopeId != change.ScopeId))
                throw new InvalidOperationException("A page cannot change its access layer. Create a new page in that layer.");
            var now = DateTimeOffset.UtcNow;
            var page = new WikiRevision(previous?.Id ?? Guid.NewGuid().ToString("N"), change.Version + 1, change.Scope,
                change.ScopeId, change.Title.Trim(), change.Body.Trim(), change.Kind, change.Status,
                Wire.Hash(change.Body.Trim()), author, previous?.CreatedAt ?? now, now);
            Write(ledger with { Revisions = [.. ledger.Revisions, page], Receipts = [.. ledger.Receipts.TakeLast(255), new(change.RequestId, digest, page.Id, page.Version)] });
            return page;
        }
    }
    private static bool CanRead(WikiRevision page, string memberId, CompanyDirectory team)
    {
        if (page.Scope == "company") return true;
        if (page.Scope == "member") return page.ScopeId == memberId;
        return team.Agents.Any(agent => agent.Id == memberId && agent.RuntimeKey != null && agent.DepartmentId == page.ScopeId);
    }
    public WikiContextSnapshot Capture(string memberId, string role, string agenda)
    {
        lock (store)
        {
            var team = directory.Read();
            if (memberId != "ceo" && !team.Agents.Any(agent => agent.Id == memberId && agent.RuntimeKey != null))
                throw new InvalidOperationException("A disconnected member cannot receive wiki context.");
            var terms = agenda.Split([' ', '\n', '\t', ',', '.', ':', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Where(word => word.Length >= 4).Select(word => word.ToLowerInvariant()).Distinct().ToArray();
            var pages = Read().Revisions.GroupBy(page => page.Id).Select(group => group.MaxBy(page => page.Version)!)
                .Where(page => page.Status == "active" && CanRead(page, memberId, team))
                .OrderByDescending(page => terms.Count(term => page.Title.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ThenBy(page => page.Scope == "company" ? 0 : page.Scope == "department" ? 1 : 2)
                .ThenBy(page => page.Title).Take(12)
                .Select(page => new WikiContextPage(page.Id, page.Version, page.Digest, page.Title, page.Kind, page.Scope, page.ScopeId)).ToArray();
            return new(memberId, role, DateTimeOffset.UtcNow, pages);
        }
    }
    public string Render(WikiContextSnapshot? snapshot)
    {
        if (snapshot == null) return "No wiki snapshot was captured for this older meeting.";
        lock (store)
        {
            var revisions = Read().Revisions;
            var lines = new List<string>(); var size = 0;
            foreach (var reference in snapshot.Pages)
            {
                var page = revisions.SingleOrDefault(item => item.Id == reference.Id && item.Version == reference.Version);
                if (page == null || page.Digest != reference.Digest) throw new InvalidOperationException("A pinned wiki revision is missing or changed.");
                if (size + page.Body.Length > 12000) break;
                lines.Add($"[{page.Title} v{page.Version}; {page.Kind}; {page.Scope}]\n{page.Body}"); size += page.Body.Length;
            }
            return lines.Count == 0 ? "No published wiki pages were available for this member at meeting start. Ask for missing facts." : string.Join("\n\n", lines);
        }
    }
}
