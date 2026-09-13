extern alias host;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Thaddeus.Core;
using Thaddeus.Infrastructure;
using Thaddeus.Lab;
using ProductProgram = host::Program;

internal static class NativeLabMaintenance
{
    internal static async Task Retire(string campaign, NativeRegistration registration, string digest, string itemId)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException("This owned QEMU maintenance path requires Windows.");
        var item = registration.Plan.Single(item => item.Id == itemId);
        var root = Path.Combine(campaign, item.Id);
        var capturePath = Path.Combine(root, "capture.json");
        var originalHash = NativeRegistration.FileHash(capturePath);
        var capture = Wire.Unpack<NativeLabCapture>(await File.ReadAllTextAsync(capturePath));
        if (capture.Item != item || capture.RegistrationHash != digest) throw new InvalidOperationException("The saved failure does not match the registered item.");
        Run before;
        using (var inspection = new Store(root))
        {
            before = inspection.List().Single();
            if (before.Id != capture.Run.Id || before.State != RunState.Cancelled || before.Research?.Phase is not ("cleanup" or "cleanup-attention") ||
                before.Approval?.Decision == "approved" || before.OutputPath != null)
                throw new InvalidOperationException("Retirement requires an already-cancelled Lab task with no approved or imported effect.");
        }
        foreach (var pin in registration.Installation.Files)
            if (NativeRegistration.FileHash(pin.Path) != pin.Sha256) throw new IOException("A registered VM maintenance input differs.");
        await NativeRegistration.WriteNew(Path.Combine(root, "retirement-intent.json"), Wire.Pack(new
        {
            registrationHash = digest, item, originalCaptureHash = originalHash, sourceRevision = NativeRegistration.Git(Directory.GetCurrentDirectory(), "rev-parse", "HEAD"),
            sources = NativeRegistration.SourceFiles(Directory.GetCurrentDirectory()), assemblies = NativeRegistration.Binaries(),
            requested = DateTimeOffset.UtcNow, newModelCalls = 0, workerExecutionAllowed = false, workspaceDeletionAllowed = false
        }));
        const int port = 5182;
        var transport = new RefuseInference();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var factory = new WebApplicationFactory<ProductProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(Path.Combine(Directory.GetCurrentDirectory(), "src/Thaddeus.Host"));
            builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", "http://127.0.0.1:" + port);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IInferenceTransport>(transport);
                services.AddSingleton<Func<ProviderSnapshot, IModelProvider>>(_ => _ => throw new InvalidOperationException("Maintenance cannot invoke a model."));
                services.AddSingleton<IResearchWorkerFactory>(services => OperatingSystem.IsWindowsVersionAtLeast(10)
                    ? new MaintenanceFactory(new QemuResearchFactory(services.GetRequiredService<Store>(), registration.Installation, port))
                    : throw new PlatformNotSupportedException());
            });
        });
        Store? store = null;
        try
        {
            // The ordinary host controller performs retirement. All execution entry points are disabled in this composition.
            factory.UseKestrel(port); using var client = factory.CreateClient();
            store = factory.Services.GetRequiredService<Store>();
            var controller = factory.Services.GetRequiredService<ResearchCoordinator>();
            if (store.Get(before.Id)!.Research!.Phase == "cleanup-attention") await controller.Cancel(before.Id);
            await controller.Tick(deadline.Token);
            var after = store.Get(before.Id)!;
            var review = await controller.InspectWorkspace(before.Id, deadline.Token);
            if (after.State != RunState.Cancelled || after.Research?.Phase != "finished" || !review.CanRemove || transport.Attempts != 0 ||
                after.ModelCalls != before.ModelCalls || after.ChargedTokens != before.ChargedTokens || after.ReservedTokens != before.ReservedTokens ||
                after.InputTokens != before.InputTokens || after.OutputTokens != before.OutputTokens || after.OutputPath != null ||
                Wire.Pack(after.Goal) != Wire.Pack(before.Goal) || Wire.Pack(after.ExecutionCommands) != Wire.Pack(before.ExecutionCommands) ||
                NativeRegistration.FileHash(capturePath) != originalHash)
                throw new InvalidOperationException("Maintenance did not establish unchanged model usage, saved evidence and retired ownership.");
            await NativeRegistration.WriteNew(Path.Combine(root, "retirement-check.json"), Wire.Pack(new
            {
                passed = true, observed = DateTimeOffset.UtcNow, originalCaptureHash = originalHash, newModelCalls = 0, transport.Attempts,
                after.ModelCalls, after.InputTokens, after.OutputTokens, after.ChargedTokens, after.ReservedTokens,
                review, run = after, events = store.AllEvents().ToArray(), workerExecutionAllowed = false, workspaceDeleted = false
            }));
            Console.WriteLine("Cancelled workspace retired and retained for inspection. Zero model calls; the failed receipt keeps its place in the ledger.");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            await NativeRegistration.WriteNew(Path.Combine(root, "retirement-failure.json"), Wire.Pack(new
                { classification = error.GetType().Name, error.Message, transport.Attempts, automaticReplay = false }));
            throw;
        }
        finally { await factory.DisposeAsync(); store?.Dispose(); }
    }

    private sealed class RefuseInference : IInferenceTransport
    {
        public int Attempts;
        public Task<InferenceReply> Send(ProviderSnapshot provider, JsonElement body, CancellationToken cancellation)
        { Attempts++; throw new InvalidOperationException("Retirement cannot dispatch inference."); }
    }
    private sealed class MaintenanceFactory(IResearchWorkerFactory inner) : IResearchWorkerFactory
    {
        public ResearchAvailability Availability => new(false, "qemu-maintenance", "retirement-only", "New research and worker execution are disabled during retirement.", true);
        public IResearchWorker Open(Run run) => new MaintenanceWorker(inner.Open(run));
    }
    private sealed class MaintenanceWorker(IResearchWorker inner) : IResearchWorker
    {
        public IExecutionBackend Execution => throw new InvalidOperationException("Maintenance cannot execute worker commands.");
        public Task Prepare(Run run, string grant, CancellationToken cancellation) => throw new InvalidOperationException("Maintenance cannot prepare a worker.");
        public Task Wake(Run run, string grant, CancellationToken cancellation) => throw new InvalidOperationException("Maintenance cannot wake a worker.");
        public Task<SandboxText> ReadArtifact(Run run, string path, CancellationToken cancellation) => throw new InvalidOperationException("Retirement does not read live artifacts.");
        public Task Reconcile(Run run, CancellationToken cancellation) => inner.Reconcile(run, cancellation);
        public Task Stop(Run run, CancellationToken cancellation) => inner.Stop(run, cancellation);
        public Task Retire(Run run, CancellationToken cancellation) => inner.Retire(run, cancellation);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
