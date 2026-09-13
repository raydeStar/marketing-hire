extern alias host;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Thaddeus.Core;
using Thaddeus.Infrastructure;
using Thaddeus.Lab;
using ProductProgram = host::Program;

internal static class NativeLabEntryPoint
{
    private static async Task Main(string[] args)
    {
        if (args.Length is < 2 or > 3 || args[0] is not ("register" or "register-live" or "run" or "run-live" or "grade" or "retire") || args.Length != (args[0] is "register" or "register-live" or "retire" ? 3 : 2))
            throw new ArgumentException("NativeLab register[-live] FRESH_ARTIFACT_ROOT PINNED_INSTALLATION_JSON | run[-live] ARTIFACT_ROOT | grade ARTIFACT_ROOT | retire ARTIFACT_ROOT REGISTERED_ITEM_ID");
        var artifacts = Path.GetFullPath("artifacts") + Path.DirectorySeparatorChar;
        var root = Path.GetFullPath(args[1]);
        if (!root.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Native Lab data belongs in a private artifacts directory.");
        if (args[0] is "register" or "register-live")
        {
            var config = Path.GetFullPath(args[2]);
            if (!config.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase) || new FileInfo(config).Length > 64000)
                throw new ArgumentException("Use a bounded pinned installation file under artifacts.");
            await NativeRegistration.Register(root, config, args[0] == "register-live"); return;
        }
        var frozenText = await File.ReadAllTextAsync(Path.Combine(root, "registration.json"));
        var registration = Wire.Unpack<NativeRegistration>(frozenText); var digest = Wire.Hash(frozenText);
        registration.ValidatePlan();
        if (args[0] == "grade") { await Grade(root, registration, digest, "regrade-" + Guid.NewGuid().ToString("N") + ".json"); return; }
        if (args[0] == "retire") { await NativeLabMaintenance.Retire(root, registration, digest, args[2]); return; }
        if (registration.Live != (args[0] == "run-live")) throw new InvalidOperationException("Live inference requires run-live and a live registration; scripted commands cannot dispatch it.");
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException("The current native development runner is Windows-only; the scorer is portable.");
        registration.Verify();
        await NativeRegistration.WriteNew(Path.Combine(root, "run-intent.json"), Wire.Pack(new { registrationHash = digest, started = DateTimeOffset.UtcNow,
            planned = registration.Plan.Length, syntheticModel = !registration.Live, permission = "Only frozen fictional fixture questions, exact import decisions and owned workspace removal" }));
        using var registrationLock = new FileStream(Path.Combine(root, "registration.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(registration.CampaignSeconds));
        if (registration.Live) await NativeRegistration.ObserveBridge(root, "bridge-before-run.json");
        var failed = false;
        foreach (var item in registration.Plan)
        {
            try
            {
                deadline.Token.ThrowIfCancellationRequested(); registration.Verify();
                await RunCase(root, registration, digest, item, deadline.Token);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                await NativeRegistration.WriteNew(Path.Combine(root, "failure.json"), Wire.Pack(new { item, classification = error.GetType().Name,
                    reason = error.Message, stopped = DateTimeOffset.UtcNow, infrastructureOrInstrumentationFailure = true, automaticReplay = false }));
                failed = true; break;
            }
        }
        var report = await Grade(root, registration, digest, "report.json");
        if (failed || report.ProtocolStatus != "PASSED") Environment.ExitCode = 1;
    }

    private static async Task RunCase(string campaign, NativeRegistration registration, string digest, NativeLabItem item, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException();
        var fixture = registration.Cases.Single(fixture => fixture.Id == item.CaseId);
        var frozen = registration.Inputs.Single(input => input.CaseId == item.CaseId && input.Arm == item.Arm);
        var root = Path.Combine(campaign, item.Id); if (Directory.Exists(root)) throw new InvalidOperationException("A planned item already exists; do not replay it.");
        PrivateWorkerDirectory.Create(root); const int port = 5182; var origin = "http://127.0.0.1:" + port;
        var watch = Stopwatch.StartNew(); var cpu = Process.GetCurrentProcess().TotalProcessorTime;
        await using var factory = new WebApplicationFactory<ProductProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(Path.Combine(registration.Repository, "src/Thaddeus.Host"));
            builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", origin);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(NativeRegistration.Profile(item.Arm));
                services.AddSingleton<IResearchWorkerFactory>(services => OperatingSystem.IsWindowsVersionAtLeast(10)
                    ? new QemuResearchFactory(services.GetRequiredService<Store>(), registration.Installation, port) : throw new PlatformNotSupportedException());
                services.AddSingleton<IInferenceTransport>(services => registration.Live
                    ? new RecordedInference(services.GetRequiredService<Store>(), new CompatibleInference(
                        new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(185) }, null))
                    : new NativeLabModel(services.GetRequiredService<Store>(), fixture));
                services.AddSingleton<Func<ProviderSnapshot, IModelProvider>>(_ => _ => throw new InvalidOperationException("The product host cannot run a second Lab model loop."));
            });
        });
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri(origin), HandleCookies = true });
        var store = factory.Services.GetRequiredService<Store>();
        try
        {
            store.Write("notes/source.md", fixture.Note, "absent"); store.Setting("provider", Wire.Pack(registration.Provider));
            client.DefaultRequestHeaders.Add("Origin", origin);
            var login = await Post<JsonElement>(client, "/api/auth/login", new { key = (await File.ReadAllTextAsync(Path.Combine(root, "host-key.txt"), cancellation)).Trim() }, cancellation);
            client.DefaultRequestHeaders.Add("X-CSRF", login.GetProperty("csrf").GetString());
            var run = await Post<Run>(client, "/api/chat", new { content = NativeRegistration.Objective, mode = "research", readScope = new[] { "notes/source.md" }, budget = registration.Budget }, cancellation);
            if (run.PreparedContext?.ContentHash != frozen.ContextHash || run.Profile?.Digest != frozen.PolicyDigest ||
                Wire.Hash(Wire.Pack(factory.Services.GetRequiredService<Runtime>().ToolsFor(run.Id))) != frozen.BrokerToolsHash)
                throw new InvalidOperationException("The admitted product context, tools or policy differ from preflight.");
            run = await Until(store, run.Id, current => current.Research?.Phase == "awaiting-input", cancellation);
            await Post<JsonElement>(client, $"/api/runs/{run.Id}/answer", new { questionId = run.Question!.Id, answer = fixture.Answer }, cancellation);
            run = await Until(store, run.Id, current => current.Research?.Phase == "awaiting-approval", cancellation);
            await Post<JsonElement>(client, $"/api/runs/{run.Id}/approve", new { approvalId = run.Approval!.Id, digest = run.Approval.Digest, allow = true }, cancellation);
            run = await Until(store, run.Id, current => current.Research?.Phase == "finished", cancellation);
            await NativeRegistration.WriteNew(Path.Combine(root, "export-before-removal.json"), await client.GetStringAsync("/api/export", cancellation));
            var review = await Post<WorkspaceReview>(client, $"/api/runs/{run.Id}/workspace/inspect", new { }, cancellation);
            if (!review.CanRemove) throw new InvalidOperationException("The completed private workspace cannot be inspected for removal.");
            await Post<JsonElement>(client, $"/api/runs/{run.Id}/workspace/remove", new { digest = review.Digest, confirmation = "REMOVE WORKSPACE" }, cancellation);
            run = store.Get(run.Id)!;
            await NativeRegistration.WriteNew(Path.Combine(root, "export-after-removal.json"), await client.GetStringAsync("/api/export", cancellation));
            if (Directory.Exists(Path.Combine(root, "qemu-" + run.Execution!.SandboxId))) throw new IOException("Private worker storage is unexpectedly present after removal.");
            await Capture(store, root, item, digest, watch, cpu);
            Console.WriteLine($"Captured {item.Id}: {run.State}, {run.ModelCalls} {(registration.Live ? "Luna" : "scripted")} calls, {run.ChargedTokens} tokens charged. A receipt, not a quality verdict.");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            var run = store.List().SingleOrDefault();
            if (run != null && !File.Exists(Path.Combine(root, "capture.json")))
            {
                var failure = error.GetType().Name + ": " + error.Message;
                try
                {
                    var coordinator = factory.Services.GetRequiredService<ResearchCoordinator>();
                    await coordinator.Cancel(run.Id);
                    using var cleanupBound = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await coordinator.Tick(cleanupBound.Token);
                    if (store.Get(run.Id)!.Research?.Phase != "finished") throw new IOException("Workspace retirement remains unconfirmed.");
                }
                catch (Exception cleanup) when (cleanup is not OutOfMemoryException) { failure += " · cleanup unconfirmed: " + cleanup.GetType().Name; }
                // Retain spent or unknown usage even when no successful artifact exists. A failed case still has a bill-shaped shadow.
                await Capture(store, root, item, digest, watch, cpu, failure);
            }
            throw;
        }
        finally { await factory.DisposeAsync(); store.Dispose(); }
    }
    private static async Task Capture(Store store, string root, NativeLabItem item, string digest, Stopwatch watch, TimeSpan cpu, string? failure = null)
    {
        var run = store.List().Single(); var page = run.OutputPath == null ? null : store.Page(run.OutputPath);
        var requests = new List<NativeLabRequest>(); var responses = new List<NativeLabResponse>();
        for (var number = 1; number <= run.ModelCalls; number++)
        {
            var requestPath = Path.Combine(root, $"model-request-{number}.json"); var responsePath = Path.Combine(root, $"model-response-{number}.json");
            if (File.Exists(requestPath)) { var raw = await File.ReadAllTextAsync(requestPath); using var request = JsonDocument.Parse(raw); requests.Add(new(number, request.RootElement.Clone(), raw)); }
            if (File.Exists(responsePath)) { var raw = await File.ReadAllTextAsync(responsePath); using var response = JsonDocument.Parse(raw); responses.Add(new(number, response.RootElement.Clone(), raw)); }
        }
        var worker = store.Setting("sandbox:" + run.Execution!.SandboxId); var grant = store.Setting("worker-grant:" + run.Id);
        var capture = new NativeLabCapture(item, digest, run, store.AllEvents().ToArray(), page,
            page == null ? null : NativeRegistration.FileHash(store.SafePath(page.Path)), requests.ToArray(),
            grant != null && Wire.Unpack<WorkerGrant>(grant).Revoked, worker == null ? "not-created" : Wire.Unpack<SandboxRegistration>(worker).Status,
            watch.ElapsedMilliseconds, (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds,
            Process.GetCurrentProcess().PeakWorkingSet64, failure, responses.ToArray());
        await NativeRegistration.WriteNew(Path.Combine(root, "capture.json"), Wire.Pack(capture));
    }
    private static async Task<T> Post<T>(HttpClient client, string path, object body, CancellationToken cancellation)
    {
        using var response = await client.PostAsJsonAsync(path, body, Wire.Json, cancellation);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Product route {path} returned {(int)response.StatusCode}; no automatic replay.");
        return (await response.Content.ReadFromJsonAsync<T>(Wire.Json, cancellation))!;
    }
    private static async Task<Run> Until(Store store, string id, Func<Run, bool> ready, CancellationToken cancellation)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellation); bounded.CancelAfter(TimeSpan.FromSeconds(store.Get(id)!.Goal.Limits.Seconds + 20));
        while (true)
        {
            bounded.Token.ThrowIfCancellationRequested(); var run = store.Get(id)!;
            if (ready(run)) return run;
            if (run.State is RunState.NeedsAttention or RunState.Failed or RunState.Cancelled || run.Research?.Phase is "attention" or "cleanup-attention")
                throw new InvalidOperationException("Native product work stopped before its expected checkpoint: " + run.Summary);
            await Task.Delay(250, bounded.Token);
        }
    }
    private static async Task<NativeLabReport> Grade(string root, NativeRegistration registration, string digest, string outputName)
    {
        var grades = new List<NativeLabGrade>();
        foreach (var item in registration.Plan)
        {
            var file = Path.Combine(root, item.Id, "capture.json"); if (!File.Exists(file)) continue;
            var capture = Wire.Unpack<NativeLabCapture>(await File.ReadAllTextAsync(file));
            if (capture.RegistrationHash != digest || capture.Item != item) throw new InvalidOperationException("Captured run identity differs from the frozen schedule.");
            // Older captures kept parsed JSON only. Read the separately retained originals without rewriting the capture.
            var requests = new List<NativeLabRequest>(); var responses = new List<NativeLabResponse>();
            foreach (var request in capture.Requests)
            {
                var original = Path.Combine(root, item.Id, $"model-request-{request.Number}.json");
                requests.Add(File.Exists(original) ? request with { RawJson = await File.ReadAllTextAsync(original) } : request);
            }
            foreach (var response in capture.Responses ?? [])
            {
                var original = Path.Combine(root, item.Id, $"model-response-{response.Number}.json");
                responses.Add(File.Exists(original) ? response with { RawJson = await File.ReadAllTextAsync(original) } : response);
            }
            capture = capture with { Requests = requests.ToArray(), Responses = responses.ToArray() };
            var input = registration.Inputs.Single(input => input.CaseId == item.CaseId && input.Arm == item.Arm);
            grades.Add(NativeLabScore.Grade(registration.Cases.Single(fixture => fixture.Id == item.CaseId), capture, registration.Provider,
                registration.Budget, input.ContextHash, input.PolicyDigest, synthetic: !registration.Live));
        }
        var report = (registration.Live ? NativeLabScore.LivePilotReport(digest, registration.Plan, grades.ToArray())
            : NativeLabScore.Report(digest, registration.Plan, grades.ToArray())) with
            { EvaluatorSha256 = NativeRegistration.FileHash(typeof(NativeLabScore).Assembly.Location), Graded = DateTimeOffset.UtcNow };
        await NativeRegistration.WriteNew(Path.Combine(root, outputName), Wire.Pack(report));
        Console.WriteLine($"Native Lab protocol: {report.ProtocolStatus}; efficacy: INCONCLUSIVE. The butler keeps his claims within the evidence.");
        return report;
    }
}
