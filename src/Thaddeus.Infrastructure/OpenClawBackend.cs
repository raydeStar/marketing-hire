using System.Text.Json;
using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

/// <summary>Uses OpenClaw's public Gateway RPC through its CLI inside the worker.</summary>
public sealed class OpenClawBackend(ISandboxBackend sandbox) : IExecutionBackend
{
    public const string PinnedVersion = "2026.9.4";
    private static void Identity(ExecutionIdentity identity)
    {
        DockerSandboxBackend.ValidateId(identity.SandboxId);
        if (identity.Backend != "openclaw" || identity.RuntimeVersion != PinnedVersion ||
            !Regex.IsMatch(identity.SessionKey, @"\Aagent:thaddeus:[a-f0-9]{32}\z"))
            throw new ArgumentException("Unrecognized execution identity or runtime version.");
    }
    /// <summary>After an explicitly reconciled restart, replace only the task grant before starting the Gateway.</summary>
    public async Task RefreshGrant(ExecutionIdentity identity, ExecutionContextSnapshot context, string grant, CancellationToken cancellation)
    {
        Identity(identity);
        if (!Regex.IsMatch(grant, @"\A[a-f0-9]{64}\z") || context.SchemaVersion != 1 || Wire.Hash(context.Text) != context.ContentHash)
            throw new ArgumentException("Invalid grant or frozen context.");
        var result = await sandbox.Execute(identity.SandboxId, ["python3", "-c", RefreshGrantProgram],
            Wire.Pack(new { runId = identity.SessionKey[15..], identity.SessionKey, identity.RuntimeVersion,
                context.ContentHash, context.ProfileDigest, grant }), cancellation);
        if (result.ExitCode != 0 || result.Output.Trim() != Wire.Hash(grant))
            throw new IOException("Task grant refresh was not confirmed; leave the Gateway stopped and inspect the worker.");
    }

