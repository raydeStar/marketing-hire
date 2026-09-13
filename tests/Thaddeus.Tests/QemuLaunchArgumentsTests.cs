using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class QemuLaunchArgumentsTests
{
    private static readonly SandboxSpec Spec = new("thaddeus-" + new string('a', 32), "worker@sha256:" + new string('b', 64));
    private static readonly QemuBootFiles Files = new("qemu", "/worker/kernel", "/worker/initrd", "/worker/base.raw", "/worker/overlay.qcow2");
    private static readonly QemuCharacterChannel Control = new(Path.GetFullPath("fixture/control"), 18001);
    private static readonly QemuCharacterChannel Console = new(Path.GetFullPath("fixture/console"), 18002);
    private static string[] Values(IReadOnlyList<string> arguments, string option) => arguments
        .Select((value, index) => (value, index)).Where(entry => entry.value == option)
        .Select(entry => arguments[entry.index + 1]).ToArray();

    [Fact]
    public void WindowsArgumentsPreserveThePreviousProductionContract()
    {
        // Frozen from QemuWorkerSession at 468b5ca. The raven checks every item on the old packing list.
        string[] expected = [
            "-name", Spec.Id, "-machine", "q35", "-accel", "whpx", "-cpu", "qemu64,-svm",
            "-m", "4096", "-smp", "2", "-nodefaults", "-nic", "none", "-display", "none", "-monitor", "none", "-no-reboot", "-qmp", "stdio", "-S",
            "-object", """{"qom-type":"tls-creds-x509","id":"tls-console","endpoint":"client","dir":DIRECTORY,"verify-peer":true}""".Replace("DIRECTORY", JsonSerializer.Serialize(Console.CredentialsDirectory)),
            "-chardev", "socket,id=console,host=127.0.0.1,port=18002,tls-creds=tls-console",
            "-object", """{"qom-type":"tls-creds-x509","id":"tls-control","endpoint":"client","dir":DIRECTORY,"verify-peer":true}""".Replace("DIRECTORY", JsonSerializer.Serialize(Control.CredentialsDirectory)),
            "-chardev", "socket,id=control,host=127.0.0.1,port=18001,tls-creds=tls-control",
            "-serial", "chardev:console", "-device", "virtio-serial-pci,id=transport",
            "-device", "virtserialport,chardev=control,name=org.thaddeus.control",
            "-kernel", "/worker/kernel", "-initrd", "/worker/initrd",
            "-append", "console=ttyS0,115200 root=/dev/vda rootfstype=ext4 rootflags=rw modules=virtio_blk,ext4 init=/opt/thaddeus/vm/init quiet",
            "-blockdev", """{"driver":"file","filename":"/worker/base.raw","node-name":"base-file","read-only":true}""",
            "-blockdev", """{"driver":"raw","file":"base-file","node-name":"base","read-only":true}""",
            "-blockdev", """{"driver":"file","filename":"/worker/overlay.qcow2","node-name":"overlay-file"}""",
            "-blockdev", """{"driver":"qcow2","file":"overlay-file","backing":"base","node-name":"worker"}""",
            "-device", "virtio-blk-pci,drive=worker"
        ];
        Assert.Equal(expected, QemuLaunchArguments.Build(QemuHostTarget.WindowsX64, Files, Spec, Control, Console));
    }

    [Theory]
    [InlineData(QemuHostTarget.WindowsX64, "x86_64", "q35", "whpx", "qemu64,-svm", "ttyS0")]
    [InlineData(QemuHostTarget.LinuxX64, "x86_64", "q35", "kvm", "host", "ttyS0")]
    [InlineData(QemuHostTarget.LinuxArm64, "aarch64", "virt", "kvm", "host", "ttyAMA0")]
    [InlineData(QemuHostTarget.MacX64, "x86_64", "q35", "hvf", "host", "ttyS0")]
    [InlineData(QemuHostTarget.MacArm64, "aarch64", "virt", "hvf", "host", "ttyAMA0")]
    public void ExplicitPlatformPlansPreserveTheTransportAndDeviceBoundary(QemuHostTarget target, string architecture, string machine, string accelerator, string cpu, string console)
    {
        var arguments = QemuLaunchArguments.Build(target, Files, Spec, Control, Console);
        Assert.Equal(architecture, QemuPlatformPlan.For(target).GuestArchitecture);
        Assert.Equal([machine], Values(arguments, "-machine"));
        Assert.Equal([accelerator], Values(arguments, "-accel"));
        Assert.Equal([cpu], Values(arguments, "-cpu"));
        Assert.StartsWith($"console={console},115200 ", Assert.Single(Values(arguments, "-append")));
        Assert.Equal(["none"], Values(arguments, "-nic"));
        Assert.Equal(["none"], Values(arguments, "-display"));
        Assert.Equal(["none"], Values(arguments, "-monitor"));
        Assert.Equal(["stdio"], Values(arguments, "-qmp"));
        Assert.Contains("-nodefaults", arguments); Assert.Contains("-S", arguments);
        Assert.Equal(["virtio-serial-pci,id=transport", "virtserialport,chardev=control,name=org.thaddeus.control", "virtio-blk-pci,drive=worker"], Values(arguments, "-device"));
        Assert.DoesNotContain(arguments, value => new[] { "-netdev", "-fsdev", "-virtfs", "-drive", "-vnc", "-spice", "-gdb" }.Contains(value));
        Assert.All(Values(arguments, "-chardev"), value => Assert.Contains("host=127.0.0.1,", value));
        Assert.All(Values(arguments, "-object"), value =>
        {
            using var json = JsonDocument.Parse(value);
            Assert.True(json.RootElement.GetProperty("verify-peer").GetBoolean());
            Assert.Equal("client", json.RootElement.GetProperty("endpoint").GetString());
        });
        var disks = Values(arguments, "-blockdev").Select(value => JsonSerializer.Deserialize<JsonElement>(value)).ToArray();
        Assert.Equal(4, disks.Length);
        Assert.True(disks[0].GetProperty("read-only").GetBoolean());
        Assert.True(disks[1].GetProperty("read-only").GetBoolean());
        Assert.Equal("base", disks[3].GetProperty("backing").GetString());
    }

    [Fact]
    public void QemuOptionCharactersInPathsRemainJsonData()
    {
        var files = Files with { BaseDisk = "/worker/base,\"read-only\":false.raw", Overlay = "/worker/overlay,backing=other.qcow2" };
        var control = Control with { CredentialsDirectory = Path.GetFullPath("fixture/control,server=on") };
        var arguments = QemuLaunchArguments.Build(QemuHostTarget.LinuxX64, files, Spec, control, Console);
        var blocks = Values(arguments, "-blockdev").Select(value => JsonSerializer.Deserialize<JsonElement>(value)).ToArray();
        Assert.Equal(files.BaseDisk, blocks[0].GetProperty("filename").GetString());
        Assert.True(blocks[0].GetProperty("read-only").GetBoolean());
        Assert.Equal(files.Overlay, blocks[2].GetProperty("filename").GetString());
        Assert.Equal("base", blocks[3].GetProperty("backing").GetString());
        var credentials = JsonSerializer.Deserialize<JsonElement>(Values(arguments, "-object")[1]);
        Assert.Equal(control.CredentialsDirectory, credentials.GetProperty("dir").GetString());
        Assert.True(credentials.GetProperty("verify-peer").GetBoolean());
        Assert.Equal(2, Values(arguments, "-chardev").Length);
    }

    [Theory]
    [InlineData(0, 4096)][InlineData(5, 4096)][InlineData(2, 1023)][InlineData(2, 8193)]
    public void ExcessResourcesCannotReachAnArgumentVector(int cpus, int memory) =>
        Assert.Throws<ArgumentException>(() => QemuLaunchArguments.Build(QemuHostTarget.WindowsX64, Files, Spec with { Cpus = cpus, MemoryMiB = memory }, Control, Console));

    [Theory]
    [InlineData(0)][InlineData(1023)][InlineData(65536)][InlineData(18002)]
    public void InvalidOrSharedChannelPortsAreRefused(int port) =>
        Assert.Throws<ArgumentException>(() => QemuLaunchArguments.Build(QemuHostTarget.WindowsX64, Files, Spec, Control with { Port = port }, Console));

    [Fact]
    public void RelativeAndAliasedCredentialDirectoriesAreRefused()
    {
        foreach (var directory in new[] { "relative", Console.CredentialsDirectory, Path.Combine(Console.CredentialsDirectory, "."), Console.CredentialsDirectory + Path.DirectorySeparatorChar })
            Assert.Throws<ArgumentException>(() => QemuLaunchArguments.Build(QemuHostTarget.WindowsX64, Files, Spec, Control with { CredentialsDirectory = directory }, Console));
        if (OperatingSystem.IsWindows())
            Assert.Throws<ArgumentException>(() => QemuLaunchArguments.Build(QemuHostTarget.WindowsX64, Files, Spec, Control with { CredentialsDirectory = Console.CredentialsDirectory.ToUpperInvariant() }, Console));
    }

    [Fact]
    public void UnknownPlatformCannotSelectAFallback() =>
        Assert.Throws<ArgumentException>(() => QemuLaunchArguments.Build((QemuHostTarget)999, Files, Spec, Control, Console));
}
