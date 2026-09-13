using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

sealed class ScriptedNativeModel(Store store, bool injectInvalidProposal = false) : IInferenceTransport
{
    public const string Summary = "# Workshop summary\nAudience: Developers\nDuration: 45 minutes\nSource: notes/source.md\n";
    public async Task<InferenceReply> Send(ProviderSnapshot provider, JsonElement body, CancellationToken cancellation)
    {
        var current = store.List().Single();
        await File.WriteAllTextAsync(Path.Combine(store.Root, $"synthetic-request-{current.ModelCalls}.json"), body.GetRawText(), cancellation);
        var stage = current.ModelCalls;
        var steps = current.Goal.Web == null
            ? new[] { "thaddeus_read_note", "thaddeus_ask_user", "write", "thaddeus_propose_import" }
            : new[] { "thaddeus_read_note", "thaddeus_fetch_public_page", "thaddeus_ask_user", "write", "thaddeus_propose_import" };
        var firstProposal = steps.Length;
        if (injectInvalidProposal) steps = [.. steps, "write", "thaddeus_propose_import"];
        if (stage < 1 || stage > steps.Length) throw new InvalidOperationException("Unexpected extra scripted dispatch.");
        var suffix = steps[stage - 1];
        var names = body.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("function").GetProperty("name").GetString()!).ToArray();
        var name = names.SingleOrDefault(n => n == suffix || n.EndsWith("_" + suffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Expected native tool is not advertised: " + suffix);
        var summary = Summary + (current.Goal.Web == null ? "" : "Public reference retrieved: https://docs.docker.com/ai/sandboxes/faq/ (claims not independently verified).\n");
        EvidenceCitation[] citations = [];
        if (current.Profile?.ProposalEvidenceVersion is 1 or 2)
        {
            var source = current.Evidence.Single(e => e.Path == "notes/source.md");
            var quote = injectInvalidProposal && stage <= firstProposal ? "A deliberately incorrect source quotation for the repair fixture." :
                source.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries).First(line => !line.StartsWith('#'));
            summary += "> " + quote + "\n";
            citations = [new(source.Path, source.Hash, quote)];
        }
        object arguments = suffix switch
        {
            "thaddeus_read_note" => new { operationId = "native-read", path = "notes/source.md" },
            "thaddeus_fetch_public_page" => new { operationId = "native-public", url = "https://docs.docker.com/ai/sandboxes/faq/" },
            "thaddeus_ask_user" => new { operationId = "native-question", question = "Which audience should the workshop address?", choices = new[] { "Developers", "Beginners" } },
            "write" => new { path = "/home/agent/thaddeus-artifacts/summary.md", content = summary },
            _ => new { operationId = "native-import", path = "plans/summary.md", artifact = "summary.md", content = summary }
        };
        if (suffix == "thaddeus_propose_import" && current.Profile?.ProposalEvidenceVersion == 1)
            arguments = new { operationId = stage > firstProposal ? "native-import-repair" : "native-import", path = "plans/summary.md", artifact = "summary.md", content = summary, citations };
        if (suffix == "thaddeus_propose_import" && current.Profile?.ProposalEvidenceVersion == 2)
            arguments = new { operationId = stage > firstProposal ? "native-import-repair" : "native-import", path = "plans/summary.md", artifact = "summary.md", citations };
        var reply = JsonSerializer.SerializeToElement(new { id = "synthetic-native-" + stage, model = provider.Model, created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            choices = new[] { new { index = 0, message = new { role = "assistant", content = (string?)null,
                tool_calls = new[] { new { id = "call-native-" + stage, type = "function", function = new { name, arguments = Wire.Pack(arguments) } } } }, finish_reason = "tool_calls" } },
            usage = new { prompt_tokens = 100, completion_tokens = 30 } });
        return new(reply, 100, 30); // Explicitly synthetic protocol accounting, never model efficacy evidence.
    }
}
