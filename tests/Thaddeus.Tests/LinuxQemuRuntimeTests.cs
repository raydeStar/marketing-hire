using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class LinuxQemuRuntimeTests
{
    private const string RootMount = "1 0 8:1 / / rw - ext4 /dev/vda rw\n";
    private const string PayloadMount = "2 1 8:2 / /payload ro,nodev,nosuid - ext4 /dev/vdb ro\n";

    [Fact] public void ReadOnlyFilesystemAndReadOnlyMountAreBothRequired()
    {
        LinuxQemuRuntime.RequireReadOnlyMount("/payload/runtime", RootMount + PayloadMount);
        Assert.Throws<IOException>(() => LinuxQemuRuntime.RequireReadOnlyMount("/payload/runtime", RootMount));
        Assert.Throws<IOException>(() => LinuxQemuRuntime.RequireReadOnlyMount("/payload/runtime", RootMount + PayloadMount.Replace("/dev/vdb ro", "/dev/vdb rw")));
        Assert.Throws<IOException>(() => LinuxQemuRuntime.RequireReadOnlyMount("/payload/runtime", RootMount + PayloadMount.Replace("ro,nodev", "rw,nodev")));
    }
    [Theory]
    [InlineData("/payload/runtime/lib")][InlineData("/payload/runtime/lib/libc.so.6")]
    public void WritableFileOrDirectorySubmountCannotHideInsideReadOnlyPackage(string mount)
    {
        Assert.Throws<IOException>(() => LinuxQemuRuntime.RequireReadOnlyMount("/payload/runtime", RootMount + PayloadMount + $"3 2 8:3 / {mount} rw - ext4 /dev/vdc rw\n"));
    }
    [Fact] public void MountPathEscapesAndComponentBoundariesAreRespected()
    {
        LinuxQemuRuntime.RequireReadOnlyMount("/payload with space/runtime", RootMount + PayloadMount.Replace("/payload", "/payload\\040with\\040space"));
        Assert.Throws<IOException>(() => LinuxQemuRuntime.RequireReadOnlyMount("/payload-other/runtime", RootMount + PayloadMount));
    }
    [Fact] public void MappingsTrackExecutableFilesAndRejectUnaccountedCode()
    {
        const string maps = "1000-2000 r-xp 0000 08:01 42 /payload/lib/libc.so.6\n2000-3000 rw-p 0000 00:00 0\n3000-4000 r-xp 0000 00:00 0 [vdso]\n";
        Assert.Equal(new[] { "/payload/lib/libc.so.6" }, LinuxQemuRuntime.ExecutablePaths(maps));
        foreach (var path in new[] { "", "[anonymous]", "/payload/lib/libc.so.6 (deleted)" })
            Assert.Throws<IOException>(() => LinuxQemuRuntime.ExecutablePaths("1000-2000 r-xp 0000 08:01 42 " + path));
    }
}
