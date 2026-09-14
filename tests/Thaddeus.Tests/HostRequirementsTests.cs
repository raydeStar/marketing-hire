using System.Runtime.InteropServices;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class HostRequirementsTests
{
    [Theory]
    [InlineData("windows", Architecture.X64, Architecture.X64, "qemu-whpx")]
    [InlineData("linux", Architecture.X64, Architecture.X64, "qemu-kvm")]
    [InlineData("windows", Architecture.Arm64, Architecture.X64, null)]
    [InlineData("windows", Architecture.Arm64, Architecture.Arm64, null)]
    [InlineData("windows", Architecture.X64, Architecture.X86, null)]
    [InlineData("linux", Architecture.Arm64, Architecture.X64, null)]
    [InlineData("linux", Architecture.Arm64, Architecture.Arm64, null)]
    [InlineData("macos", Architecture.X64, Architecture.X64, null)]
    [InlineData("macos", Architecture.Arm64, Architecture.Arm64, null)]
    [InlineData("unsupported", Architecture.X64, Architecture.X64, null)]
    public void UnsupportedOrEmulatedHostCannotSelectTheNativeWorker(string platform, Architecture system, Architecture process, string? backend) =>
        Assert.Equal(backend, NativeWorkerPlatform.Select(platform, system, process));

    [Theory]
    [InlineData("Version=255.4-1ubuntu8.12\n", 255)]
    [InlineData("Version=249.11\n", 249)]
    [InlineData("Version=258\r\n", 258)]
    [InlineData("Version=255\nVersion=249\n", null)]
    [InlineData("Version=development\n", null)]
    [InlineData("Version=255\nControlGroup=/unexpected\n", null)]
    public void OnlyAnUnambiguousManagerVersionIsAnObservation(string output, int? version) =>
        Assert.Equal(version, HostRequirements.ManagerVersion(new(0, output, "")));

    [Fact] public void FailedOrBoundedCommandDoesNotSupplyCapabilities()
    {
        Assert.Null(HostRequirements.ManagerVersion(new(1, "Version=255\n", "")));
        Assert.Null(HostRequirements.ManagerVersion(new(0, "Version=255\n", "", "output-limit")));
        Assert.Null(HostRequirements.UserControlGroup(new(0, Service(), "", "timeout"), 1100));
    }
    private static string Service(string group = "/user.slice/user-1100.slice/user@1100.service", string state = "active", string delegated = "yes") =>
        $"LoadState=loaded\nActiveState={state}\nControlGroup={group}\nDelegate={delegated}\n";

    [Fact] public void ControllerInspectionUsesTheCurrentUsersActiveDelegatedManager()
    {
        Assert.Equal("/user.slice/user-1100.slice/user@1100.service", HostRequirements.UserControlGroup(new(0, Service(), ""), 1100));
        Assert.Null(HostRequirements.UserControlGroup(new(0, Service(), ""), 1101));
        Assert.Null(HostRequirements.UserControlGroup(new(0, Service(state: "inactive"), ""), 1100));
        Assert.Null(HostRequirements.UserControlGroup(new(0, Service(delegated: "no"), ""), 1100));
        Assert.Null(HostRequirements.UserControlGroup(new(0, Service() + "Delegate=yes\n", ""), 1100));
    }
    [Theory]
    [InlineData("/../../user@1100.service")]
    [InlineData("relative/user@1100.service")]
    [InlineData("/user.slice/user@1101.service")]
    [InlineData("/user.slice/./user@1100.service")]
    public void InvalidOrForeignCgroupPathCannotBecomeAHostFileRead(string group) =>
        Assert.Null(HostRequirements.UserControlGroup(new(0, Service(group), ""), 1100));

    [Theory]
    [InlineData("cpu memory pids\n", true)]
    [InlineData("cpuset cpu io memory hugetlb pids\n", true)]
    [InlineData("cpuset memory pids\n", false)]
    [InlineData("cpu memory\n", false)]
    [InlineData(null, false)]
    public void EveryRequiredControllerMustBePresent(string? controllers, bool present) =>
        Assert.Equal(present, HostRequirements.HasControllers(controllers));

    [Fact] public void MissingOrUnknownPrerequisiteDoesNotBecomeReadiness()
    {
        var report = new HostRequirementsReport("linux", "x64", "qemu-kvm", DateTimeOffset.UtcNow,
            [new("platform", "Platform", CheckState.Passed, "Fixture"), new("resources", "Resources", CheckState.Unverified, "Fixture")]);
        Assert.False(report.Passed);
        Assert.False((report with { Checks = [] }).Passed);
        Assert.False((report with { Backend = null, Checks = [new("platform", "Platform", CheckState.Passed, "Fixture")] }).Passed);
        Assert.True((report with { Checks = [new("platform", "Platform", CheckState.Passed, "Fixture")] }).Passed);
    }
}
