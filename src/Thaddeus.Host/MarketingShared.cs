using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Thaddeus.Host;

public sealed partial class MarketingBackend
{
    // The host authenticates a paired device, then forwards its immutable slot.
    // An observed device remains distinct from proving which person holds it.
    private static bool LegacySuggestionWritesEnabled => false;
    private sealed record SharedSessionRow(string ProjectId, string SessionKey, string SessionId,
        string CreatorProfile, string? CollaboratorDevice, string? CollaboratorProfile,
        string? CollaboratorApprovedBy, string? CollaboratorApprovedAt);
    private sealed record SharedRecordedInput(string ActorId, string ActorName, string Content,
        int ProjectVersion, string Status, string SuggestionId, string GatewayProfile);

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

    private static bool IsPrivateLanIp(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
            bytes[0] == 192 && bytes[1] == 168;
    }

    private static string? NativeHostLanIp(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!IPAddress.TryParse(configured, out var address) || !IsPrivateLanIp(address))
                throw new ArgumentException("Marketing:NativeHostLanIp must be a private non-loopback IPv4 address.");
            return address.ToString();
        }
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.OperationalStatus == OperationalStatus.Up &&
                item.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet &&
                !item.Name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1)
            .SelectMany(item => item.GetIPProperties().UnicastAddresses.Select(address => address.Address))
            .FirstOrDefault(IsPrivateLanIp)?.ToString();
    }

    private string? NativeIngressIp(DeviceSession actor, IPAddress? clientAddress,
        bool secureIngress, bool localOwnerIngress)
    {
        var observed = ObservedClientIp(clientAddress);
        if (secureIngress && observed != null) return observed;
        // The local owner is already authenticated by the loopback-only host key.
        // The Gateway receives this host LAN address as a proxy address, not as
        // a claim about the browser's network source.
        return actor.Owner && localOwnerIngress ? nativeHostLanIp : null;
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

    public IResult SharedConversation(string projectId, DeviceSession actor, Security security)
    {
        if (!TaskIdPattern.IsMatch(projectId)) return Results.BadRequest(new { error = "Invalid project ID." });
        if (!HasCampaignAccess(projectId, actor, security)) return Results.StatusCode(403);
        lock (gate)
        {
            using var db = Open();
            var shared = FindShared(db, projectId);
            if (shared == null) return Results.Ok(new { available = false, suggestions = Array.Empty<object>() });
            if (!actor.Owner && shared.CollaboratorDevice != actor.PrincipalId)
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
        deviceId = device?.PrincipalId ?? deviceId;
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
            using var transaction = db.BeginTransaction();
            BindNativeDevice(db, transaction, deviceId, owner.PrincipalId);
            using var command = db.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE shared_marketing_sessions SET collaborator_device=$device," +
                "collaborator_approved_by=$owner,collaborator_approved_at=$time " +
                "WHERE project_id=$project AND (collaborator_profile IS NULL OR collaborator_device=$device)";
            command.Parameters.AddWithValue("$device", deviceId);
            command.Parameters.AddWithValue("$owner", owner.PrincipalId);
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$project", projectId);
            command.ExecuteNonQuery();
            using var membership = db.CreateCommand();
            membership.Transaction = transaction;
            membership.CommandText = "INSERT INTO campaign_memberships(project_id,device_id,granted_by,granted_at,revoked_at) " +
                "VALUES($project,$device,$owner,$time,NULL) ON CONFLICT(project_id,device_id) DO UPDATE SET " +
                "granted_by=$owner,granted_at=$time,revoked_at=NULL";
            membership.Parameters.AddWithValue("$project", projectId);
            membership.Parameters.AddWithValue("$device", deviceId);
            membership.Parameters.AddWithValue("$owner", owner.PrincipalId);
            membership.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            membership.ExecuteNonQuery();
            transaction.Commit();
            return Results.Ok(new { deviceId, name = device.Name, approved = true });
        }
    }

    public async Task<IResult> StartSharedConversation(string projectId, DeviceSession owner,
        IPAddress? clientAddress, bool secureIngress, bool localOwnerIngress,
        CancellationToken cancellation)
    {
        if (!TaskIdPattern.IsMatch(projectId)) return Results.BadRequest(new { error = "Invalid project ID." });
        if (FixtureCampaignEnabled && !fixtureNativeGatewayEnabled)
            return Results.Json(new { error = "The native Gateway is unavailable in this isolated fixture." }, statusCode: 409);
        var clientIp = NativeIngressIp(owner, clientAddress, secureIngress, localOwnerIngress);
        if (clientIp == null) return Results.Json(new
        {
            error = "Native identity ingress needs a private host LAN address for the local owner, or an observed non-loopback client through authenticated HTTPS."
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
            var status = await Runway("inspect", new { id = projectId }, cancellation);
            if (status.Error != null) return Results.Json(new { error = status.Error }, statusCode: 503);
            if (status.Value is not { ValueKind: JsonValueKind.Object } snapshot ||
                snapshot.GetProperty("campaign").ValueKind != JsonValueKind.Object)
                return Results.NotFound();
            JsonElement result;
            try { result = await SharedRpc(new { identity = NativeOwnerIdentity, clientIp, action = "create", projectId }, cancellation); }
            catch (Exception error) when (error is IOException or JsonException or OperationCanceledException)
            { return Results.Json(new { error = "The shared session was not confirmed. Refresh, then retry; creation reconciles the exact project label. " + error.Message }, statusCode: 503); }
            var sessionKey = result.GetProperty("sessionKey").GetString()!;
            var sessionId = result.GetProperty("sessionId").GetString()!;
            var creator = result.GetProperty("createdActor").GetProperty("id").GetString()!;
            if (!sessionKey.StartsWith("agent:shared-marketing:", StringComparison.Ordinal) ||
                !Guid.TryParse(sessionId, out _) || !Guid.TryParse(creator, out _) ||
                result.GetProperty("createdActor").GetProperty("type").GetString() != "human" ||
                result.GetProperty("createdActor").GetProperty("identity").GetProperty("type").GetString() != "profile" ||
                result.GetProperty("createdActor").GetProperty("identity").GetProperty("id").GetString() != creator)
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

    public async Task<IResult> AddSharedSuggestion(string projectId, JsonElement input, DeviceSession actor, Security security,
        IPAddress? clientAddress, CancellationToken cancellation)
    {
        if (!TaskIdPattern.IsMatch(projectId) || input.ValueKind != JsonValueKind.Object)
            return Results.BadRequest(new { error = "Invalid shared suggestion." });
        if (!HasCampaignAccess(projectId, actor, security)) return Results.StatusCode(403);
        if (!LegacySuggestionWritesEnabled)
            return Results.Json(new { error = "Legacy native suggestions are read-only. Use the campaign's version-linked discussion for new input." }, statusCode: 409);
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
                if (!actor.Owner && shared.CollaboratorDevice != actor.PrincipalId)
                    return Results.Json(new { error = "The owner has not approved this paired device for the native project conversation." }, statusCode: 403);
                using var previous = db.CreateCommand();
                previous.CommandText = "SELECT project_id,actor_id,content,status,suggestion_id,error FROM shared_marketing_inputs WHERE request_id=$request";
                previous.Parameters.AddWithValue("$request", requestId);
                using var reader = previous.ExecuteReader();
                if (reader.Read())
                {
                    if (reader.GetString(0) != projectId || reader.GetString(1) != actor.PrincipalId || reader.GetString(2) != content)
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
            if (!HasCampaignAccess(projectId, actor, security)) return Results.StatusCode(403);
            lock (gate)
            {
                using var db = Open();
                using var insert = db.CreateCommand();
                insert.CommandText = "INSERT OR IGNORE INTO shared_marketing_inputs" +
                    "(request_id,project_id,actor_id,actor_name,content,project_version,status,created_at,updated_at) " +
                    "VALUES($request,$project,$actor,$name,$content,$version,'pending',$time,$time)";
                insert.Parameters.AddWithValue("$request", requestId);
                insert.Parameters.AddWithValue("$project", projectId);
                insert.Parameters.AddWithValue("$actor", actor.PrincipalId);
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
            catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or InvalidOperationException or OperationCanceledException)
            {
                UpdateSharedInput(requestId, "unknown", null, null, "Gateway outcome needs reconciliation: " + error.Message);
                return Results.Json(new { error = "Gateway outcome is unknown. This request will not be resent automatically." }, statusCode: 503);
            }
            string? suggestionId = null, gatewayProfile = null;
            bool attributionValid;
            try
            {
                suggestionId = suggestion.GetProperty("id").GetString();
                gatewayProfile = suggestion.GetProperty("author").GetProperty("id").GetString();
                attributionValid = suggestion.GetProperty("sessionKey").GetString() == shared.SessionKey &&
                    suggestion.GetProperty("text").GetString() == content &&
                    suggestion.GetProperty("author").GetProperty("type").GetString() == "human" &&
                    Guid.TryParse(suggestionId, out _) && Guid.TryParse(gatewayProfile, out _) &&
                    (actor.Owner ? gatewayProfile == shared.CreatorProfile :
                        gatewayProfile != shared.CreatorProfile &&
                        (shared.CollaboratorProfile == null || gatewayProfile == shared.CollaboratorProfile));
            }
            catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                UpdateSharedInput(requestId, "unknown", suggestionId, gatewayProfile,
                    "Gateway attribution needs reconciliation: " + error.Message);
                return Results.Json(new { error = "Gateway attribution could not be verified." }, statusCode: 503);
            }
            if (!attributionValid)
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
                    bind.Parameters.AddWithValue("$device", actor.PrincipalId);
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
                actor_id = gatewayProfile, actor_name = actor.Name, content,
                version = expectedVersion, activate = RunwayLiveInferenceEnabled }, cancellation);
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

    public async Task<IResult> ReconcileSharedSuggestion(string projectId, JsonElement input,
        CancellationToken cancellation)
    {
        if (!TaskIdPattern.IsMatch(projectId) || input.ValueKind != JsonValueKind.Object)
            return Results.BadRequest(new { error = "Invalid native input receipt." });
        var requestId = RequiredString(input, "requestId", 120);
        await sharedGatewayGate.WaitAsync(cancellation);
        try
        {
            SharedRecordedInput receipt;
            lock (gate)
            {
                using var db = Open();
                var shared = FindShared(db, projectId);
                if (shared == null) return Results.NotFound();
                using var command = db.CreateCommand();
                command.CommandText = "SELECT actor_id,actor_name,content,project_version,status,suggestion_id,gateway_profile " +
                    "FROM shared_marketing_inputs WHERE project_id=$project AND request_id=$request";
                command.Parameters.AddWithValue("$project", projectId);
                command.Parameters.AddWithValue("$request", requestId);
                using var reader = command.ExecuteReader();
                if (!reader.Read()) return Results.NotFound();
                var status = reader.GetString(4);
                if (status == "recorded") return Results.Ok(new { requestId, status });
                if (status is not ("gateway_recorded" or "ledger_conflict") || reader.IsDBNull(5) || reader.IsDBNull(6))
                    return Results.Json(new { error = "No verified native receipt is available for this input; unknown Gateway outcomes remain held." }, statusCode: 409);
                receipt = new SharedRecordedInput(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetInt32(3), status, reader.GetString(5), reader.GetString(6));
                var ownerReceipt = receipt.GatewayProfile == shared.CreatorProfile;
                var collaboratorReceipt = receipt.ActorId == shared.CollaboratorDevice &&
                    receipt.GatewayProfile == shared.CollaboratorProfile;
                if (!Guid.TryParse(receipt.SuggestionId, out _) ||
                    !Guid.TryParse(receipt.GatewayProfile, out _) || !(ownerReceipt || collaboratorReceipt))
                    return Results.Json(new { error = "Stored Gateway attribution needs manual review." }, statusCode: 409);
            }
            // The verified native receipt is durable. Retrying the same ledger ID cannot
            // create a second project input, even if the first write succeeded before a crash.
            var ledger = await Runway("input", new { id = projectId,
                request_id = "native:" + receipt.SuggestionId, actor_id = receipt.GatewayProfile,
                actor_name = receipt.ActorName, content = receipt.Content,
                version = receipt.ProjectVersion, activate = RunwayLiveInferenceEnabled }, cancellation);
            if (ledger.Error != null)
            {
                UpdateSharedInput(requestId, "ledger_conflict", receipt.SuggestionId,
                    receipt.GatewayProfile, ledger.Error);
                return Results.Json(new { requestId, status = "ledger_conflict", error = ledger.Error }, statusCode: 409);
            }
            UpdateSharedInput(requestId, "recorded", receipt.SuggestionId, receipt.GatewayProfile, null);
            return Results.Ok(new { requestId, status = "recorded", suggestionId = receipt.SuggestionId });
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
