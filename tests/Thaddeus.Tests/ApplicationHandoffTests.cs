using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class ApplicationHandoffTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-open-study-" + Guid.NewGuid().ToString("N"));
    private readonly string backupId = Guid.NewGuid().ToString("N");
    private MaintenancePlan plan = null!;
    private async Task<GuidedRestore> Restored()
    {
        var source = Path.Combine(root, "original");
        using (var store = new Store(source)) store.Write("notes/history.md", "Saved before the upgrade.", "absent");
        var backupRoot = source + "-backups"; PrivateWorkerDirectory.Create(backupRoot);
        var backup = await StudyBackup.Create(source, Path.Combine(backupRoot, "snapshot"));
        await File.WriteAllTextAsync(Path.Combine(backupRoot, backupId + ".receipt.json"), Wire.Pack(backup));
        using (var store = new Store(source)) store.Write("notes/history.md", "Keep this newer original edit.", store.Version("notes/history.md"));
        var current = ApplicationPackageTests.CreatePackage(Path.Combine(root, "current-app"));
        var target = ApplicationPackageTests.CreatePackage(Path.Combine(root, "selected-app"));
        plan = new(backupId, "stop", source, backupRoot, backup.Directory, "http://localhost:5189", current,
            new("owner", "fictional-token", "fictional-csrf", "Fixture", true, DateTimeOffset.UtcNow.AddHours(1)),
            new(current, source, "http://localhost:5189", 5190, null, true));
        var restore = new GuidedRestore(plan);
        var reviewed = await restore.Review(backupId, default, target);
        restore.Begin(reviewed.Review!.Id, default); await restore.Completion;
        Assert.Equal("restored", restore.View.Phase);
        return restore;
    }

    [Theory] [InlineData("restored")] [InlineData("original")]
    public async Task PreparedOpenIsBoundToTheRecordedStudyAndLeavesNewerOriginalEditsUntouched(string target)
    {
        var restore = await Restored(); var view = restore.View;
        var prepared = await restore.PrepareOpen(new(view.Review!.Id, target, false), default);
        Assert.Equal(target == "restored" ? view.Receipt!.Directory : plan.Source, prepared.Launch.Data);
        Assert.Equal(target == "restored" ? view.Launcher!.Package : plan.Package, prepared.Application.Directory);
        Assert.Equal(plan.Origin, prepared.Launch.Origin); Assert.False(prepared.OpenBrowser); Assert.True(restore.Busy);
        Assert.True(File.Exists(Path.Combine(plan.BackupRoot, "restore-" + prepared.Id + ".open-intent.json")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restore.PrepareOpen(new(view.Review.Id, target), default));
        Assert.Equal("Keep this newer original edit.", File.ReadAllText(Path.Combine(plan.Source, "knowledge/notes/history.md")));
    }

    [Theory] [InlineData("stale", "restored")] [InlineData("current", "unknown")]
    public async Task StaleReviewsAndUnrecognizedTargetsNeverWriteALaunchIntent(string review, string target)
    {
        var restore = await Restored(); var before = restore.View;
        var request = new OpenStudyRequest(review == "current" ? before.Review!.Id : review, target);
        if (review == "stale") await Assert.ThrowsAsync<InvalidOperationException>(() => restore.PrepareOpen(request, default));
        else await Assert.ThrowsAsync<ArgumentException>(() => restore.PrepareOpen(request, default));
        Assert.Equal(before, restore.View); Assert.False(restore.Busy);
        Assert.Empty(Directory.GetFiles(plan.BackupRoot, "*.open-intent.json"));
    }

    [Fact] public async Task AChangedLaunchProfileIsRejectedBeforeAnyProcessStarts()
    {
        var restore = await Restored(); var view = restore.View;
        File.AppendAllText(view.Launcher!.Profile, " ");
        await Assert.ThrowsAsync<IOException>(() => restore.PrepareOpen(new(view.Review!.Id, "restored"), default));
        Assert.Equal(view, restore.View); Assert.False(restore.Busy);
        Assert.Empty(Directory.GetFiles(plan.BackupRoot, "*.open-intent.json"));
    }

    [Fact] public async Task PackageChangesBetweenPreparationAndLaunchRefuseExecutionAndKeepBothStudies()
    {
        var restore = await Restored(); var view = restore.View;
        var prepared = await restore.PrepareOpen(new(view.Review!.Id, "restored", false), default);
        File.AppendAllText(Path.Combine(prepared.Application.Directory, "wwwroot/index.html"), " changed");
        var result = await ApplicationHandoff.Start(prepared);
        Assert.False(result.Started); Assert.True(result.CanReopenOriginal); Assert.Null(result.ProcessId);
        Assert.True(File.Exists(Path.Combine(prepared.Launcher.Directory, "open-" + prepared.Id + ".json")));
        Assert.True(File.Exists(Path.Combine(prepared.Launch.Data, "ledger.sqlite")));
        Assert.Equal("Keep this newer original edit.", File.ReadAllText(Path.Combine(plan.Source, "knowledge/notes/history.md")));
        restore.OpeningFailed(result.Message); Assert.Equal("restored", restore.View.Phase); Assert.False(restore.Busy);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}
