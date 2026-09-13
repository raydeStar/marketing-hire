using System.Diagnostics;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

// A bounded hostile-traffic fixture against the actual VM transport. It starts no agent or inference loop.
[SupportedOSPlatform("windows10.0")]
internal static class NativeTransportCheck
{
    public static async Task Run(string directory, string configuration)
    {
        var artifacts = Path.GetFullPath("artifacts") + Path.DirectorySeparatorChar;
        var root = Path.GetFullPath(directory); var config = Path.GetFullPath(configuration);
        if (!root.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root) ||
            !config.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase) || new FileInfo(config).Length > 64000)
            throw new ArgumentException("Use a fresh private artifact directory and a bounded pinned installation under artifacts.");
        using var document = JsonDocument.Parse(File.ReadAllText(config));
        if (document.RootElement.GetProperty("kind").GetString() != "qemu") throw new ArgumentException("Expected the explicit QEMU installation.");
        var installation = document.RootElement.GetProperty("installation").Deserialize<QemuInstallation>(Wire.Json)!;
        PrivateWorkerDirectory.Create(root);
        File.WriteAllText(Path.Combine(root, "intent.json"), Wire.Pack(new
        {
            purpose = "Real VM broker routing, body/header/response bounds and per-boot request exhaustion",
            liveModelCalls = 0, gpuInference = 0, workerBoots = 1, maximumGuestRequests = 250,
            installationSha256 = Wire.Hash(File.ReadAllText(config)), sourceRevision = Environment.GetEnvironmentVariable("THADDEUS_CHECK_SOURCE")
        }));
        using var store = new Store(root); var runId = Guid.NewGuid().ToString("N"); var workerId = "thaddeus-" + runId;
        var path = "/worker/" + runId + "/mcp";
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build(); var observed = new List<object>(); var forbidden = 0;
        app.MapGet("/health", () => new { ok = true });
        app.MapGet("/api/state", () => { Interlocked.Increment(ref forbidden); return "not a worker route"; });
        app.MapGet("/escape", () => { Interlocked.Increment(ref forbidden); return "redirect destination"; });
        app.MapPost(path, async (HttpContext context) =>
        {
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            var mode = body.RootElement.GetProperty("mode").GetString();
            lock (observed) observed.Add(new { mode, authorization = context.Request.Headers.Authorization.ToString() == "Bearer fixture",
                cookiePresent = context.Request.Headers.ContainsKey("cookie"), browserCsrfPresent = context.Request.Headers.ContainsKey("x-csrf") });
            if (context.Request.Headers.Authorization != "Bearer fixture") return Results.StatusCode(403);
            if (mode == "large") return Results.Bytes(new byte[1500001], "application/octet-stream");
            if (mode == "redirect") { context.Response.Headers.SetCookie = "fixture=must-not-stick; Path=/"; return Results.Redirect("/escape"); }
            return Results.Json(new { ok = true, cookie = context.Request.Headers.ContainsKey("cookie"), csrf = context.Request.Headers.ContainsKey("x-csrf") });
        });
        await app.StartAsync(); var origin = new Uri(app.Urls.Single());
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var backend = new QemuSandboxBackend(store, installation, new(runId, origin.Port));
        using var host = Process.GetCurrentProcess(); var privateBefore = host.PrivateMemorySize64;
        try
        {
            Console.WriteLine("Transport qualification: starting one isolated worker. The guest may knock; it does not choose the door.");
            await backend.Create(new(workerId, installation.Image), deadline.Token);
            var boot = backend.Observation!;
            using var process = Process.GetProcessById(boot.ProcessId); _ = process.Handle; // Hold original identity through forced exit.
            var result = await backend.Execute(workerId, ["python3", "-c", Cases, runId], null, deadline.Token);
            File.WriteAllText(Path.Combine(root, "guest-checks.json"), Wire.Pack(result));
            if (result.ExitCode != 0) throw new InvalidOperationException("Guest routing/body/response checks failed; inspect retained output.");
            var checks = JsonSerializer.Deserialize<Dictionary<string, int>>(result.Output)!;
            if (checks.Count != 12 || forbidden != 0) throw new InvalidOperationException("A forbidden route was reached or a declared case was not observed.");
            Console.WriteLine("Twelve real guest checks passed. Exercising the bounded request ceiling next.");
            var rejected = false;
            try { await backend.Execute(workerId, ["python3", "-c", Flood, runId], null, deadline.Token); }
            catch (Exception error) when (error is IOException or OperationCanceledException)
            { rejected = true; File.WriteAllText(Path.Combine(root, "exhaustion.json"), Wire.Pack(new { rejected, exception = error.GetType().Name })); }
            if (!rejected) throw new InvalidOperationException("The request ceiling did not stop the worker.");
            await process.WaitForExitAsync(deadline.Token);
            using var client = new HttpClient(); if (!(await client.GetAsync(new Uri(origin, "/health"), deadline.Token)).IsSuccessStatusCode)
                throw new InvalidOperationException("Host did not remain responsive after guest exhaustion.");
            var replayDenied = false;
            try { await backend.Execute(workerId, ["true"], null, deadline.Token); }
            catch (InvalidOperationException) { replayDenied = true; }
            if (!replayDenied || forbidden != 0) throw new InvalidOperationException("Unexpected post-failure dispatch or forbidden destination.");
            await backend.DisposeAsync();
            await using var recovery = new QemuSandboxBackend(store, installation, new(runId, origin.Port));
            var stopped = await recovery.ReconcileStopped(workerId, deadline.Token);
            if (stopped.Status != "stopped" || stopped.Booted || stopped.ReplayedCommands || !stopped.OverlayUnchanged)
                throw new InvalidOperationException("Transport failure did not leave a stopped, inspectable worker.");
            host.Refresh();
            var records = observed.ToArray();
            if (records.Length > 200 || records.Any(record => JsonSerializer.SerializeToElement(record).GetProperty("cookiePresent").GetBoolean()))
                throw new InvalidOperationException("Forwarded traffic exceeded bounds or included an ambient cookie.");
            File.WriteAllText(Path.Combine(root, "verified.json"), Wire.Pack(new
            {
                passed = true, checks, brokerRequests = records.Length, observed = records, forbiddenDestinations = forbidden,
                boot, ownedWorkerExited = process.HasExited, hostResponsive = true, replayDenied, recovery = stopped,
                privateBefore, privateAfter = host.PrivateMemorySize64, hostPeakWorkingSet = host.PeakWorkingSet64,
                memoryScope = "Observed host snapshots, not a hard whole-host memory ceiling", liveModelCalls = 0, gpuInference = 0,
                productionQualified = false, overlayRetained = true
            }));
            Console.WriteLine("Transport checks passed: the excessive guest was stopped, the host stayed responsive, and its disk remains inspectable.");
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(root, "failed.json"), Wire.Pack(new { exception = error.GetType().Name, message = error.Message })); throw;
        }
        finally { await backend.DisposeAsync(); await app.StopAsync(); }
    }

    private const string Cases = """
import json, sys, urllib.request, urllib.error
path='/worker/'+sys.argv[1]+'/mcp'
results={}
def check(name, expected, target=path, method='POST', data=None, headers=None):
    payload=json.dumps({'mode':'echo'}).encode() if data is None else data
    request=urllib.request.Request('http://127.0.0.1:5182'+target,data=payload,method=method,
        headers={'Authorization':'Bearer fixture','Content-Type':'application/json'} if headers is None else headers)
    try:
        with urllib.request.urlopen(request,timeout=10) as response: status=response.status; body=response.read()
    except urllib.error.HTTPError as error: status=error.code; body=error.read()
    assert status==expected,(name,status,expected)
    results[name]=status
    if name=='echo': assert json.loads(body)=={'ok':True,'cookie':False,'csrf':False}
check('echo',200,headers={'Authorization':'Bearer fixture','Cookie':'owner=not-real','X-CSRF':'not-real'})
check('product',502,target='/api/state')
check('other-task',502,target='/worker/'+'f'*32+'/mcp')
check('traversal',502,target=path+'/../v1/chat/completions')
check('query',502,target=path+'?destination=elsewhere')
check('method',502,method='PUT')
check('get-body',502,method='GET')
check('missing-grant',403,headers={'Content-Type':'application/json'})
check('large-header',502,headers={'Authorization':'x'*8193})
check('large-request',413,data=b'x'*150001)
check('large-response',502,data=json.dumps({'mode':'large'}).encode())
check('redirect',302,data=json.dumps({'mode':'redirect'}).encode())
print(json.dumps(results))
""";
    private const string Flood = """
import json, sys, urllib.request
path='http://127.0.0.1:5182/worker/'+sys.argv[1]+'/mcp'
for i in range(250):
    request=urllib.request.Request(path,data=b'{"mode":"echo"}',headers={'Authorization':'Bearer fixture','Content-Type':'application/json'})
    with urllib.request.urlopen(request,timeout=5) as response: assert response.status==200
print('The guest unexpectedly completed its excessive request schedule.')
""";
}
