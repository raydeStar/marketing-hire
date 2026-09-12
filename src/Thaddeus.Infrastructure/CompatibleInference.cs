using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed class ModelAccessGate : IModelAccessGate
{
    public void Check(ProviderSnapshot provider)
    {
        var endpoint = CompatibleProvider.Endpoint(provider);
        if (provider.Kind != "compatible") throw new ArgumentException("Worker inference requires a compatible provider.");
        if (endpoint.IsLoopback && !(endpoint.Port == 5181 && endpoint.AbsolutePath == "/v1/" &&
            provider.Model == "gpt-5.6-luna" && provider.Reasoning == "high"))
            throw new InvalidOperationException("Local inference is waiting for a coordinated resource lease. No model was dispatched.");
    }
}

public sealed class CompatibleInference(HttpClient client, string? apiKey) : IInferenceTransport
{
    public async Task<InferenceReply> Send(ProviderSnapshot provider, JsonElement body, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(CompatibleProvider.Endpoint(provider), "chat/completions"))
        { Content = JsonContent.Create(body) };
        if (!string.IsNullOrEmpty(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        // No worker headers, cookies, endpoint choices or credentials cross this boundary.
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("The selected model provider did not accept the request.");
        if (response.Content.Headers.ContentType?.MediaType != "application/json")
            throw new HttpRequestException("Provider did not return the requested non-streaming JSON response.");
        using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var bytes = new MemoryStream(); var buffer = new byte[8192];
        while (await stream.ReadAsync(buffer, cancellation) is var count && count > 0)
        {
            if (bytes.Length + count > 1_000_000) throw new IOException("Provider response exceeds the transport limit.");
            bytes.Write(buffer, 0, count);
        }
        using var document = JsonDocument.Parse(bytes.ToArray());
        var result = document.RootElement;
        if (result.ValueKind != JsonValueKind.Object || result.TryGetProperty("error", out _) ||
            !result.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1 ||
            !choices[0].TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("role", out var role) || role.GetString() != "assistant" ||
            !choices[0].TryGetProperty("finish_reason", out var finish) || finish.ValueKind != JsonValueKind.String)
            throw new JsonException("Provider response does not match the completion contract.");
        var usage = result.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object ? u : default;
        return new(result.Clone(), Usage(usage, "prompt_tokens"), Usage(usage, "completion_tokens"));
    }
    private static int? Usage(JsonElement usage, string key)
    {
        if (usage.ValueKind != JsonValueKind.Object || !usage.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (!value.TryGetInt32(out var count) || count is < 0 or > 100_000_000) throw new JsonException("Invalid provider usage.");
        return count;
    }

    public static JsonElement Normalize(Run run, JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object || input.GetRawText().Length > 150_000) throw new ArgumentException("Invalid model request.");
        string[] allowed = ["model", "messages", "tools", "tool_choice", "parallel_tool_calls", "stream", "stream_options",
            "max_tokens", "max_completion_tokens", "reasoning_effort", "temperature", "top_p", "stop", "n", "store"];
        var properties = input.EnumerateObject().ToArray();
        if (properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length || properties.Any(p => !allowed.Contains(p.Name, StringComparer.Ordinal)))
            throw new ArgumentException("Model request includes unsupported fields.");
        if (input.TryGetProperty("stream", out var stream) && stream.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ArgumentException("Invalid streaming option.");
        if (!input.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.String || model.GetString() != run.Goal.Provider.Model)
            throw new ArgumentException("Model must match the task's frozen provider.");
        if (!input.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() is 0 or > 256)
            throw new ArgumentException("Provide a bounded message history.");
        foreach (var message in messages.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("role", out var role) || role.ValueKind != JsonValueKind.String ||
                role.GetString() is not ("system" or "developer" or "user" or "assistant" or "tool")) throw new ArgumentException("Unsupported message role.");
            string[] messageFields = ["role", "content", "name", "tool_call_id", "tool_calls", "refusal", "reasoning_content"];
            var fields = message.EnumerateObject().ToArray();
            if (fields.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length || fields.Any(p => !messageFields.Contains(p.Name, StringComparer.Ordinal)))
                throw new ArgumentException("Unsupported message fields.");
            if (message.TryGetProperty("content", out var content) && content.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                if (content.ValueKind != JsonValueKind.Array || content.EnumerateArray().Any(part => part.ValueKind != JsonValueKind.Object ||
                    !part.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "text" ||
                    !part.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String || part.EnumerateObject().Count() != 2))
                    throw new ArgumentException("This worker transport accepts text only.");
            }
        }
        if (input.TryGetProperty("tools", out var tools))
        {
            if (tools.ValueKind != JsonValueKind.Array || tools.GetArrayLength() > 64) throw new ArgumentException("Too many model tools.");
            foreach (var tool in tools.EnumerateArray())
                if (tool.ValueKind != JsonValueKind.Object || !tool.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "function")
                    throw new ArgumentException("Only worker-executed function tools are supported.");
        }
        if (input.TryGetProperty("n", out var n) && (!n.TryGetInt32(out var count) || count != 1)) throw new ArgumentException("One completion per dispatch is permitted.");
        if (input.TryGetProperty("reasoning_effort", out var effort) && (effort.ValueKind != JsonValueKind.String || effort.GetString() != run.Goal.Provider.Reasoning))
            throw new ArgumentException("Reasoning effort must match the task's frozen provider.");
        var outputLimit = run.Goal.Limits.MaxOutputTokens;
        foreach (var key in new[] { "max_tokens", "max_completion_tokens" })
            if (input.TryGetProperty(key, out var limit))
            {
                if (!limit.TryGetInt32(out var number) || number < 1) throw new ArgumentException("Invalid model output limit.");
                outputLimit = Math.Min(outputLimit, number);
            }
        var body = JsonNode.Parse(input.GetRawText())!.AsObject();
        foreach (var message in body["messages"]!.AsArray())
            if (message!["content"] is JsonArray parts) message["content"] = string.Join("\n", parts.Select(part => part!["text"]!.GetValue<string>()));
        body.Remove("max_tokens"); body.Remove("stream_options");
        body["model"] = run.Goal.Provider.Model; body["reasoning_effort"] = run.Goal.Provider.Reasoning;
        body["max_completion_tokens"] = outputLimit; body["stream"] = false; body["store"] = false; body["n"] = 1;
        return JsonSerializer.SerializeToElement(body);
    }
}
