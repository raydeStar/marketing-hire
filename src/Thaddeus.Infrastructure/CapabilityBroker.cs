using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Runtime
{
    public IReadOnlyList<CapabilityDefinition> Tools { get; } =
    [
        new("thaddeus_read_note", "Read one explicitly selected source note. Content and source hash are recorded.", Schema("""
            {"type":"object","properties":{"path":{"type":"string","maxLength":120}},"required":["path"],"additionalProperties":false}
            """)),
        new("thaddeus_ask_user", "Pause this task with a durable question. Stop working until the user answers.", Schema("""
            {"type":"object","properties":{"question":{"type":"string","minLength":1,"maxLength":2000},"choices":{"type":"array","items":{"type":"string","minLength":1,"maxLength":200},"maxItems":5}},"required":["question","choices"],"additionalProperties":false}
            """)),
        new("thaddeus_propose_import", "Offer a text artifact for exact user approval. This tool does not write the original host file. Stop after proposing.", Schema("""
            {"type":"object","properties":{"path":{"type":"string","maxLength":120,"description":"Destination Markdown path under the task's granted plans/ scope."},"content":{"type":"string","minLength":1,"maxLength":100000,"description":"Exact UTF-8 text already written into the worker artifact."},"artifact":{"type":"string","maxLength":100,"pattern":"^[a-z0-9][a-z0-9-]{0,90}\\.(md|txt|json)$","description":"Existing filename in the worker artifact directory, including its .md, .txt or .json extension. Use lowercase letters, digits and hyphens, for example draft.md. This is a filename, not a title or full path."}},"required":["path","content","artifact"],"additionalProperties":false}
            """))
    ];
    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public async Task<CapabilityResult> Call(string runId, CapabilityCall call, CancellationToken cancellation)
    {
        if (!Regex.IsMatch(call.OperationId, @"\A[a-zA-Z0-9_-]{1,100}\z")) throw new ArgumentException("Invalid capability operation ID.");
        await Gate(runId).WaitAsync(cancellation);
        try
        {
            var run = store.Get(runId) ?? throw new ArgumentException("Run not found.");
            if (run.Execution?.Backend != "openclaw") throw new InvalidOperationException("This task has no OpenClaw execution grant.");
            var requestHash = Wire.Hash(Wire.Pack(new { call.Name, call.Arguments }));
            var previous = run.Capabilities.SingleOrDefault(r => r.OperationId == call.OperationId);
            if (previous != null)
            {
                if (previous.RequestHash != requestHash) throw new InvalidOperationException("Operation ID was already used for different arguments.");
                return new(previous.Result, previous.IsError);
            }
            if (run.State != RunState.Running) throw new InvalidOperationException("Task is not accepting worker operations.");
            if (run.ToolCalls >= run.Goal.Limits.ToolCalls) throw new InvalidOperationException("Task tool budget is exhausted.");
            if (call.Arguments.ValueKind != JsonValueKind.Object || call.Arguments.GetRawText().Length > 150_000) throw new ArgumentException("Invalid capability arguments.");
            run.ToolCalls++;
            // Read/proposal/question results and their receipt commit together. No external write occurs here.
            object result; var isError = false;
            try
            {
                result = call.Name switch
                {
                    "thaddeus_read_note" => ReadSelectedNote(run, call.Arguments),
                    "thaddeus_ask_user" => AskUser(run, call.OperationId, call.Arguments),
                    "thaddeus_propose_import" => ProposeImport(run, call.OperationId, call.Arguments),
                    _ => throw new ArgumentException("Capability is not granted by this host.")
                };
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            { result = new { error = ex.Message }; isError = true; }
            var data = JsonSerializer.SerializeToElement(result, Wire.Json);
            var receipt = new CapabilityReceipt(call.OperationId, requestHash, call.Name, "broker-verified", DateTimeOffset.UtcNow, data, isError);
            run.Capabilities.Add(receipt);
            store.Save(run, "capability.result", receipt);
            return new(data, isError);
        }
        finally { Gate(runId).Release(); }
    }
    private EvidenceRef ReadSelectedNote(Run run, JsonElement args)
    {
        Fields(args, "path"); var path = Text(args, "path", 120);
        if (!run.Goal.ReadScope.Contains(path, StringComparer.Ordinal)) throw new ArgumentException("Note is outside this task's selected read scope.");
        var previous = run.Evidence.SingleOrDefault(e => e.Path == path);
        if (previous != null) return previous;
        var evidence = store.Read(path).Evidence ?? throw new InvalidOperationException("Source read did not return evidence.");
        run.Evidence.Add(evidence); return evidence;
    }
    private static object AskUser(Run run, string operationId, JsonElement args)
    {
        Fields(args, "question", "choices"); var question = Text(args, "question", 2000);
        if (!args.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() > 5)
            throw new ArgumentException("Provide at most five concise answer choices.");
        var labels = choices.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString()! : "").ToArray();
        if (labels.Any(label => string.IsNullOrWhiteSpace(label) || label.Length > 200)) throw new ArgumentException("Invalid question choices.");
        run.Question = new(operationId, question, labels, DateTimeOffset.UtcNow);
        run.State = RunState.AwaitingInput; run.Summary = "A question is waiting for your answer";
        return new { questionId = operationId, status = "awaiting-user", instruction = "Stop this turn. The host will deliver the answer on continuation." };
    }
    private object ProposeImport(Run run, string operationId, JsonElement args)
    {
        Fields(args, "path", "content", "artifact");
        var path = Text(args, "path", 120); var content = Text(args, "content", 100_000); var artifact = Text(args, "artifact", 100);
        DockerSandboxBackend.ValidateArtifactPath(artifact); store.SafePath(path);
        if (!path.StartsWith(run.Goal.WriteScope, StringComparison.Ordinal) || run.Goal.WriteScope != "plans/") throw new ArgumentException("Import destination is outside the granted write scope.");
        if (Encoding.UTF8.GetByteCount(content) > 100_000) throw new ArgumentException("Import exceeds 100 KB.");
        if (store.Setting("writes") == "off") throw new InvalidOperationException("Knowledge writes are currently Off.");
        if (run.ToolCalls >= run.Goal.Limits.ToolCalls) throw new InvalidOperationException("Reserve one tool call for the approved import.");
        var approvalId = Guid.NewGuid().ToString("N"); var version = store.Version(path); var expires = DateTimeOffset.UtcNow.AddMinutes(15);
        var action = new ToolRequest("knowledge.write", path, content);
        run.Approval = new(approvalId, run.Id, action, ApprovalDigest(run.Id, approvalId, action, version, expires), version, expires);
        run.DraftText = content; run.State = RunState.AwaitingApproval; run.Summary = "Artifact ready for review · exact import requires your approval";
        return new { operationId, approvalId, status = "awaiting-approval", artifact, contentHash = Wire.Hash(content), provenance = "worker-proposed", instruction = "Stop this turn. No original host file has been changed." };
    }
    public async Task<Run> AnswerQuestion(string runId, string questionId, string answer, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(answer) || answer.Length > 4000) throw new ArgumentException("Answer must contain 1–4,000 characters.");
        await Gate(runId).WaitAsync(cancellation);
        try
        {
            var run = store.Get(runId) ?? throw new ArgumentException("Run not found.");
            if (run.Execution == null || run.State != RunState.AwaitingInput || run.Question is not { Answer: null } question || question.Id != questionId)
                throw new InvalidOperationException("This question is stale or already answered.");
            run.Question = question with { Answer = answer, Answered = DateTimeOffset.UtcNow };
            run.State = RunState.Paused; run.Summary = "Answer saved · ready to continue with the execution backend";
            store.Save(run, "question.answered", new { authority = "user", questionId, answer });
            return run;
        }
        finally { Gate(runId).Release(); }
    }
    private static string Text(JsonElement args, string name, int limit)
    {
        if (!args.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > limit)
            throw new ArgumentException($"Invalid {name} argument.");
        return value.GetString()!;
    }
    private static void Fields(JsonElement args, params string[] names)
    {
        var properties = args.EnumerateObject().ToArray();
        if (properties.Length != names.Length || properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length || properties.Any(p => !names.Contains(p.Name, StringComparer.Ordinal)))
            throw new ArgumentException("Capability argument fields do not match its contract.");
    }
}
