using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Thaddeus.Infrastructure;

/// <summary>Excludes QEMU writers while inspecting or removing an owned disk. Host processes must honor the lock protocol.</summary>
public sealed class QemuDiskLease : IDisposable
{
    public FileStream Stream { get; }
    private QemuDiskLease(FileStream stream) => Stream = stream;

    public static QemuDiskLease Open(string path, bool allowReadOnlyQemu = false)
    {
        Store.AssertNoLinks(path);
        var stream = new FileStream(path, FileMode.Open, allowReadOnlyQemu ? FileAccess.Read : FileAccess.ReadWrite,
            OperatingSystem.IsLinux() ? FileShare.ReadWrite | FileShare.Delete : allowReadOnlyQemu ? FileShare.Read : FileShare.None);
        try
        {
            if (OperatingSystem.IsLinux())
            {
                if (RuntimeInformation.ProcessArchitecture != Architecture.X64) throw new PlatformNotSupportedException("The Linux disk-lock ABI currently requires x64.");
                if (!allowReadOnlyQemu) Lock(stream.SafeFileHandle, 0, 0, 1); // Whole-file OFD write lock, including future growth.
                else
                {
                    // QEMU 11.1.0: permission bytes 100..103, unshared bytes 200..203.
                    // Claim consistent read; deny write, write-unchanged and resize before checking existing writers.
                    Lock(stream.SafeFileHandle, 100, 1, 0);
                    Lock(stream.SafeFileHandle, 201, 3, 0);
                    Test(stream.SafeFileHandle, 200, 1);
                    Test(stream.SafeFileHandle, 101, 3);
                }
            }
            return new(stream);
        }
        catch { stream.Dispose(); throw; }
    }

    private static void Lock(SafeFileHandle fd, long start, long length, short type)
    {
        var value = new Flock { Type = type, Start = start, Length = length };
        if (Fcntl(fd, 37, ref value) != 0) throw new IOException("The QEMU disk is locked or this filesystem does not support OFD locks.", new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
    }
    private static void Test(SafeFileHandle fd, long start, long length)
    {
        var value = new Flock { Type = 1, Start = start, Length = length };
        if (Fcntl(fd, 36, ref value) != 0 || value.Type != 2) throw new IOException("A QEMU writer or incompatible reader still owns this disk.");
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Flock { public short Type, Whence; public long Start, Length; public int Pid; }
    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int Fcntl(SafeFileHandle fd, int command, ref Flock value);
    public void Dispose() => Stream.Dispose();
}
