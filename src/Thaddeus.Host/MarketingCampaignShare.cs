using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Thaddeus.Host;

public sealed partial class MarketingBackend
{
    private readonly SemaphoreSlim fixtureRevisionGate = new(1, 1);
    private sealed record CampaignInputRow(string RequestId, string ProjectId, string ActorId,
        string ActorName, string Kind, string ArtifactId, string ArtifactDigest, int ProjectVersion,
        string Content, string? LedgerInputId, string? OwnerReviewId, string Status,
        string? Error, string CreatedAt, string? NativeSuggestionId, string? NativeProfileId);

    internal bool HasCampaignAccess(string projectId, DeviceSession actor, Security security)
    {
        if (actor.Owner) return true;
        if (security.ActiveDevice(actor.Id) is not { Owner: false }) return false;
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT 1 FROM campaign_memberships WHERE project_id=$project " +
            "AND device_id=$device AND revoked_at IS NULL";
        command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.AddWithValue("$device", actor.PrincipalId);
        return command.ExecuteScalar() != null;
    }

    public bool HasEverCampaignMembership(string deviceId)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT 1 FROM campaign_memberships WHERE device_id=$device LIMIT 1";
        command.Parameters.AddWithValue("$device", deviceId);
        return command.ExecuteScalar() != null;
    }

    private static CampaignInputRow ReadCampaignInput(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetInt32(7),
        reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9),
        reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetString(11),
        reader.IsDBNull(12) ? null : reader.GetString(12), reader.GetString(13),
        reader.IsDBNull(14) ? null : reader.GetString(14),
        reader.IsDBNull(15) ? null : reader.GetString(15));

    private static CampaignInputRow? FindCampaignInput(SqliteConnection db, string requestId)
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT request_id,project_id,actor_id,actor_name,kind,artifact_id," +
            "artifact_digest,project_version,content,ledger_input_id,owner_review_id,status,error,created_at," +
            "native_suggestion_id,native_profile_id " +
            "FROM campaign_shared_inputs WHERE request_id=$request";
        command.Parameters.AddWithValue("$request", requestId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadCampaignInput(reader) : null;
    }

    private async Task<(JsonElement Artifact, JsonElement ReviewSnapshot)?> SelectedSharedAsset(
        JsonElement snapshot, string artifactId, string digest, CancellationToken cancellation)
    {
        if (!snapshot.TryGetProperty("campaign", out var campaign) || campaign.ValueKind != JsonValueKind.Object ||
            !campaign.TryGetProperty("asset_artifact_id", out var currentAsset) ||
            currentAsset.ValueKind != JsonValueKind.String || currentAsset.GetString() != artifactId ||
            !campaign.TryGetProperty("asset_artifact_digest", out var currentDigest) ||
            currentDigest.GetString() != digest) return null;
        var local = snapshot.GetProperty("artifacts").EnumerateArray().FirstOrDefault(item =>
            item.GetProperty("id").GetString() == artifactId &&
            item.GetProperty("digest").GetString() == digest &&
            item.GetProperty("kind").GetString() is "post_angles" or "revision_angles");
        if (local.ValueKind == JsonValueKind.Object) return (local, snapshot);
        var verified = WithCampaignAuthority(snapshot);
        var adoption = verified.GetProperty("campaign_actions").EnumerateArray().LastOrDefault(item =>
            item.GetProperty("action").GetString() == "adopt_revision" &&
            item.TryGetProperty("owner_verified", out var approved) && approved.GetBoolean());
        if (adoption.ValueKind != JsonValueKind.Object) return null;
        using var payload = JsonDocument.Parse(adoption.GetProperty("payload_json").GetString()!);
        if (payload.RootElement.GetProperty("revision_artifact_id").GetString() != artifactId ||
            payload.RootElement.GetProperty("revision_artifact_digest").GetString() != digest)
            return null;
        var revisionId = payload.RootElement.GetProperty("revision_runway_id").GetString();
        var linked = await Runway("inspect", new { id = revisionId }, cancellation);
        if (linked.Error != null || linked.Value is not { ValueKind: JsonValueKind.Object } revision ||
            revision.GetProperty("project").GetProperty("source_runway_id").GetString() !=
            snapshot.GetProperty("project").GetProperty("id").GetString()) return null;
        var asset = revision.GetProperty("artifacts").EnumerateArray().FirstOrDefault(item =>
            item.GetProperty("id").GetString() == artifactId &&
            item.GetProperty("digest").GetString() == digest &&
            item.GetProperty("kind").GetString() == "revision_angles");
        return asset.ValueKind == JsonValueKind.Object ? (asset, revision) : null;
    }

    public async Task<IResult> SharedCampaignList(DeviceSession actor, Security security,
        CancellationToken cancellation)
    {
        if (actor.Owner)
        {
            var archive = await Runway("list", null, cancellation);
            return archive.Error == null ? Results.Ok(archive.Value) :
                Results.Json(new { error = archive.Error }, statusCode: 503);
        }
        if (security.ActiveDevice(actor.Id) is not { Owner: false }) return Results.StatusCode(403);
        string[] ids;
        using (var db = Open())
        using (var command = db.CreateCommand())
        {
            command.CommandText = "SELECT project_id FROM campaign_memberships WHERE device_id=$device " +
                "AND revoked_at IS NULL ORDER BY granted_at DESC LIMIT 30";
            command.Parameters.AddWithValue("$device", actor.PrincipalId);
            using var reader = command.ExecuteReader();
            var values = new List<string>();
            while (reader.Read()) values.Add(reader.GetString(0));
            ids = values.ToArray();
        }
        var projects = new List<object>();
        foreach (var id in ids)
        {
            if (!HasCampaignAccess(id, actor, security)) continue;
            var inspected = await Runway("inspect", new { id }, cancellation);
            if (inspected.Error != null || inspected.Value is not { ValueKind: JsonValueKind.Object } snapshot) continue;
            var project = snapshot.GetProperty("project");
            if (!HasCampaignAccess(id, actor, security)) continue;
            projects.Add(new { id, goal = project.GetProperty("goal").GetString(),
                status = project.GetProperty("status").GetString(),
                updated_at = project.GetProperty("updated_at").GetDouble() });
        }
        return Results.Ok(new { projects });
    }

    public IResult CampaignAccess(string projectId, Security security)
    {
        if (!TaskIdPattern.IsMatch(projectId)) return Results.BadRequest(new { error = "Invalid campaign ID." });
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT device_id,granted_at,revoked_at FROM campaign_memberships " +
            "WHERE project_id=$project ORDER BY granted_at";
        command.Parameters.AddWithValue("$project", projectId);
        using var reader = command.ExecuteReader();
        var members = new List<object>();
        while (reader.Read())
        {
            var deviceId = reader.GetString(0);
            var device = security.ActiveDevice(deviceId);
            members.Add(new { deviceId, name = device?.Name ?? "Former or expired device",
                active = device is { Owner: false } && reader.IsDBNull(2),
                grantedAt = reader.GetString(1), revokedAt = reader.IsDBNull(2) ? null : reader.GetString(2) });
        }
        return Results.Ok(new { members });
    }

    public async Task<IResult> ChangeCampaignAccess(string projectId, JsonElement input,
        Security security, DeviceSession owner, CancellationToken cancellation)
    {
        if (!TaskIdPattern.IsMatch(projectId) || input.ValueKind != JsonValueKind.Object)
            return Results.BadRequest(new { error = "Invalid access change." });
        var deviceId = RequiredString(input, "deviceId", 32);
        var action = RequiredString(input, "action", 16);
        if (action is not ("grant" or "revoke")) return Results.BadRequest(new { error = "Invalid access action." });
        var inspected = await Runway("inspect", new { id = projectId }, cancellation);
        if (inspected.Error == "Project not found") return Results.NotFound();
        if (inspected.Error != null) return Results.Json(new { error = inspected.Error }, statusCode: 503);
        if (inspected.Value is not { ValueKind: JsonValueKind.Object } snapshot ||
            snapshot.GetProperty("campaign").ValueKind != JsonValueKind.Object)
            return Results.Json(new { error = "Only a saved campaign can be shared." }, statusCode: 409);
        var device = security.ActiveDevice(deviceId);
        deviceId = device?.PrincipalId ?? deviceId;
        if (action == "grant" && device is not { Owner: false })
            return Results.Json(new { error = "Choose an active, paired collaborator device." }, statusCode: 409);
        await sharedGatewayGate.WaitAsync(cancellation);
        try
        {
            lock (gate)
            {
                using var db = Open();
                using var transaction = db.BeginTransaction();
                if (action == "grant") BindNativeDevice(db, transaction, deviceId, owner.PrincipalId);
                using var command = db.CreateCommand();
                command.Transaction = transaction;
                if (action == "grant")
                {
                    command.CommandText = "INSERT INTO campaign_memberships(project_id,device_id,granted_by,granted_at,revoked_at) " +
                        "VALUES($project,$device,$owner,$time,NULL) ON CONFLICT(project_id,device_id) DO UPDATE SET " +
                        "granted_by=$owner,granted_at=$time,revoked_at=NULL";
                    command.Parameters.AddWithValue("$owner", owner.PrincipalId);
                }
                else command.CommandText = "UPDATE campaign_memberships SET revoked_at=$time " +
                    "WHERE project_id=$project AND device_id=$device AND revoked_at IS NULL";
                command.Parameters.AddWithValue("$project", projectId);
                command.Parameters.AddWithValue("$device", deviceId);
                command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
                var changed = command.ExecuteNonQuery();
                if (action == "revoke")
                {
                    using var native = db.CreateCommand();
                    native.Transaction = transaction;
                    native.CommandText = "UPDATE shared_marketing_sessions SET collaborator_device=NULL " +
                        "WHERE project_id=$project AND collaborator_device=$device";
                    native.Parameters.AddWithValue("$project", projectId);
                    native.Parameters.AddWithValue("$device", deviceId);
                    native.ExecuteNonQuery();
                }
                transaction.Commit();
                return changed == 0 ? Results.NotFound() : Results.Ok(new { deviceId,
                    name = device?.Name, access = action == "grant" ? "shared" : "revoked" });
            }
        }
        finally { sharedGatewayGate.Release(); }
    }

    public async Task<IResult> SharedCampaignReview(string projectId, DeviceSession actor,
        Security security, CancellationToken cancellation)
    {
        if (!TaskIdPattern.IsMatch(projectId)) return Results.BadRequest(new { error = "Invalid campaign ID." });
        if (!HasCampaignAccess(projectId, actor, security)) return Results.StatusCode(403);
        var inspected = await Runway("inspect", new { id = projectId }, cancellation);
        if (inspected.Error == "Project not found") return Results.NotFound();
        if (inspected.Error != null) return Results.Json(new { error = inspected.Error }, statusCode: 503);
        if (!HasCampaignAccess(projectId, actor, security)) return Results.StatusCode(403);
        var snapshot = inspected.Value!.Value;
        if (!snapshot.TryGetProperty("campaign", out var campaign) || campaign.ValueKind != JsonValueKind.Object)
            return Results.Json(new { error = "This assignment has no shared campaign brief." }, statusCode: 409);
        var project = snapshot.GetProperty("project");
        var assetId = campaign.GetProperty("asset_artifact_id").GetString();
        var assetDigest = campaign.GetProperty("asset_artifact_digest").GetString();
        var selected = await SelectedSharedAsset(snapshot, assetId!, assetDigest!, cancellation);
        if (selected == null)
            return Results.Json(new { error = "The selected shared draft is unavailable." }, statusCode: 409);
        var (asset, reviewSnapshot) = selected.Value;
        var requestChangesAvailable = project.GetProperty("status").GetString() == "needs_review" &&
            asset.GetProperty("kind").GetString() == "post_angles" &&
            !reviewSnapshot.GetProperty("reviews").EnumerateArray().Any(item =>
                item.GetProperty("artifact_id").GetString() == assetId &&
                item.GetProperty("artifact_digest").GetString() == assetDigest);
        CampaignInputRow[] discussion;
        object[] members;
        object? review = null;
        bool nativeSession;
        bool nativeProfileObserved;
        string? nativeIdentity;
        string? nativeProfileId;
        lock (gate)
        {
            using var db = Open();
            using (var command = db.CreateCommand())
            {
                command.CommandText = "SELECT request_id,project_id,actor_id,actor_name,kind,artifact_id," +
                    "artifact_digest,project_version,content,ledger_input_id,owner_review_id,status,error,created_at," +
                    "native_suggestion_id,native_profile_id " +
                    "FROM campaign_shared_inputs WHERE project_id=$project ORDER BY created_at,request_id LIMIT 500";
                command.Parameters.AddWithValue("$project", projectId);
                using var reader = command.ExecuteReader();
                var rows = new List<CampaignInputRow>();
                while (reader.Read()) rows.Add(ReadCampaignInput(reader));
                discussion = rows.ToArray();
            }
            using (var command = db.CreateCommand())
            {
                command.CommandText = "SELECT device_id FROM campaign_memberships WHERE project_id=$project AND revoked_at IS NULL";
                command.Parameters.AddWithValue("$project", projectId);
                using var reader = command.ExecuteReader();
                var rows = new List<object>();
                while (reader.Read())
                {
                    var device = security.ActiveDevice(reader.GetString(0));
                    if (device is { Owner: false }) rows.Add(new { name = device.Name, role = "collaborator" });
                }
                members = rows.ToArray();
            }
            var native = FindShared(db, projectId);
            nativeSession = native != null;
            nativeIdentity = NativeIdentity(db, actor);
            using var binding = db.CreateCommand();
            binding.CommandText = "SELECT gateway_profile FROM native_device_bindings WHERE device_id=$device";
            binding.Parameters.AddWithValue("$device", actor.PrincipalId);
            var boundProfile = binding.ExecuteScalar() as string;
            nativeProfileObserved = native != null && (actor.Owner
                ? Guid.TryParse(native.CreatorProfile, out _)
                : Guid.TryParse(boundProfile, out _));
            nativeProfileId = native == null ? null : actor.Owner ? native.CreatorProfile : boundProfile;
            var decision = reviewSnapshot.GetProperty("reviews").EnumerateArray().LastOrDefault(item =>
                item.GetProperty("artifact_id").GetString() == assetId &&
                item.GetProperty("artifact_digest").GetString() == assetDigest);
            if (decision.ValueKind == JsonValueKind.Object)
            {
                using var command = db.CreateCommand();
                command.CommandText = "SELECT 1 FROM owner_runway_reviews WHERE review_id=$review " +
                    "AND project_id=$project AND artifact_id=$artifact AND artifact_digest=$digest AND decision=$decision";
                command.Parameters.AddWithValue("$review", decision.GetProperty("id").GetString());
                command.Parameters.AddWithValue("$project", reviewSnapshot.GetProperty("project").GetProperty("id").GetString());
                command.Parameters.AddWithValue("$artifact", assetId);
                command.Parameters.AddWithValue("$digest", assetDigest);
                command.Parameters.AddWithValue("$decision", decision.GetProperty("decision").GetString());
                if (command.ExecuteScalar() != null) review = new {
                    decision = decision.GetProperty("decision").GetString(),
                    instruction = decision.GetProperty("instruction").GetString(),
                    actorName = decision.GetProperty("actor_name").GetString(),
                    createdAt = decision.GetProperty("created_at").GetDouble() };
            }
        }
        var sharedInputs = discussion.Select(item => new {
            requestId = item.RequestId, kind = item.Kind, actorName = item.ActorName,
            actorId = item.ActorId, content = item.Content, artifactId = item.ArtifactId,
            artifactDigest = item.ArtifactDigest, inputId = item.LedgerInputId,
            nativeProfileId = item.NativeProfileId,
            nativeRecorded = item.NativeSuggestionId != null,
            status = item.OwnerReviewId != null ? "authorized_execution_unavailable" :
                item.Kind == "revision_request" && item.Status == "recorded" ? "awaiting_owner_authorization" : item.Status,
            createdAt = item.CreatedAt
        }).ToArray();
        if (!HasCampaignAccess(projectId, actor, security)) return Results.StatusCode(403);
        return Results.Ok(new {
            project = new { id = projectId, goal = project.GetProperty("goal").GetString(),
                status = project.GetProperty("status").GetString(), version = project.GetProperty("version").GetInt32(),
                updatedAt = project.GetProperty("updated_at").GetDouble() },
            campaign = new { version = campaign.GetProperty("version").GetInt32(),
                stage = campaign.GetProperty("stage").GetString(),
                brief = campaign.GetProperty("brief_json").GetString(),
                experiment = campaign.GetProperty("experiment_json").GetString(),
                provisional = true },
            artifact = new { id = assetId, digest = assetDigest,
                kind = asset.GetProperty("kind").GetString(), content = asset.GetProperty("content").GetString(),
                createdAt = asset.GetProperty("created_at").GetDouble() },
            review, discussion = sharedInputs,
            members = members.Prepend(new { name = "Owner", role = "owner" }).ToArray(),
            employee = new { name = "Marketing employee", kind = "AI" },
            presentViewers = Array.Empty<object>(),
            native = new { sessionConnected = nativeSession,
                boundDevice = nativeIdentity != null,
                gatewayProfileObserved = nativeProfileObserved,
                gatewayProfileId = nativeProfileId,
                identitySlot = nativeIdentity,
                requiresHttps = true },
            execution = new { liveEnabled = RunwayLiveInferenceEnabled, publicationEnabled = false,
                requestChangesAvailable }
        });
    }

    public async Task<IResult> AddCampaignSharedInput(string projectId, JsonElement input,
        DeviceSession actor, Security security, System.Net.IPAddress? clientAddress,
        bool secureIngress, bool localOwnerIngress, CancellationToken cancellation)
    {
        await sharedGatewayGate.WaitAsync(cancellation);
        try
        {
            return await AddCampaignSharedInputSerial(projectId, input, actor, security,
                clientAddress, secureIngress, localOwnerIngress, cancellation);
        }
        finally { sharedGatewayGate.Release(); }
    }

    private async Task<IResult> AddCampaignSharedInputSerial(string projectId, JsonElement input,
        DeviceSession actor, Security security, System.Net.IPAddress? clientAddress,
        bool secureIngress, bool localOwnerIngress, CancellationToken cancellation)
    {
        if (!TaskIdPattern.IsMatch(projectId) || input.ValueKind != JsonValueKind.Object)
            return Results.BadRequest(new { error = "Invalid campaign input." });
        if (!HasCampaignAccess(projectId, actor, security)) return Results.StatusCode(403);
        var requestId = RequiredString(input, "requestId", 120);
        var kind = RequiredString(input, "kind", 32);
        var artifactId = RequiredString(input, "artifactId", 32);
        var digest = RequiredString(input, "artifactDigest", 64);
        var content = RequiredString(input, "content", 1000);
        if (kind is not ("comment" or "revision_request") || !Guid.TryParse(requestId, out _))
            return Results.BadRequest(new { error = "Use a unique request ID and a supported input type." });
        if (!input.TryGetProperty("projectVersion", out var version) || !version.TryGetInt32(out var expectedVersion))
            return Results.BadRequest(new { error = "Exact project version required." });
        using (var db = Open())
        {
            var prior = FindCampaignInput(db, requestId);
            if (prior != null && (prior.ProjectId != projectId || prior.ActorId != actor.PrincipalId ||
                prior.Kind != kind || prior.ArtifactId != artifactId || prior.ArtifactDigest != digest ||
                prior.Content != content)) return Results.Json(new { error = "Request ID belongs to another input." }, statusCode: 409);
            if (prior?.Status == "recorded") return Results.Ok(new { requestId, inputId = prior.LedgerInputId,
                status = kind == "revision_request" ? "awaiting_owner_authorization" : "recorded" });
        }
        var inspected = await Runway("inspect", new { id = projectId }, cancellation);
        if (inspected.Error == "Project not found") return Results.NotFound();
        if (inspected.Error != null) return Results.Json(new { error = inspected.Error }, statusCode: 503);
        var snapshot = inspected.Value!.Value;
        var selectedAsset = await SelectedSharedAsset(snapshot, artifactId, digest, cancellation);
        if (selectedAsset == null ||
            snapshot.GetProperty("project").GetProperty("version").GetInt32() != expectedVersion)
            return Results.Json(new { error = "The shared draft or project version changed. Refresh and reapply your text." }, statusCode: 409);
        if (kind == "revision_request" &&
            selectedAsset.Value.Artifact.GetProperty("kind").GetString() == "revision_angles")
            return Results.Json(new { error = "A further worker revision of an adopted draft is not supported in this pilot. Add a comment for owner review instead." }, statusCode: 409);
        if (kind == "revision_request" &&
            (snapshot.GetProperty("project").GetProperty("status").GetString() != "needs_review" ||
             selectedAsset.Value.ReviewSnapshot.GetProperty("reviews").EnumerateArray().Any(item =>
                 item.GetProperty("artifact_id").GetString() == artifactId &&
                 item.GetProperty("artifact_digest").GetString() == digest)))
            return Results.Json(new { error = "This exact draft already has a decision or is no longer awaiting review. Add a comment for owner review instead." }, statusCode: 409);
        if (NativeSessionExists(projectId))
        {
            if (NativeIngressIp(actor, clientAddress, secureIngress, localOwnerIngress) == null)
                return Results.Json(new { error = "This native-linked campaign requires the local owner session or an authenticated HTTPS collaborator address." }, statusCode: 409);
            using var db = Open();
            if (NativeIdentity(db, actor) == null)
                return Results.Json(new { error = "This paired device has no native identity slot. Ask the owner to grant campaign access again." }, statusCode: 409);
        }
        if (!HasCampaignAccess(projectId, actor, security)) return Results.StatusCode(403);
        lock (gate)
        {
            using var db = Open();
            var claimed = FindCampaignInput(db, requestId);
            if (claimed != null && (claimed.ProjectId != projectId || claimed.ActorId != actor.PrincipalId ||
                claimed.Kind != kind || claimed.ArtifactId != artifactId ||
                claimed.ArtifactDigest != digest || claimed.Content != content))
                return Results.Json(new { error = "Request ID belongs to another input." }, statusCode: 409);
            if (claimed == null)
            {
                using var command = db.CreateCommand();
                command.CommandText = "INSERT INTO campaign_shared_inputs(request_id,project_id,actor_id,actor_name," +
                    "kind,artifact_id,artifact_digest,project_version,content,status,created_at,updated_at) " +
                    "VALUES($request,$project,$actor,$name,$kind,$artifact,$digest,$version,$content,'pending',$time,$time)";
                command.Parameters.AddWithValue("$request", requestId);
                command.Parameters.AddWithValue("$project", projectId);
                command.Parameters.AddWithValue("$actor", actor.PrincipalId);
                command.Parameters.AddWithValue("$name", actor.Name);
                command.Parameters.AddWithValue("$kind", kind);
                command.Parameters.AddWithValue("$artifact", artifactId);
                command.Parameters.AddWithValue("$digest", digest);
                command.Parameters.AddWithValue("$version", expectedVersion);
                command.Parameters.AddWithValue("$content", content);
                command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
            }
        }
        if (NativeSessionExists(projectId))
        {
            var native = await EnsureNativeSuggestion(projectId, requestId, digest, content,
                actor, NativeIngressIp(actor, clientAddress, secureIngress, localOwnerIngress)!, cancellation);
            if (native.Failure != null) return native.Failure;
        }
        if (!HasCampaignAccess(projectId, actor, security)) return Results.StatusCode(403);
        var saved = await Runway("input", new { id = projectId, request_id = requestId,
            actor_id = actor.PrincipalId, actor_name = actor.Name, content, version = expectedVersion,
            activate = false }, cancellation);
        if (saved.Error != null)
        {
            var reconciled = await Runway("inspect", new { id = projectId }, cancellation);
            var priorInput = reconciled.Value is { ValueKind: JsonValueKind.Object } current
                ? current.GetProperty("inputs").EnumerateArray().FirstOrDefault(item =>
                    item.GetProperty("request_id").GetString() == requestId &&
                    item.GetProperty("actor_id").GetString() == actor.PrincipalId &&
                    item.GetProperty("content").GetString() == content) : default;
            if (priorInput.ValueKind == JsonValueKind.Object)
            {
                lock (gate)
                {
                    using var recovered = Open();
                    using var update = recovered.CreateCommand();
                    update.CommandText = "UPDATE campaign_shared_inputs SET status='recorded',ledger_input_id=$input," +
                        "error=NULL,updated_at=$time WHERE request_id=$request AND actor_id=$actor";
                    update.Parameters.AddWithValue("$input", priorInput.GetProperty("id").GetString());
                    update.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
                    update.Parameters.AddWithValue("$request", requestId);
                    update.Parameters.AddWithValue("$actor", actor.PrincipalId);
                    update.ExecuteNonQuery();
                }
                return Results.Ok(new { requestId, inputId = priorInput.GetProperty("id").GetString(),
                    status = kind == "revision_request" ? "awaiting_owner_authorization" : "recorded" });
            }
            using var db = Open();
            using var command = db.CreateCommand();
            command.CommandText = "UPDATE campaign_shared_inputs SET status=$status,error=$error,updated_at=$time " +
                "WHERE request_id=$request AND status IN ('pending','native_recorded')";
            command.Parameters.AddWithValue("$status", saved.Error.Contains("Stale project version", StringComparison.OrdinalIgnoreCase)
                ? "conflict" : "unknown");
            command.Parameters.AddWithValue("$error", saved.Error);
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$request", requestId);
            command.ExecuteNonQuery();
            var stale = saved.Error.Contains("Stale project version", StringComparison.OrdinalIgnoreCase);
            return Results.Json(new { error = stale
                    ? "The project version changed. Reload the campaign and reapply your text."
                    : "The campaign input was not confirmed. Keep the text and inspect its receipt before another attempt.",
                requestId }, statusCode: stale ? 409 : 503);
        }
        var recorded = saved.Value!.Value.GetProperty("inputs").EnumerateArray().FirstOrDefault(item =>
            item.GetProperty("request_id").GetString() == requestId &&
            item.GetProperty("actor_id").GetString() == actor.PrincipalId &&
            item.GetProperty("content").GetString() == content);
        if (recorded.ValueKind != JsonValueKind.Object)
            return Results.Json(new { error = "The ledger outcome needs reconciliation; do not resend automatically.",
                requestId }, statusCode: 503);
        lock (gate)
        {
            using var db = Open();
            using var command = db.CreateCommand();
            command.CommandText = "UPDATE campaign_shared_inputs SET status='recorded',ledger_input_id=$input," +
                "error=NULL,updated_at=$time WHERE request_id=$request AND actor_id=$actor";
            command.Parameters.AddWithValue("$input", recorded.GetProperty("id").GetString());
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$request", requestId);
            command.Parameters.AddWithValue("$actor", actor.PrincipalId);
            command.ExecuteNonQuery();
        }
        return Results.Ok(new { requestId, inputId = recorded.GetProperty("id").GetString(),
            status = kind == "revision_request" ? "awaiting_owner_authorization" : "recorded" });
    }

    public async Task<IResult> AuthorizeCampaignRevisionRequest(string projectId, string requestId,
        JsonElement input, DeviceSession owner, CancellationToken cancellation)
    {
        if (!TaskIdPattern.IsMatch(projectId) || !Guid.TryParse(requestId, out _) ||
            input.ValueKind != JsonValueKind.Object ||
            !input.TryGetProperty("projectVersion", out var version) || !version.TryGetInt32(out var current))
            return Results.BadRequest(new { error = "Exact campaign request and project version required." });
        CampaignInputRow request;
        using (var db = Open()) request = FindCampaignInput(db, requestId)!;
        if (request == null || request.ProjectId != projectId) return Results.NotFound();
        if (request.Kind != "revision_request" || request.Status != "recorded" || request.LedgerInputId == null)
            return Results.Json(new { error = "Only a saved revision request can be authorized." }, statusCode: 409);
        if (request.OwnerReviewId != null) return Results.Ok(new { requestId,
            status = "authorized_execution_unavailable", reviewId = request.OwnerReviewId });
        var inspected = await Runway("inspect", new { id = projectId }, cancellation);
        if (inspected.Error != null) return Results.Json(new { error = inspected.Error }, statusCode: 503);
        var snapshot = inspected.Value!.Value;
        if (await SelectedSharedAsset(snapshot, request.ArtifactId, request.ArtifactDigest, cancellation) == null ||
            snapshot.GetProperty("project").GetProperty("version").GetInt32() != current)
            return Results.Json(new { error = "The selected draft or project version changed. Review it again." }, statusCode: 409);
        var reviewRequestId = "shared-review:" + requestId;
        var reviewBody = JsonSerializer.SerializeToElement(new { requestId = reviewRequestId,
            artifactId = request.ArtifactId, digest = request.ArtifactDigest,
            decision = "revision_requested", instruction = request.Content, version = current });
        var result = await ReviewRunway(projectId, reviewBody, owner, cancellation);
        var after = await Runway("inspect", new { id = projectId }, cancellation);
        if (after.Error != null) return result;
        var review = after.Value!.Value.GetProperty("reviews").EnumerateArray().FirstOrDefault(item =>
            item.GetProperty("request_id").GetString() == reviewRequestId &&
            item.GetProperty("artifact_id").GetString() == request.ArtifactId &&
            item.GetProperty("artifact_digest").GetString() == request.ArtifactDigest &&
            item.GetProperty("decision").GetString() == "revision_requested" &&
            item.GetProperty("actor_id").GetString() == owner.PrincipalId);
        if (review.ValueKind != JsonValueKind.Object) return result;
        lock (gate)
        {
            using var db = Open();
            using var command = db.CreateCommand();
            command.CommandText = "UPDATE campaign_shared_inputs SET owner_review_id=$review," +
                "updated_at=$time WHERE request_id=$request AND owner_review_id IS NULL";
            command.Parameters.AddWithValue("$review", review.GetProperty("id").GetString());
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$request", requestId);
            command.ExecuteNonQuery();
        }
        return Results.Ok(new { requestId, status = "authorized_execution_unavailable",
            reviewId = review.GetProperty("id").GetString() });
    }

    public async Task<IResult> RunFixtureLinkedRevision(string projectId, string requestId,
        DeviceSession owner, CancellationToken cancellation)
    {
        if (!FixtureCampaignEnabled) return Results.NotFound();
        if (!TaskIdPattern.IsMatch(projectId) || !Guid.TryParse(requestId, out _))
            return Results.BadRequest(new { error = "Exact fixture campaign and request required." });
        await fixtureRevisionGate.WaitAsync(cancellation);
        try
        {
            CampaignInputRow? request;
            using (var db = Open())
            {
                request = FindCampaignInput(db, requestId);
                if (request?.OwnerReviewId is { } reviewId)
                {
                    using var receipt = db.CreateCommand();
                    receipt.CommandText = "SELECT 1 FROM owner_runway_reviews WHERE review_id=$review " +
                        "AND project_id=$project AND artifact_id=$artifact AND artifact_digest=$digest " +
                        "AND decision='revision_requested'";
                    receipt.Parameters.AddWithValue("$review", reviewId);
                    receipt.Parameters.AddWithValue("$project", projectId);
                    receipt.Parameters.AddWithValue("$artifact", request.ArtifactId);
                    receipt.Parameters.AddWithValue("$digest", request.ArtifactDigest);
                    if (receipt.ExecuteScalar() == null) request = null;
                }
            }
            if (request is not { ProjectId: var sourceId, Kind: "revision_request", Status: "recorded",
                LedgerInputId: not null, OwnerReviewId: not null } || sourceId != projectId)
                return Results.Json(new { error = "A saved, owner-authorized change request is required." }, statusCode: 409);
            var source = await Runway("inspect", new { id = projectId }, cancellation);
            if (source.Error != null) return Results.Json(new { error = source.Error }, statusCode: 503);
            var snapshot = source.Value!.Value;
            if (snapshot.GetProperty("campaign").GetProperty("mode").GetString() != "fixture" ||
                await SelectedSharedAsset(snapshot, request.ArtifactId, request.ArtifactDigest, cancellation) == null)
                return Results.Json(new { error = "Only the selected fixture draft can be revised." }, statusCode: 409);
            var grantRequestId = "fixture-grant:" + requestId;
            var previousGrant = snapshot.GetProperty("revision_grants").EnumerateArray().FirstOrDefault(item =>
                item.GetProperty("request_id").GetString() == grantRequestId);
            var prepared = await Runway("prepare-revision-grant", new {
                id = projectId, request_id = grantRequestId, review_id = request.OwnerReviewId,
                artifact_id = request.ArtifactId, digest = request.ArtifactDigest,
                version = previousGrant.ValueKind == JsonValueKind.Object
                    ? previousGrant.GetProperty("source_runway_version").GetInt32()
                    : snapshot.GetProperty("project").GetProperty("version").GetInt32(),
                owner_actor = owner.PrincipalId, actor_owner = true, budget_mode = "fresh_pilot",
                deadline_at = previousGrant.ValueKind == JsonValueKind.Object
                    ? previousGrant.GetProperty("deadline_at").GetDouble()
                    : DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds(),
                max_runs = 1, max_model_requests = 1, token_limit = 25000, max_active_seconds = 300
            }, cancellation);
            if (prepared.Error != null)
                return Results.Json(new { error = prepared.Error }, statusCode: 409);
            var grant = prepared.Value!.Value.GetProperty("revision_grants").EnumerateArray()
                .First(item => item.GetProperty("request_id").GetString() == grantRequestId);
            var grantId = grant.GetProperty("id").GetString()!;
            var released = await Runway("release-revision-grant", new {
                grant_id = grantId, owner_actor = owner.PrincipalId, actor_owner = true, transport_ready = true
            }, cancellation);
            if (released.Error != null)
                return Results.Json(new { error = released.Error }, statusCode: 409);
            var linked = released.Value!.Value;
            if (linked.GetProperty("artifacts").GetArrayLength() > 0) return Results.Ok(linked);
            if (linked.GetProperty("project").GetProperty("status").GetString() != "ready")
                return Results.Json(new { error = "The fixture revision is already claimed or needs reconciliation." }, statusCode: 409);
            var claimed = await Runway("claim", null, cancellation);
            if (claimed.Error != null || claimed.Value is not { ValueKind: JsonValueKind.Object } packet ||
                packet.GetProperty("project").GetProperty("id").GetString() != linked.GetProperty("project").GetProperty("id").GetString())
                return Results.Json(new { error = "The linked fixture claim was not confirmed." }, statusCode: 503);
            var original = snapshot.GetProperty("artifacts").EnumerateArray().First(item =>
                item.GetProperty("id").GetString() == request.ArtifactId);
            var draft = JsonNode.Parse(original.GetProperty("content").GetString()!)!.AsObject();
            draft["angles"]![0]!["hook"] = "Ask what your marketing agent may do before work is approved";
            draft["revisionOf"] = request.ArtifactId;
            draft["revisionRequest"] = request.OwnerReviewId;
            draft["fixtureOnly"] = true;
            draft["sourceInputIds"] = JsonSerializer.SerializeToNode(packet.GetProperty("inputs").EnumerateArray()
                .Where(item => item.GetProperty("source_input_id").ValueKind == JsonValueKind.String)
                .Select(item => item.GetProperty("source_input_id").GetString()).ToArray());
            var content = draft.ToJsonString();
            var validated = ValidateRunwayArtifact("revision_angles", content, packet);
            var finished = await Runway("finish", new { execution_id = packet.GetProperty("execution_id").GetString(),
                content = validated.Content, source_urls = validated.SourceUrls,
                usage = new { totalTokens = 0 } }, cancellation);
            if (finished.Error != null)
                return Results.Json(new { error = "Fixture settlement needs reconciliation: " + finished.Error }, statusCode: 503);
            return Results.Ok(finished.Value!.Value);
        }
        finally { fixtureRevisionGate.Release(); }
    }
}
