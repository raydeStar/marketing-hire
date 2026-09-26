using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record RedraftRequest(string TaskId, string Key, string Title, string Feedback, string By, DateTimeOffset At, string? Result = null, DateTimeOffset? DoneAt = null);
public record RedraftAsk(string Key, string Feedback);

/// <summary>Work the owner sent back with feedback: each request is a task for the employee, and the next cycle rewrites the
/// original to answer the feedback. A document comes back as a new version of the same page; a post as a new draft.</summary>
public sealed class Redrafts(Store store)
{
    private const string Key = "redrafts-v1";
    RedraftRequest[] Read() => store.Setting(Key) is { } json ? Wire.Unpack<RedraftRequest[]>(json) : [];
    public RedraftRequest[] All() { lock (store) return Read(); }
    public RedraftRequest? For(string taskId) { lock (store) return Read().LastOrDefault(item => item.TaskId == taskId && item.DoneAt == null); }
    /// <summary>A redraft already waiting for this item, so asking twice doesn't queue it twice.</summary>
    public RedraftRequest? Waiting(string key) { lock (store) return Read().LastOrDefault(item => item.Key == key && item.DoneAt == null); }
    public void Add(RedraftRequest request) { lock (store) store.Setting(Key, Wire.Pack(Read().TakeLast(299).Append(request).ToArray())); }
    public void Complete(string taskId, string result)
    {
        lock (store) store.Setting(Key, Wire.Pack(Read().Select(item => item.TaskId == taskId && item.DoneAt == null ? item with { Result = result, DoneAt = DateTimeOffset.UtcNow } : item).ToArray()));
    }
}
