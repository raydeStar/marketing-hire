using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed record WorkerBundleReceipt(string Directory, string Runtime, int Files, long LogicalBytes, string DescriptorSha256);

/// <summary>Offline packaging of explicitly supplied pins. No guest, model or downloaded executable is run.</summary>
public static class WorkerBundlePreparation
{
    private const long MaxBytes = 16L * 1024 * 1024 * 1024;

    public static async Task<WorkerBundleReceipt> Create(QemuInstallation installation, string destination, string runtime, CancellationToken cancellation)
    {
        var kind = runtime switch { "win-x64" => "qemu-windows-runtime", "linux-x64" => "qemu-linux-x64-runtime", _ => throw new ArgumentException("Use a supported native worker target.") };
        if (!Path.IsPathFullyQualified(destination) || Path.GetPathRoot(destination) == destination || installation.RuntimePackage == null)
            throw new ArgumentException("Supply a complete pinned installation and a fresh bundle directory.");
        destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)); Store.AssertNoLinks(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("The bundle destination already exists; it will not be replaced.");
        var runtimeRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation.RuntimePackage.Root));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (destination.StartsWith(runtimeRoot + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("Keep the new bundle outside the original runtime package.");
        using var lease = await QemuRuntimeLease.Open(installation.RuntimePackage, installation.Executable, installation.ImageTool, cancellation, kind);
        var manifest = Wire.Unpack<QemuRuntimeManifest>(await File.ReadAllTextAsync(installation.RuntimePackage.Manifest.Path, cancellation));
        var document = Descriptor(installation, runtime);
        using (var parsed = JsonDocument.Parse(Wire.Pack(document))) _ = BundledWorkerInstallation.Resolve(parsed.RootElement, destination, runtime);
        var expectedBytes = checked(manifest.Files.Sum(file => new FileInfo(lease.FilePin(file.Path).Path).Length) +
            new[] { installation.RuntimePackage.Manifest, installation.Kernel, installation.Initrd, installation.BaseDisk }.Sum(file => new FileInfo(file.Path).Length));
        if (expectedBytes > MaxBytes) throw new IOException("The worker bundle exceeds its bounded storage allowance.");
        cancellation.ThrowIfCancellationRequested(); EnsureSpace(AvailableSpace(destination), expectedBytes); PrivateWorkerDirectory.Create(destination);
        long bytes = 0; var files = 0;
        async Task Copy(QemuPinnedFile source, string relative)
        {
            var path = Path.Combine(destination, relative); Store.AssertNoLinks(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            bytes += await CopyPinned(source, path, MaxBytes - bytes, cancellation); files++;
        }
        foreach (var file in manifest.Files) await Copy(lease.FilePin(file.Path), "runtime/" + file.Path);
        await Copy(installation.RuntimePackage.Manifest, "runtime-manifest.json");
        await Copy(installation.Kernel, "guest/kernel"); await Copy(installation.Initrd, "guest/initrd");
        await Copy(installation.BaseDisk, "guest/root.ext4");
        lease.VerifyInventory();
        // Write admission metadata last. An interrupted packing job must never look like an installed worker.
        var descriptor = System.Text.Encoding.UTF8.GetBytes(Wire.Pack(document));
        await using (var output = new FileStream(Path.Combine(destination, "installation.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { await output.WriteAsync(descriptor, cancellation); output.Flush(true); }
        return new(destination, runtime, files + 1, bytes + descriptor.Length, Convert.ToHexStringLower(SHA256.HashData(descriptor)));
    }

    internal static BundledWorkerDocument Descriptor(QemuInstallation installation, string runtime)
    {
        var package = installation.RuntimePackage ?? throw new ArgumentException("A worker bundle requires its complete runtime package.");
        var executable = Path.GetRelativePath(package.Root, installation.Executable.Path).Replace('\\', '/');
        var imageTool = Path.GetRelativePath(package.Root, installation.ImageTool.Path).Replace('\\', '/');
        return new(1, BundledWorkerInstallation.Kind, runtime,
            new(new("runtime/" + executable, installation.Executable.Sha256), new("runtime/" + imageTool, installation.ImageTool.Sha256),
                new("guest/kernel", installation.Kernel.Sha256), new("guest/initrd", installation.Initrd.Sha256), new("guest/root.ext4", installation.BaseDisk.Sha256),
                new("runtime", new("runtime-manifest.json", package.Manifest.Sha256))));
    }

    internal static async Task<long> CopyPinned(QemuPinnedFile pin, string destination, long maximum, CancellationToken cancellation, Func<long>? availableSpace = null)
    {
        Store.AssertNoLinks(pin.Path); Store.AssertNoLinks(destination);
        await using var source = new FileStream(pin.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var length = source.Length;
        if (length < 0 || length > maximum) throw new IOException("The worker bundle exceeds its bounded storage allowance.");
        availableSpace ??= () => AvailableSpace(destination);
        EnsureSpace(availableSpace(), Math.Min(length, 64 * 1024 * 1024));
        // A synchronous handle keeps the sparse-file IOCTL synchronous; async stream operations remain cancellable.
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1024 * 1024, FileOptions.SequentialScan);
        var compressed = OperatingSystem.IsWindows() && (File.GetAttributes(destination) & FileAttributes.Compressed) != 0;
        if (OperatingSystem.IsWindows() && !compressed && length >= 16 * 1024 * 1024 &&
            !DeviceIoControl(output.SafeFileHandle, 0x000900c4, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new IOException("The destination cannot preserve sparse worker images.", new Win32Exception(Marshal.GetLastWin32Error()));
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024]; long copied = 0, nextSpaceCheck = 0;
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellation); if (count == 0) break;
            if (copied >= nextSpaceCheck)
            {
                // Keep room for the user's running study even when another process consumes space mid-copy.
                EnsureSpace(availableSpace(), Math.Min(length - copied, 64 * 1024 * 1024)); nextSpaceCheck = copied + 64 * 1024 * 1024;
            }
            copied += count; if (copied > length) throw new IOException("A worker input grew while it was being packaged.");
            digest.AppendData(buffer, 0, count);
            if (!compressed && buffer.AsSpan(0, count).IndexOfAnyExcept((byte)0) < 0) output.Seek(count, SeekOrigin.Current);
            else await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
        }
        if (copied != length || Convert.ToHexStringLower(digest.GetHashAndReset()) != pin.Sha256)
            throw new IOException("A worker input differs from its supplied pin. The incomplete copy remains for inspection.");
        output.SetLength(length); output.Flush(true); output.Position = 0;
        if (Convert.ToHexStringLower(await SHA256.HashDataAsync(output, cancellation)) != pin.Sha256)
            throw new IOException("The copied worker file did not verify.");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead |
            (File.GetUnixFileMode(pin.Path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)));
        return length;
    }

    internal static long AvailableSpace(string path)
        => StorageSpace.Available(path);

    private static void EnsureSpace(long available, long nextBytes)
    {
        if (available < 10L * 1024 * 1024 * 1024 + nextBytes)
            throw new IOException("Worker preparation needs room for its copy plus 10 GiB of free space. Use a destination with more room; the incomplete bundle is not enabled.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle file, uint code, IntPtr input, uint inputBytes,
        IntPtr output, uint outputBytes, out uint returned, IntPtr overlapped);
}
