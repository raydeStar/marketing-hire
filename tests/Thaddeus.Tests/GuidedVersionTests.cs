using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class GuidedVersionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus version ' $-" + Guid.NewGuid().ToString("N"));
    private readonly string id = Guid.NewGuid().ToString("N");
    private MaintenancePlan plan = null!;
    private string target = "";
    private async Task<GuidedRestore> Prepare(int? targetSchema = null)
    {
        var source = Path.Combine(root, "original-study");
        using (var store = new Store(source)) store.Write("notes/version.md", "Before the app change.", "absent");
        var backupRoot = source + "-backups"; PrivateWorkerDirectory.Create(backupRoot);
        var backup = await StudyBackup.Create(source, Path.Combine(backupRoot, "before-upgrade"));
        await File.WriteAllTextAsync(Path.Combine(backupRoot, id + ".receipt.json"), Wire.Pack(backup));
        using (var store = new Store(source)) store.Write("notes/version.md", "Newer original edits stay here.", store.Version("notes/version.md"));
        var originalPackage = ApplicationPackageTests.CreatePackage(Path.Combine(root, "original-package"));
        target = ApplicationPackageTests.CreatePackage(Path.Combine(root, "selected-package"), targetSchema);
        plan = new(id, "stop", source, backupRoot, backup.Directory, "http://localhost:5179", originalPackage,
            new("owner", "token", "csrf", "Fixture", true, DateTimeOffset.UtcNow.AddMinutes(30)),
            new(originalPackage, source, "http://localhost:5179", 5183, null, true));
        return new(plan);
    }
    [Fact] public async Task SelectedVersionCreatesGuardedLauncherAndPreservesTheOriginal()
    {
        var restore = await Prepare(); var review = await restore.Review(id, default, target);
        Assert.Equal(target, review.Review!.Application!.Directory); Assert.False(Directory.Exists(review.Review.Destination));
        restore.Begin(review.Review.Id, default); await restore.Completion;
        Assert.Equal("restored", restore.View.Phase); var launcher = restore.View.Launcher!;
        Assert.Equal(target, launcher.Package);
        var original = restore.View.ReturnLauncher!; Assert.Equal(plan.Package, original.Package);
        var originalProfile = DesktopLaunch.Parse(["--desktop", "--no-browser", "--launch-profile", original.Profile], plan.Package);
        Assert.Equal(plan.Source, originalProfile!.Data);
        Assert.True(File.Exists(original.EntryPoint));
        if (OperatingSystem.IsWindows())
        {
            using var originalTarget = JsonDocument.Parse(File.ReadAllText(Path.Combine(original.Directory, "launch-target.json")));
            Assert.Equal(JsonValueKind.Null, originalTarget.RootElement.GetProperty("packageManifestSha256").ValueKind);
        }
        Assert.Equal("Before the app change.", File.ReadAllText(Path.Combine(restore.View.Receipt!.Directory, "knowledge/notes/version.md")));
        Assert.Equal("Newer original edits stay here.", File.ReadAllText(Path.Combine(plan.Source, "knowledge/notes/version.md")));
        var scripts = string.Join('\n', Directory.GetFiles(launcher.Directory).Where(file => file.EndsWith(".ps1") || file.EndsWith(".command") || file.EndsWith(".sh")).Select(File.ReadAllText));
        Assert.Contains("--verify-package", scripts);
        if (OperatingSystem.IsWindows())
        {
            using var targetJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(launcher.Directory, "launch-target.json")));
            Assert.Equal(review.Review.Application.ManifestSha256, targetJson.RootElement.GetProperty("packageManifestSha256").GetString());
            Assert.Equal(Path.Combine(plan.Package, "Thaddeus.Host.exe"), targetJson.RootElement.GetProperty("verifier").GetString());
        }
        else Assert.Contains(review.Review.Application.ManifestSha256, scripts);
        Assert.Equal(restore.View, restore.Begin(review.Review.Id, default));
    }
    [Fact] public async Task IncompatibleOlderVersionRefusesBeforeCreatingAStudy()
    {
        var restore = await Prepare(Store.CurrentSchemaVersion - 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restore.Review(id, default, target));
        Assert.Equal("idle", restore.View.Phase); Assert.False(restore.Busy);
        Assert.Empty(Directory.GetDirectories(root, "*-restored-*"));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ChangingPackageAfterReviewNeverInstallsTheStudy(bool manifest)
    {
        var restore = await Prepare(); var review = await restore.Review(id, default, target);
        if (manifest) ApplicationPackageTests.Rewrite(target, json => json["sourceHead"] = new string('b', 40));
        else File.WriteAllText(Path.Combine(target, "wwwroot/index.html"), "Changed after review.");
        restore.Begin(review.Review!.Id, default); await restore.Completion;
        Assert.Equal("failed", restore.View.Phase); Assert.Null(restore.View.Receipt); Assert.False(Directory.Exists(review.Review.Destination));
        Assert.Equal(restore.View, restore.Begin(review.Review.Id, default));
    }
    [Fact] public async Task SourceLaunchCannotSelectAnExternalApplication()
    {
        await Prepare(); var restore = new GuidedRestore(plan with { Launch = null });
        await Assert.ThrowsAsync<InvalidOperationException>(() => restore.Review(id, default, target));
        Assert.False(restore.Busy);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
