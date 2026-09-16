using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public record FeedSubscription(string Id, string Url, string Title, bool Paused, string Version,
    DateTimeOffset Created, DateTimeOffset NextRefresh, DateTimeOffset? LastAttempt = null, DateTimeOffset? LastChecked = null,
    string? Error = null, int Failures = 0, bool Truncated = false, string? ValidatorUrl = null,
    string? ETag = null, DateTimeOffset? LastModified = null);
public record FeedEntry(string Id, string SubscriptionId, string Key, string Title, string Summary, string? Url,
    DateTimeOffset? Published, DateTimeOffset Received, bool Read, string Version, string? SavedItemId = null, FeedEngagement? Engagement = null);
public record FeedEngagement(DateTimeOffset? Opened = null, DateTimeOffset? Saved = null, DateTimeOffset? Discussed = null,
    int Preference = 0, DateTimeOffset? Preferred = null);
public record FeedPreferences(bool Enabled = true, string Version = "absent");
public record FeedState(FeedSubscription[] Subscriptions, FeedEntry[] Entries, string Revision, FeedPreferences? Preferences = null);

public sealed partial class Store
{
    public const int MaxSubscriptions = 20;
    public FeedState Feeds()
    {
        lock (gate)
        {
            var saved = Library().Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            return new(Subscriptions(), FeedEntries().GroupBy(FeedStoryKey).Select(group =>
            {
                var entry = group.OrderBy(row => row.Id, StringComparer.Ordinal).First();
                return entry with { Read = group.Any(row => row.Read), Engagement = MergeFeedEngagement(group),
                    SavedItemId = SavedFeedItem(group, saved) };
            }).ToArray(), FeedRevision(), FeedPreferences());
        }
    }
    private FeedEntry[] FeedEntries() => Query("SELECT body FROM feed_entries ORDER BY rowid DESC").Select(Wire.Unpack<FeedEntry>).ToArray();
    private static string FeedStoryKey(FeedEntry entry) => entry.SubscriptionId + ":" + (string.IsNullOrEmpty(entry.Url) ? "id:" + entry.Id : "url:" + entry.Url);
    private FeedEntry[] FeedAliases(FeedEntry entry) => FeedEntries().Where(row => FeedStoryKey(row) == FeedStoryKey(entry)).ToArray();
    private static string? SavedFeedItem(IEnumerable<FeedEntry> aliases, HashSet<string> saved) => aliases
        .SelectMany(row => new[] { row.SavedItemId, row.Id[..32] }).FirstOrDefault(id => id != null && saved.Contains(id));
    private static FeedEngagement? MergeFeedEngagement(IEnumerable<FeedEntry> aliases)
    {
        var values = aliases.Where(row => row.Engagement != null).Select(row => row.Engagement!).ToArray();
        if (values.Length == 0) return null;
        var preference = values.OrderBy(value => value.Preferred).Last();
        return new(values.Max(value => value.Opened), values.Max(value => value.Saved), values.Max(value => value.Discussed), preference.Preference, preference.Preferred);
    }
    public string FeedRevision() { lock (gate) return Setting("feed-revision") ?? "absent"; }
    private void ChangedFeeds() => Setting("feed-revision", Guid.NewGuid().ToString("N"));
    private FeedSubscription[] Subscriptions() => Query("SELECT body FROM feed_subscriptions ORDER BY rowid").Select(Wire.Unpack<FeedSubscription>).ToArray();
    private FeedSubscription Subscription(string id, string version)
    {
        var item = Subscriptions().SingleOrDefault(item => item.Id == id);
        if (item == null || item.Version != version) throw new InvalidOperationException("This subscription changed. Reload it before acting again.");
        return item;
    }
    private void PutSubscription(FeedSubscription item) => Exec("INSERT INTO feed_subscriptions VALUES($id,$body) ON CONFLICT(id) DO UPDATE SET body=$body", ("$id", item.Id), ("$body", Wire.Pack(item)));
    private void PutEntry(FeedEntry item) => Exec("INSERT INTO feed_entries VALUES($id,$subscription,$body) ON CONFLICT(id) DO UPDATE SET body=$body",
        ("$id", item.Id), ("$subscription", item.SubscriptionId), ("$body", Wire.Pack(item)));

    public FeedSubscription Subscribe(string url, DateTimeOffset now)
    {
        var source = FeedParser.SourceUrl(url);
        lock (gate)
        {
            var items = Subscriptions();
            if (items.Any(item => item.Url == source.AbsoluteUri)) throw new InvalidOperationException("That feed is already subscribed.");
            if (items.Length >= MaxSubscriptions) throw new InvalidOperationException("This study supports up to 20 subscriptions. Remove one before adding another.");
            var item = new FeedSubscription(Guid.NewGuid().ToString("N"), source.AbsoluteUri, source.IdnHost, false, Guid.NewGuid().ToString("N"), now, now);
            using var transaction = db.BeginTransaction(); PutSubscription(item); ChangedFeeds(); transaction.Commit(); return item;
        }
    }

