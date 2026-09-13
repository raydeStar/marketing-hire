using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class LinuxServiceOwnershipTests
{
    private const string Stopped = "LoadState=not-found\nActiveState=inactive\nSubState=dead\nMainPID=0\nControlGroup=\nJob=0\n";
    [Fact] public void MissingCollectedServiceHasAnExplicitStoppedState() => Assert.Equal("", LinuxServiceOwnership.StoppedControlGroup(Stopped));
    [Theory]
    [InlineData("ActiveState=inactive", "ActiveState=active")]
    [InlineData("ActiveState=inactive", "ActiveState=activating")]
    [InlineData("SubState=dead", "SubState=running")]
    [InlineData("MainPID=0", "MainPID=42")]
    [InlineData("Job=0", "Job=123")]
    [InlineData("LoadState=not-found", "LoadState=error")]
    public void ActiveQueuedOrUnknownServiceCannotClaimToBeStopped(string before, string after) =>
        Assert.Throws<IOException>(() => LinuxServiceOwnership.StoppedControlGroup(Stopped.Replace(before, after)));
    [Fact] public void PartialAndDuplicateObservationsAreRefused()
    {
        Assert.Throws<IOException>(() => LinuxServiceOwnership.StoppedControlGroup("MainPID=0\n"));
        Assert.Throws<IOException>(() => LinuxServiceOwnership.StoppedControlGroup(Stopped + "Job=0\n"));
    }
    [Fact] public void DurableUnitIdentityCannotNameAnUnrelatedService()
    {
        var owner = new LinuxServiceOwnership(1, Guid.NewGuid().ToString(), 1000, "thaddeus-worker-" + new string('a', 32) + ".service");
        owner.Validate();
        Assert.Throws<IOException>(() => (owner with { Unit = "docker.service" }).Validate());
        Assert.Throws<IOException>(() => (owner with { BootId = "unknown" }).Validate());
        Assert.Throws<IOException>(() => (owner with { SchemaVersion = 2 }).Validate());
    }
}
