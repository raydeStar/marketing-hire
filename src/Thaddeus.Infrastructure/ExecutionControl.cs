using System.Collections.Concurrent;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Runtime
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> executionLocks = new();

    // These methods control an already provisioned worker. They do not qualify or admit a sandbox.
    // The fixture and future production coordinator share this path; neither gets a second agent loop.
    public Task<ExecutionObservation> StartExecution(string id, IExecutionBackend backend, CancellationToken cancellation) =>
        ControlExecution(id, "start", backend, cancellation);
    public Task<ExecutionObservation> ResumeExecution(string id, IExecutionBackend backend, CancellationToken cancellation) =>
        ControlExecution(id, "resume", backend, cancellation);
    public Task<ExecutionObservation> QuiesceExecution(string id, IExecutionBackend backend, CancellationToken cancellation) =>
        ControlExecution(id, "quiesce", backend, cancellation);

    public async Task<ExecutionObservation> InspectExecution(string id, IExecutionBackend backend, CancellationToken cancellation)
    {
        var controlGate = executionLocks.GetOrAdd(id, _ => new(1, 1));
        await controlGate.WaitAsync(cancellation);
        try
        {
            var run = store.Get(id) ?? throw new ArgumentException("Task not found.");
            if (run.Execution?.RuntimeRunId == null || run.ExecutionCommands.Any(command =>
                command.Kind is "start" or "resume" && command.Status != "acknowledged"))
                throw new InvalidOperationException("Admission has no confirmed native run ID. Inspect the native transcript; querying an older run cannot resolve it.");
            var observation = await backend.Inspect(run.Execution, cancellation);
            if (observation.RuntimeRunId != run.Execution.RuntimeRunId) throw new InvalidOperationException("Inspection replied about a different native run.");
            await Gate(id).WaitAsync(cancellation);
            try
            {
                var current = store.Get(id)!;
                store.Save(current, "execution.inspected", new { observation, outcomeVerified = false });
            }
            finally { Gate(id).Release(); }
            return observation; // A worker's report remains a report. No completion or replay authority is manufactured.
        }
        finally { controlGate.Release(); }
    }

    private async Task<ExecutionObservation> ControlExecution(string id, string kind, IExecutionBackend backend, CancellationToken cancellation)
    {
        var controlGate = executionLocks.GetOrAdd(id, _ => new(1, 1));
        await controlGate.WaitAsync(cancellation);
        try
        {
            ExecutionCommand command; Run admitted; string? message = null;
            await Gate(id).WaitAsync(cancellation);
            try
            {
                admitted = store.Get(id) ?? throw new ArgumentException("Task not found.");
                if (admitted.Execution?.Backend != "openclaw" || admitted.PreparedContext == null)
                    throw new InvalidOperationException("Execution requires an existing worker and frozen context.");
                if (admitted.Execution.SessionKey != "agent:thaddeus:" + id)
                    throw new InvalidOperationException("The native session does not match this task.");
                if (admitted.Profile?.Digest != admitted.PreparedContext.ProfileDigest)
                    throw new InvalidOperationException("The execution profile differs from its frozen context.");
                if (admitted.ExecutionCommands.Count >= 64) throw new InvalidOperationException("Execution control limit reached. Inspect the existing receipts.");
                var operationId = kind == "start" ? id : Guid.NewGuid().ToString("N");
                if (kind == "resume")
                {
                    if (admitted.Question is not { Answer: not null } question)
                        throw new InvalidOperationException("Continuation requires a saved user answer.");
                    operationId = "answer-" + Wire.Hash(Wire.Pack(question));
                    message = "The user answered the pending question. Continue the original task within its existing scope and remaining budget.\n" +
                        "Question: " + question.Text + "\nUser answer: " + question.Answer;
                }
                var hash = Wire.Hash(Wire.Pack(new { kind, admitted.Execution.Backend, admitted.Execution.SandboxId,
                    admitted.Execution.SessionKey, admitted.Execution.RuntimeVersion, message, admitted.Goal.Objective,
                    admitted.Goal.Provider, admitted.Goal.Limits, admitted.Goal.ReadScope, admitted.Goal.WriteScope,
                    admitted.PreparedContext.ContentHash, admitted.PreparedContext.ProfileDigest }));
                var previous = admitted.ExecutionCommands.SingleOrDefault(item => item.Id == operationId);
                if (previous != null)
                {
                    if (previous.Kind != kind || previous.RequestHash != hash) throw new InvalidOperationException("Execution operation changed; do not reuse its identity.");
                    if (previous.Status == "acknowledged" && previous.Observation != null) return previous.Observation;
                    throw new InvalidOperationException("The execution request has an unknown outcome. Inspect it; do not replay it.");
                }
                if (kind != "quiesce" && admitted.ExecutionCommands.Any(item => item.Status != "acknowledged"))
                    throw new InvalidOperationException("An earlier execution request has an unknown outcome. No continuation was dispatched.");
                if (kind == "start" && (admitted.State != RunState.Queued || admitted.Execution.RuntimeRunId != null))
                    throw new InvalidOperationException("Only a fresh queued execution can start.");
                if (kind == "resume" && (admitted.State != RunState.Paused || admitted.Execution.RuntimeRunId == null ||
                    admitted.ExecutionCommands.LastOrDefault() is not { Kind: "quiesce", Status: "acknowledged" }))
                    throw new InvalidOperationException("Continuation requires a paused task and a recorded native stop acknowledgement.");
                if (kind == "quiesce" && admitted.State is not (RunState.AwaitingInput or RunState.AwaitingApproval or RunState.Paused or RunState.NeedsAttention))
                    throw new InvalidOperationException("Quiescence is only available after broker work has stopped.");
                if (kind != "quiesce")
                {
                    if (admitted.ExecutionActiveSeconds >= admitted.Goal.Limits.Seconds || admitted.ReservedTokens != 0 ||
                        admitted.ModelCalls >= admitted.Goal.Limits.ModelCalls || admitted.ChargedTokens >= admitted.Goal.Limits.MaxTotalTokens)
                        throw new InvalidOperationException("Execution budget is exhausted or unresolved. No request was dispatched.");
                    admitted.State = RunState.Running;
                    admitted.ExecutionDeadlineStart = DateTimeOffset.UtcNow;
                    admitted.Summary = "Execution request recorded · awaiting native acknowledgement";
                }
                command = new(operationId, kind, hash, DateTimeOffset.UtcNow);
                admitted.ExecutionCommands.Add(command);
                store.Save(admitted, "execution.command.intent", new { command, authority = "host-controller" });
            }
            finally { Gate(id).Release(); }

            // Release the broker lock before RPC: OpenClaw can call MCP/model routes before its acknowledgement arrives.
            ExecutionObservation observation;
            try
            {
                observation = kind switch
                {
                    "start" => await backend.Start(new(id, admitted.Execution!, admitted.Goal.Objective, admitted.Goal.Provider, admitted.Goal.Limits), cancellation),
                    "resume" => await backend.Resume(admitted.Execution!, message!, command.Id, cancellation),
                    _ => await backend.Cancel(admitted.Execution!, cancellation)
                };
                if (kind != "quiesce" && (string.IsNullOrWhiteSpace(observation.RuntimeRunId) || observation.Status is not ("accepted" or "started" or "ok")))
                    throw new InvalidOperationException("Native execution did not acknowledge admission.");
                if (kind == "quiesce" && (observation.Status is not ("aborted" or "no-active-run") ||
                    !observation.Report.TryGetProperty("ok", out var ok) || ok.ValueKind != System.Text.Json.JsonValueKind.True))
                    throw new InvalidOperationException("The native stop was not acknowledged.");
            }
            catch (Exception)
            {
                await RecordExecutionFailure(id, command.Id);
                throw new InvalidOperationException("Execution request outcome is unknown. Inspect native receipts before any continuation; no automatic retry.");
            }
            await Gate(id).WaitAsync();
            try
            {
                var current = store.Get(id)!;
                var index = current.ExecutionCommands.FindIndex(item => item.Id == command.Id);
                current.ExecutionCommands[index] = command with { Status = "acknowledged", Observation = observation };
                if (kind != "quiesce") current.Execution = current.Execution! with { RuntimeRunId = observation.RuntimeRunId };
                if (kind != "quiesce" && current.State == RunState.Running) current.Summary = "Native execution acknowledged · waiting for broker evidence";
                // The latest row may already contain a question, proposal or broker failure. Never overwrite it with "running".
                store.Save(current, "execution.command.acknowledged", new { command = current.ExecutionCommands[index], authority = "host-observed-rpc", outcomeVerified = false });
                return observation;
            }
            finally { Gate(id).Release(); }
        }
        finally { controlGate.Release(); }
    }

    private async Task RecordExecutionFailure(string id, string commandId)
    {
        await Gate(id).WaitAsync();
        try
        {
            var run = store.Get(id)!;
            if (run.State == RunState.Running) { PauseExecutionClock(run); run.State = RunState.NeedsAttention; }
            run.Summary = "Execution request outcome unknown · inspect native receipts before continuing";
            store.Save(run, "execution.command.unknown", new { commandId, run.Summary });
        }
        finally { Gate(id).Release(); }
    }

    private static void PauseExecutionClock(Run run)
    {
        if (run.ExecutionDeadlineStart is { } started)
        {
            run.ExecutionActiveSeconds += Math.Max(0, (DateTimeOffset.UtcNow - started).TotalSeconds);
            run.ExecutionDeadlineStart = null;
        }
    }

    private static TimeSpan RemainingExecutionTime(Run run) =>
        TimeSpan.FromSeconds(run.Goal.Limits.Seconds - run.ExecutionActiveSeconds) -
        (DateTimeOffset.UtcNow - (run.ExecutionDeadlineStart ?? run.Created));
}
