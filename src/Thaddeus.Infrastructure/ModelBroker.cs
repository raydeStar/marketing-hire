using System.Text.Json;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Runtime
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> activeWorkerModels = new();

    public async Task<InferenceReply> Infer(string id, JsonElement request, IInferenceTransport transport,
        IModelAccessGate access, CancellationToken cancellation)
    {
        Run admitted; JsonElement body; ModelDispatch dispatch; CancellationTokenSource? pendingCancellation = null;
        await Gate(id).WaitAsync(cancellation);
        try
        {
            var run = store.Get(id) ?? throw new ArgumentException("Task not found.");
            if (run.Execution?.Backend != "openclaw" || run.State != RunState.Running)
                throw new InvalidOperationException("Task is not accepting model requests.");
            try { store.AssertMemoriesCurrent(run); }
            catch (InvalidOperationException)
            {
                run.State = RunState.NeedsAttention; run.Summary = "Selected memory changed or was forgotten. Start a new task with reviewed context.";
                store.Save(run, "context.memory.invalidated", new { run.Summary }); throw;
            }
            if (run.Goal.Provider.Reasoning is not ("low" or "medium" or "high")) throw new ArgumentException("Unsupported reasoning profile.");
            body = CompatibleInference.Normalize(run, request);
            access.Check(run.Goal.Provider);
            if (run.Goal.Limits.RequireCertifiedTokenBound)
                throw new InvalidOperationException("Strict admission requires certified provider token bounds. No inference dispatched.");
            if (activeWorkerModels.ContainsKey(id) || run.ModelCalls >= run.Goal.Limits.ModelCalls || run.ReservedTokens != 0 || run.ChargedTokens >= run.Goal.Limits.MaxTotalTokens)
                throw new InvalidOperationException("Task model budget is exhausted or has an unresolved dispatch.");
            var remainingTime = RemainingExecutionTime(run);
            if (remainingTime <= TimeSpan.Zero) throw new InvalidOperationException("Task execution time budget is exhausted.");
            var cts = pendingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            cts.CancelAfter(remainingTime); cancellations[id] = cts;
            try { await transport.Prepare(run.Goal.Provider, cts.Token); cts.Token.ThrowIfCancellationRequested(); }
            catch { cancellations.TryRemove(id, out _); throw; }
            dispatch = new ModelDispatch(Guid.NewGuid().ToString("N"), Wire.Hash(body.GetRawText()), DateTimeOffset.UtcNow,
                "dispatched-outcome-unknown", run.Goal.Limits.MaxTotalTokens - run.ChargedTokens,
                ContextHash: run.PreparedContext?.ContentHash, ContextObserved: run.PreparedContext == null ? null :
                    body.GetProperty("messages").EnumerateArray().Any(message => message.TryGetProperty("content", out var content) &&
                        content.ValueKind == JsonValueKind.String && content.GetString()!.Contains(run.PreparedContext.Text, StringComparison.Ordinal)));
            run.ModelDispatches.Add(dispatch); run.ModelCalls++; run.ReservedTokens = dispatch.ReservedTokens;
            run.TokenAccounting = "Uncertified provider: remaining allowance reserved; no hard remote token ceiling claimed";
            store.Save(run, "worker.model.reserved", new { dispatch, provider = run.Goal.Provider, authority = "broker-verified" });
            activeWorkerModels[id] = dispatch.Id;
            admitted = run;
        }
        catch
        {
            if (pendingCancellation != null) { cancellations.TryRemove(id, out _); pendingCancellation.Dispose(); }
            throw;
        }
        finally { Gate(id).Release(); }

        using (var cts = pendingCancellation!)
        {
            InferenceReply? reply = null; Exception? failure = null;
            // The reservation serializes inference. Release the task lock so user control remains responsive.
            try { reply = await transport.Send(admitted.Goal.Provider, body, cts.Token); cts.Token.ThrowIfCancellationRequested(); }
            catch (Exception error) when (error is not OutOfMemoryException) { failure = error; }
            await Gate(id).WaitAsync();
            try
            {
                // Guidance, cancellation or a broker checkpoint may have changed this row while the model worked.
                var run = store.Get(id) ?? throw new InvalidOperationException("The model's task record is unavailable.");
                var index = run.ModelDispatches.FindIndex(item => item.Id == dispatch.Id);
                if (index < 0 || run.ModelDispatches[index].Status != "dispatched-outcome-unknown")
                    throw new InvalidOperationException("Model dispatch settlement no longer matches its recorded reservation.");
                try
                {
                    if (failure != null) throw failure;
                    cts.Token.ThrowIfCancellationRequested();
                    if (reply == null) throw new InvalidOperationException("No model response was received.");
                    var used = reply.InputTokens is { } input && reply.OutputTokens is { } output ? checked(input + output) : (int?)null;
                    run.ChargedTokens = checked(run.ChargedTokens + (used ?? run.ReservedTokens)); run.ReservedTokens = 0;
                    run.InputTokens = AddUsage(run.InputTokens, reply.InputTokens, run.ModelCalls);
                    run.OutputTokens = AddUsage(run.OutputTokens, reply.OutputTokens, run.ModelCalls);
                    run.ModelDispatches[index] = dispatch with { Status = used == null ? "completed-usage-unknown" : "completed",
                        InputTokens = reply.InputTokens, OutputTokens = reply.OutputTokens, ResponseHash = Wire.Hash(reply.Body.GetRawText()) };
                    store.Save(run, "worker.model.completed", new { dispatch = run.ModelDispatches[index], authority = "broker-observed", usageAuthority = "provider-reported" });
                    store.AssertMemoriesCurrent(run); // Settle the meter, then refuse a reply whose remembered context was revoked in flight.
                    if (run.ChargedTokens > run.Goal.Limits.MaxTotalTokens || reply.OutputTokens > body.GetProperty("max_completion_tokens").GetInt32())
                        throw new InvalidOperationException("Provider exceeded the task budget. No response was released to the worker.");
                    if (run.State != RunState.Running)
                        throw new InvalidOperationException("Task stopped while the model was working. Usage was recorded; no response was released.");
                    return reply;
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    run.ChargedTokens += run.ReservedTokens; run.ReservedTokens = 0;
                    if (run.ModelDispatches[index].Status == "dispatched-outcome-unknown")
                        run.ModelDispatches[index] = dispatch with { Status = "outcome-unknown" };
                    const string summary = "Model dispatch stopped. Inspect the recorded outcome before continuing; no automatic retry.";
                    if (run.State == RunState.Running) { PauseExecutionClock(run); run.State = RunState.NeedsAttention; run.Summary = summary; }
                    store.Save(run, "worker.model.stopped", new { dispatch = run.ModelDispatches[index], summary, authority = "broker-observed" });
                    throw new InvalidOperationException(summary);
                }
            }
            finally
            {
                activeWorkerModels.TryRemove(id, out _);
                cancellations.TryRemove(id, out _);
                Gate(id).Release();
            }
        }
    }
}
