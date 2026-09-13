using System.Globalization;
using System.Text.Json;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public enum QemuHostTarget { WindowsX64, LinuxX64, LinuxArm64, MacX64, MacArm64 }
public sealed record QemuCharacterChannel(string CredentialsDirectory, int Port);

/// <summary>Native VM hardware choices. A launch plan does not certify the host's process or resource boundary.</summary>
public sealed record QemuPlatformPlan(QemuHostTarget Target, string GuestArchitecture, string Machine, string Accelerator, string Cpu, string Console)
{
    public string KernelCommandLine => $"console={Console},115200 root=/dev/vda rootfstype=ext4 rootflags=rw modules=virtio_blk,ext4 init=/opt/thaddeus/vm/init quiet";
    public static QemuPlatformPlan For(QemuHostTarget target) => target switch
    {
        QemuHostTarget.WindowsX64 => new(target, "x86_64", "q35", "whpx", "qemu64,-svm", "ttyS0"),
        QemuHostTarget.LinuxX64 => new(target, "x86_64", "q35", "kvm", "host", "ttyS0"),
        QemuHostTarget.LinuxArm64 => new(target, "aarch64", "virt", "kvm", "host", "ttyAMA0"),
        QemuHostTarget.MacX64 => new(target, "x86_64", "q35", "hvf", "host", "ttyS0"),
        QemuHostTarget.MacArm64 => new(target, "aarch64", "virt", "hvf", "host", "ttyAMA0"),
        _ => throw new ArgumentException("Unsupported VM host target.")
    };
}

public static class QemuLaunchArguments
{
    public static IReadOnlyList<string> Build(QemuHostTarget target, QemuBootFiles files, SandboxSpec spec, QemuCharacterChannel control, QemuCharacterChannel console)
    {
        DockerSandboxBackend.ValidateSpec(spec);
        var platform = QemuPlatformPlan.For(target);
        foreach (var channel in new[] { control, console })
            if (!Path.IsPathFullyQualified(channel.CredentialsDirectory) || channel.Port is < 1024 or > 65535)
                throw new ArgumentException("Use a private channel directory and unprivileged loopback port.");
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (control.Port == console.Port || string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(control.CredentialsDirectory)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(console.CredentialsDirectory)), pathComparison))
            throw new ArgumentException("The control and console channels need separate endpoints and credentials.");
        var arguments = new List<string> { "-name", spec.Id, "-machine", platform.Machine, "-accel", platform.Accelerator, "-cpu", platform.Cpu,
            "-m", spec.MemoryMiB.ToString(CultureInfo.InvariantCulture), "-smp", spec.Cpus.ToString(CultureInfo.InvariantCulture), "-nodefaults", "-nic", "none", "-display", "none", "-monitor", "none", "-no-reboot", "-qmp", "stdio", "-S" };
        foreach (var (name, channel) in new[] { ("console", console), ("control", control) })
        {
            arguments.AddRange(["-object", JsonSerializer.Serialize(new Dictionary<string, object> { ["qom-type"] = "tls-creds-x509", ["id"] = "tls-" + name,
                ["endpoint"] = "client", ["dir"] = channel.CredentialsDirectory, ["verify-peer"] = true }),
                "-chardev", $"socket,id={name},host=127.0.0.1,port={channel.Port},tls-creds=tls-{name}"]);
        }
        arguments.AddRange(["-serial", "chardev:console", "-device", "virtio-serial-pci,id=transport", "-device", "virtserialport,chardev=control,name=org.thaddeus.control",
            "-kernel", files.Kernel, "-initrd", files.Initrd, "-append", platform.KernelCommandLine]);
        foreach (var block in new object[] {
            new Dictionary<string, object> { ["driver"] = "file", ["filename"] = files.BaseDisk, ["node-name"] = "base-file", ["read-only"] = true },
            new Dictionary<string, object> { ["driver"] = "raw", ["file"] = "base-file", ["node-name"] = "base", ["read-only"] = true },
            new Dictionary<string, object> { ["driver"] = "file", ["filename"] = files.Overlay, ["node-name"] = "overlay-file" },
            new Dictionary<string, object> { ["driver"] = "qcow2", ["file"] = "overlay-file", ["backing"] = "base", ["node-name"] = "worker" } })
        {
            if (target is QemuHostTarget.LinuxX64 or QemuHostTarget.LinuxArm64 && block is Dictionary<string, object> node && node["driver"] is "file")
                node["locking"] = "on";
            arguments.AddRange(["-blockdev", JsonSerializer.Serialize(block)]);
        }
        arguments.AddRange(["-device", "virtio-blk-pci,drive=worker"]);
        return arguments;
    }
}
