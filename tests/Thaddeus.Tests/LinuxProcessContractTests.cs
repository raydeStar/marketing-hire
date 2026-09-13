using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class LinuxProcessContractTests
{
    private static readonly string Unit = "thaddeus-worker-" + new string('a', 32) + ".service";
    private static readonly LinuxProcessLimits Limits = new(268435456, 25, 64);
    private static readonly OwnedProcessRequest Request = new(Path.GetFullPath("fixture/qemu"), ["literal $HOME; value"],
        Path.GetFullPath("fixture/work"), new Dictionary<string, string> { ["EXPLICIT"] = "literal $VALUE" }, TimeSpan.FromSeconds(20));
    private static readonly LinuxResourceObservation Observation = new("/user.slice/" + Unit, 268435456, 0, 25000, 100000, 64);

    [Fact]
    public void ServiceUsesCgroupOwnershipWithNoScopeOrShellFallback()
    {
        var supervisor = Path.GetFullPath("fixture/supervisor $literal"); var request = Path.GetFullPath("fixture/request 'literal'.json");
        var arguments = LinuxProcessContract.Arguments(Unit, supervisor, request, new string('b', 64), Request, Limits);
        foreach (var expected in new[] { "--user", "--pipe", "--wait", "--service-type=exec", "--expand-environment=no", "--property=KillMode=control-group",
            "--property=KillSignal=SIGKILL", "--property=SendSIGKILL=yes", "--property=MemoryMax=268435456", "--property=MemorySwapMax=0",
            "--property=CPUQuota=25%", "--property=CPUQuotaPeriodSec=100ms", "--property=TasksMax=64", "--property=RuntimeMaxSec=20", "--property=ProtectControlGroups=yes" })
            Assert.Contains(expected, arguments);
        Assert.Equal(["--", supervisor, "--linux-supervise", request, new string('b', 64)], arguments.TakeLast(5));
        Assert.DoesNotContain("--scope", arguments); Assert.DoesNotContain("--shell", arguments);
        Assert.DoesNotContain("literal $HOME; value", arguments); // Adapter argv stays in the private, hash-bound request.
    }

    [Theory]
    [InlineData("other.service")][InlineData("../thaddeus-worker-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.service")][InlineData("thaddeus-worker-;touch-x.service")]
    public void ForeignUnitNamesAreRefused(string unit) => Assert.Throws<ArgumentException>(() =>
        LinuxProcessContract.Arguments(unit, Path.GetFullPath("helper"), Path.GetFullPath("request"), new string('b', 64), Request, Limits));

    [Fact] public void ActualMatchingLimitsAreAccepted() => LinuxProcessContract.VerifyResources(Unit, Limits, Observation);

    [Theory]
    [InlineData("memory")][InlineData("swap")][InlineData("cpu")][InlineData("period")][InlineData("tasks")][InlineData("group")]
    public void RequestedLimitsCannotStandInForKernelObservations(string changed)
    {
        var observed = changed switch
        {
            "memory" => Observation with { MemoryBytes = Observation.MemoryBytes * 2 },
            "swap" => Observation with { SwapBytes = 1 },
            "cpu" => Observation with { CpuQuotaMicroseconds = -1 },
            "period" => Observation with { CpuPeriodMicroseconds = 1000000 },
            "tasks" => Observation with { Tasks = 65 },
            _ => Observation with { ControlGroup = "/unrelated.service" }
        };
        Assert.Throws<IOException>(() => LinuxProcessContract.VerifyResources(Unit, Limits, observed));
    }

    [Theory]
    [InlineData(0, 25, 64)][InlineData(268435456, 0, 64)][InlineData(268435456, 401, 64)][InlineData(268435456, 25, 513)]
    public void InvalidLimitsAreRefused(long memory, int cpu, int tasks) =>
        Assert.Throws<ArgumentException>(() => LinuxProcessContract.Validate(Request, new(memory, cpu, tasks)));

    [Fact]
    public void WindowsLimitsAndUnboundedRequestsAreRefused()
    {
        foreach (var request in new[] { Request with { Resources = new(268435456, 2500, 1) }, Request with { Lifetime = TimeSpan.FromHours(1) },
            Request with { Arguments = ["a\0b"] }, Request with { Executable = "relative" }, Request with { Environment = new Dictionary<string, string> { ["A=B"] = "value" } } })
            Assert.Throws<ArgumentException>(() => LinuxProcessContract.Validate(request, Limits));
    }
}
