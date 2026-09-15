using System.Net;
using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

if (args.Length == 3 && args[0] == "execution-control-check")
{
    if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException("This fixture needs the Windows VM backend.");
    await NativeExecutionControlCheck.Run(args[1], args[2]); return;
}

if (args.Length == 3 && args[0] == "transport-check")
{
    if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException("This fixture needs the Windows VM backend.");
    await NativeTransportCheck.Run(args[1], args[2]); return;
}

// Explicit developer integration fixture. This executable is not shipped or reachable through the product host.
if (args.Length is < 3 or > 5 || args[1] is not ("scripted" or "scripted-web" or "luna") ||
    args.Length == 5 && args[4] != "recover" ||
    !System.Text.RegularExpressions.Regex.IsMatch(args[2], @"\Athaddeus-[a-f0-9]{32}\z"))
    throw new ArgumentException("Usage: NativeCheck ARTIFACT_DIRECTORY scripted|scripted-web|luna OWNED_WORKER_NAME [PRIVATE_VM_TRANSPORT_JSON [recover]]");
var artifacts = Path.GetFullPath("artifacts") + Path.DirectorySeparatorChar;
var root = Path.GetFullPath(args[0]);
var recovering = args.Length == 5;
if (!root.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root) != recovering)
    throw new ArgumentException("Use a fresh private artifacts directory, or explicitly recover an existing owned fixture.");
if (args.Length >= 4 && (!Path.GetFullPath(args[3]).StartsWith(artifacts, StringComparison.OrdinalIgnoreCase) || new FileInfo(args[3]).Length > 64000))
    throw new ArgumentException("Use a bounded private transport configuration under artifacts.");
using var transportConfiguration = args.Length >= 4 ? JsonDocument.Parse(File.ReadAllText(args[3])) : null;
var installationHash = transportConfiguration == null ? "container" : Wire.Hash(transportConfiguration.RootElement.GetRawText());
if (recovering && (!transportConfiguration!.RootElement.TryGetProperty("kind", out var recoveryKind) || recoveryKind.GetString() != "qemu"))
    throw new ArgumentException("Existing-data recovery is limited to the explicitly owned QEMU fixture.");
if (!recovering) PrivateWorkerDirectory.Create(root);
const int port = 5182;
var mode = args[1]; var container = args[2];
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:" + port);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 150000);
builder.Services.AddSingleton(_ => new Store(root));
builder.Services.AddSingleton<IValidator, PlanValidator>();
builder.Services.AddSingleton<IAgentPolicy, EvidencePolicy>();
builder.Services.AddSingleton<Func<ProviderSnapshot, IModelProvider>>(_ => _ => throw new InvalidOperationException("The native engine owns this test's loop."));
builder.Services.AddSingleton<Runtime>();
builder.Services.AddSingleton<IPublicWebReader>(_ => new PublicWebReader());
builder.Services.AddSingleton<IModelAccessGate, ModelAccessGate>();
builder.Services.AddSingleton<IInferenceTransport>(services => mode == "luna" ? new CompatibleInference(
    new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromMinutes(5) }, null)
    : new ScriptedNativeModel(services.GetRequiredService<Store>()));
