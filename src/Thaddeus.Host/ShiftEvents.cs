using System.Collections.Concurrent;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record ShiftEvent(int N, DateTimeOffset At, string Kind, string Text, string? Key = null);

/// <summary>What a shift is doing as it does it: each model step as it starts, each page it reads, each grade and fix, each save.
/// The last few hundred per shift, written through to the workspace so the feed is still there after a restart; the last twenty
/// shifts keep theirs.</summary>
public sealed class ShiftEvents(Store store)
{
    const int Keep = 300, Shifts = 20;
    const string Prefix = "shift-events-v1:", IndexKey = "shift-events-v1";
    readonly ConcurrentDictionary<string, List<ShiftEvent>> feeds = new();

    List<ShiftEvent> Feed(string shiftId) => feeds.GetOrAdd(shiftId, id => store.Setting(Prefix + id) is { Length: > 2 } json ? Wire.Unpack<List<ShiftEvent>>(json) : []);

    public void Add(string shiftId, string kind, string text, string? key = null)
    {
        text = text.ReplaceLineEndings(" ").Trim();
        if (text.Length == 0) return;
        if (text.Length > 280) text = text[..279].TrimEnd() + "…";
        var feed = Feed(shiftId);
        lock (feed)
        {
            var first = feed.Count == 0;
            feed.Add(new ShiftEvent(first ? 1 : feed[^1].N + 1, DateTimeOffset.UtcNow, kind, text, key));
            if (feed.Count > Keep) feed.RemoveRange(0, feed.Count - Keep);
            store.Setting(Prefix + shiftId, Wire.Pack(feed));
            if (first) Remember(shiftId);
        }
    }

    /// <summary>The newest shifts keep their feeds; an older one's is emptied.</summary>
    void Remember(string shiftId)
    {
        lock (feeds)
        {
            var index = store.Setting(IndexKey) is { } json ? Wire.Unpack<List<string>>(json) : [];
            index.Remove(shiftId); index.Add(shiftId);
            foreach (var old in index.Take(Math.Max(0, index.Count - Shifts)).ToArray()) { store.Setting(Prefix + old, "[]"); feeds.TryRemove(old, out _); index.Remove(old); }
            store.Setting(IndexKey, Wire.Pack(index));
        }
    }

    public ShiftEvent[] After(string shiftId, int after)
    {
        var feed = Feed(shiftId);
        lock (feed) return [.. feed.Where(item => item.N > after)];
    }
}

/// <summary>Notes a cycle keeps for its stage summary, each also told to the live view as it's written.</summary>
public sealed class NarratedNotes(ShiftEvents events, string shiftId, string kind) : List<string>
{
    public new void Add(string note) { base.Add(note); events.Add(shiftId, kind, note); }
}
