using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

internal sealed class RecordedInference(Store store, IInferenceTransport inner, Action? beforeDispatch = null) : IInferenceTransport
{
    public async Task<InferenceReply> Send(ProviderSnapshot provider, JsonElement body, CancellationToken cancellation)
    {
        var number = store.List().Single().ModelCalls;
        await NativeRegistration.WriteNew(Path.Combine(store.Root, $"model-request-{number}.json"), body.GetRawText());
        beforeDispatch?.Invoke();
        // Observe the existing transport once. The ledger is not another model loop.
        var reply = await inner.Send(provider, body, cancellation);
        await NativeRegistration.WriteNew(Path.Combine(store.Root, $"model-response-{number}.json"), reply.Body.GetRawText());
        return reply;
    }
}
