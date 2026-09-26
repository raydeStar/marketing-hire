using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record DraftMediaChange(string MediaId, bool Attach);
public record DraftAttachment(string Id, string Name, string MediaType, long Bytes);

/// <summary>The images and videos that go with a draft: the post image the employee made, the video a caption is for, or any
/// Library file the owner attaches. Up to four per draft, images and videos only.</summary>
public sealed class DraftMedia(Store store, WorkspaceLibrary library)
{
    private const string Key = "draft-media-v1";
    public const int MaxPerDraft = 4;
    Dictionary<string, string[]> Read() => store.Setting(Key) is { } json ? Wire.Unpack<Dictionary<string, string[]>>(json) : [];

    /// <summary>Every draft's attachments, with each file's name and type. A post image filed before attachments existed
    /// (tagged "draft-12" in the Library) counts as attached to its draft.</summary>
    public Dictionary<string, DraftAttachment[]> All()
    {
        Dictionary<string, string[]> stored;
        lock (store) stored = Read();
        var merged = stored.ToDictionary(item => item.Key, item => item.Value.ToList());
        foreach (var entry in library.View("").Entries.Where(entry => entry.Key.StartsWith("media:", StringComparison.Ordinal)))
            foreach (var tag in entry.Tags.Where(tag => tag.StartsWith("draft-", StringComparison.Ordinal) && tag[6..].All(char.IsDigit) && tag.Length > 6))
            {
                var draft = tag[6..];
                if (!merged.TryGetValue(draft, out var list)) merged[draft] = list = [];
                if (!list.Contains(entry.Key[6..]) && !stored.ContainsKey(draft)) list.Add(entry.Key[6..]);
            }
        return merged.ToDictionary(item => item.Key, item => item.Value.Select(id => store.Upload(id)).OfType<UploadFile>().Where(file => !file.Archived)
            .Select(file => new DraftAttachment(file.Id, file.Name, file.MediaType, file.Bytes)).ToArray());
    }

    public DraftAttachment[] For(string draftId) => All().GetValueOrDefault(draftId) ?? [];

    public DraftAttachment[] Set(string draftId, string mediaId, bool attach)
    {
        if (!draftId.All(char.IsDigit) || draftId.Length is 0 or > 9) throw new ArgumentException("That isn't a draft.");
        var file = store.Upload(mediaId) ?? throw new KeyNotFoundException("That file is no longer in the Library.");
        if (attach && !(file.MediaType.StartsWith("image/", StringComparison.Ordinal) || file.MediaType.StartsWith("video/", StringComparison.Ordinal)))
            throw new ArgumentException("Attach an image or a video.");
        lock (store)
        {
            var all = Read();
            // The first change to a draft starts from what it already shows, including an image filed before attachments existed.
            var current = all.TryGetValue(draftId, out var saved) ? saved.ToList() : [.. For(draftId).Select(item => item.Id)];
            if (attach) { if (!current.Contains(mediaId)) current.Add(mediaId); }
            else current.Remove(mediaId);
            if (current.Count > MaxPerDraft) throw new ArgumentException($"A draft can carry up to {MaxPerDraft} images or videos.");
            all[draftId] = [.. current];
            store.Setting(Key, Wire.Pack(all));
        }
        return For(draftId);
    }

    /// <summary>A redraft carries the original's images and videos.</summary>
    public void CarryOver(string fromDraft, string toDraft)
    {
        foreach (var item in For(fromDraft)) try { Set(toDraft, item.Id, true); } catch (ArgumentException) { break; }
    }
}
