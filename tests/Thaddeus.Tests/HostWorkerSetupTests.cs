using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class HostWorkerSetupTests : IDisposable
{
    private readonly Store store = new(Path.Combine(Path.GetTempPath(), "thaddeus-setup-" + Guid.NewGuid().ToString("N")));
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private readonly Factory backend = new();
    private int inspections;
    private bool fail;
    private HostWorkerSetup Setup(string digest = Digest) => new(store, new("qemu-whpx", "Fixture worker", digest, true), backend, (_, _) =>
    {
        inspections++;
        if (fail) throw new IOException("Private installer path must not enter the response.");
        return Task.FromResult(new SandboxInspection("qemu-whpx", "11.1.0", "11.1.0", DateTimeOffset.UtcNow, "qualification-required", "Fixture files only",
            [new("pinned-inputs", CheckState.Passed, "Fixture inputs"), new("runtime-package", CheckState.Passed, "Fixture package"), new("release-package", CheckState.Unverified, "Release remains open")]));
    });
    [Fact] public void UnconfiguredHostCannotBeEnabled()
    {
        var setup = new HostWorkerSetup(store);
        Assert.False(setup.Availability.Enabled); Assert.Null(setup.View.Worker);
        Assert.Throws<InvalidOperationException>(() => setup.SetEnabled(Digest, true));
        Assert.Null(store.Setting("host-worker-enrollment"));
    }
    [Fact] public async Task CheckStartsNoWorkerAndDoesNotEnableResearch()
    {
        var setup = Setup(); var view = await setup.Check(default);
        Assert.True(view.CanEnable); Assert.False(view.Enabled); Assert.True(view.Worker!.DevelopmentOnly);
        Assert.Equal(0, backend.Opens); Assert.Empty(store.List()); Assert.Equal(1, inspections);
        Assert.Contains(view.LastCheck!.Checks, check => check.Id == "release-package" && check.State == CheckState.Unverified);
    }
    [Fact] public async Task OwnerSelectionRequiresCurrentSuccessfulCheckAndExactDigest()
    {
        var setup = Setup(); Assert.Throws<InvalidOperationException>(() => setup.SetEnabled(Digest, true));
        await setup.Check(default);
        Assert.Throws<InvalidOperationException>(() => setup.SetEnabled(new string('b', 64), true));
        Assert.True(setup.SetEnabled(Digest, true).Enabled); Assert.True(setup.Availability.Enabled);
        Assert.Equal("development-preview", setup.Availability.Status); Assert.True(setup.Availability.DevelopmentOnly);
        Assert.Equal(0, backend.Opens);
    }
    [Fact] public async Task EnrollmentSurvivesRestartOnlyForTheSameInstallation()
    {
        var setup = Setup(); await setup.Check(default); setup.SetEnabled(Digest, true);
        Assert.True(Setup().Availability.Enabled);
        var changed = Setup(new string('b', 64)); Assert.False(changed.Availability.Enabled); Assert.False(changed.View.CanEnable); Assert.Null(changed.View.LastCheck);
    }
    [Theory][InlineData(-11)][InlineData(1)]
    public async Task ExpiredOrFutureCheckCannotEnableTheWorker(int minutes)
    {
        var setup = Setup(); await setup.Check(default);
        var check = setup.View.LastCheck! with { CheckedAt = DateTimeOffset.UtcNow.AddMinutes(minutes) };
        store.Setting("host-worker-check", Wire.Pack(check));
        Assert.False(setup.View.CanEnable); Assert.Throws<InvalidOperationException>(() => setup.SetEnabled(Digest, true));
    }
    [Fact] public async Task FailedRecheckDisablesAdmissionWithoutLeakingPrivateDiagnostics()
    {
        var setup = Setup(); await setup.Check(default); setup.SetEnabled(Digest, true); fail = true;
        var result = await setup.Check(default);
        Assert.False(result.Enabled); Assert.False(result.LastCheck!.Passed);
        Assert.DoesNotContain("Private installer path", Wire.Pack(result)); Assert.Equal(0, backend.Opens);
    }
    [Fact] public async Task DisableNeedsNoNewCheckOrWorkerDispatch()
    {
        var setup = Setup(); await setup.Check(default); setup.SetEnabled(Digest, true);
        Assert.False(setup.SetEnabled(Digest, false).Enabled); Assert.False(Setup().Availability.Enabled);
        Assert.Equal(1, inspections); Assert.Equal(0, backend.Opens);
    }
    [Fact] public async Task ProgressAndExactCancellationInvalidateThePreviousPositiveCheck()
    {
        var previous = Setup(); await previous.Check(default); previous.SetEnabled(Digest, true);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var setup = new HostWorkerSetup(store, new("qemu-whpx", "Fixture worker", Digest, true), backend, async (token, report) =>
        {
            report(new("runtime-files", 1, 4)); entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new Exception("A cancelled inspection cannot reach a successful result.");
        });
        var checking = setup.Check(default); await entered.Task;
        var view = setup.View;
        Assert.False(view.Enabled); Assert.False(view.CanEnable); Assert.False(view.LastCheck!.Passed);
        Assert.Equal("checking", view.Status); Assert.Equal(new("runtime-files", 1, 4), view.Progress!.Step);
        Assert.Throws<InvalidOperationException>(() => setup.SetEnabled(Digest, true));
        Assert.Throws<InvalidOperationException>(() => setup.CancelCheck("stale-check"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Check(default));
        Assert.False(checking.IsCompleted);
        setup.CancelCheck(view.Progress.Id);
        var stopped = await checking;
        Assert.Null(stopped.Progress); Assert.False(stopped.LastCheck!.Passed); Assert.False(stopped.CanEnable);
        Assert.False(Setup().Availability.Enabled); Assert.False(Setup().View.CanEnable);
        Assert.Throws<InvalidOperationException>(() => setup.CancelCheck(view.Progress.Id));
        Assert.Empty(store.List()); Assert.Equal(0, backend.Opens);
    }
    [Fact] public async Task APositiveResultAfterCancellationCannotAdmitTheWorker()
    {
        var release = new TaskCompletionSource<SandboxInspection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var setup = new HostWorkerSetup(store, new("qemu-whpx", "Fixture worker", Digest, true), backend, (_, _) => release.Task);
        var checking = setup.Check(default); setup.CancelCheck(setup.View.Progress!.Id);
        release.SetResult(new("qemu-whpx", "11.1.0", "11.1.0", DateTimeOffset.UtcNow, "fixture", "Late result",
            [new("pinned-inputs", CheckState.Passed, "Fixture inputs"), new("runtime-package", CheckState.Passed, "Fixture package")]));
        var result = await checking;
        Assert.False(result.LastCheck!.Passed); Assert.Null(result.Progress); Assert.False(result.CanEnable);
        Assert.Contains("stopped", result.LastCheck.Summary); Assert.Equal(0, backend.Opens);
    }
    [Fact] public async Task CoordinatorSerializesSetupWithAdmissionAndRefusesActiveTaskChanges()
    {
        store.Write("notes/source.md", "A fictional note.", "absent"); var setup = Setup();
        var runtime = new Runtime(store, _ => throw new Exception("No inference in setup tests."), new PlanValidator(), new EvidencePolicy());
        await using var coordinator = new ResearchCoordinator(store, runtime, new WorkerAuthorization(store), setup);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var configure = coordinator.ConfigureWorker(async token =>
        {
            entered.SetResult(); await release.Task; await setup.Check(token); return setup.SetEnabled(Digest, true);
        }, default);
        await entered.Task;
        var admission = coordinator.Submit(new("Read a fictional note", ["notes/source.md"]), new("compatible", "fixture-model", "high", "http://127.0.0.1:5181/v1"), default);
        Assert.False(admission.IsCompleted); release.SetResult(); await configure; await admission;
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ConfigureWorker(_ => Task.FromResult(setup.SetEnabled(Digest, false)), default));
        Assert.True(setup.Availability.Enabled); Assert.Equal(0, backend.Opens);
    }
    private sealed class Factory : IResearchWorkerFactory
    {
        public int Opens;
        public ResearchAvailability Availability => new(true, "qemu-whpx", "fixture", "No worker execution", true);
        public IResearchWorker Open(Run run) { Opens++; throw new InvalidOperationException("A fixture worker must not start during setup."); }
    }
    public void Dispose() { var root = store.Root; store.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
}
