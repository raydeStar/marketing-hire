using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class SandboxTests : IDisposable
{
    private readonly Store store = new(Path.Combine(Path.GetTempPath(), "thaddeus-sandbox-" + Guid.NewGuid().ToString("N")));
    private const string Worker = "thaddeus-0123456789abcdef0123456789abcdef";
    private static readonly string Image = "docker/sandbox-templates@sha256:" + new string('a', 64);
    private sealed class RecordingRunner(params HostProcessResult[] responses) : IHostProcessRunner
    {
        public readonly List<HostProcessRequest> Calls = [];
        public Task<HostProcessResult> Run(HostProcessRequest request, CancellationToken cancellation)
        { Calls.Add(request); return Task.FromResult(responses[Calls.Count - 1]); }
    }
    private DockerSandboxBackend Backend(IHostProcessRunner runner) => new(runner, Path.Combine(Path.GetTempPath(), "sbx"), store);
    private void RegisterWorker() => store.Setting("sandbox:" + Worker, Wire.Pack(new SandboxRegistration(new(Worker, Image), "created-unqualified", DateTimeOffset.UtcNow)));

    [Fact] public async Task MissingExecutableDoesNotFallBackToHostExecution()
    {
        var runner = new RecordingRunner(new HostProcessResult(null, "", "", "start-failed"));
        var report = await Backend(runner).Inspect(default);
        Assert.Equal("installation-required", report.Status);
        Assert.Single(runner.Calls);
        Assert.Equal(new[] { "version" }, runner.Calls[0].Arguments);
    }
    [Fact] public async Task MismatchedVersionStopsBeforeContactingService()
    {
        var runner = new RecordingRunner(new HostProcessResult(0, "sbx version: v0.43.0 abc", ""));
        var report = await Backend(runner).Inspect(default);
        Assert.Equal("version-mismatch", report.Status); Assert.Single(runner.Calls);
    }
    [Fact] public async Task SignInFailureIsActionableWithoutEchoingDiagnosticSecrets()
    {
        var runner = new RecordingRunner(new(0, "sbx version: v0.42.1 abc", ""), new(1, "", "Not authenticated to Docker\nsecret-fixture-not-for-ui"));
        var report = await Backend(runner).Inspect(default);
        Assert.Equal("sign-in-required", report.Status);
        Assert.DoesNotContain("secret-fixture", Wire.Pack(report));
    }
    [Fact] public async Task ServiceSuccessNeverClaimsConfinementWasVerified()
    {
        var runner = new RecordingRunner(new(0, "sbx version: v0.42.1 abc", ""), new(0, "[]", ""), new(0, "[]", ""));
        var report = await Backend(runner).Inspect(default);
        Assert.Equal("qualification-required", report.Status);
        Assert.Equal(CheckState.Unverified, report.Checks.Single(c => c.Id == "network-confinement").State);
        Assert.Equal(CheckState.Unverified, report.Checks.Single(c => c.Id == "vm-boundary").State);
    }
    [Fact] public async Task ProvisioningIsMountlessPinnedBoundedAndHasNoAutomaticRetry()
    {
        var runner = new RecordingRunner(new(0, "sbx version: v0.42.1 abc", ""), new(0, "[]", ""), new(0, "[]", ""), new(1, "", "failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Backend(runner).Create(new(Worker, Image), default));
        Assert.Equal(4, runner.Calls.Count);
        Assert.Equal("creation-unknown", Wire.Unpack<SandboxRegistration>(store.Setting("sandbox:" + Worker)!).Status);
        Assert.Equal(new[] { "create", "shell", "--name", Worker, "--template", Image, "--cpus", "2", "--memory", "4096m", "--deny-network", "**", "--no-share-skills" }, runner.Calls[3].Arguments);
    }
    [Fact] public async Task HealthyInventoryWithBrokenImageServiceDoesNotPermitCreation()
    {
        var runner=new RecordingRunner(new(0,"sbx version: v0.42.1 abc",""),new(0,"{\"sandboxes\":[]}",""),new(1,"","private-daemon-diagnostic"));
        var report=await Backend(runner).Inspect(default);
        Assert.Equal("image-service-unavailable",report.Status);Assert.DoesNotContain("private-daemon",Wire.Pack(report));
        Assert.Equal(CheckState.Passed,report.Checks.Single(check=>check.Id=="authentication").State);
    }
    [Fact] public async Task UnknownCreationReconcilesOnlyAgainstCurrentEmptyInventory()
    {
        store.Setting("sandbox:"+Worker,Wire.Pack(new SandboxRegistration(new(Worker,Image),"creation-unknown",DateTimeOffset.UtcNow)));
        var runner=new RecordingRunner(new HostProcessResult(0,"{\"sandboxes\":[{\"name\":\"existing\"}]}",""),new HostProcessResult(0,"{\"sandboxes\":[]}",""));
        var backend=Backend(runner);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>backend.ReconcileAbsentCreation(Worker,default));
        await backend.ReconcileAbsentCreation(Worker,default);
        Assert.Equal("removed",Wire.Unpack<SandboxRegistration>(store.Setting("sandbox:"+Worker)!).Status);
        Assert.NotNull(store.Setting("sandbox-absence:"+Worker));
    }
    [Theory]
    [InlineData("other-worker")][InlineData("--cloud")][InlineData("thaddeus-../../")]
    public async Task LifecycleCannotTargetAnUnrelatedSandbox(string id)
    {
        var runner = new RecordingRunner();
        await Assert.ThrowsAsync<ArgumentException>(()=>Backend(runner).Stop(id, default));
        Assert.Empty(runner.Calls);
    }
    [Fact] public async Task CorrectlyShapedButUnregisteredWorkerCannotBeStopped()
    {
        var runner = new RecordingRunner();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Backend(runner).Stop(Worker, default));
        Assert.Empty(runner.Calls);
    }
    [Fact] public async Task ArtifactTransferUsesStdinAndNeverCopiesToAnAgentChosenHostPath()
    {
        const string content = "# Literal text\n$(do-not-execute) `whoami` ; C:\\private";
        RegisterWorker();
        var runner = new RecordingRunner(new HostProcessResult(0, Wire.Hash(content) + "\n", ""));
        await Backend(runner).PutText(Worker, "context.md", content, default);
        var call = Assert.Single(runner.Calls);
        Assert.DoesNotContain(call.Arguments, a => a.Contains(content));
        Assert.Equal(content, System.Text.Json.JsonDocument.Parse(call.Input!).RootElement.GetProperty("content").GetString());
        Assert.Equal("exec", call.Arguments[0]); Assert.Contains(Worker, call.Arguments);
    }
    [Theory][InlineData("../host.md")][InlineData("/etc/passwd")][InlineData("C:\\Users\\private.md")][InlineData("x.md:stream")]
    public async Task InvalidArtifactNamesAreRejectedBeforeDispatch(string path)
    {
        var runner = new RecordingRunner();
        await Assert.ThrowsAsync<ArgumentException>(()=>Backend(runner).GetText(Worker, path, default));
        Assert.Empty(runner.Calls);
    }
    [Fact] public async Task WorkerHashClaimIsIndependentlyRecomputed()
    {
        RegisterWorker();
        var runner = new RecordingRunner(new HostProcessResult(0, Wire.Pack(new SandboxText("result.md", "changed", Wire.Hash("old"))), ""));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Backend(runner).GetText(Worker, "result.md", default));
    }
    [Fact] public async Task RealProcessMissingExecutableReturnsBoundedFailure()
    {
        var request = new HostProcessRequest(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing"), [], Path.GetTempPath(), TimeSpan.FromSeconds(1));
        var result = await new HostProcessRunner().Run(request, default);
        Assert.Equal("start-failed", result.Failure); Assert.False(result.Succeeded);
    }
    public void Dispose(){var root=store.Root;store.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
}
