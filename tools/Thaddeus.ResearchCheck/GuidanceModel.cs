using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

// Opt-in protocol barrier. It lives only in this test executable, never in the host or guest package.
internal sealed class GuidanceModel(Store store) : IInferenceTransport
{
    public const string Message = "Keep the workshop summary concise and use the selected source quotations.";
    private readonly ScriptedNativeModel inner = new(store, injectInvalidProposal: true);
    public async Task<InferenceReply> Send(ProviderSnapshot provider, JsonElement body, CancellationToken cancellation)
    {
        var run = store.List().Single();
        if (run.ModelCalls == 1)
        {
            await File.WriteAllTextAsync(Path.Combine(store.Root, "guidance-model-waiting.json"), Wire.Pack(new
            { run.Id, run.ReservedTokens, run.ExecutionDeadlineStart, purpose = "Browser guidance must be acknowledged while this synthetic model call is still pending" }), cancellation);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            while (!File.Exists(Path.Combine(store.Root, "release-guidance-model.json"))) await Task.Delay(50, deadline.Token);
        }
        return await inner.Send(provider, body, cancellation);
    }
}
