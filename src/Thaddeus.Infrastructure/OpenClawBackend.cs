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
        return Rpc(identity, "chat.send", new { sessionKey = identity.SessionKey, message, queueMode = "steer", deliver = false, suppressCommandInterpretation = true, idempotencyKey = operationId }, cancellation, requiresRunId: true);
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
        import json, subprocess, sys, os
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
        print(json.dumps(report))
        """;
}
