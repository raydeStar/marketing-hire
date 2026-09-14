using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Runtime
{
    private static CapabilityDefinition ArtifactProposalTool
    {
        get
        {
            var schema = JsonNode.Parse(EvidenceProposalTool.InputSchema.GetRawText())!;
            schema["properties"]!.AsObject().Remove("content");
            schema["required"] = new JsonArray("path", "artifact", "citations");
            return new("thaddeus_propose_import",
                "Request review of an existing worker file. Provide its filename, destination and captured source citations, without copying its contents. Stop this turn; the host captures the file before source checks and exact approval. Only a later host continuation may request a bounded correction. No host file is written by this tool.",
                JsonSerializer.SerializeToElement(schema));
        }
    }

    private object RequestArtifactImport(Run run, string operationId, JsonElement args)
    {
        run.Profile!.Validate();
        if (run.Research?.Phase != "working" && run.Research?.Phase is not ("provisioning" or "resuming"))
            throw new InvalidOperationException("Artifact capture requires the managed research controller.");
        var citations = ParseCitations(args, artifactReference: true);
        var path = Text(args, "path", 120); var artifact = Text(args, "artifact", 100);
        DockerSandboxBackend.ValidateArtifactPath(artifact); store.SafePath(path);
        if (run.Goal.WriteScope != "plans/" || !path.StartsWith(run.Goal.WriteScope, StringComparison.Ordinal))
            throw new ArgumentException("Import destination is outside the granted write scope.");
        if (store.Setting("writes") == "off") throw new InvalidOperationException("Knowledge writes are currently Off.");
        if (run.ToolCalls >= run.Goal.Limits.ToolCalls) throw new InvalidOperationException("Reserve one tool call for the approved import.");
        var import = new ArtifactImport(Guid.NewGuid().ToString("N"), operationId, path, artifact, store.Version(path), citations, DateTimeOffset.UtcNow);
        run.ArtifactImports.Add(import);
        run.State = RunState.Paused; PauseExecutionClock(run);
        run.Summary = "Checking the written file before requesting your approval";
        return new { operationId, importId = import.Id, status = "awaiting-artifact-review", artifact,
            instruction = "Stop this turn. The host will capture the file and check its sources. No import approval exists yet and no host file was changed." };
    }

    internal async Task<Run> CaptureArtifactImport(string id, string importId, SandboxText? artifact, string? failureType = null)
    {
        await Gate(id).WaitAsync();
        try
        {
            var run = store.Get(id) ?? throw new ArgumentException("Task not found.");
            if (run.Profile?.ProposalEvidenceVersion != 2 || run.Research?.Phase != "quiescing" || run.State != RunState.Paused ||
                run.Approval != null || run.ArtifactImports.LastOrDefault() is not { Status: "requested" } import || import.Id != importId)
                throw new InvalidOperationException("Artifact capture no longer matches the pending request.");
            var bounded = artifact != null && !string.IsNullOrWhiteSpace(artifact.Content) && Encoding.UTF8.GetByteCount(artifact.Content) <= 100_000;
            var hash = bounded ? Wire.Hash(artifact!.Content) : null;
            var status = artifact == null ? "unavailable" : !bounded ? "invalid-content" :
                artifact.Path != import.Artifact || artifact.Sha256 != hash ? "identity-mismatch" : "captured";
            import = import with { Status = status, Sha256 = hash, CapturedAt = DateTimeOffset.UtcNow, FailureType = failureType };
            if (status != "captured")
            {
                run.State = RunState.NeedsAttention;
                run.Summary = status == "identity-mismatch" ? "The captured file identity or hash is inconsistent. Nothing was imported."
                    : status == "invalid-content" ? "The file must contain nonempty text no larger than 100 KB. Nothing was imported."
                    : "The worker file could not be read and verified. Nothing was imported.";
            }
            else if (store.Setting("writes") == "off" || store.Version(import.Path) != import.ResourceVersion)
            {
                import = import with { Status = "destination-changed" };
                run.State = RunState.NeedsAttention;
                run.Summary = "The destination or write setting changed before review. No approval was created.";
            }
            else
            {
                // The file is the authority for content. The butler does not ask the raven to transcribe it twice.
                var args = JsonSerializer.SerializeToElement(new { path = import.Path, artifact = import.Artifact, content = artifact!.Content, citations = import.Citations }, Wire.Json);
                var assessed = ReviewNativeProposal(run, import.Path, import.Artifact, artifact.Content, args);
                if (assessed is ProposalRepairFeedback feedback)
                    import = import with { Status = feedback.Status, Repair = feedback };
                else
                {
                    var approvalId = Guid.NewGuid().ToString("N"); var expires = DateTimeOffset.UtcNow.AddMinutes(15);
                    var action = new ToolRequest("knowledge.write", import.Path, artifact.Content);
                    run.Approval = new(approvalId, id, action, ApprovalDigest(id, approvalId, action, import.ResourceVersion, expires), import.ResourceVersion, expires);
                    run.NativeProposals[^1] = (NativeProposalReview)assessed with { ApprovalId = approvalId };
                    import = import with { Status = "ready-for-approval", ApprovalId = approvalId };
                    run.DraftText = artifact.Content; run.State = RunState.AwaitingApproval;
                    run.Summary = run.Profile.ValidateEvidence ? "Written file captured and source-checked · exact import requires your approval"
                        : "Written file captured · source checks omitted in the experiment control; exact import still requires approval";
                }
            }
            run.ArtifactImports[^1] = import;
            store.Save(run, "research.artifact.captured", new { import, authority = "host-readback", imported = false });
            return run;
        }
        finally { Gate(id).Release(); }
    }
}
