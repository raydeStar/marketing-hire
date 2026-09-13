using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class StoreLeaseTests
{
    [Fact] public async Task DisposalWaitsForAnAdmittedWriteBeforeReleasingTheStoreLease()
    {
        var root = Path.Combine(Path.GetTempPath(), "thaddeus-store-close-" + Guid.NewGuid().ToString("N"));
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var closing = new ManualResetEventSlim();
        var store = new Store(root, stage => { if (stage == "after-projection") { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); } });
        Task? writer = null, disposal = null;
        try
        {
            writer = Task.Run(() => store.Write("notes/fixture.md", "retained", "absent"));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            disposal = Task.Run(() => { closing.Set(); store.Dispose(); });
            Assert.True(closing.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotSame(disposal, await Task.WhenAny(disposal, Task.Delay(100)));
            release.Set(); await writer; await disposal;
            using (File.Open(Path.Combine(root, "ledger.sqlite"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            using var restarted = new Store(root); Assert.Equal("retained", restarted.Page("notes/fixture.md")!.Content);
        }
        finally
        {
            release.Set();
            try { if (writer != null) await writer; }
            finally { if (disposal != null) await disposal; store.Dispose(); Directory.Delete(root, true); }
        }
    }
    [Fact] public void ClosingTheStoreReleasesItsDatabaseWithoutClearingGlobalPools()
    {
        var root = Path.Combine(Path.GetTempPath(), "thaddeus-store-lease-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var store = new Store(root)) store.Setting("fixture", "retained");
            // Windows denies this exclusive open while any pooled SQLite handle remains on the file.
            using (File.Open(Path.Combine(root, "ledger.sqlite"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            using var restarted = new Store(root); Assert.Equal("retained", restarted.Setting("fixture"));
        }
        finally { Directory.Delete(root, true); }
    }
}
