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
        if (args.Length != 2) throw new ArgumentException("Usage: ResearchCheck FRESH_PRIVATE_ARTIFACT_DIRECTORY PINNED_INSTALLATION_JSON");
        var artifacts = Path.GetFullPath("artifacts") + Path.DirectorySeparatorChar;
        var root = Path.GetFullPath(args[0]); var config = Path.GetFullPath(args[1]);
        if (!root.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root) ||
            !config.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase) || new FileInfo(config).Length > 64000)
            throw new ArgumentException("Use a fresh fixture directory and bounded installation configuration under artifacts.");
        PrivateWorkerDirectory.Create(root);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(config));
        var installation = document.RootElement.GetProperty("installation").Deserialize<QemuInstallation>(Wire.Json)!;
        const int port = 5182;
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(Path.GetFullPath("src/Thaddeus.Host"));
            builder.UseSetting("Thaddeus:Data", root);
            builder.UseSetting("Thaddeus:LocalOrigin", "http://127.0.0.1:" + port);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IResearchWorkerFactory>(services => OperatingSystem.IsWindowsVersionAtLeast(10)
                    ? new QemuResearchFactory(services.GetRequiredService<Store>(), installation, port) : throw new PlatformNotSupportedException());
                services.AddSingleton<IInferenceTransport, ScriptedNativeModel>();
                services.AddSingleton<Func<ProviderSnapshot, IModelProvider>>(_ => _ => throw new InvalidOperationException("This fixture never dispatches a host model loop."));
            });
        });
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new() { BaseAddress = new("http://127.0.0.1:" + port) });
        var store = factory.Services.GetRequiredService<Store>();
        store.Write("notes/source.md", "# Workshop\nA fictional workshop lasts 45 minutes. Its audience has not been selected.\n", "absent");
        store.Write("notes/memory-source.md", "The workshop handout color is cobalt.\nThe spare notebook is jade.\nUnselected source content: marigold-administration.\n", "absent");
        store.Setting("provider", Wire.Pack(new ProviderSnapshot("compatible", "scripted-native-protocol-fixture", "high", "https://model.fixture.invalid/v1")));
        await File.WriteAllTextAsync(Path.Combine(root, "ready.json"), Wire.Pack(new { origin = client.BaseAddress, modelTransport = "scripted", realVm = true, isolationQualified = false }));
        Console.WriteLine("Research browser fixture ready on port 5182. A real worker, a fictional model, and no GPU appetite.");
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(12));
            while (!File.Exists(Path.Combine(root, "browser-finished.json"))) await Task.Delay(500, deadline.Token);
            var run = store.List().Single();
            if (run.State != RunState.Succeeded || run.Research is not { Phase: "finished", Review: not null } ||
                run.OutputPath == null || store.Version(run.OutputPath) != run.Research.Review.Sha256 ||
                run.ExecutionCommands.Any(command => command.Status != "acknowledged") || run.Capabilities.Any(call => call.IsError))
                throw new InvalidOperationException("Browser fixture ended without a verified import and retired worker.");
            var removal = Wire.Unpack<WorkspaceRemoval>(store.Setting("workspace-removal:" + run.Id) ?? throw new InvalidOperationException("Removal receipt is missing."));
            var registration = Wire.Unpack<SandboxRegistration>(store.Setting("sandbox:" + run.Execution!.SandboxId)!);
            if (run.PreparedContext?.Memories is not { Length: 1 } selected || selected[0].Statement != "Use cobalt workshop handouts." ||
                run.Goal.ReadScope.Contains("notes/memory-source.md") || run.ModelDispatches.Any(dispatch => dispatch.ContextObserved != true))
                throw new InvalidOperationException("Selected memory activation was not observed in every native model dispatch.");
            foreach (var dispatch in Enumerable.Range(1, run.ModelCalls))
            {
                using var input = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, $"synthetic-request-{dispatch}.json")));
                var text = string.Join("\n", input.RootElement.GetProperty("messages").EnumerateArray().Where(message => message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String).Select(message => message.GetProperty("content").GetString()));
                if (!text.Contains(run.PreparedContext.Text, StringComparison.Ordinal) || text.Contains("marigold-administration", StringComparison.Ordinal) || text.Contains("spare notebook", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Native memory context was missing or included unselected source data.");
            }
            if (run.Research.WorkerRetained || removal.Status != "removed" || removal.Verified == null ||
                removal.RunId != run.Id || removal.WorkerId != run.Execution.SandboxId || registration.Status != "purged" ||
                Directory.Exists(Path.Combine(root, "qemu-" + run.Execution.SandboxId)))
                throw new InvalidOperationException("Reviewed workspace removal was not independently verified.");
            await File.WriteAllTextAsync(Path.Combine(root, "verified.json"), Wire.Pack(new
            {
                run, events = store.AllEvents(), page = store.Page(run.OutputPath),
                syntheticModelUsage = true, productionQualification = false, selectedMemoryObserved = true, unselectedSourceExcluded = true,
                worker = registration, workspaceRemoval = removal,
                grantRevoked = Wire.Unpack<WorkerGrant>(store.Setting("worker-grant:" + run.Id)!).Revoked
            }));
            Console.WriteLine("Browser research passed: question, continuation, exact import and reviewed workspace removal. The receipts survived the spring cleaning.");
        }
        finally
        {
            await factory.DisposeAsync(); store.Dispose();
        }
    }
}