    private const string RefreshGrantProgram = """
        import json, os, sys, stat, hashlib, re, secrets, socket
        request = json.load(sys.stdin)
        gateway_guard = socket.socket()
        gateway_guard.bind(('127.0.0.1', 18789))
        root = '/home/agent/.openclaw'
        for path in ['/home', '/home/agent', root]:
            assert stat.S_ISDIR(os.lstat(path).st_mode), 'Linked state directory'
        directory = os.open(root, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        def read(name):
            fd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=directory)
            with os.fdopen(fd, 'rb') as stream:
                info = os.fstat(stream.fileno())
                assert stat.S_ISREG(info.st_mode) and info.st_size <= 150000, 'Invalid state file'
                value = stream.read(150001)
                assert len(value) <= 150000
                return value
        binding = json.loads(read('thaddeus-binding.json'))
        for field in ['runId', 'sessionKey', 'runtimeVersion', 'contentHash', 'profileDigest']:
            assert binding[field] == request[field], 'Worker binding differs from host context'
        context = json.loads(read('thaddeus-context.json'))
        assert hashlib.sha256(context['text'].encode()).hexdigest() == request['contentHash']
        assert hashlib.sha256(read('openclaw.json')).hexdigest() == binding['configHash']
        old = read('.env').decode()
        match = re.fullmatch(r'THADDEUS_WORKER_TOKEN=[a-f0-9]{64}\nTHADDEUS_GATEWAY_TOKEN=([a-f0-9]{64})\n', old)
        assert match and re.fullmatch(r'[a-f0-9]{64}', request['grant']), 'Invalid environment'
        replacement = ('THADDEUS_WORKER_TOKEN=' + request['grant'] + '\nTHADDEUS_GATEWAY_TOKEN=' + match[1] + '\n').encode()
        temporary = '.grant-' + secrets.token_hex(16)
        fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=directory)
        try:
            with os.fdopen(fd, 'wb') as output:
                output.write(replacement); output.flush(); os.fsync(output.fileno())
            os.replace(temporary, '.env', src_dir_fd=directory, dst_dir_fd=directory)
            os.fsync(directory)
            assert read('.env') == replacement
        finally:
            try: os.unlink(temporary, dir_fd=directory)
            except FileNotFoundError: pass
            os.close(directory)
        print(hashlib.sha256(request['grant'].encode()).hexdigest())
        """;
    public Task<ExecutionObservation> Start(ExecutionStart request, CancellationToken cancellation)
    {
        Identity(request.Identity);
        if (!Regex.IsMatch(request.RunId, @"\A[a-f0-9]{32}\z") || request.Identity.SessionKey != "agent:thaddeus:" + request.RunId)
            throw new ArgumentException("Execution session must match its product task.");
        Message(request.Objective); Operation(request.RunId);
        if (request.Provider.Kind != "compatible" || string.IsNullOrWhiteSpace(request.Provider.Model) || request.Provider.Model.Length > 200 ||
            request.Provider.Reasoning is not ("low" or "medium" or "high")) throw new ArgumentException("Execution requires an exact compatible model profile.");
        if (request.Limits.Seconds is < 1 or > 600) throw new ArgumentException("Execution time budget is outside supported limits.");
        return Rpc(request.Identity, "agent", new
        {
            agentId = "thaddeus", sessionKey = request.Identity.SessionKey, message = request.Objective,
            // Public Gateway callers use the bootstrapped agent model. Per-call route overrides require internal authority.
            // The external model broker independently rejects a route that differs from the frozen product task.
            idempotencyKey = request.RunId, deliver = false, disableMessageTool = true,
            thinking = request.Provider.Reasoning, timeout = request.Limits.Seconds
        }, cancellation, requiresRunId: true);
    }
    public Task<ExecutionObservation> Steer(ExecutionIdentity identity, string message, string operationId, CancellationToken cancellation)
    {
        Identity(identity); Message(message); Operation(operationId);
        // Steering is ordinary user input. System-provenance fields require Gateway admin scope.
        return Rpc(identity, "chat.send", new { sessionKey = identity.SessionKey, message, queueMode = "steer", deliver = false, idempotencyKey = operationId }, cancellation, requiresRunId: true);
    }
    public Task<ExecutionObservation> Resume(ExecutionIdentity identity, string message, string operationId, CancellationToken cancellation)
    {
        Identity(identity); Message(message); Operation(operationId);
        return Rpc(identity, "sessions.send", new { key = identity.SessionKey, message, idempotencyKey = operationId, timeoutMs = 0 }, cancellation, requiresRunId: true);
    }
    public Task<ExecutionObservation> Cancel(ExecutionIdentity identity, CancellationToken cancellation)
    {
        Identity(identity);
        return Rpc(identity, "sessions.abort", identity.RuntimeRunId == null ?
            (object)new { key = identity.SessionKey, clearQueued = true } : new { key = identity.SessionKey, runId = identity.RuntimeRunId, clearQueued = true }, cancellation);
    }
    public Task<ExecutionObservation> Inspect(ExecutionIdentity identity, CancellationToken cancellation)
    {
        Identity(identity);
        if (string.IsNullOrWhiteSpace(identity.RuntimeRunId)) throw new InvalidOperationException("No runtime run ID was acknowledged; inspect the native transcript before continuation.");
        return Rpc(identity, "agent.wait", new { runId = identity.RuntimeRunId, timeoutMs = 0 }, cancellation, requiresRunId: true);
    }
    private async Task<ExecutionObservation> Rpc(ExecutionIdentity identity, string method, object parameters, CancellationToken cancellation, bool requiresRunId = false)
    {
        // Arguments go through JSON stdin; neither the objective nor credentials enter a host shell.
        var result = await sandbox.Execute(identity.SandboxId, ["python3", "-c", RpcProgram],
            Wire.Pack(new { method, parameters, version = PinnedVersion }), cancellation);
        if (result.ExitCode != 0) throw new InvalidOperationException("OpenClaw did not confirm the request. Inspect its transcript before retrying an effect.");
        JsonElement report;
        try { using var parsed = JsonDocument.Parse(result.Output); report = parsed.RootElement.Clone(); }
        catch (JsonException) { throw new InvalidOperationException("OpenClaw returned an unrecognized RPC result."); }
        if (report.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("OpenClaw RPC result must be an object.");
        if (method == "sessions.abort" && (!report.TryGetProperty("thaddeusFilesystemCheckpoint", out var checkpoint) || checkpoint.GetString() != "syncfs"))
            throw new InvalidOperationException("Native stop lacks an acknowledged filesystem checkpoint.");
        var runId = report.TryGetProperty("runId", out var run) && run.ValueKind == JsonValueKind.String ? run.GetString() : null;
        if (requiresRunId && string.IsNullOrWhiteSpace(runId)) throw new InvalidOperationException("OpenClaw did not return a correlated run ID.");
        if (method == "agent.wait" && runId != identity.RuntimeRunId) throw new InvalidOperationException("OpenClaw replied about a different run.");
        var status = report.TryGetProperty("status", out var state) && state.ValueKind == JsonValueKind.String ? state.GetString()! : "reported";
        return new(status, runId, report); // A worker's "ok" is evidence to inspect, never a verified product outcome.
    }
    private static void Message(string message)
    { if (string.IsNullOrWhiteSpace(message) || message.Length > 16000) throw new ArgumentException("Execution message must contain 1–16,000 characters."); }
    private static void Operation(string id)
    { if (!Regex.IsMatch(id, @"\A[a-zA-Z0-9_-]{1,100}\z")) throw new ArgumentException("Invalid execution operation ID."); }
    private const string RpcProgram = """
        import json, subprocess, sys, os, ctypes
        request = json.load(sys.stdin)
        allowed = {'agent', 'agent.wait', 'chat.send', 'sessions.send', 'sessions.abort'}
        assert request['method'] in allowed, 'Unsupported gateway method'
        version = subprocess.run(['openclaw', '--version'], capture_output=True, text=True, timeout=30, check=True)
        assert request['version'] in version.stdout.split(), 'OpenClaw version mismatch'
        command = ['openclaw', 'gateway', 'call', request['method'], '--params', json.dumps(request['parameters']), '--json', '--timeout', '30000']
        result = subprocess.run(command, capture_output=True, text=True, timeout=40)
        if result.returncode != 0:
            # Native diagnostics stay inside the worker; a public error must not echo prompt or credential text.
            path = '/home/agent/.openclaw/thaddeus-rpc-last-error.json'
            fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600)
            with os.fdopen(fd, 'w') as receipt:
                json.dump({'method': request['method'], 'exitCode': result.returncode,
                           'stdout': result.stdout[:20000], 'stderr': result.stderr[:20000]}, receipt)
            print('Gateway request was not confirmed', file=sys.stderr)
            sys.exit(1)
        # Require one clean JSON result. Diagnostic chatter is not a protocol envelope.
        report = json.loads(result.stdout)
        if request['method'] == 'sessions.abort' and report.get('ok') is True:
            # A stop acknowledgement must include storage, not merely a quiet agent. Buffers are forgetful footmen.
            directory = os.open('/home/agent', os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
            try:
                library = ctypes.CDLL(None, use_errno=True)
                if library.syncfs(directory) != 0:
                    raise OSError(ctypes.get_errno(), 'Worker filesystem checkpoint failed')
            finally:
                os.close(directory)
            report['thaddeusFilesystemCheckpoint'] = 'syncfs'
        print(json.dumps(report))
        """;
}
