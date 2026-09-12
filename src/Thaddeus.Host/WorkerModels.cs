using System.Text.Json;
using System.Text.Json.Nodes;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public static class WorkerModels
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/worker/{runId}/v1/chat/completions", async (string runId, JsonElement body, HttpContext context,
            Runtime runtime, IInferenceTransport transport, IModelAccessGate access) =>
        {
            try
            {
                var reply = await runtime.Infer(runId, body, transport, access, context.RequestAborted);
                if (body.TryGetProperty("stream", out var stream) && stream.ValueKind == JsonValueKind.True)
                {
                    context.Response.ContentType = "text/event-stream";
                    foreach (var frame in Frames(reply.Body))
                        await context.Response.WriteAsync("data: " + frame + "\n\n", context.RequestAborted);
                    await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
                }
                else await context.Response.WriteAsJsonAsync(reply.Body, context.RequestAborted);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
            {
                if (context.Response.HasStarted) return;
                context.Response.StatusCode = ex is InvalidOperationException ? 409 : 400;
                await context.Response.WriteAsJsonAsync(new { error = new { message = ex is JsonException ? "Invalid model protocol JSON." : ex.Message,
                    type = "thaddeus_admission_error", code = "request_stopped" } }, context.RequestAborted);
            }
        });
    }

    public static IEnumerable<string> Frames(JsonElement body)
    {
        // Buffering is deliberate: settle the full response before the worker receives executable proposals.
        var choice = body.GetProperty("choices")[0];
        var delta = JsonNode.Parse(choice.GetProperty("message").GetRawText())!.AsObject();
        if (delta["tool_calls"] is JsonArray calls)
            for (var i = 0; i < calls.Count; i++) calls[i]!["index"] = i;
        var id = body.TryGetProperty("id", out var identifier) ? identifier.GetString() : "thaddeus-completion";
        var model = body.TryGetProperty("model", out var m) ? m.GetString() : null;
        var created = body.TryGetProperty("created", out var at) && at.TryGetInt64(out var timestamp) ? timestamp : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        yield return Wire.Pack(new { id, model, created, @object = "chat.completion.chunk", choices = new[] { new { index = 0, delta, finish_reason = (string?)null } } });
        yield return Wire.Pack(new { id, model, created, @object = "chat.completion.chunk", choices = new[] { new { index = 0, delta = new { }, finish_reason = choice.GetProperty("finish_reason").GetString() } } });
        if (body.TryGetProperty("usage", out var usage))
            yield return Wire.Pack(new { id, model, created, @object = "chat.completion.chunk", choices = Array.Empty<object>(), usage });
    }
}
