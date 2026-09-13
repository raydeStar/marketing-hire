using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

/// <summary>Owned, retired workspace storage only. No executable, VM boot, shell or inference is involved.</summary>
public sealed class QemuWorkspaceStorage(Store store, Action<string>? testFault = null) : IResearchWorkspaceStorage
{
    private sealed record Entry(string RelativePath, long Bytes, long Modified);
    private sealed record Inventory(string Directory, SandboxRegistration Registration, Entry[] Files, string[] Directories, string Digest);
    private static readonly Regex WorkerImage = new(@"\Athaddeus-qemu@sha256:[a-f0-9]{64}\z");
    private static readonly Regex Boot = new(@"\Aboot-[a-f0-9]{32}\z");
    private static readonly Regex Receipt = new(@"\A(?:termination-[0-9]+|recovery-[a-f0-9]{32})\.json\z");
    private static readonly Regex LinuxService = new(@"\Alinux-service-[a-f0-9]{32}\.json\z");

    private string WorkerId(Run run)
    {
        if (!Regex.IsMatch(run.Id, @"\A[a-f0-9]{32}\z") || run.Execution?.SandboxId != "thaddeus-" + run.Id ||
            run.Execution.Backend != "openclaw" || run.Research?.Phase != "finished")
            throw new InvalidOperationException("Finish or cancel this research task before removing its workspace.");
        return run.Execution.SandboxId;
    }
    private FileStream Ownership()
    {
        var path = Path.Combine(store.Root, "qemu-owner.lock"); Store.AssertNoLinks(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private WorkspaceRemoval? Prior(Run run) => store.Setting("workspace-removal:" + run.Id) is { } saved ? Wire.Unpack<WorkspaceRemoval>(saved) : null;

    private Inventory InspectOwned(Run run)
    {
        var id = WorkerId(run);
        var registered = Wire.Unpack<SandboxRegistration>(store.Setting("sandbox:" + id) ?? throw new InvalidOperationException("Workspace ownership is not recorded."));
        var route = Wire.Unpack<QemuBrokerRoute>(store.Setting("qemu-route:" + id) ?? throw new InvalidOperationException("Workspace task binding is missing."));
        if (registered.Spec.Id != id || !WorkerImage.IsMatch(registered.Spec.Image) || route.RunId != run.Id ||
            registered.Status is not ("retired" or "purge-incomplete" or "purged"))
            throw new InvalidOperationException("Only a registered, retired workspace can be removed.");
        var root = Path.GetFullPath(Path.Combine(store.Root, "qemu-" + id));
        if (Path.GetDirectoryName(root) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(store.Root))) throw new IOException("Workspace is outside its owned directory.");
        Store.AssertNoLinks(root);
        var prior = Prior(run);
        if (prior != null && (prior.RunId != run.Id || prior.WorkerId != id)) throw new IOException("Removal receipt belongs to a different workspace.");
        if (prior != null && (prior.Status is not ("removed" or "removal-incomplete") ||
            !Regex.IsMatch(prior.Digest, @"\A[a-f0-9]{64}\z") || prior.Status == "removed" && prior.Verified == null))
            throw new IOException("Removal receipt is not a recognized intention or verified result.");
        var files = new List<Entry>(); var directories = new List<string>();
        if (!Directory.Exists(root))
        {
            if (File.Exists(root) || prior == null) throw new IOException("Workspace absence has no recorded removal intention.");
        }
        else
        {
            if (registered.Status == "purged") throw new IOException("A removed workspace path reappeared; inspect it before further action.");
            var children = Directory.EnumerateFileSystemEntries(root).Take(514).ToArray();
            if (children.Length > 513) throw new IOException("Workspace inventory limit exceeded.");
            var boots = 0;
            foreach (var path in children)
            {
                Store.AssertNoLinks(path); var name = Path.GetFileName(path);
                if (Directory.Exists(path))
                {
                    if (!Boot.IsMatch(name) || ++boots > 256) throw new IOException("Unexpected workspace directory.");
                    directories.Add(name);
                    var contents = Directory.EnumerateFileSystemEntries(path).Take(5).ToArray();
                    if (contents.Length > 4) throw new IOException("Unexpected boot entries; reconcile credentials before removal.");
                    foreach (var file in contents)
                    {
                        if (Path.GetFileName(file) == "%SystemDrive%") { EmptyCacheDirectories(file); continue; }
                        if (Path.GetFileName(file) is not ("observation.json" or "console.log" or "qemu-stderr.log") && !LinuxService.IsMatch(Path.GetFileName(file)))
                            throw new IOException("Unexpected boot entry; reconcile credentials before removal.");
                        Add(file);
                    }
                }
                else
                {
                    if (name != "worker.qcow2" && !Receipt.IsMatch(name) && !LinuxService.IsMatch(name)) throw new IOException("Unexpected workspace file.");
                    Add(path);
                }
            }
            if (prior == null && files.All(file => file.RelativePath != "worker.qcow2")) throw new IOException("The private overlay is missing without a removal intention.");
        }
        var ordered = files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
        var folders = directories.Order(StringComparer.Ordinal).ToArray();
        var digest = Wire.Hash(Wire.Pack(new { run.Id, worker = id, registered.Spec.Image, registered.Status, registered.Updated, files = ordered, directories = folders }));
        return new(root, registered, ordered, folders, digest);

        void Add(string path)
        {
            Store.AssertNoLinks(path);
            if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) throw new IOException("Workspace entry is not a regular file.");
            var info = new FileInfo(path);
            files.Add(new(Path.GetRelativePath(root, path).Replace('\\', '/'), info.Length, info.LastWriteTimeUtc.Ticks));
        }
        void EmptyCacheDirectories(string path)
        {
            // Observed with the pinned Windows QEMU process's minimal environment. Only the exact empty tree is owned.
            foreach (var child in new string?[] { "ProgramData", "Microsoft", "Windows", "Caches", null })
            {
                Store.AssertNoLinks(path);
                if (!Directory.Exists(path)) throw new IOException("Unexpected cache entry.");
                directories.Add(Path.GetRelativePath(root, path).Replace('\\', '/'));
                var entries = Directory.EnumerateFileSystemEntries(path).Take(2).ToArray();
                if (child == null)
                {
                    if (entries.Length != 0) throw new IOException("Cache directory is not empty.");
                }
                else
                {
                    if (entries.Length != 1 || Path.GetFileName(entries[0]) != child) throw new IOException("Unexpected cache directory contents.");
                    path = entries[0];
                }
            }
        }
    }
    public WorkspaceReview Inspect(Run run)
    {
        try
        {
            using var lease = Ownership(); var inventory = InspectOwned(run);
            var deleted = inventory.Registration.Status == "purged" && !Directory.Exists(inventory.Directory) && Prior(run)?.Status == "removed";
            return new(run.Id, WorkerId(run), "qemu", deleted ? "removed" : Prior(run) == null ? "retained" : "removal-incomplete",
                inventory.Files.Length, inventory.Files.Sum(file => file.Bytes), inventory.Digest, !deleted,
                deleted ? "Private workspace removal is verified. Imported notes and task receipts remain." : "Removes the private disk, native transcript and worker logs. Imported notes and task receipts remain.", deleted ? Prior(run) : null);
        }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException)
        { throw new InvalidOperationException("Workspace inspection could not confirm a safe inventory. Finish active work and inspect worker receipts; no removal started."); }
    }
    public WorkspaceRemoval Remove(Run run, string digest, CancellationToken cancellation)
    {
        using var lease = Ownership(); var inventory = InspectOwned(run);
        if (inventory.Registration.Status == "purged" && Prior(run) is { Status: "removed" } completed && completed.Digest == digest) return completed;
        if (digest != inventory.Digest) throw new InvalidOperationException("Workspace inventory changed. Inspect it again before confirming removal.");
        if (inventory.Registration.Status == "purged") return Prior(run) ?? throw new InvalidOperationException("Removal receipt is missing.");
        cancellation.ThrowIfCancellationRequested();
        using var linuxDisk = OperatingSystem.IsLinux() && inventory.Files.Any(file => file.RelativePath == "worker.qcow2")
            ? QemuDiskLease.Open(Path.Combine(inventory.Directory, "worker.qcow2")) : null;
        if (Directory.Exists(inventory.Directory)) LinuxServiceOwnership.AssertWorkspaceStopped(inventory.Directory,
            requireRecords: store.Setting("qemu-host:" + WorkerId(run)) == "qemu-kvm" && Prior(run) == null);
        // Open every file before the first removal. A locked file must not cause a partly deleted workspace.
        var probes = new List<FileStream>();
        try
        {
            foreach (var entry in inventory.Files)
            {
                if (linuxDisk != null && entry.RelativePath == "worker.qcow2") continue; // The stronger OFD lease is already held through unlink.
                probes.Add(new FileStream(Path.Combine(inventory.Directory, entry.RelativePath), FileMode.Open, FileAccess.Read, FileShare.None));
            }
        }
        finally { foreach (var probe in probes) probe.Dispose(); }
        var removal = new WorkspaceRemoval(run.Id, WorkerId(run), digest, "removal-incomplete", DateTimeOffset.UtcNow);
        store.Setting("workspace-removal:" + run.Id, Wire.Pack(removal));
        store.Setting("sandbox:" + removal.WorkerId, Wire.Pack(inventory.Registration with { Status = "purge-incomplete", Updated = DateTimeOffset.UtcNow }));
        try
        {
            foreach (var entry in inventory.Files)
            {
                cancellation.ThrowIfCancellationRequested();
                var path = Path.Combine(inventory.Directory, entry.RelativePath); Store.AssertNoLinks(path);
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != entry.Bytes || info.LastWriteTimeUtc.Ticks != entry.Modified) throw new IOException("Workspace changed during removal.");
                File.Delete(path);
                testFault?.Invoke("after-file");
            }
            foreach (var relative in inventory.Directories.OrderByDescending(path => path.Count(character => character == '/')).ThenBy(path => path, StringComparer.Ordinal))
            {
                var directory = Path.Combine(inventory.Directory, relative); Store.AssertNoLinks(directory);
                Directory.Delete(directory, recursive: false);
            }
            Store.AssertNoLinks(inventory.Directory);
            if (Directory.Exists(inventory.Directory)) Directory.Delete(inventory.Directory, recursive: false);
            if (Directory.Exists(inventory.Directory) || File.Exists(inventory.Directory)) throw new IOException("Workspace removal was not confirmed.");
            removal = removal with { Status = "removed", Verified = DateTimeOffset.UtcNow };
            store.Setting("workspace-removal:" + run.Id, Wire.Pack(removal));
            store.Setting("sandbox:" + removal.WorkerId, Wire.Pack(inventory.Registration with { Status = "purged", Updated = DateTimeOffset.UtcNow }));
            return removal;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            store.Setting("workspace-removal:" + run.Id, Wire.Pack(removal with { Failure = error.GetType().Name }));
            throw new InvalidOperationException("Workspace removal is incomplete. Remaining files and the removal receipt were retained. Inspect again before another attempt.");
        }
    }
}