WorkerMcp.Register(builder.Services);
await using var app = builder.Build();
var store = app.Services.GetRequiredService<Store>();
var runtime = app.Services.GetRequiredService<Runtime>();
var authorization = app.Services.GetRequiredService<WorkerAuthorization>();
var controlToken = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
const string publicUrl = "https://docs.docker.com/ai/sandboxes/faq/";
Run run;
if (recovering)
{
    var marker = Wire.Unpack<NativeFixtureIdentity>(store.Setting("native-fixture") ?? throw new InvalidOperationException("Missing owned fixture identity."));
    if (marker.SchemaVersion != 1 || marker.Mode != mode || marker.WorkerId != container || marker.InstallationHash != installationHash)
        throw new InvalidOperationException("Fixture recovery identity differs from its original installation.");
    authorization.Revoke(marker.RunId); runtime.Recover();
    run = store.Get(marker.RunId) ?? throw new InvalidOperationException("Missing existing fixture task.");
    if (run.Execution?.SandboxId != container || run.PreparedContext == null) throw new InvalidOperationException("Missing original execution binding.");
}
else
{
    store.Write("notes/source.md", "# Workshop\nA fictional workshop lasts 45 minutes. Its audience has not been selected.\n", "absent");
    run = new Run
    {
        Goal = new("Read notes/source.md using thaddeus_read_note, " +
            (mode == "scripted-web" ? "read " + publicUrl + " using thaddeus_fetch_public_page, " : "") +
            "then ask which audience to use with thaddeus_ask_user (Developers or Beginners). Stop until the answer arrives. After the answer, write summary.md in your private artifact directory and propose its exact contents for plans/summary.md with thaddeus_propose_import. Mention the workshop duration and source path. This is a fictional integration fixture.",
            ["notes/source.md"], "plans/", [], new(ModelCalls: 6, ToolCalls: 12, Seconds: 600, MaxTotalTokens: 96000),
            new("compatible", "gpt-5.6-luna", "high", "http://127.0.0.1:5181/v1"), "research",
            mode == "scripted-web" ? new(["docs.docker.com"], 1) : null),
        Profile = PolicyProfile.Evidence
    };
    run.Execution = new("openclaw", container, "agent:thaddeus:" + run.Id, OpenClawBackend.PinnedVersion);
    store.Save(run, "fixture.created", new { mode, isolationQualification = false, modelUsage = mode == "luna" ? "provider-reported" : "synthetic" });
    await runtime.PrepareExecutionContext(run.Id, default);
    store.Setting("native-fixture", Wire.Pack(new NativeFixtureIdentity(1, mode, container, run.Id, installationHash)));
    run = store.Get(run.Id)!;
}
var context = run.PreparedContext!;
var grant = authorization.Issue(run.Id, TimeSpan.FromMinutes(20));
var binding = new { schemaVersion = 1, runId = run.Id, brokerOrigin = "http://127.0.0.1:" + port,
    model = run.Goal.Provider.Model, reasoning = "high", context, grantToken = grant };

ISandboxBackend transport;
var managedVm = false;
Func<object?> vmObservation = () => null;
Func<Task<QemuRecoveryReceipt>>? reconcile = null;
Func<Task<bool>>? rejectLiveRecovery = null;
Func<Task<object>>? recoveryNegatives = null;
if (transportConfiguration != null && transportConfiguration.RootElement.TryGetProperty("kind", out var kind) && kind.GetString() == "qemu")
{
    if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException("The first owned QEMU adapter is Windows-only.");
    var installation = transportConfiguration.RootElement.GetProperty("installation").Deserialize<QemuInstallation>(Wire.Json)!;
    var qemu = new QemuSandboxBackend(store, installation, new(run.Id, port));
    transport = qemu; managedVm = true;
    vmObservation = () => OperatingSystem.IsWindowsVersionAtLeast(10) ? qemu.Observation : null;
    reconcile = () => OperatingSystem.IsWindowsVersionAtLeast(10) ? qemu.ReconcileStopped(container, default) : throw new PlatformNotSupportedException();
    rejectLiveRecovery = async () =>
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException();
        var before = store.Setting("sandbox:" + container);
        await using var rival = new QemuSandboxBackend(store, installation, new(run.Id, port));
        try { await rival.ReconcileStopped(container, default); return false; }
        catch (IOException) { return before == store.Setting("sandbox:" + container); }
    };
    recoveryNegatives = async () =>
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException();
        if (qemu.Observation != null || Wire.Unpack<SandboxRegistration>(store.Setting("sandbox:" + container)!).Status != "stopped")
            throw new InvalidOperationException("Stop the owned fixture before preparing destructive copies.");
        return await NativeQemuFaultCheck.Run(store, container, installation, new(run.Id, port));
    };
}
else transport = args.Length == 4 ? new FixtureVm(container, root, args[3]) : new FixtureContainer(container, root);
var backend = new OpenClawBackend(transport);

