using System.Globalization;
using System.Text.RegularExpressions;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record Campaign(string Id, string Name, string Goal, string? Starts, string? Ends, string[] Channels, string Status, string? Moves,
    string? PlanWikiId, string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public record CampaignChange(int ExpectedVersion, string? Name, string? Goal, string? Starts, string? Ends, string[]? Channels, string? Status, string? Moves);
public record CampaignAssign(int ExpectedVersion, string Key, string? CampaignId);
public record CampaignFromPlan(int ExpectedVersion, string WikiId);
public record CampaignLedger(int Version, Campaign[] Campaigns, Dictionary<string, string> Items);

/// <summary>Named campaigns (a goal, dates, channels, a status) and the work that belongs to each: tasks, drafts, documents and
/// media, by key. Work that belongs to none is always-on. A campaign's own files live in Library → Campaigns → its name.</summary>
public sealed partial class Campaigns(Store store, WorkspaceLibrary library, CompanyWiki wiki)
{
    private const string Key = "campaigns-v1";
    public static readonly string[] Statuses = ["planned", "active", "paused", "done"];
    public const int MaxCampaigns = 50, MaxItems = 5000;

    CampaignLedger Read() => store.Setting(Key) is { } json ? Wire.Unpack<CampaignLedger>(json) : new(0, [], []);
    public CampaignLedger View() { lock (store) return Read(); }
    public Campaign? Find(string? id) => id == null ? null : View().Campaigns.FirstOrDefault(campaign => campaign.Id == id);
    public string? Of(string key) => View().Items.GetValueOrDefault(key);
    /// <summary>What a shift may plan against: campaigns that are running or about to.</summary>
    public Campaign[] Open() => [.. View().Campaigns.Where(campaign => campaign.Status is "active" or "planned")];

    [GeneratedRegex(@"^(task|draft|wiki|media):[A-Za-z0-9_-]{1,80}$")] private static partial Regex ItemKey();

    static string Text(string? value, int limit, string field, bool required = false)
    {
        var text = Regex.Replace((value ?? "").Trim(), @"\s+", " ");
        if (required && text.Length == 0) throw new ArgumentException(field + " is required.");
        if (text.Length > limit) throw new ArgumentException($"{field} can be up to {limit} characters.");
        return text;
    }
    static string? Date(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? null : DateOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture, out var date) ? date.ToString("yyyy-MM-dd") : throw new ArgumentException(field + " must be a date.");

    /// <summary>Create (id null) or change a campaign. A renamed campaign's folder moves with it.</summary>
    public Campaign Save(string? id, CampaignChange change, string author)
    {
        var name = Text(change.Name, 60, "The campaign name", required: true).Replace('/', '-');
        if (name.Length < 2) throw new ArgumentException("Give the campaign a name of two characters or more.");
        // Its folder sits beside Campaigns → Videos, Drafts and the rest, so it can't share their names.
        if (new[] { "Blog", "Drafts", "Posts", "Emails", "Docs", "Videos", "Video", "Images", "Image" }.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"“{name}” is a Library folder name; call the campaign something else.");
        var goal = Text(change.Goal, 400, "The goal");
        var starts = Date(change.Starts, "The start"); var ends = Date(change.Ends, "The end");
        if (starts != null && ends != null && string.CompareOrdinal(ends, starts) < 0) throw new ArgumentException("The campaign can't end before it starts.");
        var channels = (change.Channels ?? []).Select(channel => Text(channel, 40, "A channel")).Where(channel => channel.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (channels.Length > 12) throw new ArgumentException("Up to 12 channels.");
        var status = change.Status is { Length: > 0 } given ? given.Trim().ToLowerInvariant() : "active";
        if (!Statuses.Contains(status)) throw new ArgumentException("The status is planned, active, paused or done.");
        var moves = Text(change.Moves, 120, "What it moves") is { Length: > 0 } moved ? moved : null;
        string? renamedFrom = null; Campaign saved;
        lock (store)
        {
            var ledger = Read();
            if (change.ExpectedVersion != ledger.Version) throw new InvalidOperationException("Campaigns changed. Refresh and try again.");
            if (ledger.Campaigns.Any(campaign => campaign.Id != id && string.Equals(campaign.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"There is already a campaign named “{name}”.");
            var now = DateTimeOffset.UtcNow;
            Campaign[] campaigns;
            if (id == null)
            {
                if (ledger.Campaigns.Length >= MaxCampaigns) throw new InvalidOperationException($"Up to {MaxCampaigns} campaigns; mark old ones done and remove them first.");
                saved = new Campaign(Guid.NewGuid().ToString("N")[..12], name, goal, starts, ends, channels, status, moves, null, author, now, now);
                campaigns = [.. ledger.Campaigns, saved];
            }
            else
            {
                var current = ledger.Campaigns.FirstOrDefault(campaign => campaign.Id == id) ?? throw new KeyNotFoundException("That campaign no longer exists.");
                if (current.Name != name) renamedFrom = current.Name;
                saved = current with { Name = name, Goal = goal, Starts = starts, Ends = ends, Channels = channels, Status = status, Moves = moves, UpdatedAt = now };
                campaigns = [.. ledger.Campaigns.Select(campaign => campaign.Id == id ? saved : campaign)];
            }
            store.Setting(Key, Wire.Pack(ledger with { Version = ledger.Version + 1, Campaigns = campaigns }));
        }
        if (renamedFrom != null) MoveFolder(Folder(renamedFrom), Folder(name), author);
        return saved;
    }

    /// <summary>Put an item in a campaign (or back to always-on with null). Files already under Library → Campaigns move into the campaign's folder.</summary>
    public void Assign(string key, string? campaignId, string author, int? expectedVersion = null, bool refile = true)
    {
        if (!ItemKey().IsMatch(key)) throw new ArgumentException("That isn't an item a campaign can hold.");
        Campaign? campaign;
        lock (store)
        {
            var ledger = Read();
            if (expectedVersion is { } expected && expected != ledger.Version) throw new InvalidOperationException("Campaigns changed. Refresh and try again.");
            campaign = campaignId == null ? null : ledger.Campaigns.FirstOrDefault(item => item.Id == campaignId) ?? throw new KeyNotFoundException("That campaign no longer exists.");
            var items = new Dictionary<string, string>(ledger.Items);
            if (campaign == null ? !items.Remove(key) : items.TryGetValue(key, out var was) && was == campaign.Id) return;
            if (campaign != null) items[key] = campaign.Id;
            if (items.Count > MaxItems) throw new InvalidOperationException("Campaigns hold up to 5,000 items.");
            store.Setting(Key, Wire.Pack(ledger with { Version = ledger.Version + 1, Items = items }));
        }
        if (refile) Refile(key, campaign, author);
    }

    /// <summary>The Library folder for a campaign's files.</summary>
    public static string Folder(string name) => "Campaigns/" + name.Replace('/', '-').Trim();

    /// <summary>Where a file goes when its campaign changes: Campaigns/Videos → Campaigns/Launch week/Videos, and back. Files outside
    /// Campaigns (research, reports) stay where they are; they're linked to the campaign, not moved.</summary>
    public static string? FolderFor(string? current, string? campaignName, IEnumerable<string> campaignNames, bool force = false)
    {
        string sub;
        if (current is { } folder && folder.StartsWith("Campaigns/", StringComparison.Ordinal))
        {
            // Inside another campaign's folder, the part after it; otherwise the part after Campaigns ("Videos", "Drafts").
            var inside = campaignNames.Select(Folder).FirstOrDefault(root => folder == root || folder.StartsWith(root + "/", StringComparison.Ordinal));
            sub = inside == null ? folder["Campaigns/".Length..] : folder.Length > inside.Length ? folder[(inside.Length + 1)..] : "Docs";
        }
        else if (force) sub = "Docs";
        else return null;
        sub = sub switch { "Drafts" => "Posts", "Video" => "Videos", "Image" => "Images", _ => sub };
        return campaignName == null ? "Campaigns/" + (sub is "Posts" or "Docs" ? "Drafts" : sub) : Folder(campaignName) + "/" + sub;
    }

    void Refile(string key, Campaign? campaign, string author, bool force = false)
    {
        if (!key.StartsWith("wiki:", StringComparison.Ordinal) && !key.StartsWith("media:", StringComparison.Ordinal)) return;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var view = library.View("");
            var entry = view.Entries.FirstOrDefault(item => item.Key == key);
            if (entry == null && !force) return;
            var target = FolderFor(entry?.Folder, campaign?.Name, View().Campaigns.Select(item => item.Name), force);
            if (target == null || target == entry?.Folder) return;
            try { library.SaveEntry(key, new LibraryEntryChange(view.Version, target, entry?.Tags ?? []), author, "employee"); return; }
            catch (InvalidOperationException) when (attempt < 2) { }
        }
    }

    void MoveFolder(string from, string to, string author)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var view = library.View("");
            if (!view.Folders.Contains(from)) return;
            try { library.SaveFolders(new LibraryFoldersChange(view.Version, view.Folders, [new LibraryFolderMove(from, to)]), author, "employee"); return; }
            catch (InvalidOperationException) when (attempt < 2) { }
        }
    }

    /// <summary>A campaign from a plan document: its name from the title ("Launch-week plan: Sep 26–30" is “Launch week”), its dates
    /// from the title or its day headings, its goal from the Goal line and its channels from the Channels lines. The plan is filed
    /// with the campaign. The owner edits whatever it got wrong.</summary>
    public Campaign FromPlan(CampaignFromPlan request, string author, DateOnly? today = null)
    {
        var page = wiki.List().FirstOrDefault(item => item.Id == request.WikiId) ?? throw new KeyNotFoundException("That document no longer exists.");
        var parsed = ParsePlan(page.Title, page.Body, today ?? DateOnly.FromDateTime(DateTime.UtcNow));
        var name = parsed.Name; var suffix = 2;
        while (View().Campaigns.Any(campaign => string.Equals(campaign.Name, name, StringComparison.OrdinalIgnoreCase))) name = $"{parsed.Name} {suffix++}";
        var campaign = Save(null, parsed with { ExpectedVersion = request.ExpectedVersion, Name = name }, author);
        lock (store)
        {
            var ledger = Read();
            store.Setting(Key, Wire.Pack(ledger with { Version = ledger.Version + 1, Campaigns = [.. ledger.Campaigns.Select(item => item.Id == campaign.Id ? item with { PlanWikiId = page.Id } : item)] }));
        }
        Assign("wiki:" + page.Id, campaign.Id, author, refile: false);
        Refile("wiki:" + page.Id, campaign, author, force: true);
        return Find(campaign.Id)!;
    }

    static readonly string Months = "Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec";

    public static CampaignChange ParsePlan(string title, string body, DateOnly today)
    {
        var head = title.Split(':')[0];
        var name = Regex.Replace(head, @"\b(plan|campaign plan|campaign)\b", "", RegexOptions.IgnoreCase).Replace('-', ' ');
        name = Regex.Replace(name, @"\s+", " ").Trim(' ', '—', '–', '-');
        if (name.Length < 2) name = head.Trim();
        if (name.Length > 60) name = name[..60].Trim();
        name = name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : "Campaign";

        DateOnly? Day(string month, string day)
        {
            if (!DateOnly.TryParseExact($"{month[..3]} {day} {today.Year}", "MMM d yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
            return date < today.AddMonths(-6) ? date.AddYears(1) : date;
        }
        DateOnly? starts = null, ends = null;
        var range = Regex.Match(title, $@"\b({Months})\w*\.?\s+(\d{{1,2}})\s*[–—-]\s*(?:({Months})\w*\.?\s+)?(\d{{1,2}})\b", RegexOptions.IgnoreCase);
        if (range.Success)
        {
            starts = Day(range.Groups[1].Value, range.Groups[2].Value);
            ends = Day(range.Groups[3].Success ? range.Groups[3].Value : range.Groups[1].Value, range.Groups[4].Value);
        }
        else
        {
            var days = Regex.Matches(body, $@"^#+\s*(?:\w+,\s*)?({Months})\w*\.?\s+(\d{{1,2}})\b", RegexOptions.IgnoreCase | RegexOptions.Multiline)
                .Select(match => Day(match.Groups[1].Value, match.Groups[2].Value)).OfType<DateOnly>().ToArray();
            if (days.Length > 0) { starts = days.Min(); ends = days.Max(); }
        }
        if (starts > ends) (starts, ends) = (ends, starts);

        static string Plain(string text) => Regex.Replace(text, @"[*_`]", "").Trim();
        var goal = Regex.Match(body, @"^\s*(?:[-*]\s*)?\**Goal\**\s*:?\**\s*:?\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline) is { Success: true } found
            ? Plain(found.Groups[1].Value)
            : Plain(body.Split("\n\n").Select(part => part.Trim()).FirstOrDefault(part => part.Length > 20 && !part.StartsWith('#') && !part.StartsWith('_')) ?? "");
        if (goal.Length > 400) goal = goal[..397].TrimEnd() + "…";
        var channels = Regex.Matches(body, @"^\s*(?:[-*]\s*)?\**Channels\**\s*:?\**\s*:?\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)
            .SelectMany(match => Plain(match.Groups[1].Value).TrimEnd('.').Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(channel => Regex.Replace(channel, @"^(and|or)\s+", "", RegexOptions.IgnoreCase).Trim())
            .Where(channel => channel.Length is > 0 and <= 40).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
        var status = starts is { } from && from > today ? "planned" : ends is { } to && to < today ? "done" : "active";
        return new CampaignChange(0, name, goal, starts?.ToString("yyyy-MM-dd"), ends?.ToString("yyyy-MM-dd"), channels, status, null);
    }

    /// <summary>What a shift sees: the campaigns it may plan against, briefly.</summary>
    public object[] Context() => [.. Open().Select(campaign => new { id = campaign.Id, name = campaign.Name, goal = campaign.Goal, starts = campaign.Starts, ends = campaign.Ends,
        channels = campaign.Channels, status = campaign.Status, moves = campaign.Moves })];

    /// <summary>The campaign a priority serves: the one it names, else the one its task belongs to, else none (always-on).</summary>
    public Campaign? For(string? named, string? taskKey)
    {
        var ledger = View();
        var given = named?.Trim();
        var byName = string.IsNullOrEmpty(given) || given.Equals("always-on", StringComparison.OrdinalIgnoreCase) || given.Equals("null", StringComparison.OrdinalIgnoreCase) ? null
            : ledger.Campaigns.FirstOrDefault(campaign => campaign.Id == given) ?? ledger.Campaigns.FirstOrDefault(campaign => string.Equals(campaign.Name, given, StringComparison.OrdinalIgnoreCase));
        if (byName != null) return byName;
        return taskKey != null && ledger.Items.TryGetValue(taskKey, out var owner) ? ledger.Campaigns.FirstOrDefault(campaign => campaign.Id == owner) : null;
    }
}
