using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

if (args.Length is < 2 or > 3 || args[0] is not ("inspect" or "create" or "probe" or "stop" or "remove" or "reconcile-absent"))
    throw new ArgumentException("Usage: WorkerCheck inspect|create|probe|stop|remove|reconcile-absent DATA_DIRECTORY [PINNED_IMAGE]");
using var store = new Store(args[1]);
var runner = new CapturingRunner(store.Root);
var backend = new DockerSandboxBackend(runner, DockerSandboxBackend.FindExecutable(), store);
var step = args[0];
using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(12));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    if (step == "inspect") Console.WriteLine(Wire.Pack(await backend.Inspect(cancellation.Token)));
    else if (step == "create")
    {
        if (args.Length != 3) throw new ArgumentException("Creation requires an exact image digest.");
        var spec = new SandboxSpec("thaddeus-" + Guid.NewGuid().ToString("N"), args[2]);
        await backend.Create(spec, cancellation.Token);
        Console.WriteLine(Wire.Pack(new { spec, status = "created-unqualified", message = "The estate exists. Its locks still need inspection." }));
    }
    else
    {
        var id = store.Setting("active-sandbox") ?? throw new InvalidOperationException("No worker is registered in this check directory.");
        if (step == "stop") { await backend.Stop(id, cancellation.Token); Console.WriteLine("Owned worker stopped."); }
        else if (step == "remove") { await backend.Remove(id, cancellation.Token); Console.WriteLine("Owned worker removed."); }
        else if (step == "reconcile-absent") { await backend.ReconcileAbsentCreation(id, cancellation.Token); Console.WriteLine("Current inventory confirmed the unacknowledged worker is absent. No effect was retried."); }
        else
        {
            var probe = await backend.Execute(id, ["python3", "-c", Probe.Program], null, cancellation.Token);
            if (probe.ExitCode != 0) throw new InvalidOperationException("Boundary probe did not finish. Inspect process receipts.");
            using var parsed = JsonDocument.Parse(probe.Output);
            var artifact = "probe-" + Guid.NewGuid().ToString("N") + ".txt";
            var content = "Private worker round trip · " + Guid.NewGuid().ToString("N");
            await backend.PutText(id, artifact, content, cancellation.Token);
            var copy = await backend.GetText(id, artifact, cancellation.Token);
            await backend.Stop(id, cancellation.Token);
            var recovered = await backend.GetText(id, artifact, cancellation.Token);
            var receipt = new { schemaVersion = 1, id, time = DateTimeOffset.UtcNow, authority = "pre-agent-observed",
                observation = parsed.RootElement.Clone(), artifactTransfer = copy.Content == content,
                stopAndRestartPersistence = recovered.Sha256 == copy.Sha256,
                unverified = new[] { "Host-side VM and mount metadata", "Broker-only network grant", "OpenClaw native execution", "Production admission" } };
            await File.WriteAllTextAsync(Path.Combine(store.Root, "boundary-probe.json"), Wire.Pack(receipt));
            Console.WriteLine(Wire.Pack(receipt));
        }
    }
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OperationCanceledException)
{
    Console.Error.WriteLine(Wire.Pack(new { step, error = ex.Message, evidenceDirectory = store.Root })); Environment.ExitCode = 1;
}

sealed class CapturingRunner(string root) : IHostProcessRunner
{
    private readonly HostProcessRunner inner = new();
    public async Task<HostProcessResult> Run(HostProcessRequest request, CancellationToken cancellation)
    {
        var result = await inner.Run(request, cancellation);
        // This diagnostic tool uses only fixed pre-agent commands. Never route model credentials through it.
        var receipt = new { time = DateTimeOffset.UtcNow, request.Arguments, result.ExitCode, result.Failure, result.Output, result.Error };
        await File.WriteAllTextAsync(Path.Combine(root, "process-" + Guid.NewGuid().ToString("N") + ".json"), Wire.Pack(receipt));
        return result;
    }
}
static class Probe
{
    public const string Program = """
        import os, pathlib, json, socket, urllib.request, subprocess, glob
        def read(path):
            try: return pathlib.Path(path).read_text()[:20000]
            except OSError: return None
        def tcp(host, port):
            try:
                with socket.create_connection((host, port), timeout=3): return 'connected'
            except OSError: return 'blocked-or-unreachable'
        def https():
            try:
                with urllib.request.urlopen('https://example.com/', timeout=5) as r: return str(r.status)
            except Exception as e: return type(e).__name__
        home = pathlib.Path.home()
        try: runtime_version = subprocess.check_output(['openclaw','--version'], text=True, timeout=30).strip()
        except (OSError, subprocess.SubprocessError): runtime_version = 'unavailable; base-isolation probe only'
        print(json.dumps({
            'uid': os.getuid(), 'home': str(home), 'hostname': socket.gethostname(),
            'cpuCount': os.cpu_count(), 'memoryInfo': read('/proc/meminfo'),
            'cgroupCpu': read('/sys/fs/cgroup/cpu.max'), 'cgroupMemory': read('/sys/fs/cgroup/memory.max'),
            'mounts': read('/proc/mounts'), 'gpuDevices': glob.glob('/dev/nvidia*'),
            'sensitivePathsPresent': {p: os.path.exists(p) for p in ['/var/run/docker.sock','/run/docker.sock','/host','/mnt/host','/mnt/c']},
            'credentialDirectoriesPresent': {p: (home/p).exists() for p in ['.ssh','.aws','.azure','.kube','.docker']},
            'sshAgentForwarded': bool(os.environ.get('SSH_AUTH_SOCK')),
            'publicTcp443': tcp('1.1.1.1',443), 'publicHttps': https(),
            'openclawVersion': runtime_version
        }))
        """;
}
