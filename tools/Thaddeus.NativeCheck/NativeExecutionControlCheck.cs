using System.Diagnostics;
using System.Net;
using System.Runtime.Versioning;
using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

// This checks the real Gateway adapter. It is deliberately absent from the shipped host.
[SupportedOSPlatform("windows10.0")]
internal static class NativeExecutionControlCheck
{
    private const string Guidance = "CONTROL_STEER_47: keep the current task and acknowledge this additional guidance.";

    public static async Task Run(string directory, string configuration)
    {
        var artifacts = Path.GetFullPath("artifacts") + Path.DirectorySeparatorChar;
        var root = Path.GetFullPath(directory); var config = Path.GetFullPath(configuration);
        if (!root.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root) ||
            !config.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase) || new FileInfo(config).Length > 64000)
            throw new ArgumentException("Use a fresh private artifact directory and an existing pinned installation.");
        Store.AssertNoLinks(config); Store.AssertNoLinks(Path.GetDirectoryName(root)!);
        using var document = JsonDocument.Parse(File.ReadAllText(config));
        if (document.RootElement.GetProperty("kind").GetString() != "qemu") throw new ArgumentException("Expected a pinned QEMU installation.");
        var installation = document.RootElement.GetProperty("installation").Deserialize<QemuInstallation>(Wire.Json)!;
        var extra = checked(new FileInfo(installation.BaseDisk.Path).Length + 64L * 1024 * 1024);
        if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < 10L * 1024 * 1024 * 1024 + extra)
            throw new IOException("One worst-case overlay and the 10 GiB reserve must fit before this control check starts.");
        PrivateWorkerDirectory.Create(root);
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(_ => new Store(root));
        builder.Services.AddSingleton<IValidator, PlanValidator>();
        builder.Services.AddSingleton<IAgentPolicy, EvidencePolicy>();
        builder.Services.AddSingleton<Func<ProviderSnapshot, IModelProvider>>(_ => _ =>
            throw new InvalidOperationException("OpenClaw owns this fixture's only agent loop."));
        builder.Services.AddSingleton<Runtime>();
        builder.Services.AddSingleton<IPublicWebReader>(_ => new PublicWebReader());
        builder.Services.AddSingleton<IModelAccessGate, ModelAccessGate>();
        var model = new ControlModel(root);
        builder.Services.AddSingleton<IInferenceTransport>(model);
        WorkerMcp.Register(builder.Services);
        await using var app = builder.Build();
        var store = app.Services.GetRequiredService<Store>();
        var runtime = app.Services.GetRequiredService<Runtime>();
        var authorization = app.Services.GetRequiredService<WorkerAuthorization>();
        var run = new Run
        {
            Goal = new("A fictional execution-control check. Respond briefly without using tools.", [], "plans/", [],
                new(ModelCalls: 3, ToolCalls: 4, Seconds: 240, MaxTotalTokens: 96000),
                new("compatible", "scripted-native-controls", "high", "https://model.fixture.invalid/v1"), "research"),
            Profile = PolicyProfile.ArtifactEvidence
        };
        var workerId = "thaddeus-" + run.Id;
        run.Execution = new("openclaw", workerId, "agent:thaddeus:" + run.Id, OpenClawBackend.PinnedVersion);
        store.Save(run, "fixture.created", new { synthetic = true });
        await runtime.PrepareExecutionContext(run.Id, default);
        run = store.Get(run.Id)!;
        var port = 0;
        app.Use(async (http, next) =>
        {
            if (!WorkerMcp.IsWorkerRequest(http) || !WorkerMcp.Authenticate(http, authorization, "http://localhost:5179", port))
            { http.Response.StatusCode = 403; return; }
            await next();
        });
        app.MapMcp("/worker/{runId}/mcp"); WorkerModels.Map(app);
        await app.StartAsync(); port = new Uri(app.Urls.Single()).Port;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var sandbox = new QemuSandboxBackend(store, installation, new(run.Id, port));
        var worker = new OpenClawResearchWorker(sandbox, new(workerId, installation.Image), "http://127.0.0.1:5182",
            async token => { await sandbox.ReconcileStopped(workerId, token); },
            token => sandbox.Retire(workerId, token));
        var grant = authorization.Issue(run.Id, TimeSpan.FromMinutes(5));
        var observations = new List<object>(); Process? process = null;
        var passed = false; var removed = false; var steeringVerified = false; var gatewayAbortVerified = false;
        var lostCallerResumeRefused = false; var liveGrantRefreshRefused = false; string? failure = null;
        File.WriteAllText(Path.Combine(root, "intent.json"), Wire.Pack(new
        {
            purpose = "Native active-run steering, correlated inspection and cancellation through the production Gateway adapter",
            installationSha256 = Wire.Hash(File.ReadAllText(config)), liveModelCalls = 0, gpuInference = 0,
            workerBoots = 1, maximumSyntheticCalls = 3, reservedAdditionalBytes = extra, activeQaTouched = false
        }));
        try
        {
            Console.WriteLine("Checking native execution controls. A fictional errand, one worker, and no model bill.");
            await worker.Prepare(run, grant, deadline.Token);
            var boot = sandbox.Observation ?? throw new InvalidOperationException("Missing native worker ownership.");
            process = Process.GetProcessById(boot.ProcessId); _ = process.Handle;
            run = store.Get(run.Id)!; run.State = RunState.Running; run.ExecutionDeadlineStart = DateTimeOffset.UtcNow;
            store.Save(run, "fixture.native-control-admitted", new { purpose = "Direct adapter contract, not product orchestration" });
            var execution = worker.Execution;
            var started = await execution.Start(new(run.Id, run.Execution!, run.Goal.Objective, run.Goal.Provider, run.Goal.Limits), deadline.Token);
            Observe("start", started);
            var identity = run.Execution! with { RuntimeRunId = started.RuntimeRunId };
            await model.Entered[0].Task.WaitAsync(TimeSpan.FromSeconds(45), deadline.Token);
            try { await ((OpenClawBackend)execution).RefreshGrant(identity, run.PreparedContext!, grant, deadline.Token); }
            catch (IOException) { liveGrantRefreshRefused = true; }
            if (!liveGrantRefreshRefused) throw new InvalidOperationException("A live Gateway allowed replacement of its caller lease.");
            var steered = await execution.Steer(identity, Guidance, "control-steer-47", deadline.Token);
            Observe("steer", steered);
            // The guidance ticket can finish when its input is injected. The original execution remains active.
            var guidedIdentity = identity;
            model.Release[0].TrySetResult();
            await model.Entered[1].Task.WaitAsync(TimeSpan.FromSeconds(45), deadline.Token);
            var second = model.Requests[1];
            if (!second.GetProperty("messages").EnumerateArray().Any(message =>
                    message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String &&
                    content.GetString()!.Contains(Guidance, StringComparison.Ordinal)))
                throw new InvalidOperationException("The native follow-up request omitted the exact steering message.");
            steeringVerified = true;
            var inspected = await execution.Inspect(guidedIdentity, deadline.Token);
            Observe("inspect-active", inspected);
            if (inspected.RuntimeRunId != guidedIdentity.RuntimeRunId || inspected.Status is not ("pending" or "timeout"))
                throw new InvalidOperationException("Inspection did not identify the active guidance turn.");
            var queued = await execution.Steer(guidedIdentity, "CONTROL_QUEUED_49: this guidance must not run after cancellation.", "control-queued-49", deadline.Token);
            Observe("queue-guidance", queued);
            var cancelled = await execution.Cancel(guidedIdentity, deadline.Token);
            Observe("cancel", cancelled);
            if (!cancelled.Report.TryGetProperty("ok", out var ok) || !ok.GetBoolean() ||
                cancelled.Status != "aborted" ||
                !cancelled.Report.TryGetProperty("abortedRunId", out var aborted) ||
                (aborted.GetString() != guidedIdentity.RuntimeRunId && aborted.GetString() != identity.RuntimeRunId && aborted.GetString() != queued.RuntimeRunId) ||
                cancelled.Report.GetProperty("thaddeusFilesystemCheckpoint").GetString() != "syncfs")
                throw new InvalidOperationException("Native cancellation did not acknowledge its filesystem checkpoint.");
            gatewayAbortVerified = true;
            var stopped = await execution.Inspect(guidedIdentity, deadline.Token);
            Observe("inspect-after-cancel", stopped);
            if (stopped.Status is "pending" or "timeout")
                throw new InvalidOperationException("The cancelled native run still appears active.");
            var stoppedQueue = await execution.Inspect(guidedIdentity with { RuntimeRunId = queued.RuntimeRunId }, deadline.Token);
            Observe("inspect-queued-after-cancel", stoppedQueue);
            if (stoppedQueue.Status is "pending" or "timeout") throw new InvalidOperationException("Queued guidance survived task cancellation.");
            var caller = started.Report.GetProperty("thaddeusGatewayConnectionId").GetString();
            foreach (var result in new[] { started, steered, inspected, queued, cancelled, stopped, stoppedQueue })
            {
                var scopes = result.Report.GetProperty("thaddeusGatewayScopes").EnumerateArray().Select(value => value.GetString()).Order().ToArray();
                if (result.Report.GetProperty("thaddeusGatewayConnectionId").GetString() != caller ||
                    !scopes.SequenceEqual(new[] { "operator.read", "operator.write" }))
                    throw new InvalidOperationException("Gateway caller identity or least-privilege scopes changed.");
            }
            var callerStopped = await sandbox.Execute(workerId, ["python3", "-c", """
                import json, os, select, signal, sys
                expected = json.load(sys.stdin)
                fd = os.open('/home/agent/.openclaw/thaddeus-control-hello.json', os.O_RDONLY | os.O_NOFOLLOW)
                with os.fdopen(fd) as stream: hello = json.load(stream)
                assert hello['connectionId'] == expected['connectionId'] and hello['processId'] > 1
                handle = os.pidfd_open(hello['processId'])
                try:
                    with open('/proc/' + str(hello['processId']) + '/cmdline', 'rb') as stream: command = stream.read()
                    assert b'--input-type=module\x00-\x00' in command
                    signal.pidfd_send_signal(handle, signal.SIGTERM)
                    poll = select.poll(); poll.register(handle, select.POLLIN)
                    assert poll.poll(5000), 'Owned controller did not exit'
                finally: os.close(handle)
                print(json.dumps({'stopped': True, 'connectionId': hello['connectionId'], 'processId': hello['processId']}))
                """], Wire.Pack(new { connectionId = caller }), deadline.Token);
            if (callerStopped.ExitCode != 0) throw new InvalidOperationException("The fixture could not stop its owned Gateway caller.");
            File.WriteAllText(Path.Combine(root, "lost-caller.json"), callerStopped.Output);
            try { await execution.Resume(guidedIdentity, "This fictional request must not dispatch after caller loss.", "lost-caller-48", deadline.Token); }
            catch (InvalidOperationException) { lostCallerResumeRefused = true; }
            if (!lostCallerResumeRefused) throw new InvalidOperationException("A lost Gateway caller was silently replaced.");
            var refusal = await sandbox.Execute(workerId, ["python3", "-c", "import os, stat, sys; fd = os.open('/home/agent/.openclaw/thaddeus-rpc-last-error.json', os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK); info = os.fstat(fd); assert stat.S_ISREG(info.st_mode) and info.st_size <= 100000; sys.stdout.buffer.write(os.read(fd, 100001)); os.close(fd)"], null, deadline.Token);
            if (refusal.ExitCode != 0 || JsonDocument.Parse(refusal.Output).RootElement.GetProperty("method").GetString() != "chat.send")
                throw new InvalidOperationException("Missing correlated lost-caller refusal.");
            File.WriteAllText(Path.Combine(root, "lost-caller-refusal.json"), refusal.Output);
            // The VM relay owns host HTTP requests. Its shutdown is the provider-cancellation boundary.
            authorization.Revoke(run.Id);
            await sandbox.Stop(workerId, deadline.Token);
            await model.CancelledSecond.Task.WaitAsync(TimeSpan.FromSeconds(15), deadline.Token);
            if (model.Count != 2 || store.Get(run.Id)!.Capabilities.Count != 0)
                throw new InvalidOperationException("Unexpected extra inference or capability effect in the control fixture.");
            passed = true;
        }
        catch (Exception error)
        {
            failure = error.GetType().Name + ": " + error.Message;
            // The guest is disposable; take its small diagnostic calling card before dismissing it.
            if (sandbox.Observation != null)
            {
                try
                {
                    using var captureDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var diagnostic = await sandbox.Execute(workerId, ["python3", "-c", "import os, stat, sys; fd = os.open('/home/agent/.openclaw/thaddeus-rpc-last-error.json', os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK); info = os.fstat(fd); assert stat.S_ISREG(info.st_mode) and info.st_size <= 100000; sys.stdout.buffer.write(os.read(fd, 100001)); os.close(fd)"], null, captureDeadline.Token);
                    File.WriteAllText(Path.Combine(root, "native-rpc-diagnostic.json"), Wire.Pack(diagnostic));
                }
                catch (Exception captureError)
                { File.WriteAllText(Path.Combine(root, "diagnostic-capture-failure.txt"), captureError.GetType().Name + ": " + captureError.Message); }
            }
        }
        finally
        {
            authorization.Revoke(run.Id);
            try
            {
                using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                if (sandbox.Observation != null) await sandbox.Stop(workerId, cleanupDeadline.Token);
                if (store.Setting("sandbox:" + workerId) != null)
                {
                    await sandbox.ReconcileStopped(workerId, cleanupDeadline.Token);
                    await sandbox.Remove(workerId, cleanupDeadline.Token);
                    removed = true;
                }
                if (process != null && !process.HasExited) throw new InvalidOperationException("Owned VM process is still running.");
            }
            catch (Exception cleanupError) { passed = false; failure = (failure ?? "") + " Cleanup: " + cleanupError.Message; }
            await sandbox.DisposeAsync();
            model.ReleaseAll();
            await app.StopAsync();
            File.WriteAllText(Path.Combine(root, "observations.json"), Wire.Pack(observations));
            File.WriteAllText(Path.Combine(root, "final-run.json"), Wire.Pack(new { run = store.Get(run.Id), events = store.AllEvents() }));
            File.WriteAllText(Path.Combine(root, "verified.json"), Wire.Pack(new
            {
                passed, failure, steeringVerified, gatewayAbortVerified, liveGrantRefreshRefused, lostCallerResumeRefused, adapterOnly = true, exactSteeringMessage = Guidance, syntheticCalls = model.Count,
                providerRequestCancellationObserved = model.CancelledSecond.Task.IsCompletedSuccessfully,
                providerCancellationBoundary = "vm-stop", gatewayAbortAloneCancelsProvider = false,
                processId = process?.Id, ownedProcessExited = process?.HasExited ?? true, overlayRemoved = removed,
                grantsRevoked = true, liveModelCalls = 0, gpuInference = 0, productionQualified = false,
                checks = observations, freeBytes = new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace
            }));
            process?.Dispose();
        }
        if (!passed) throw new InvalidOperationException(failure ?? "Native control check was not completed.");
        Console.WriteLine("Native steering and cancellation passed. The errand stopped; the ledger kept the evidence.");

        void Observe(string operation, ExecutionObservation result)
        {
            observations.Add(new { operation, result });
            File.WriteAllText(Path.Combine(root, "observations.json"), Wire.Pack(observations));
            Console.WriteLine(operation + ": " + result.Status + ". The ledger has its receipt.");
        }
    }

    private sealed class ControlModel(string root) : IInferenceTransport
    {
        public TaskCompletionSource[] Entered { get; } = [NewSignal(), NewSignal()];
        public TaskCompletionSource[] Release { get; } = [NewSignal(), NewSignal()];
        public TaskCompletionSource CancelledSecond { get; } = NewSignal();
        public JsonElement[] Requests { get; } = new JsonElement[2];
        private int calls;
        public int Count => Volatile.Read(ref calls);
        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseAll() { foreach (var gate in Release) gate.TrySetResult(); }
        public async Task<InferenceReply> Send(ProviderSnapshot provider, JsonElement body, CancellationToken cancellation)
        {
            var stage = Interlocked.Increment(ref calls) - 1;
            if (stage >= 2) throw new InvalidOperationException("Unplanned extra synthetic request.");
            Requests[stage] = body.Clone();
            await File.WriteAllTextAsync(Path.Combine(root, "synthetic-request-" + (stage + 1) + ".json"), body.GetRawText(), cancellation);
            Entered[stage].TrySetResult();
            try { await Release[stage].Task.WaitAsync(cancellation); }
            catch (OperationCanceledException) { if (stage == 1) CancelledSecond.TrySetResult(); throw; }
            var reply = JsonSerializer.SerializeToElement(new
            {
                id = "synthetic-controls-" + stage, model = provider.Model,
                choices = new[] { new { index = 0, message = new { role = "assistant", content = "Fictional control reply." }, finish_reason = "stop" } },
                usage = new { prompt_tokens = 100, completion_tokens = 10 }
            });
            return new(reply, 100, 10);
        }
    }
}
