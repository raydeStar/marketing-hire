using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

/// <summary>The live feed of a shift is still there after the workspace restarts, and only the newest shifts keep theirs.</summary>
public sealed class ShiftEventsTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "shift-events-" + Guid.NewGuid().ToString("N"));
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root) && attempt < 10; attempt++) { try { Directory.Delete(root, true); } catch (IOException) { Thread.Sleep(200); } }
    }

    [Fact] public void TheFeedSurvivesARestartAndOldShiftsLetTheirsGo()
    {
        Directory.CreateDirectory(root);
        using (var store = new Store(root))
        {
            var events = new ShiftEvents(store);
            events.Add("s1", "think", "Reading hirezero.app");
            events.Add("s1", "review", "“Launch post”, pass 1: B (Distinctive C)");
            for (var shift = 2; shift <= 22; shift++) events.Add("s" + shift, "think", "Choosing what matters most today");
        }
        using (var store = new Store(root))
        {
            // A new process: what s22 did is read back from the workspace, in order, and it goes on counting from there.
            var events = new ShiftEvents(store);
            Assert.Equal([1], events.After("s22", 0).Select(item => item.N));
            events.Add("s22", "work", "Saved the draft");
            Assert.Equal(["think", "work"], events.After("s22", 0).Select(item => item.Kind));
            Assert.Equal(2, events.After("s22", 1).Single().N);
            // Twenty shifts keep their feeds; the oldest two have let theirs go.
            Assert.Empty(events.After("s1", 0));
            Assert.Empty(events.After("s2", 0));
            Assert.Single(events.After("s3", 0));
        }
    }
}
