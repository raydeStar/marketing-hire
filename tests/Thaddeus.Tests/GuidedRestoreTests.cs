using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Thaddeus.Tests;

public sealed class GuidedRestoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus restore ' fixture-" + Guid.NewGuid().ToString("N"));
    private readonly string source, backupRoot, backup, id = Guid.NewGuid().ToString("N");
    private readonly MaintenancePlan plan;
    public GuidedRestoreTests()
    {
        source = Path.Combine(root, "study"); backupRoot = source + "-backups";
        backup = Path.Combine(backupRoot, "recorded-backup");
        using var store = new Store(source); store.Write("notes/source.md", "Original café and a raven.", "absent");
        File.WriteAllText(Path.Combine(source, "host-key.txt"), "fictional-owner-key");
        plan = new(id, "stop", source, backupRoot, backup, "http://localhost:5179", root,
            new("fixture-owner", "hash", "csrf", "Fixture", true, DateTimeOffset.UtcNow.AddHours(1)));
    }
    private async Task<StudyBackupReceipt> CreateBackup()
    {
        PrivateWorkerDirectory.Create(backupRoot);
        var receipt = await StudyBackup.Create(source, backup);
        await File.WriteAllTextAsync(Path.Combine(backupRoot, id + ".receipt.json"), Wire.Pack(receipt));
        return receipt;
    }
    [Fact] public async Task ReviewDoesNotCreateAStudyAndRestorePreservesNewerOriginalEdits()
    {
        var receipt = await CreateBackup();
        using (var store = new Store(source)) store.Write("notes/source.md", "A newer original edit.", store.Version("notes/source.md"));
        var restore = new GuidedRestore(plan); Assert.Equal(id, Assert.Single(await restore.Backups(default)).Id);
        var review = await restore.Review(id, default); Assert.Equal(receipt.ManifestSha256, review.Review!.Backup.ManifestSha256);
        Assert.False(Directory.Exists(review.Review.Destination));
        Assert.Throws<InvalidOperationException>(() => restore.Begin("stale", default));
        restore.Begin(review.Review.Id, default); await restore.Completion;
        Assert.Equal("restored", restore.View.Phase); Assert.Null(restore.View.Launcher);
        Assert.Equal("Original café and a raven.", File.ReadAllText(Path.Combine(restore.View.Receipt!.Directory, "knowledge/notes/source.md")));
        Assert.Equal("A newer original edit.", File.ReadAllText(Path.Combine(source, "knowledge/notes/source.md")));
        var files = Directory.GetDirectories(root).Length;
        Assert.Equal(restore.View, restore.Begin(review.Review.Id, default)); Assert.Equal(files, Directory.GetDirectories(root).Length);
        Assert.True(File.Exists(Path.Combine(backupRoot, "restore-" + review.Review.Id + ".intent.json")));
        Assert.True(File.Exists(Path.Combine(backupRoot, "restore-" + review.Review.Id + ".result.json")));
    }
    [Fact] public async Task ChangingTheReviewedManifestCannotChangeTheAcceptedBackup()
    {
        await CreateBackup(); var restore = new GuidedRestore(plan); var review = await restore.Review(id, default);
        var path = Path.Combine(backup, "backup.json"); var manifest = Wire.Unpack<StudyBackupManifest>(await File.ReadAllTextAsync(path));
        await File.WriteAllTextAsync(path, Wire.Pack(manifest with { Created = manifest.Created.AddSeconds(1) }));
        restore.Begin(review.Review!.Id, default); await restore.Completion;
        Assert.Equal("failed", restore.View.Phase); Assert.Null(restore.View.Receipt); Assert.False(Directory.Exists(review.Review.Destination));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restore.Review(id, default));
    }
    [Fact] public async Task ModifiedBackupFileFailsWithoutInstallingATargetOrReplayingTheAttempt()
    {
        await CreateBackup(); var restore = new GuidedRestore(plan); var review = await restore.Review(id, default);
        await File.WriteAllTextAsync(Path.Combine(backup, "data/knowledge/notes/source.md"), "Tampered backup.");
        restore.Begin(review.Review!.Id, default); await restore.Completion;
        Assert.Equal("failed", restore.View.Phase); Assert.Null(restore.View.Receipt); Assert.False(Directory.Exists(review.Review.Destination));
        Assert.Equal(restore.View, restore.Begin(review.Review.Id, default));
        Assert.Single(Directory.GetDirectories(root, "*.incomplete-*"));
    }
    [Fact] public async Task ExistingDestinationAndPreCancelledCopiesNeverOverwriteData()
    {
        await CreateBackup(); var restore = new GuidedRestore(plan); var review = await restore.Review(id, default);
        Directory.CreateDirectory(review.Review!.Destination); await File.WriteAllTextAsync(Path.Combine(review.Review.Destination, "keep.txt"), "Keep this.");
        restore.Begin(review.Review.Id, default); await restore.Completion;
        Assert.Equal("failed", restore.View.Phase); Assert.Equal("Keep this.", await File.ReadAllTextAsync(Path.Combine(review.Review.Destination, "keep.txt")));
        review = await restore.Review(id, default); restore.Begin(review.Review!.Id, new CancellationToken(true)); await restore.Completion;
        Assert.Equal("failed", restore.View.Phase); Assert.False(Directory.Exists(review.Review.Destination));
    }
    [Theory] [InlineData("../other")] [InlineData("receipt.json")] [InlineData("")]
    public async Task ReceiptSelectionIsAnOpaqueId(string selection)
    { await Assert.ThrowsAsync<ArgumentException>(() => new GuidedRestore(plan).Review(selection, default)); }
    [Fact] public async Task ForeignBackupReceiptsAreNotAnArbitraryFilesystemReader()
    {
        var receipt = await CreateBackup();
        await File.WriteAllTextAsync(Path.Combine(backupRoot, id + ".receipt.json"), Wire.Pack(receipt with { Directory = source }));
        var restore = new GuidedRestore(plan); Assert.False(Assert.Single(await restore.Backups(default)).Available);
        await Assert.ThrowsAsync<ArgumentException>(() => restore.Review(id, default));
    }
    [Fact] public async Task LauncherPreparationUsesASeparateFolderAndPreservesTheExactRestoredData()
    {
        await CreateBackup(); var package = Path.Combine(root, "package ' with $ and spaces"); Directory.CreateDirectory(Path.Combine(package, "wwwroot"));
        await File.WriteAllTextAsync(Path.Combine(package, "wwwroot/index.html"), "Fixture package");
        await File.WriteAllTextAsync(Path.Combine(package, OperatingSystem.IsWindows() ? "Thaddeus.Host.exe" : "Thaddeus.Host"), "Fixture, never executed.");
        await File.WriteAllTextAsync(Path.Combine(package, "launch-host.ps1"), "Fixture, never executed.");
        var restore = new GuidedRestore(plan with { Launch = new(package, source, plan.Origin, 5183, null, true) });
        var review = await restore.Review(id, default); Assert.True(review.Review!.CanPrepareLauncher);
        restore.Begin(review.Review.Id, default); await restore.Completion;
        Assert.Equal("restored", restore.View.Phase); var launcher = Assert.IsType<RestoredLauncher>(restore.View.Launcher);
        Assert.Equal(Path.GetDirectoryName(review.Review.Destination), Path.GetDirectoryName(launcher.Directory));
        Assert.Matches("^thaddeus-launcher-[a-f0-9]{32}$", Path.GetFileName(launcher.Directory));
        Assert.DoesNotContain(Directory.GetFiles(review.Review.Destination), file => Path.GetFileName(file).Contains("launch"));
        foreach (var file in launcher.FileHashes)
            Assert.Equal(file.Value, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(launcher.Directory, file.Key)))));
        var parsed = DesktopLaunch.Parse(["--desktop", "--no-browser", "--launch-profile", launcher.Profile], package);
        Assert.Equal(review.Review.Destination, parsed!.Data); Assert.Equal(5183, parsed.WorkerPort);
    }
    [Fact] public void ReopeningAPrivateBackupFolderPreservesItsContents()
    {
        var directory = PrivateWorkerDirectory.OpenOrCreate(backupRoot);
        var marker = Path.Combine(directory, "keep.txt"); File.WriteAllText(marker, "A prior backup receipt lives here.");
        Assert.Equal(directory, PrivateWorkerDirectory.OpenOrCreate(directory));
        Assert.Equal("A prior backup receipt lives here.", File.ReadAllText(marker));
        Assert.Throws<IOException>(() => PrivateWorkerDirectory.Create(directory));
    }
    [Fact] public void BroaderBackupFolderPermissionsAreRefusedWithoutChangingThem()
    {
        PrivateWorkerDirectory.Create(backupRoot);
        if (OperatingSystem.IsWindows())
        {
            var directory = new DirectoryInfo(backupRoot); var security = directory.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.Read, AccessControlType.Allow)); directory.SetAccessControl(security);
            var before = directory.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
            Assert.Throws<InvalidOperationException>(() => PrivateWorkerDirectory.OpenOrCreate(backupRoot));
            Assert.Equal(before, directory.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All));
        }
        else
        {
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead;
            File.SetUnixFileMode(backupRoot, mode);
            Assert.Throws<InvalidOperationException>(() => PrivateWorkerDirectory.OpenOrCreate(backupRoot));
            Assert.Equal(mode, File.GetUnixFileMode(backupRoot));
        }
        Assert.Empty(Directory.GetFiles(backupRoot));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
