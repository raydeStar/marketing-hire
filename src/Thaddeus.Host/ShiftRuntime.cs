using System.Text.Json;
using System.Text.Json.Nodes;

namespace Thaddeus.Host;

public record ShiftTurnRequest(string TurnId, string Stage, string Prompt, JsonElement Data, string ShiftId = "", string Owner = "owner", int TurnBudget = 1, DateTimeOffset EndsAt = default, int? TokenBudget = null);
public record ShiftTurnResult(string Reply, int Tokens);
/// <summary>Nothing reached the model, so the turn is not counted against the shift's budget.</summary>
public sealed class ShiftTurnNotSentException(string message) : InvalidOperationException(message);
/// <summary>The turn reached the provider and the Gateway recorded that it failed: the shift carries on,
/// and the turn's reservation stays counted because its real usage is unknown.</summary>
public sealed class ShiftTurnFailedException(string message, int tokens) : InvalidOperationException(message) { public int Tokens { get; } = tokens; }

/// <summary>Where a shift's model turns run. The host owns the loop, validation and every effect;
/// the runtime only turns one bounded packet into one JSON reply. Local OpenClaw now, Plow later.</summary>
public interface IShiftRuntime
{
    string Name { get; }
    bool Live { get; }
    Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation);
}

/// <summary>A deterministic stand-in model. It answers each stage from the packet's data, so a whole
/// shift can run in tests and demos without spending a model budget. Its output is labeled as scripted.</summary>
public sealed class ScriptedShiftRuntime : IShiftRuntime
{
    public string Name => "scripted";
    public bool Live => false;
    static readonly string[] DraftWords = ["post", "email", "announce", "announcement", "reply", "tweet", "linkedin", "newsletter", "caption", "draft"];

