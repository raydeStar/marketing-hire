using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;
using Thaddeus.Lab;

internal sealed class NativeLabModel(Store store, NativeLabCase fixture) : IInferenceTransport
{
    public async Task<InferenceReply> Send(ProviderSnapshot provider, JsonElement body, CancellationToken cancellation)
    {
        var run = store.List().Single(); var stage = run.ModelCalls;
        await NativeRegistration.WriteNew(Path.Combine(store.Root, $"model-request-{stage}.json"), body.GetRawText());
        // The synthetic transport follows the actual tool feedback, without inspecting the selected policy arm.
        var feedback = body.GetProperty("messages").EnumerateArray().Where(message => message.GetProperty("role").GetString() == "tool" &&
            message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String).Select(message => message.GetProperty("content").GetString()!).ToArray();
        var repair = feedback.Any(text => text.Contains("\"status\":\"repair-requested\"", StringComparison.Ordinal));
        var steps = new[] { "thaddeus_read_note", "thaddeus_ask_user", "write", "thaddeus_propose_import", "write", "thaddeus_propose_import" };
        if (stage < 1 || stage > steps.Length || stage > 4 && !repair) throw new InvalidOperationException("Unexpected native fixture dispatch without repair feedback.");
        var suffix = steps[stage - 1];
        var names = body.GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("function").GetProperty("name").GetString()!).ToArray();
        var name = names.Single(candidate => candidate == suffix || candidate.EndsWith("_" + suffix, StringComparison.Ordinal));
        var quote = fixture.Fault == "quotation-and-duration" && !repair ? "A deliberately invented workshop quotation." : fixture.Note;
        var duration = repair ? fixture.DurationMinutes : fixture.DurationMinutes + 15;
        var content = "# Workshop brief\n\n" + Wire.Pack(new { durationMinutes = duration, audience = run.Question?.Answer ?? "undecided", sourcePath = "notes/source.md", sourceQuote = quote });
        var source = run.Evidence.Single(evidence => evidence.Path == "notes/source.md");
        object arguments = suffix switch
        {
            "thaddeus_read_note" => new { operationId = "lab-read", path = "notes/source.md" },
            "thaddeus_ask_user" => new { operationId = "lab-question", question = "Which audience should the workshop address?", choices = new[] { "Developers", "Beginners" } },
            "write" => new { path = "/home/agent/thaddeus-artifacts/report.md", content },
            _ => new { operationId = repair ? "lab-import-repair" : "lab-import", path = "plans/report.md", artifact = "report.md", content,
                citations = new[] { new EvidenceCitation(source.Path, source.Hash, quote) } }
        };
        var response = JsonSerializer.SerializeToElement(new { id = "synthetic-lab-" + stage, model = provider.Model, created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            choices = new[] { new { index = 0, message = new { role = "assistant", content = (string?)null,
                tool_calls = new[] { new { id = "call-lab-" + stage, type = "function", function = new { name, arguments = Wire.Pack(arguments) } } } }, finish_reason = "tool_calls" } },
            usage = new { prompt_tokens = 100, completion_tokens = 30 } });
        await NativeRegistration.WriteNew(Path.Combine(store.Root, $"model-response-{stage}.json"), response.GetRawText());
        return new(response, 100, 30);
    }
}
