using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Thaddeus.Host;

public sealed partial class MarketingBackend
{
    private sealed record SharedSessionRow(string ProjectId, string SessionKey, string SessionId,
        string CreatorProfile, string? CollaboratorDevice, string? CollaboratorProfile,
        string? CollaboratorApprovedBy, string? CollaboratorApprovedAt);

    private static SharedSessionRow? FindShared(SqliteConnection db, string projectId)
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT project_id,session_key,session_id,creator_profile,collaborator_device,collaborator_profile," +
            "collaborator_approved_by,collaborator_approved_at " +
            "FROM shared_marketing_sessions WHERE project_id=$project";
        command.Parameters.AddWithValue("$project", projectId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new SharedSessionRow(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7)) : null;
    }

    private static string? ObservedClientIp(IPAddress? address)
    {
        if (address == null || IPAddress.IsLoopback(address)) return null;
        var normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return IPAddress.IsLoopback(normalized) ? null : normalized.ToString();
    }

    private async Task<JsonElement> SharedRpc(object request, CancellationToken cancellation)
    {
        var response = await Docker(sharedContainer, JsonSerializer.Serialize(request), TimeSpan.FromSeconds(22),
            cancellation, "node", "/opt/hire/dev/shared-rpc.cjs");
        if (response.Exit != 0)
            throw new IOException(string.IsNullOrWhiteSpace(response.Error)
                ? "The isolated shared Gateway did not confirm the request." : response.Error.Trim());
        using var document = JsonDocument.Parse(response.Output);
        return document.RootElement.Clone();
    }

    public IResult SharedConversation(string projectId, DeviceSession actor)
    {
        if (!TaskIdPattern.IsMatch(projectId)) return Results.BadRequest(new { error = "Invalid project ID." });
        lock (gate)
        {
            using var db = Open();
            var shared = FindShared(db, projectId);
            if (shared == null) return Results.Ok(new { available = false, suggestions = Array.Empty<object>() });
            if (!actor.Owner && shared.CollaboratorDevice != actor.Id)
                return Results.Json(new { error = "The owner has not approved this paired device for the native project conversation." }, statusCode: 403);
            using var command = db.CreateCommand();
            command.CommandText = "SELECT request_id,actor_name,content,status,suggestion_id,error,created_at " +
                "FROM shared_marketing_inputs WHERE project_id=$project ORDER BY created_at ASC LIMIT 100";
            command.Parameters.AddWithValue("$project", projectId);
            using var reader = command.ExecuteReader();
            var suggestions = new List<object>();
            while (reader.Read()) suggestions.Add(new
            {
                requestId = reader.GetString(0), actorName = reader.GetString(1), content = reader.GetString(2),
                status = reader.GetString(3), suggestionId = reader.IsDBNull(4) ? null : reader.GetString(4),
                error = reader.IsDBNull(5) ? null : reader.GetString(5), createdAt = reader.GetString(6)
            });
            return Results.Ok(new { available = true, sessionKey = shared.SessionKey,
                sessionId = shared.SessionId, collaboratorDevice = actor.Owner ? shared.CollaboratorDevice : null,
                collaboratorApprovedAt = actor.Owner ? shared.CollaboratorApprovedAt : null,
                suggestions });
        }
    }

    public IResult ApproveSharedCollaborator(string projectId, JsonElement input, Security security, DeviceSession owner)
    {
        if (!TaskIdPattern.IsMatch(projectId) || input.ValueKind != JsonValueKind.Object)
            return Results.BadRequest(new { error = "Invalid collaborator selection." });
        var deviceId = RequiredString(input, "deviceId", 32);
        var device = security.ActiveDevice(deviceId);
        if (device is not { Owner: false }) return Results.Json(new { error = "Choose an active, paired collaborator device." }, statusCode: 409);
        lock (gate)
        {
            using var db = Open();
            var shared = FindShared(db, projectId);
            if (shared == null) return Results.NotFound();
            if (shared.CollaboratorProfile != null && shared.CollaboratorDevice != deviceId)
                return Results.Json(new { error = "A different collaborator has native contributions; review them before changing access." }, statusCode: 409);
            if (shared.CollaboratorDevice != null && shared.CollaboratorDevice != deviceId)
            {
                using var prior = db.CreateCommand();
                prior.CommandText = "SELECT COUNT(*) FROM shared_marketing_inputs WHERE project_id=$project AND actor_id=$actor";
                prior.Parameters.AddWithValue("$project", projectId);
                prior.Parameters.AddWithValue("$actor", shared.CollaboratorDevice);
                if ((long)prior.ExecuteScalar()! > 0)
                    return Results.Json(new { error = "The previous collaborator has an unresolved native input; review it before changing access." }, statusCode: 409);
            }
            using var command = db.CreateCommand();
            command.CommandText = "UPDATE shared_marketing_sessions SET collaborator_device=$device," +
                "collaborator_approved_by=$owner,collaborator_approved_at=$time " +
                "WHERE project_id=$project AND (collaborator_profile IS NULL OR collaborator_device=$device)";
            command.Parameters.AddWithValue("$device", deviceId);
            command.Parameters.AddWithValue("$owner", owner.Id);
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$project", projectId);
            command.ExecuteNonQuery();
            return Results.Ok(new { deviceId, name = device.Name, approved = true });
        }
    }

    public async Task<IResult> StartSharedConversation(string projectId, DeviceSession owner,
        IPAddress? clientAddress, CancellationToken cancellation)
    {
        if (!TaskIdPattern.IsMatch(projectId)) return Results.BadRequest(new { error = "Invalid project ID." });
        var clientIp = ObservedClientIp(clientAddress);
        if (clientIp == null) return Results.Json(new
        {
            error = "Native identity ingress needs an observed non-loopback client address. Open the cockpit through a trusted LAN or identity proxy before starting the shared conversation."
        }, statusCode: 409);
        await sharedGatewayGate.WaitAsync(cancellation);
        try
        {
            lock (gate)
            {
                using var db = Open();
                if (FindShared(db, projectId) is { } existing)
                    return Results.Ok(new { available = true, sessionKey = existing.SessionKey, sessionId = existing.SessionId });
            }
            var status = await Runway("status", null, cancellation);
            if (status.Error != null) return Results.Json(new { error = status.Error }, statusCode: 503);
            if (status.Value is not { ValueKind: JsonValueKind.Object } snapshot ||
                snapshot.GetProperty("project").GetProperty("id").GetString() != projectId)
                return Results.NotFound();
            JsonElement result;
            try { result = await SharedRpc(new { principal = "owner", clientIp, action = "create", projectId }, cancellation); }
            catch (Exception error) when (error is IOException or JsonException or OperationCanceledException)
            { return Results.Json(new { error = "The shared session was not confirmed. Refresh, then retry; creation reconciles the exact project label. " + error.Message }, statusCode: 503); }
            var sessionKey = result.GetProperty("sessionKey").GetString()!;
            var sessionId = result.GetProperty("sessionId").GetString()!;
            var creator = result.GetProperty("createdActor").GetProperty("id").GetString()!;
            if (!sessionKey.StartsWith("agent:shared-marketing:", StringComparison.Ordinal) ||
                !Guid.TryParse(sessionId, out _) || !Guid.TryParse(creator, out _))
                return Results.Json(new { error = "Gateway returned an invalid shared-session identity." }, statusCode: 503);
            lock (gate)
            {
                using var db = Open();
                using var command = db.CreateCommand();
                command.CommandText = "INSERT OR IGNORE INTO shared_marketing_sessions" +
                    "(project_id,session_key,session_id,creator_profile,created_at) " +
                    "VALUES($project,$key,$session,$creator,$time)";
                command.Parameters.AddWithValue("$project", projectId);
                command.Parameters.AddWithValue("$key", sessionKey);
                command.Parameters.AddWithValue("$session", sessionId);
                command.Parameters.AddWithValue("$creator", creator);
                command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
            }
            return Results.Ok(new { available = true, sessionKey, sessionId });
        }
        finally { sharedGatewayGate.Release(); }
    }

    public async Task<IResult> AddSharedSuggestion(string projectId, JsonElement input, DeviceSession actor,
        IPAddress? clientAddress, CancellationToken cancellation)
    {
        if (!TaskIdPattern.IsMatch(projectId) || input.ValueKind != JsonValueKind.Object)
            return Results.BadRequest(new { error = "Invalid shared suggestion." });
        var clientIp = ObservedClientIp(clientAddress);
        if (clientIp == null) return Results.Json(new { error = "Observed non-loopback client address required for native identity." }, statusCode: 409);
        var requestId = RequiredString(input, "requestId", 120);
        var content = RequiredString(input, "content", 1000);
        if (!input.TryGetProperty("version", out var version) || !version.TryGetInt32(out var expectedVersion) || expectedVersion < 1)
            return Results.BadRequest(new { error = "Current project version required." });
        await sharedGatewayGate.WaitAsync(cancellation);
        try
        {
            SharedSessionRow shared;
            lock (gate)
            {
                using var db = Open();
                shared = FindShared(db, projectId)!;
                if (shared == null) return Results.NotFound();
                if (!actor.Owner && shared.CollaboratorDevice != actor.Id)
                    return Results.Json(new { error = "The owner has not approved this paired device for the native project conversation." }, statusCode: 403);
                using var previous = db.CreateCommand();
                previous.CommandText = "SELECT project_id,actor_id,content,status,suggestion_id,error FROM shared_marketing_inputs WHERE request_id=$request";
                previous.Parameters.AddWithValue("$request", requestId);
                using var reader = previous.ExecuteReader();
                if (reader.Read())
                {
                    if (reader.GetString(0) != projectId || reader.GetString(1) != actor.Id || reader.GetString(2) != content)
                        return Results.Json(new { error = "Request ID belongs to different input." }, statusCode: 409);
                    return Results.Ok(new { requestId, status = reader.GetString(3),
                        suggestionId = reader.IsDBNull(4) ? null : reader.GetString(4),
                        error = reader.IsDBNull(5) ? null : reader.GetString(5) });
                }
            }
            var runway = await Runway("status", null, cancellation);
            if (runway.Error != null) return Results.Json(new { error = runway.Error }, statusCode: 503);
            if (runway.Value is not { ValueKind: JsonValueKind.Object } snapshot ||
                snapshot.GetProperty("project").GetProperty("id").GetString() != projectId)
                return Results.NotFound();
            if (snapshot.GetProperty("project").GetProperty("version").GetInt32() != expectedVersion)
                return Results.Json(new { error = "Project version changed; refresh before suggesting a revision." }, statusCode: 409);
            lock (gate)
            {
                using var db = Open();
                using var insert = db.CreateCommand();
                insert.CommandText = "INSERT OR IGNORE INTO shared_marketing_inputs" +
                    "(request_id,project_id,actor_id,actor_name,content,project_version,status,created_at,updated_at) " +
                    "VALUES($request,$project,$actor,$name,$content,$version,'pending',$time,$time)";
                insert.Parameters.AddWithValue("$request", requestId);
                insert.Parameters.AddWithValue("$project", projectId);
                insert.Parameters.AddWithValue("$actor", actor.Id);
                insert.Parameters.AddWithValue("$name", actor.Name);
                insert.Parameters.AddWithValue("$content", content);
                insert.Parameters.AddWithValue("$version", expectedVersion);
                insert.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
                if (insert.ExecuteNonQuery() == 0)
                    return Results.Json(new { error = "Request was already claimed; inspect its receipt before retrying." }, statusCode: 409);
            }
            JsonElement suggestion;
            try
            {
                var response = await SharedRpc(new { principal = actor.Owner ? "owner" : "collaborator", clientIp,
                    action = "suggest", sessionKey = shared.SessionKey, content }, cancellation);
                suggestion = response.GetProperty("suggestion");
            }
            catch (Exception error) when (error is IOException or JsonException or OperationCanceledException)
            {
                UpdateSharedInput(requestId, "unknown", null, null, "Gateway outcome needs reconciliation: " + error.Message);
                return Results.Json(new { error = "Gateway outcome is unknown. This request will not be resent automatically." }, statusCode: 503);
            }
            var suggestionId = suggestion.GetProperty("id").GetString()!;
            var gatewayProfile = suggestion.GetProperty("author").GetProperty("id").GetString()!;
            if (suggestion.GetProperty("sessionKey").GetString() != shared.SessionKey ||
                suggestion.GetProperty("text").GetString() != content ||
                suggestion.GetProperty("author").GetProperty("type").GetString() != "human" ||
                !Guid.TryParse(suggestionId, out _) || !Guid.TryParse(gatewayProfile, out _) ||
                (actor.Owner && gatewayProfile != shared.CreatorProfile) ||
                (!actor.Owner && shared.CollaboratorProfile != null && gatewayProfile != shared.CollaboratorProfile))
            {
                UpdateSharedInput(requestId, "unknown", suggestionId, gatewayProfile, "Gateway attribution needs reconciliation.");
                return Results.Json(new { error = "Gateway attribution could not be verified." }, statusCode: 503);
            }
            lock (gate)
            {
                using var db = Open();
                using var transaction = db.BeginTransaction();
                if (!actor.Owner)
                {
                    using var bind = db.CreateCommand();
                    bind.Transaction = transaction;
                    bind.CommandText = "UPDATE shared_marketing_sessions SET collaborator_profile=$profile " +
                        "WHERE project_id=$project AND collaborator_device=$device " +
                        "AND (collaborator_profile IS NULL OR collaborator_profile=$profile)";
                    bind.Parameters.AddWithValue("$device", actor.Id);
                    bind.Parameters.AddWithValue("$profile", gatewayProfile);
                    bind.Parameters.AddWithValue("$project", projectId);
                    if (bind.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        UpdateSharedInput(requestId, "unknown", suggestionId, gatewayProfile, "Collaborator binding changed after native admission.");
                        return Results.StatusCode(409);
                    }
                }
                using var update = db.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE shared_marketing_inputs SET status='gateway_recorded',suggestion_id=$suggestion," +
                    "gateway_profile=$profile,updated_at=$time WHERE request_id=$request AND status='pending'";
                update.Parameters.AddWithValue("$suggestion", suggestionId);
                update.Parameters.AddWithValue("$profile", gatewayProfile);
                update.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
                update.Parameters.AddWithValue("$request", requestId);
                update.ExecuteNonQuery();
                transaction.Commit();
            }
            var ledger = await Runway("input", new { id = projectId, request_id = "native:" + suggestionId,
                actor_id = gatewayProfile, actor_name = actor.Name, content, version = expectedVersion }, cancellation);
            if (ledger.Error != null)
            {
                UpdateSharedInput(requestId, "ledger_conflict", suggestionId, gatewayProfile, ledger.Error);
                return Results.Json(new { requestId, suggestionId, status = "ledger_conflict", error = ledger.Error }, statusCode: 409);
            }
            UpdateSharedInput(requestId, "recorded", suggestionId, gatewayProfile, null);
            return Results.Ok(new { requestId, suggestionId, status = "recorded", gatewayProfile });
        }
        finally { sharedGatewayGate.Release(); }
    }

    private void UpdateSharedInput(string requestId, string status, string? suggestionId,
        string? profile, string? error)
    {
        lock (gate)
        {
            using var db = Open();
            using var command = db.CreateCommand();
            command.CommandText = "UPDATE shared_marketing_inputs SET status=$status,suggestion_id=$suggestion," +
                "gateway_profile=$profile,error=$error,updated_at=$time WHERE request_id=$request";
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$suggestion", (object?)suggestionId ?? DBNull.Value);
            command.Parameters.AddWithValue("$profile", (object?)profile ?? DBNull.Value);
            command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$request", requestId);
            command.ExecuteNonQuery();
        }
    }
}
