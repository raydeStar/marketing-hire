using System.Text.Json;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Runtime
{
    public async Task<InferenceReply> Infer(string id, JsonElement request, IInferenceTransport transport,
        IModelAccessGate access, CancellationToken cancellation)
    {
        await Gate(id).WaitAsync(cancellation);
        try
        {
            var run = store.Get(id) ?? throw new ArgumentException("Task not found.");
            if (run.Execution?.Backend != "openclaw" || run.State != RunState.Running)
                throw new InvalidOperationException("Task is not accepting model requests.");
            if (run.Goal.Provider.Reasoning is not ("low" or "medium" or "high")) throw new ArgumentException("Unsupported reasoning profile.");
            var body = CompatibleInference.Normalize(run, request);
            access.Check(run.Goal.Provider);
            if (run.Goal.Limits.RequireCertifiedTokenBound)
                throw new InvalidOperationException("Strict admission requires certified provider token bounds. No inference dispatched.");
            if (run.ModelCalls >= run.Goal.Limits.ModelCalls || run.ReservedTokens != 0 || run.ChargedTokens >= run.Goal.Limits.MaxTotalTokens)
                throw new InvalidOperationException("Task model budget is exhausted or has an unresolved dispatch.");
            var remainingTime = TimeSpan.FromSeconds(run.Goal.Limits.Seconds) - (DateTimeOffset.UtcNow - (run.ExecutionDeadlineStart ?? run.Created));
            if (remainingTime <= TimeSpan.Zero) throw new InvalidOperationException("Task execution time budget is exhausted.");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            cts.CancelAfter(remainingTime); cancellations[id] = cts;
            var dispatch = new ModelDispatch(Guid.NewGuid().ToString("N"), Wire.Hash(body.GetRawText()), DateTimeOffset.UtcNow,
                "dispatched-outcome-unknown", run.Goal.Limits.MaxTotalTokens - run.ChargedTokens,
                ContextHash: run.PreparedContext?.ContentHash, ContextObserved: run.PreparedContext == null ? null :
                    body.GetProperty("messages").EnumerateArray().Any(message => message.TryGetProperty("content", out var content) &&
                        content.ValueKind == JsonValueKind.String && content.GetString()!.Contains(run.PreparedContext.Text, StringComparison.Ordinal)));
            run.ModelDispatches.Add(dispatch); run.ModelCalls++; run.ReservedTokens = dispatch.ReservedTokens;
            run.TokenAccounting = "Uncertified provider: remaining allowance reserved; no hard remote token ceiling claimed";
            store.Save(run, "worker.model.reserved", new { dispatch, provider = run.Goal.Provider, authority = "broker-verified" });
            try
            {
                var reply = await transport.Send(run.Goal.Provider, body, cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                var used = reply.InputTokens is { } input && reply.OutputTokens is { } output ? checked(input + output) : (int?)null;
                run.ChargedTokens = checked(run.ChargedTokens + (used ?? run.ReservedTokens)); run.ReservedTokens = 0;
                run.InputTokens = AddUsage(run.InputTokens, reply.InputTokens, run.ModelCalls);
                run.OutputTokens = AddUsage(run.OutputTokens, reply.OutputTokens, run.ModelCalls);
                run.ModelDispatches[^1] = dispatch with { Status = used == null ? "completed-usage-unknown" : "completed",
                    InputTokens = reply.InputTokens, OutputTokens = reply.OutputTokens, ResponseHash = Wire.Hash(reply.Body.GetRawText()) };
                store.Save(run, "worker.model.completed", new { dispatch = run.ModelDispatches[^1], authority = "broker-observed", usageAuthority = "provider-reported" });
                if (run.ChargedTokens > run.Goal.Limits.MaxTotalTokens || reply.OutputTokens > body.GetProperty("max_completion_tokens").GetInt32())
                    throw new InvalidOperationException("Provider exceeded the task budget. No response was released to the worker.");
                return reply;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException or InvalidOperationException or OverflowException)
            {
                run.ChargedTokens += run.ReservedTokens; run.ReservedTokens = 0;
                if (run.ModelDispatches[^1].Status == "dispatched-outcome-unknown")
                    run.ModelDispatches[^1] = dispatch with { Status = "outcome-unknown" };
                run.State = RunState.NeedsAttention;
                run.Summary = "Model dispatch stopped. Inspect the recorded outcome before continuing; no automatic retry.";
                store.Save(run, "worker.model.stopped", new { dispatch = run.ModelDispatches[^1], run.Summary, authority = "broker-observed" });
                throw new InvalidOperationException(run.Summary);
            }
            finally { cancellations.TryRemove(id, out _); }
        }
        finally { Gate(id).Release(); }
    }
}
