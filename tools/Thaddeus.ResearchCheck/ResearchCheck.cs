using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

// This test-only executable hosts the actual product entry point and background coordinator.
// The browser supplies every task decision through the normal authenticated product API.
internal static class ResearchCheck
{
    private static async Task Main(string[] args)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException("The development VM fixture requires Windows 10 or newer.");
        if (args.Length is < 2 or > 3 || args.Length == 3 && args[2] is not ("checkpoint-recovery" or "public-search"))
            throw new ArgumentException("Usage: ResearchCheck FRESH_PRIVATE_ARTIFACT_DIRECTORY PINNED_INSTALLATION_JSON [checkpoint-recovery|public-search]");
        var checkpointRecovery = args.Length == 3 && args[2] == "checkpoint-recovery";
        var publicSearch = args.Length == 3 && args[2] == "public-search";
        var searchTransport = new SearchTransport();
        var artifacts = Path.GetFullPath("artifacts") + Path.DirectorySeparatorChar;
        var root = Path.GetFullPath(args[0]); var config = Path.GetFullPath(args[1]);
        if (!root.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root) ||
            !config.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase) || new FileInfo(config).Length > 64000)
            throw new ArgumentException("Use a fresh fixture directory and bounded installation configuration under artifacts.");
        PrivateWorkerDirectory.Create(root);
        const int port = 5182, workerPort = 5184;
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(Path.GetFullPath("src/Thaddeus.Host"));
            builder.UseSetting("Thaddeus:Data", root);
            builder.UseSetting("Thaddeus:LocalOrigin", "http://127.0.0.1:" + port);
            builder.UseSetting("Thaddeus:DevelopmentWorkerInstallation", config);
            builder.UseSetting("Thaddeus:WorkerPort", workerPort.ToString());
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IInferenceTransport>(services => new ScriptedNativeModel(services.GetRequiredService<Store>(), injectInvalidProposal: true));
                services.AddSingleton<Func<ProviderSnapshot, IModelProvider>>(_ => _ => throw new InvalidOperationException("This fixture never dispatches a host model loop."));
                if (publicSearch) services.AddSingleton<IPublicSearch>(services => new BravePublicSearch(services.GetRequiredService<IPublicSearchCredentials>(), () => searchTransport));
                if (checkpointRecovery) services.AddSingleton<IResearchWorkerFactory>(services =>
                    new CheckpointInterruption(services.GetRequiredService<HostWorkerSetup>(), services.GetRequiredService<Store>()));
            });
        });
        factory.UseKestrel(options => { options.Listen(System.Net.IPAddress.Loopback, port); options.Listen(System.Net.IPAddress.Loopback, workerPort); });
        using var client = factory.CreateClient(new() { BaseAddress = new("http://127.0.0.1:" + port) });
        var store = factory.Services.GetRequiredService<Store>();
        store.Write("notes/source.md", "# Workshop\nA fictional workshop lasts 45 minutes. Its audience has not been selected.\n", "absent");
        store.Write("notes/memory-source.md", "The workshop handout color is cobalt.\nThe spare notebook is jade.\nUnselected source content: marigold-administration.\n", "absent");
        store.Setting("provider", Wire.Pack(new ProviderSnapshot("compatible", "scripted-native-protocol-fixture", "high", "https://model.fixture.invalid/v1")));
        await File.WriteAllTextAsync(Path.Combine(root, "ready.json"), Wire.Pack(new { origin = client.BaseAddress, modelTransport = "scripted", realVm = true, isolationQualified = false, checkpointRecovery, publicSearch, searchTransport = publicSearch ? "synthetic-provider-http" : null }));
        Console.WriteLine("Research browser fixture ready on port 5182. A real worker, a fictional model, and no GPU appetite.");
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            while (!File.Exists(Path.Combine(root, "browser-finished.json"))) await Task.Delay(500, deadline.Token);
            var run = store.List().Single();
            if (run.State != RunState.Succeeded || run.Research is not { Phase: "finished", Review: not null } ||
                run.OutputPath == null || store.Version(run.OutputPath) != run.Research.Review.Sha256 ||
                run.ExecutionCommands.Any(command => command.Status != "acknowledged") || run.Capabilities.Any(call => call.IsError))
                throw new InvalidOperationException("Browser fixture ended without a verified import and retired worker.");
            if (run.Profile != PolicyProfile.ArtifactEvidence || run.ArtifactImports.Count != 2 ||
                run.ArtifactImports[0] is not { Status: "repair-dispatched", Repair.Status: "repair-requested", ApprovalId: null } ||
                run.ArtifactImports[1].Status != "ready-for-approval" || run.ArtifactImports[1].ApprovalId != run.Approval!.Id ||
                run.ArtifactImports[1].Sha256 != store.Version(run.OutputPath) ||
                run.ExecutionCommands.Count(command => command.Kind == "artifact-repair") != 1 ||
                run.Capabilities.Count(call => call.Name == "thaddeus_propose_import" && call.Result.GetProperty("status").GetString() == "awaiting-artifact-review") != 2)
                throw new InvalidOperationException("The reference import contract did not capture both files and dispatch one recorded native correction.");
            if (run.ModelCalls != (publicSearch ? 8 : 7) || run.Repairs != 1 || run.NativeProposals.Count != 2 ||
                run.NativeProposals[0].Status != "repair-requested" || run.NativeProposals[0].Assessment.Passed ||
                run.NativeProposals[1].Status != "passed" || !run.NativeProposals[1].Assessment.Passed ||
                run.NativeProposals[1].ApprovalId != run.Approval!.Id || run.NativeProposals[1].ContentHash != store.Version(run.OutputPath))
                throw new InvalidOperationException("Native bounded repair did not preserve the failed attempt and exact approved correction.");
            var removal = Wire.Unpack<WorkspaceRemoval>(store.Setting("workspace-removal:" + run.Id) ?? throw new InvalidOperationException("Removal receipt is missing."));
            var registration = Wire.Unpack<SandboxRegistration>(store.Setting("sandbox:" + run.Execution!.SandboxId)!);
            if (checkpointRecovery && (store.AllEvents().Count(entry => entry.Type == "research.recovery.reviewed") != 1 ||
                store.AllEvents().Count(entry => entry.Type == "research.recovery.restored") != 1 ||
                run.Research.Recovery is not { CanRestore: true, WorkerStopped: true, Checkpoint: "saved-question" }))
                throw new InvalidOperationException("The interrupted checkpoint was not explicitly inspected and restored through the product.");
            if (run.PreparedContext?.Memories is not { Length: 1 } selected || selected[0].Statement != "Use cobalt workshop handouts." ||
                run.Goal.ReadScope.Contains("notes/memory-source.md") || run.ModelDispatches.Any(dispatch => dispatch.ContextObserved != true))
                throw new InvalidOperationException("Selected memory activation was not observed in every native model dispatch.");
            foreach (var dispatch in Enumerable.Range(1, run.ModelCalls))
            {
                using var input = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, $"synthetic-request-{dispatch}.json")));
                var text = string.Join("\n", input.RootElement.GetProperty("messages").EnumerateArray().Where(message => message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String).Select(message => message.GetProperty("content").GetString()));
                if (!text.Contains(run.PreparedContext.Text, StringComparison.Ordinal) || text.Contains("marigold-administration", StringComparison.Ordinal) || text.Contains("spare notebook", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Native memory context was missing or included unselected source data.");
                if (publicSearch && (input.RootElement.GetRawText().Contains(SearchTransport.Key, StringComparison.Ordinal) ||
                    dispatch >= 3 && !input.RootElement.GetRawText().Contains("Discovery fixture description", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Search result delivery was missing or exposed the provider key.");
                if (dispatch == (publicSearch ? 7 : 6) && !input.RootElement.GetProperty("messages").EnumerateArray().Any(message =>
                    message.GetProperty("role").GetString() == "user" && message.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String && content.GetString()!.Contains(run.NativeProposals[0].ProposalHash, StringComparison.Ordinal) &&
                    content.GetString()!.Contains("repair-requested", StringComparison.Ordinal) &&
                    content.GetString()!.Contains(run.NativeProposals[0].Assessment.Problems.Single(), StringComparison.Ordinal)))
                    throw new InvalidOperationException("The actual native repair request did not receive the recorded quotation feedback.");
            }
            if (publicSearch)
            {
                var search = run.Capabilities.Single(call => call.Name == "thaddeus_search_public_web");
                var fetched = run.Capabilities.Single(call => call.Name == "thaddeus_fetch_public_page");
                if (searchTransport.Calls != 1 || run.Goal.Web is not { Hosts.Length: 0, Search.OpenResults: true } ||
                    search.IsError || fetched.IsError || search.Authority != "broker-observed" || fetched.Authority != "broker-observed" ||
                    !run.NativeProposals[1].Citations.Any(citation => citation.Source.StartsWith("https://docs.docker.com/", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Search discovery, result-only retrieval and public quotation evidence were not verified.");
            }
            if (run.Research.WorkerRetained || removal.Status != "removed" || removal.Verified == null ||
                removal.RunId != run.Id || removal.WorkerId != run.Execution.SandboxId || registration.Status != "purged" ||
                Directory.Exists(Path.Combine(root, "qemu-" + run.Execution.SandboxId)))
                throw new InvalidOperationException("Reviewed workspace removal was not independently verified.");
            await File.WriteAllTextAsync(Path.Combine(root, "verified.json"), Wire.Pack(new
            {
                run, events = store.AllEvents(), page = store.Page(run.OutputPath),
                syntheticModelUsage = true, productionQualification = false, selectedMemoryObserved = true, unselectedSourceExcluded = true, boundedRepairObserved = true, artifactReferenceContract = 2,
                worker = registration, workspaceRemoval = removal,
                checkpointRecovery, publicSearch, syntheticSearchProviderRequests = searchTransport.Calls, liveSearchProviderRequests = 0,
                interruption = checkpointRecovery ? "injected after real stopped-VM checkpoint; not an abrupt host crash" : null,
                grantRevoked = Wire.Unpack<WorkerGrant>(store.Setting("worker-grant:" + run.Id)!).Revoked
            }));
            Console.WriteLine("Browser research passed: question, continuation, exact import and reviewed workspace removal. The receipts survived the spring cleaning.");
        }
        finally
        {
            await factory.DisposeAsync(); store.Dispose();
        }
    }

    // Only the provider HTTP response is fictional; native MCP, key custody and public-page retrieval are real.
    private sealed class SearchTransport : HttpMessageHandler
    {
        internal const string Key = "fictional-native-search-key";
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Calls++;
            if (Calls != 1 || request.Method != HttpMethod.Get || request.RequestUri?.AbsoluteUri != BravePublicSearch.Endpoint + "?q=Docker%20Sandboxes%20FAQ&count=5&text_decorations=false" ||
                request.Headers.GetValues("X-Subscription-Token").Single() != Key || request.Headers.Authorization != null || request.Headers.Contains("Cookie"))
                throw new InvalidOperationException("Unexpected search transport or credential routing.");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(
                "{\"web\":{\"results\":[{\"url\":\"https://docs.docker.com/ai/sandboxes/faq/\",\"title\":\"Docker Sandboxes FAQ\",\"description\":\"Discovery fixture description; fetch the page before using a quotation.\"}]}}",
                System.Text.Encoding.UTF8, "application/json") });
        }
    }

    // Fault injection lives only in this verification executable; production has no interruption switch.
    private sealed class CheckpointInterruption(IResearchWorkerFactory inner, Store store) : IResearchWorkerFactory
    {
        private int injected;
        public ResearchAvailability Availability => inner.Availability;
        public IResearchWorker Open(Run run) => new Worker(inner.Open(run), this);
        private sealed class Worker(IResearchWorker inner, CheckpointInterruption owner) : IResearchWorker
        {
            public IExecutionBackend Execution => inner.Execution;
            public Task Prepare(Run run, string grant, CancellationToken token) => inner.Prepare(run, grant, token);
            public Task Wake(Run run, string grant, CancellationToken token) => inner.Wake(run, grant, token);
            public async Task Reconcile(Run run, CancellationToken token)
            {
                await inner.Reconcile(run, token);
                if (run.Research?.Phase == "attention") owner.RecordInspection(run);
            }
            public Task Retire(Run run, CancellationToken token) => inner.Retire(run, token);
            public Task<SandboxText> ReadArtifact(Run run, string path, CancellationToken token) => inner.ReadArtifact(run, path, token);
            public async Task Stop(Run run, CancellationToken token)
            {
                await inner.Stop(run, token);
                if (Interlocked.CompareExchange(ref owner.injected, 1, 0) != 0) return;
                owner.RecordGap(run);
                throw new IOException("Verification interruption after the real worker stopped, before the ready-state receipt.");
            }
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
        private void RecordGap(Run run)
        {
            var current = store.Get(run.Id)!;
            File.WriteAllText(Path.Combine(store.Root, "checkpoint-gap.json"), Wire.Pack(new
            {
                run = current, worker = Wire.Unpack<SandboxRegistration>(store.Setting("sandbox:" + run.Execution!.SandboxId)!),
                simulatedInterruption = true, actualVmStopped = true
            }));
        }
        private void RecordInspection(Run run) => File.WriteAllText(Path.Combine(store.Root, "checkpoint-inspection.json"),
            store.Setting("qemu-recovery:" + run.Execution!.SandboxId) ?? throw new InvalidOperationException("Physical recovery receipt missing."));
    }
}
