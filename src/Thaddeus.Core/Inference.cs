using System.Text.Json;

namespace Thaddeus.Core;

public record InferenceReply(JsonElement Body, int? InputTokens, int? OutputTokens);
public record ModelDispatch(string Id, string RequestHash, DateTimeOffset Started, string Status,
    int ReservedTokens, int? InputTokens = null, int? OutputTokens = null, string? ResponseHash = null,
    string? ContextHash = null, bool? ContextObserved = null);

// This is one inference request. OpenClaw alone decides whether another turn is needed.
public interface IInferenceTransport
{
    Task Prepare(ProviderSnapshot provider, CancellationToken cancellation) => Task.CompletedTask;
    Task<InferenceReply> Send(ProviderSnapshot provider, JsonElement request, CancellationToken cancellation);
}
public interface IProviderCredentials
{
    Task<string?> Read(ProviderSnapshot provider, CancellationToken cancellation);
}
public interface IModelAccessGate
{
    void Check(ProviderSnapshot provider);
}