    public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation)
    {
        var data = request.Data;
        JsonNode reply = request.Stage switch
        {
            "prioritize" => Prioritize(data),
            "create" => Create(data),
            "institutionalize" => Learn(data),
            "review" => Review(data),
            "revise" => new JsonObject { ["edits"] = new JsonArray() }, // The stand-in is no wordsmith; it keeps the draft honestly.
            _ => throw new InvalidOperationException("Unknown shift stage.")
        };
        return Task.FromResult(new ShiftTurnResult(reply.ToJsonString(), 0));
    }

    static string Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    static JsonNode Prioritize(JsonElement data)
    {
        var priorities = new JsonArray();
        foreach (var signal in data.GetProperty("signals").EnumerateArray().Where(item => Text(item, "kind") is "anomaly" or "mention_spike" or "sentiment_drop").Take(2))
            priorities.Add(new JsonObject { ["title"] = Text(signal, "kind") == "anomaly" ? "Explain the move in " + Text(signal, "metric_name") : "What people are saying about " + Text(signal, "metric_name"), ["reason"] = Text(signal, "detail"),
                ["deliverable"] = "document", ["taskId"] = null, ["signalRef"] = Text(signal, "ref") });
        foreach (var task in data.GetProperty("queue").EnumerateArray().Where(item => Text(item, "status") == "ready" && Text(item, "action_state") == "agent_ready"))
        {
            if (priorities.Count >= 3) break;
            var title = Text(task, "title");
            var draft = DraftWords.Any(word => title.Contains(word, StringComparison.OrdinalIgnoreCase));
            priorities.Add(new JsonObject { ["title"] = title, ["reason"] = "Assigned and ready: " + Text(task, "next_action"),
                ["deliverable"] = draft ? "draft" : "document", ["taskId"] = Text(task, "id"), ["signalRef"] = null });
        }
        return new JsonObject { ["priorities"] = priorities, ["newTasks"] = new JsonArray(),
            ["note"] = priorities.Count == 0 ? "Nothing needs work this cycle." : $"Chose {priorities.Count} item(s) by severity, then the assigned queue." };
    }

    static JsonNode Create(JsonElement data)
    {
        var priority = data.GetProperty("priority");
        var brief = data.GetProperty("brief");
        var title = Text(priority, "title");
        var product = Text(brief, "product_summary") is { Length: > 0 } summary ? summary : "the product";
        var audience = Text(brief, "audience") is { Length: > 0 } who ? who : "the target audience";
        if (Text(priority, "deliverable") == "draft")
        {
            var link = "https://example.com/?utm_source=linkedin&utm_medium=social&utm_campaign=shift";
            return new JsonObject { ["deliverable"] = "draft", ["title"] = title, ["channel"] = "LinkedIn", ["destination"] = "https://www.linkedin.com/feed/",
                ["body"] = $"{audience} told us the same thing again this week: the hard part isn't ideas, it's follow-through.\n\n{product}\n\nIf that sounds familiar, learn more: {link}",
                ["rationale"] = "Leads with the audience's own problem, one clear claim, one call to action. Scripted stand-in text; review before use.",
                ["folder"] = null };
        }
        var signal = data.TryGetProperty("signal", out var found) && found.ValueKind == JsonValueKind.Object ? found : default;
        if (signal.ValueKind == JsonValueKind.Object && Text(signal, "kind") is "mention_spike" or "sentiment_drop")
        {
            var heard = string.Join("\n", data.GetProperty("sources").EnumerateArray().Select(source => $"- {Text(source, "title")} ({Text(source, "via")}) [{source.GetProperty("number").GetInt32()}]"));
            return new JsonObject { ["deliverable"] = "document", ["title"] = title, ["kind"] = "hypothesis", ["folder"] = "Research/Listening",
                ["body"] = $"# {title}\n\n## What we heard\n\n{Text(signal, "detail")}\n\n{heard}\n\n## Recommended next step\n\nRead the mentions above before responding; decide whether this needs a reply, a post or nothing.\n\n_Prepared by the scripted stand-in model during a shift. Verify before relying on it._" };
        }
        var body = signal.ValueKind == JsonValueKind.Object
            ? $"# {title}\n\n## What we know\n\n{Text(signal, "detail")}\n\n## What it might mean\n\n- A real change in demand or behavior, or\n- A tracking or data change (check first), or\n- Normal variation. The change clears the 25% / 2.5σ bar, so this is unlikely.\n\n## Recommended next step\n\nConfirm the data source didn't change, then compare by channel and segment before acting.\n\n## Open questions\n\n- Did anything launch, pause or break on this date?\n\n_Prepared by the scripted stand-in model during a shift. Verify before relying on it._"
            : $"# {title}\n\n## Goal\n\n{Text(data.GetProperty("task"), "next_action")}\n\n## Plan\n\n1. Gather what we already know in the Library.\n2. Draft the deliverable against the brief ({audience}).\n3. Bring anything public-facing to the owner for approval.\n\n## Assumptions\n\n- The brief is current.\n\n_Prepared by the scripted stand-in model during a shift. Verify before relying on it._";
        return new JsonObject { ["deliverable"] = "document", ["title"] = title, ["body"] = body, ["kind"] = signal.ValueKind == JsonValueKind.Object ? "hypothesis" : "policy",
            ["folder"] = signal.ValueKind == JsonValueKind.Object ? "Research/Analyses" : "Campaigns/Plans" };
    }

    /// <summary>The stand-in reviewer scores the rubric and keeps the text: it can't judge words, so it never rewrites them.</summary>
    static JsonNode Review(JsonElement data)
    {
        var body = Text(data.GetProperty("deliverable"), "body");
        var scores = new JsonObject();
        foreach (var name in new[] { "strategy", "customer", "distinctive", "channel", "brand", "action", "claims", "shareable" }) scores[name] = 4;
        var issues = new JsonArray();
        if (!body.Contains("http", StringComparison.Ordinal) && Text(data.GetProperty("deliverable"), "type") == "draft") { scores["action"] = 3; issues.Add("No link for the next step"); }
        return new JsonObject { ["scores"] = scores, ["issues"] = issues, ["revised"] = null };
    }

    static JsonNode Learn(JsonElement data)
    {
        var created = data.GetProperty("created").GetArrayLength();
        var decisions = data.GetProperty("decisions").GetArrayLength();
        return new JsonObject
        {
            ["learnings"] = new JsonArray(created > 0 ? $"Produced {created} deliverable(s); the owner's review decides which were useful." : "No deliverables this shift; nothing new was actionable.",
                decisions > 0 ? $"{decisions} item(s) wait on the owner; decisions are the bottleneck, not production." : "No owner decisions were needed."),
            ["nextShiftFocus"] = decisions > 0 ? "Clear the owner's pending decisions first, then continue the queue." : "Keep watching the scorecard; work the assigned queue.",
            ["notebook"] = new JsonObject
            {
                ["known"] = new JsonArray(), ["decided"] = new JsonArray(),
                ["openQuestions"] = decisions > 0 ? new JsonArray($"{decisions} item(s) from the last shift wait on the owner's decision.") : new JsonArray(),
                ["worked"] = new JsonArray(), ["didNotWork"] = new JsonArray(), ["resolved"] = new JsonArray()
            }
        };
    }
}

/// <summary>The local OpenClaw employee, behind the same interface. Every turn takes a metered claim in the employee's
/// receipt ledger, runs the tool-less worker once and settles with the provider-reported usage.</summary>
public sealed class OpenClawShiftRuntime(MarketingBackend marketing) : IShiftRuntime
{
    public string Name => "openclaw";
    public bool Live => true;
    public Task<ShiftTurnResult> Turn(ShiftTurnRequest request, CancellationToken cancellation) => marketing.LiveShiftTurn(request, cancellation);
}
