using System.Text.Json;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed class ConnectedEmailDelegationDispatcher(IConnectedToolBroker broker) : IDelegationDispatcher
{
    public async Task<DelegationDispatchResult> Dispatch(DelegationJob job, DelegationOccurrence occurrence, CancellationToken cancellation)
    {
        if (job.Kind != "email" || job.Action.Kind != "email") throw new ArgumentException("This dispatcher accepts only scheduled email jobs.");
        ScheduledEmailPayload payload;
        try { payload = job.Action.Payload.Deserialize<ScheduledEmailPayload>(Wire.Json) ?? throw new JsonException(); }
        catch (JsonException) { throw new InvalidOperationException("The reviewed email payload is unreadable; no request was sent."); }
        if (payload.Recipient != job.Action.Target || payload.Arguments.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("The reviewed email target no longer matches its payload; no request was sent.");
        var current = broker.Snapshot().SingleOrDefault(tool => tool.ConnectorId == payload.Tool.ConnectorId &&
            tool.RemoteName == payload.Tool.RemoteName && tool.ModelName == payload.Tool.ModelName &&
            tool.ConnectionVersion == payload.Tool.ConnectionVersion);
        if (current == null || DelegationEmailConversation.ToolVersion(current) != DelegationEmailConversation.ToolVersion(payload.Tool))
            throw new InvalidOperationException("The email connector is unavailable or changed; no request was sent. Reconnect and review a new schedule.");
        CapabilityResult result;
        try { result = await broker.Call(payload.Tool, payload.Arguments, cancellation); }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            throw new DelegationOutcomeUnknownException("The email provider did not return a conclusive result after dispatch began.");
        }
        return result.IsError
            ? new("failed", "The email provider returned an error; no delivery is claimed.", false,
                payload.Tool.ConnectorId + ":" + payload.Tool.RemoteName, result.Value, "in-app-result")
            : new("accepted", "The email provider accepted the exact approved message. Recipient reading is not claimed.", true,
                payload.Tool.ConnectorId + ":" + payload.Tool.RemoteName, result.Value, "in-app-result");
    }
}