app.Use(async (http, next) =>
{
    if (WorkerMcp.IsWorkerRequest(http))
    {
        if (!WorkerMcp.Authenticate(http, authorization, "http://localhost:5179", port)) { http.Response.StatusCode = 403; return; }
    }
    else if (http.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip) ||
        http.Request.Headers.Authorization.ToString() != "Bearer " + controlToken ||
        http.Request.Headers.ContainsKey("Origin") || http.Request.Headers.ContainsKey("Cookie")) { http.Response.StatusCode = 403; return; }
    try { await next(); }
    catch (Exception error) { http.Response.StatusCode = 409; await http.Response.WriteAsJsonAsync(new { error = error.Message }); }
});
app.MapMcp("/worker/{runId}/mcp"); WorkerModels.Map(app);
app.MapGet("/fixture/state", () => Results.Json(new { run = store.Get(run.Id), events = store.AllEvents(), mode }, Wire.Json));
if (managedVm)
{
    app.MapGet("/fixture/vm-state", () => Results.Text(JsonSerializer.Serialize(vmObservation(), Wire.Json), "application/json"));
    app.MapPost("/fixture/vm-reject-live-recovery", async () => new { rejected = await rejectLiveRecovery!() });
    app.MapPost("/fixture/vm-recovery-negatives", async () => await recoveryNegatives!());
    app.MapPost("/fixture/vm-refresh-grant", async () =>
    {
        if (!recovering) throw new InvalidOperationException("Grant refresh requires explicit fixture recovery.");
        await backend.RefreshGrant(run.Execution!, context, grant, default); return Results.Ok(new { refreshed = true });
    });
    app.MapPost("/fixture/vm-execute", async (VmFixtureCommand command) => await transport.Execute(container, command.Command, command.Input, default));
    app.MapPost("/fixture/vm-stop", async () => { await transport.Stop(container, default); return Results.Json(new { stopped = true,
        termination = Wire.Unpack<QemuTermination>(store.Setting("qemu-termination:" + container)!) }, Wire.Json); });
    app.MapPost("/fixture/vm-start", async () => { await transport.Execute(container, ["true"], null, default); return Results.Json(vmObservation(), Wire.Json); });
}
app.MapPost("/fixture/start", async () => await runtime.StartExecution(run.Id, backend, default));
app.MapPost("/fixture/abort", async () => await runtime.QuiesceExecution(run.Id, backend, default));
app.MapPost("/fixture/resume", async () =>
{
    var current = store.Get(run.Id)!;
    if (current.Question is not { Answer: null } question) throw new InvalidOperationException("The fixture has no unanswered question.");
    await runtime.AnswerQuestion(current.Id, question.Id, "Developers", default);
    return await runtime.ResumeExecution(run.Id, backend, default);
});
app.MapPost("/fixture/approve", async () =>
{
    var current = store.Get(run.Id)!; var approval = current.Approval ?? throw new InvalidOperationException("No exact proposal.");
    var artifact = await transport.GetText(container, "summary.md", default);
    if (artifact.Content != approval.Action.Content) throw new InvalidOperationException("Native artifact differs from proposed import.");
    return await runtime.Decide(current.Id, approval.Id, approval.Digest, true);
});
app.MapPost("/fixture/shutdown", () => { app.Lifetime.StopApplication(); return Results.Ok(); });
await app.StartAsync();
if (managedVm)
{
    var installation = transportConfiguration!.RootElement.GetProperty("installation").Deserialize<QemuInstallation>(Wire.Json)!;
    try
    {
        if (recovering)
        {
            // An ordinary execute cannot implicitly recover an uncertain worker.
            var denied = false;
            if (Wire.Unpack<SandboxRegistration>(store.Setting("sandbox:" + container)!).Status != "stopped")
            {
                try { await transport.Execute(container, ["true"], null, default); }
                catch (InvalidOperationException) { denied = true; }
                if (!denied) throw new InvalidOperationException("Uncertain worker executed without explicit reconciliation.");
            }
            var recovered = await reconcile!();
            await File.WriteAllTextAsync(Path.Combine(root, "recovery.json"), Wire.Pack(new { recovered, executeBeforeRecoveryDenied = denied,
                vmStopped = vmObservation() == null, task = store.Get(run.Id), hostProcessId = Environment.ProcessId }));
        }
        else await transport.Create(new(container, installation.Image), default);
    }
    catch { if (transport is IAsyncDisposable failed) await failed.DisposeAsync(); throw; }
}
await File.WriteAllTextAsync(Path.Combine(root, "controller.json"), Wire.Pack(new { controlToken, port, binding, hostProcessId = Environment.ProcessId }));
Console.WriteLine("Native integration fixture ready. Fictional papers only; the family silver stays upstairs.");
try { await app.WaitForShutdownAsync(); }
finally
{
    authorization.Revoke(run.Id);
    if (transport is IAsyncDisposable disposable) await disposable.DisposeAsync();
    await File.WriteAllTextAsync(Path.Combine(root, "final-state.json"), Wire.Pack(new { run = store.Get(run.Id), events = store.AllEvents(), mode }));
}

sealed record VmFixtureCommand(string[] Command, string? Input);
sealed record NativeFixtureIdentity(int SchemaVersion, string Mode, string WorkerId, string RunId, string InstallationHash);

