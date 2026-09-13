using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class WorkspaceStorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-workspace-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    private readonly QemuWorkspaceStorage storage;
    public WorkspaceStorageTests() { store = new(root); storage = new(store); }
    private (Run Run, string Directory) Retained() => Retained(store);
    internal static (Run Run, string Directory) Retained(Store store)
    {
        var run = new Run { Goal = new("Fictional completed research", [], "plans/", [], new(), new()), State = RunState.Succeeded,
            Research = new("finished", "Fixture workspace retained", WorkerRetained: true) };
        var id = "thaddeus-" + run.Id;
        run.Execution = new("openclaw", id, "agent:thaddeus:" + run.Id, OpenClawBackend.PinnedVersion);
        store.Save(run, "fixture.retained", new { });
        store.Setting("sandbox:" + id, Wire.Pack(new SandboxRegistration(new(id, "thaddeus-qemu@sha256:" + new string('a', 64)), "retired", DateTimeOffset.UtcNow)));
        store.Setting("qemu-route:" + id, Wire.Pack(new QemuBrokerRoute(run.Id, 5182)));
        store.Setting("active-sandbox", id);
        var directory = PrivateWorkerDirectory.Create(Path.Combine(store.Root, "qemu-" + id));
        File.WriteAllText(Path.Combine(directory, "worker.qcow2"), "Fictional disk with a private transcript.");
        File.WriteAllText(Path.Combine(directory, "termination-123.json"), "{}");
        var boot = Directory.CreateDirectory(Path.Combine(directory, "boot-" + new string('b', 32))).FullName;
        File.WriteAllText(Path.Combine(boot, "console.log"), "Fictional private worker output.");
        File.WriteAllText(Path.Combine(boot, "observation.json"), "{}");
        return (run, directory);
    }

    [Fact] public async Task RetiredWorkspaceRemovedWithoutRuntimeAndImportedNotesRemainUntilSeparateDataDeletion()
    {
        var (run, directory) = Retained();
        store.Write("plans/result.md", "Approved import", "absent");
        store.Setting("provider", Wire.Pack(new ProviderSnapshot())); store.Setting("sessions", "[]");
        store.Setting("qemu-observation:" + run.Execution!.SandboxId, "private diagnostic");
        store.Setting("qemu-host:" + run.Execution.SandboxId, "qemu-whpx");
        var runtime = new Runtime(store, _ => throw new Exception("Cleanup must never invoke a model."), new PlanValidator(), new EvidencePolicy());
        await using var coordinator = new ResearchCoordinator(store, runtime, new(store), new UnavailableResearchFactory(), storage);
        var preview = await coordinator.InspectWorkspace(run.Id, default);
        Assert.Equal(4, preview.Files); Assert.True(preview.CanRemove); Assert.True(preview.Bytes > 0);
        var saved = await coordinator.RemoveWorkspace(run.Id, preview.Digest, default);
        Assert.False(Directory.Exists(directory)); Assert.False(saved.Research!.WorkerRetained);
        Assert.Equal("Approved import", store.Page("plans/result.md")!.Content);
        Assert.Equal("removed", storage.Inspect(saved).Status);
        await coordinator.RemoveWorkspace(run.Id, preview.Digest, default);
        Assert.Single(store.AllEvents(), item => item.Type == "research.workspace.removed");
        await coordinator.DeletePersonalData(default);
        Assert.Empty(store.List()); Assert.Empty(store.Pages()); Assert.Null(store.Setting("active-sandbox"));
        Assert.Null(store.Setting("workspace-removal:" + run.Id)); Assert.Null(store.Setting("qemu-observation:" + run.Execution.SandboxId));
        Assert.Null(store.Setting("qemu-host:" + run.Execution.SandboxId));
        Assert.NotNull(store.Setting("provider")); Assert.NotNull(store.Setting("sessions"));
    }

    [Fact] public void OlderRetiredWorkspaceCanBeRemovedWithoutTouchingNewerRegistrationOrFiles()
    {
        var older = Retained(); var newer = Retained();
        storage.Remove(older.Run, storage.Inspect(older.Run).Digest, default);
        Assert.False(Directory.Exists(older.Directory)); Assert.True(File.Exists(Path.Combine(newer.Directory, "worker.qcow2")));
        Assert.Equal(newer.Run.Execution!.SandboxId, store.Setting("active-sandbox"));
    }

    [Theory] [InlineData("modified")] [InlineData("extra-file")] [InlineData("credentials")] [InlineData("wrong-route")] [InlineData("active")]
    public void InventoryOrOwnershipDriftRefusesRemovalBeforeAnyFileIsDeleted(string drift)
    {
        var (run, directory) = Retained(); var preview = storage.Inspect(run);
        if (drift == "modified") File.AppendAllText(Path.Combine(directory, "worker.qcow2"), " changed");
        if (drift == "extra-file") File.WriteAllText(Path.Combine(directory, "unrelated.txt"), "Preserve me");
        if (drift == "credentials") Directory.CreateDirectory(Path.Combine(directory, "boot-" + new string('b', 32), "control"));
        if (drift == "wrong-route") store.Setting("qemu-route:" + run.Execution!.SandboxId, Wire.Pack(new QemuBrokerRoute(new string('c', 32), 5182)));
        if (drift == "active") store.Setting("sandbox:" + run.Execution!.SandboxId, Wire.Pack(new SandboxRegistration(new(run.Execution.SandboxId, "thaddeus-qemu@sha256:" + new string('a', 64)), "running-unqualified", DateTimeOffset.UtcNow)));
        Assert.ThrowsAny<Exception>(() => storage.Remove(run, preview.Digest, default));
        Assert.True(File.Exists(Path.Combine(directory, "worker.qcow2"))); Assert.True(File.Exists(Path.Combine(directory, "termination-123.json")));
        Assert.Null(store.Setting("workspace-removal:" + run.Id));
    }

    [Fact] public void ActiveOwnershipLeasePreventsEvenInspectingTheDeletionInventory()
    {
        var (run, directory) = Retained(); using var lease = new FileStream(Path.Combine(root, "qemu-owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<InvalidOperationException>(() => storage.Inspect(run)); Assert.True(Directory.Exists(directory));
    }

    [Fact] public void LockedFileIsDetectedBeforeRemovingAnyOtherFile()
    {
        var (run, directory) = Retained(); var preview = storage.Inspect(run);
        using var held = new FileStream(Path.Combine(directory, "worker.qcow2"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => storage.Remove(run, preview.Digest, default));
        Assert.True(File.Exists(Path.Combine(directory, "termination-123.json"))); Assert.Null(store.Setting("workspace-removal:" + run.Id));
    }

    [Fact] public async Task RestartAfterVerifiedPhysicalRemovalReconcilesOnlyTheDurableTaskFlag()
    {
        var (run, directory) = Retained(); storage.Remove(run, storage.Inspect(run).Digest, default);
        Assert.True(store.Get(run.Id)!.Research!.WorkerRetained); Assert.False(Directory.Exists(directory));
        var runtime = new Runtime(store, _ => throw new Exception("No model during recovery."), new PlanValidator(), new EvidencePolicy());
        await using var coordinator = new ResearchCoordinator(store, runtime, new(store), new UnavailableResearchFactory(), storage);
        await coordinator.Initialize();
        Assert.False(store.Get(run.Id)!.Research!.WorkerRetained); Assert.Single(store.AllEvents(), item => item.Type == "research.workspace.removed");
    }

    [Fact] public void PartialRemovalRequiresFreshReviewAndDoesNotRecreateMissingFiles()
    {
        var (run, directory) = Retained(); var original = storage.Inspect(run);
        store.Setting("workspace-removal:" + run.Id, Wire.Pack(new WorkspaceRemoval(run.Id, run.Execution!.SandboxId, original.Digest, "removal-incomplete", DateTimeOffset.UtcNow)));
        File.Delete(Path.Combine(directory, "worker.qcow2"));
        Assert.Throws<InvalidOperationException>(() => storage.Remove(run, original.Digest, default));
        var remaining = storage.Inspect(run); Assert.Equal("removal-incomplete", remaining.Status); Assert.Equal(3, remaining.Files);
        storage.Remove(run, remaining.Digest, default); Assert.False(Directory.Exists(directory));
    }

    [Fact] public void MissingWorkspaceWithoutRemovalIntentCannotBeReportedAsDeleted()
    {
        var (run, directory) = Retained(); Directory.Delete(directory, true);
        Assert.Throws<InvalidOperationException>(() => storage.Inspect(run)); Assert.True(store.Get(run.Id)!.Research!.WorkerRetained);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ObservedEmptyWindowsCacheTreeIsOwnedButAnyContentsAreRefused(bool unexpectedFile)
    {
        var (run, directory) = Retained();
        var cache = Directory.CreateDirectory(Path.Combine(directory, "boot-" + new string('b', 32), "%SystemDrive%", "ProgramData", "Microsoft", "Windows", "Caches")).FullName;
        if (unexpectedFile)
        {
            File.WriteAllText(Path.Combine(cache, "preserve.txt"), "Unexpected contents must survive.");
            Assert.Throws<InvalidOperationException>(() => storage.Inspect(run));
            Assert.True(File.Exists(Path.Combine(directory, "worker.qcow2")));
        }
        else
        {
            storage.Remove(run, storage.Inspect(run).Digest, default);
            Assert.False(Directory.Exists(directory));
        }
    }

    [Fact] public async Task InterruptedPhysicalRemovalRetainsIntentAndDoesNotContinueDuringStartup()
    {
        var (run, directory) = Retained(); var reviewed = storage.Inspect(run);
        var failing = new QemuWorkspaceStorage(store, point => { if (point == "after-file") throw new IOException("Injected interruption after an actual file removal."); });
        Assert.Throws<InvalidOperationException>(() => failing.Remove(run, reviewed.Digest, default));
        var receipt = Wire.Unpack<WorkspaceRemoval>(store.Setting("workspace-removal:" + run.Id)!);
        Assert.Equal("removal-incomplete", receipt.Status); Assert.Equal(nameof(IOException), receipt.Failure); Assert.Null(receipt.Verified);
        Assert.Equal(3, Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length);
        var runtime = new Runtime(store, _ => throw new Exception("No inference during maintenance."), new PlanValidator(), new EvidencePolicy());
        await using var coordinator = new ResearchCoordinator(store, runtime, new(store), new UnavailableResearchFactory(), storage);
        await coordinator.Initialize();
        Assert.True(store.Get(run.Id)!.Research!.WorkerRetained);
        Assert.Equal(3, Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RemoveWorkspace(run.Id, reviewed.Digest, default));
        var remaining = await coordinator.InspectWorkspace(run.Id, default);
        await coordinator.RemoveWorkspace(run.Id, remaining.Digest, default);
        Assert.False(Directory.Exists(directory)); Assert.False(store.Get(run.Id)!.Research!.WorkerRetained);
    }

    [Theory] [InlineData("files")] [InlineData("registration")]
    public void OrphanedWorkerStatePreventsPersonalDataDeletion(string state)
    {
        store.Write("notes/preserve.md", "Do not erase the ledger yet.", "absent");
        if (state == "files") Directory.CreateDirectory(Path.Combine(root, "qemu-thaddeus-" + new string('d', 32)));
        else store.Setting("sandbox:thaddeus-" + new string('d', 32), "{}");
        Assert.Throws<InvalidOperationException>(() => store.DeletePersonalData());
        Assert.Equal("Do not erase the ledger yet.", store.Page("notes/preserve.md")!.Content);
    }

    [Fact] public void LinkedWorkerDirectoryCannotDeleteItsTarget()
    {
        var (run, directory) = Retained(); var target = Directory.CreateDirectory(Path.Combine(root, "unrelated")).FullName;
        File.WriteAllText(Path.Combine(target, "console.log"), "Preserve me");
        var link = Path.Combine(directory, "boot-" + new string('c', 32));
        if (OperatingSystem.IsWindows())
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
                { ArgumentList = { "/c", "mklink", "/J", link, target }, CreateNoWindow = true, RedirectStandardOutput = true })!;
            process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, target);
        try { Assert.Throws<InvalidOperationException>(() => storage.Inspect(run)); Assert.Equal("Preserve me", File.ReadAllText(Path.Combine(target, "console.log"))); }
        finally { Directory.Delete(link, recursive: false); }
    }

    [Fact] public void DataDeletionRefusesRetainedOwnershipEvenWhenCallerBypassesCoordinator()
    {
        var (run, directory) = Retained(); Assert.Throws<InvalidOperationException>(store.DeletePersonalData);
        run.Research = run.Research! with { WorkerRetained = false }; store.Save(run, "fixture.false-flag", new { });
        Assert.Throws<InvalidOperationException>(store.DeletePersonalData);
        Assert.True(Directory.Exists(directory)); Assert.NotNull(store.Get(run.Id));
    }
    public void Dispose() { store.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
}