    public FeedSubscription ChangeSubscription(string id, string version, bool paused, DateTimeOffset now)
    {
        lock (gate)
        {
            var current = Subscription(id, version);
            var item = current with { Paused = paused, Version = Guid.NewGuid().ToString("N"),
                NextRefresh = current.NextRefresh > now ? current.NextRefresh : now };
            using var transaction = db.BeginTransaction(); PutSubscription(item); ChangedFeeds(); transaction.Commit(); return item;
        }
    }
    public void RemoveSubscription(string id, string version)
    {
        lock (gate)
        {
            Subscription(id, version);
            using var transaction = db.BeginTransaction();
            Exec("DELETE FROM feed_entries WHERE subscription=$id", ("$id", id));
            Exec("DELETE FROM feed_subscriptions WHERE id=$id", ("$id", id));
            ChangedFeeds(); transaction.Commit();
        }
    }
    public void QueueFeedRefresh(string id, string version, DateTimeOffset now)
    {
        lock (gate)
        {
            var item = Subscription(id, version);
            if (item.Paused) throw new InvalidOperationException("Resume this subscription before refreshing it.");
            if (item.LastAttempt is { } attempted && now < attempted.AddMinutes(5)) throw new InvalidOperationException("Wait five minutes between refresh attempts for this source.");
            if (item.Failures > 0 && now < item.NextRefresh) throw new InvalidOperationException("This source is resting after a failed request. Try again after its next check time.");
            using var transaction = db.BeginTransaction(); PutSubscription(item with { NextRefresh = now }); ChangedFeeds(); transaction.Commit();
        }
    }
    public FeedSubscription? ClaimFeedRefresh(DateTimeOffset now)
    {
        lock (gate)
        {
            var item = Subscriptions().Where(item => !item.Paused && item.NextRefresh <= now).OrderBy(item => item.NextRefresh).FirstOrDefault();
            if (item == null) return null;
            // Persist the lease before network I/O; a crash cannot create a hot retry loop.
            item = item with { LastAttempt = now, NextRefresh = now.AddMinutes(60), Version = Guid.NewGuid().ToString("N") };
            using var transaction = db.BeginTransaction(); PutSubscription(item); ChangedFeeds(); transaction.Commit(); return item;
        }
    }
    public bool FinishFeedRefresh(FeedSubscription claimed, FeedFetch fetched, DateTimeOffset now)
    {
        lock (gate)
        {
            var item = Subscriptions().SingleOrDefault(item => item.Id == claimed.Id);
            if (item == null || item.Paused || item.Version != claimed.Version) return false;
            var error = fetched.Error ?? (fetched.Feed == null && !fetched.NotModified ? "No supported feed was returned." : null);
            var failures = error == null ? 0 : Math.Min(item.Failures + 1, 5);
            var delay = Math.Clamp(Math.Max(fetched.RefreshMinutes, failures == 0 ? 60 : 60 * (1 << (failures - 1))), 60, 1440);
            using var transaction = db.BeginTransaction();
            if (error == null && fetched.Feed is { } feed)
            {
                var existing = Query("SELECT body FROM feed_entries WHERE subscription=$id ORDER BY rowid DESC", ("$id", item.Id))
                    .Select(Wire.Unpack<FeedEntry>).ToDictionary(entry => entry.Key, StringComparer.Ordinal);
                var retained = new HashSet<string>(StringComparer.Ordinal);
                var saved = Library().Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var incoming in feed.Entries.Take(FeedParser.MaxEntries).Reverse())
                {
                    existing.TryGetValue(incoming.Key, out var previous);
                    if (!string.IsNullOrEmpty(incoming.Url)) previous = existing.Values.Where(entry => entry.Url == incoming.Url).OrderBy(entry => entry.Id, StringComparer.Ordinal).FirstOrDefault() ?? previous;
                    var id = previous?.Id ?? Wire.Hash(item.Id + ":" + incoming.Key); retained.Add(id);
                    var aliases = previous == null ? [] : existing.Values.Where(row => FeedStoryKey(row) == FeedStoryKey(previous)).ToArray();
                    var entry = new FeedEntry(id, item.Id, previous?.Key ?? incoming.Key, incoming.Title, incoming.Summary, incoming.Url, incoming.Published,
                        previous?.Received ?? now, aliases.Any(row => row.Read), previous?.Version ?? Guid.NewGuid().ToString("N"),
                        SavedItemId: SavedFeedItem(aliases, saved), Engagement: MergeFeedEngagement(aliases));
                    if (previous != null && entry != previous) entry = entry with { Version = Guid.NewGuid().ToString("N") };
                    PutEntry(entry);
                    existing[entry.Key] = entry;
                }
                foreach (var previous in existing.Values.OrderByDescending(entry => entry.Received))
                {
                    if (retained.Contains(previous.Id)) continue;
                    if (retained.Count < FeedParser.MaxEntries) retained.Add(previous.Id);
                    else Exec("DELETE FROM feed_entries WHERE id=$id", ("$id", previous.Id));
                }
            }
            var unchanged = fetched.NotModified;
            item = item with { Title = error == null ? fetched.Feed?.Title ?? item.Title : item.Title,
                Error = error, Failures = failures, LastChecked = now, NextRefresh = now.AddMinutes(delay),
                Truncated = error == null ? fetched.Feed?.Truncated ?? item.Truncated : item.Truncated,
                ValidatorUrl = error == null ? fetched.Url : item.ValidatorUrl,
                ETag = error == null ? fetched.ETag ?? (unchanged ? item.ETag : null) : item.ETag,
                LastModified = error == null ? fetched.LastModified ?? (unchanged ? item.LastModified : null) : item.LastModified };
            PutSubscription(item); ChangedFeeds(); testFault?.Invoke("before-feed-commit"); transaction.Commit(); return true;
        }
    }
    public FeedEntry ReadFeedEntry(string id, string version, bool read)
    {
        lock (gate)
        {
            var item = Query("SELECT body FROM feed_entries WHERE id=$id", ("$id", id)).Select(Wire.Unpack<FeedEntry>).SingleOrDefault();
            if (item == null || item.Version != version) throw new InvalidOperationException("This update changed. Reload it before acting again.");
            using var transaction = db.BeginTransaction();
            foreach (var alias in FeedAliases(item)) PutEntry(alias with { Read = read, Version = Guid.NewGuid().ToString("N") });
            ChangedFeeds(); transaction.Commit(); return FeedEntries().Single(entry => entry.Id == id);
        }
    }
    public LibraryItem SaveFeedEntry(string id, string version)
    {
        lock (gate)
        {
            var entry = Query("SELECT body FROM feed_entries WHERE id=$id", ("$id", id)).Select(Wire.Unpack<FeedEntry>).SingleOrDefault();
            if (entry == null || entry.Version != version) throw new InvalidOperationException("This update changed. Reload it before saving it.");
            var aliases = FeedAliases(entry); var savedId = aliases.OrderBy(row => row.Id, StringComparer.Ordinal).First().Id[..32];
            // A deterministic ID makes a repeated click harmless. Saved notes outlive rotating updates.
            var existing = Library().FirstOrDefault(item => aliases.Any(alias => item.Id == alias.Id[..32] || item.Id == alias.SavedItemId));
            if (existing != null) return existing;
            return EditLibrary(savedId, new("feed", entry.Title, entry.Summary, "open", entry.Url, null, "absent"));
        }
    }

    public FeedPreferences FeedPreferences() => Setting("feed-preferences") is { } value ? Wire.Unpack<FeedPreferences>(value) : new();

    public FeedPreferences ChangeFeedPreferences(string version, bool enabled, bool reset)
    {
        lock (gate)
        {
            if (FeedPreferences().Version != version) throw new InvalidOperationException("Feed preferences changed. Reload before saving.");
            using var transaction = db.BeginTransaction();
            // Forget means forget; an old tab cannot quietly teach the raven yesterday's lesson again.
            if (reset || !enabled)
                foreach (var entry in Query("SELECT body FROM feed_entries").Select(Wire.Unpack<FeedEntry>).Where(entry => entry.Engagement != null))
                    PutEntry(entry with { Engagement = null });
            var preferences = new FeedPreferences(enabled, Guid.NewGuid().ToString("N"));
            Setting("feed-preferences", Wire.Pack(preferences)); ChangedFeeds(); transaction.Commit(); return preferences;
        }
    }

    public FeedEntry RecordFeedInteraction(string id, string preferenceVersion, string action, DateTimeOffset now)
    {
        if (action is not ("open" or "save" or "discuss" or "more" or "less" or "clear")) throw new ArgumentException("Unknown feed feedback.");
        lock (gate)
        {
            var preferences = FeedPreferences();
            if (preferences.Version != preferenceVersion) throw new InvalidOperationException("Feed preferences changed. Reload before recording feedback.");
            var entry = Query("SELECT body FROM feed_entries WHERE id=$id", ("$id", id)).Select(Wire.Unpack<FeedEntry>).SingleOrDefault()
                ?? throw new InvalidOperationException("This update is no longer available.");
            if (!preferences.Enabled) return entry;
            var aliases = FeedAliases(entry);
            if (action == "save" && !Library().Any(item => aliases.Any(alias => item.Id == alias.Id[..32] || item.Id == alias.SavedItemId))) throw new InvalidOperationException("Save the article before recording a save.");
            var before = MergeFeedEngagement(aliases) ?? new();
            var value = action switch
            {
                "open" => before with { Opened = before.Opened ?? now },
                "save" => before with { Saved = before.Saved ?? now },
                "discuss" => before with { Discussed = before.Discussed ?? now },
                "more" => before.Preference == 1 ? before : before with { Preference = 1, Preferred = now },
                "less" => before.Preference == -1 ? before : before with { Preference = -1, Preferred = now },
                _ => before with { Preference = 0, Preferred = null }
            };
            if (value == before) return entry;
            // Feedback has its own policy version; it never invalidates a simultaneous read/save action.
            entry = entry with { Engagement = value };
            using var transaction = db.BeginTransaction(); foreach (var alias in aliases) PutEntry(alias with { Engagement = value }); ChangedFeeds(); transaction.Commit(); return entry;
        }
    }
}