sealed class FixtureContainer(string owned, string evidenceRoot) : FixtureTransport
{
    private readonly HostProcessRunner runner = new();
    public override async Task<SandboxCommandResult> Execute(string id, IReadOnlyList<string> command, string? input, CancellationToken cancellation)
    {
        if (id != owned) throw new InvalidOperationException("This fixture cannot address another container.");
        var result = await runner.Run(new("docker", ["exec", "-i", owned, .. command], Environment.CurrentDirectory, TimeSpan.FromSeconds(55), input), cancellation);
        await File.WriteAllTextAsync(Path.Combine(evidenceRoot, "native-rpc-" + Guid.NewGuid().ToString("N") + ".json"), Wire.Pack(new { result.ExitCode, result.Failure, result.Output, result.Error }), cancellation);
        return new(result.ExitCode ?? -1, result.Output, result.Error);
    }
}

sealed class FixtureVm : FixtureTransport
{
    private readonly string owned, evidenceRoot, token;
    private readonly Uri endpoint;
    private readonly HttpClient client = new(new HttpClientHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(65) };
    public FixtureVm(string owned, string evidenceRoot, string configurationPath)
    {
        this.owned = owned; this.evidenceRoot = evidenceRoot;
        var full = Path.GetFullPath(configurationPath);
        if (!full.StartsWith(Path.GetFullPath("artifacts") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("VM fixture transport configuration must be a private artifact.");
        using var config = JsonDocument.Parse(File.ReadAllText(full));
        var origin = new Uri(config.RootElement.GetProperty("origin").GetString()!);
        token = config.RootElement.GetProperty("token").GetString()!;
        if (origin.Scheme != "http" || origin.Host != "127.0.0.1" || origin.Port < 1024 || origin.AbsolutePath != "/" ||
            origin.Query != "" || origin.Fragment != "" || origin.UserInfo != "" ||
            !System.Text.RegularExpressions.Regex.IsMatch(token, @"\A[a-f0-9]{64}\z"))
            throw new ArgumentException("Invalid private VM fixture transport.");
        endpoint = new Uri(origin, "execute");
    }
    public override async Task<SandboxCommandResult> Execute(string id, IReadOnlyList<string> command, string? input, CancellationToken cancellation)
    {
        if (id != owned) throw new InvalidOperationException("This fixture cannot address another VM.");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            { Content = JsonContent.Create(new { id, command, input }) };
        request.Headers.Authorization = new("Bearer", token);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var bytes = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, cancellation)) > 0)
        {
            if (bytes.Length + count > 1500000) throw new InvalidOperationException("VM command response exceeded its bound.");
            bytes.Write(buffer, 0, count);
        }
        var result = JsonSerializer.Deserialize<SandboxCommandResult>(bytes.ToArray(), Wire.Json)
            ?? throw new InvalidOperationException("VM command response was empty.");
        await File.WriteAllTextAsync(Path.Combine(evidenceRoot, "native-rpc-" + Guid.NewGuid().ToString("N") + ".json"), Wire.Pack(result), cancellation);
        return result;
    }
}

abstract class FixtureTransport : ISandboxBackend
{
    public abstract Task<SandboxCommandResult> Execute(string id, IReadOnlyList<string> command, string? input, CancellationToken cancellation);
    public async Task<SandboxText> GetText(string id, string path, CancellationToken cancellation)
    {
        if (path != "summary.md") throw new ArgumentException("Only the fixture artifact is readable.");
        var result = await Execute(id, ["python3", "-c", "import os,json; fd=os.open('/home/agent/thaddeus-artifacts/summary.md',os.O_RDONLY|os.O_NOFOLLOW); data=os.read(fd,100001); os.close(fd); assert len(data)<=100000; print(json.dumps(data.decode('utf-8')))"], null, cancellation);
        if (result.ExitCode != 0) throw new InvalidOperationException("The native artifact was not readable.");
        var content = JsonSerializer.Deserialize<string>(result.Output)!; return new(path, content, Wire.Hash(content));
    }
    public Task<SandboxInspection> Inspect(CancellationToken cancellation) => throw new NotSupportedException("Fixture only; no qualification claim.");
    public Task Create(SandboxSpec spec, CancellationToken cancellation) => throw new NotSupportedException();
    public Task Stop(string id, CancellationToken cancellation) => throw new NotSupportedException();
    public Task Remove(string id, CancellationToken cancellation) => throw new NotSupportedException();
    public Task PutText(string id, string path, string content, CancellationToken cancellation) => throw new NotSupportedException();
}
