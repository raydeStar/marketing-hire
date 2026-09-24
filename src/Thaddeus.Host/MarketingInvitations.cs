using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Thaddeus.Core;

namespace Thaddeus.Host;

public sealed partial class MarketingBackend
{
    private const string InvitationScope = "Review this campaign's shared draft, comment and request changes. Employee execution still needs owner approval.";
    private sealed record CampaignInvitation(string Id, string ProjectId, string CampaignName, string Issuer,
        string SubjectPrefix, string Provider, string Email, string InvitedBy, string CreatedAt,
        string ExpiresAt, string? RevokedAt, string? AcceptedAt, string? AcceptedBy,
        string? TargetAccountId, string? TargetSubject, string? TargetName);

    private static CampaignInvitation ReadInvitation(SqliteDataReader row) => new(row.GetString(0),
        row.GetString(1), row.GetString(2), row.GetString(3), row.GetString(4), row.GetString(5),
        row.GetString(6), row.GetString(7), row.GetString(8), row.GetString(9),
        row.IsDBNull(10) ? null : row.GetString(10), row.IsDBNull(11) ? null : row.GetString(11),
        row.IsDBNull(12) ? null : row.GetString(12), row.IsDBNull(13) ? null : row.GetString(13),
        row.IsDBNull(14) ? null : row.GetString(14), row.IsDBNull(15) ? null : row.GetString(15));
    private const string InvitationColumns = "id,project_id,campaign_name,issuer,subject_prefix,provider,email,invited_by,created_at,expires_at,revoked_at,accepted_at,accepted_by,target_account_id,target_subject,target_name";

    private static string? InvitationProvider(CustomerAccount account, CustomerLoginSettings settings) =>
        account.Issuer != settings.Authority ? null :
        account.Subject.StartsWith(settings.GoogleSubjectPrefix, StringComparison.Ordinal) && settings.Providers.Contains("google") ? "google" :
        account.Subject.StartsWith(settings.MicrosoftSubjectPrefix, StringComparison.Ordinal) && settings.Providers.Contains("microsoft") ? "microsoft" : null;

    public IResult InvitationAccounts(DeviceSession owner, Security security, CustomerLoginSettings? settings)
    {
        if (!owner.Owner) return Results.StatusCode(403);
        if (settings == null) return Results.Conflict(new { error = "Customer sign-in is not configured." });
        return Results.Ok(new { accounts = security.KnownReviewerAccounts()
            .Select(account => new { account.Id, account.Name, account.Email, account.EmailVerified,
                provider = InvitationProvider(account, settings) }).Where(account => account.provider != null).ToArray() });
    }

    public IResult CampaignInvitations(string projectId, DeviceSession owner)
    {
        if (!owner.Owner) return Results.StatusCode(403);
        if (!TaskIdPattern.IsMatch(projectId)) return Results.BadRequest();
        using var db = Open(); using var command = db.CreateCommand();
        command.CommandText = $"SELECT {InvitationColumns} FROM campaign_invitations WHERE project_id=$project ORDER BY created_at DESC LIMIT 100";
        command.Parameters.AddWithValue("$project", projectId);
        using var reader = command.ExecuteReader();
        var items = new List<object>();
        while (reader.Read())
        {
            var item = ReadInvitation(reader);
            var status = item.AcceptedAt != null ? "accepted" : item.RevokedAt != null ? "revoked" :
                DateTimeOffset.Parse(item.ExpiresAt) <= DateTimeOffset.UtcNow ? "expired" : "pending";
            items.Add(new { item.Id, item.Email, item.Provider, item.ExpiresAt, item.AcceptedBy, item.TargetAccountId,
                recipient = item.TargetName ?? item.Email, targetKind = item.TargetAccountId == null ? "email" : "account", status });
        }
        return Results.Ok(new { invitations = items, scope = InvitationScope });
    }

    public async Task<IResult> CreateCampaignInvitation(string projectId, JsonElement input,
        DeviceSession owner, CustomerLoginSettings? settings, Security security, CancellationToken cancellation)
    {
        if (!owner.Owner) return Results.StatusCode(403);
        if (settings == null) return Results.Conflict(new { error = "Configure customer sign-in before inviting a person." });
        if (!TaskIdPattern.IsMatch(projectId)) return Results.BadRequest();
        CustomerAccount? target = null;
        string email; string provider;
        if (input.TryGetProperty("accountId", out var selected))
        {
            if (selected.ValueKind != JsonValueKind.String || !TaskIdPattern.IsMatch(selected.GetString()!) ||
                (target = security.Account(selected.GetString()!)) == null || target.Owner ||
                InvitationProvider(target, settings) is not { } availableProvider)
                return Results.BadRequest(new { error = "Choose a known reviewer account from an available sign-in provider." });
            provider = availableProvider; email = target.Email ?? "";
        }
        else
        {
            email = RequiredString(input, "email", 320).Trim().ToLowerInvariant();
            provider = RequiredString(input, "provider", 16);
            if (!MailAddress.TryCreate(email, out var address) || address.Address != email || !settings.Providers.Contains(provider))
                return Results.BadRequest(new { error = "Choose an available sign-in provider and a full email address." });
        }
        if (!input.TryGetProperty("expiresInHours", out var hoursValue) ||
            !hoursValue.TryGetInt32(out var hours) || hours < 1 || hours > 168)
            return Results.BadRequest(new { error = "Choose an available sign-in provider, a full email address and an expiry from 1 to 168 hours." });
        var inspected = await Runway("inspect", new { id = projectId }, cancellation);
        if (inspected.Error == "Project not found") return Results.NotFound();
        if (inspected.Error != null) return Results.Json(new { error = "The campaign could not be checked. Try again when its ledger is available." }, statusCode: 503);
        if (inspected.Value is not { ValueKind: JsonValueKind.Object } snapshot ||
            !snapshot.TryGetProperty("campaign", out var campaign) || campaign.ValueKind != JsonValueKind.Object)
            return Results.Conflict(new { error = "Save a campaign before inviting someone to review it." });
        var name = snapshot.GetProperty("project").GetProperty("goal").GetString() ?? "Shared campaign";
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow; var expires = now.AddHours(hours).ToString("O");
        lock (gate)
        {
            using var db = Open(); using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO campaign_invitations(id,token_hash,project_id,campaign_name,issuer,subject_prefix,provider,email,invited_by,created_at,expires_at,target_account_id,target_subject,target_name) " +
                "VALUES($id,$hash,$project,$name,$issuer,$prefix,$provider,$email,$owner,$now,$expires,$account,$subject,$recipient)";
            command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$hash", Wire.Hash(token));
            command.Parameters.AddWithValue("$project", projectId); command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$issuer", settings.Authority);
            command.Parameters.AddWithValue("$prefix", provider == "google" ? settings.GoogleSubjectPrefix : settings.MicrosoftSubjectPrefix);
            command.Parameters.AddWithValue("$provider", provider); command.Parameters.AddWithValue("$email", email);
            command.Parameters.AddWithValue("$owner", owner.PrincipalId); command.Parameters.AddWithValue("$now", now.ToString("O"));
            command.Parameters.AddWithValue("$account", (object?)target?.Id ?? DBNull.Value);
            command.Parameters.AddWithValue("$subject", (object?)target?.Subject ?? DBNull.Value);
            command.Parameters.AddWithValue("$recipient", (object?)target?.Name ?? DBNull.Value);
            command.Parameters.AddWithValue("$expires", expires); command.ExecuteNonQuery();
        }
        // The invitation travels in the fragment, never in HTTP request paths or Referer headers.
        return Results.Ok(new { id, url = settings.Origin + "/#invite=" + token, email, provider, expiresAt = expires,
            recipient = target?.Name ?? email, targetKind = target == null ? "email" : "account", targetAccountId = target?.Id, scope = InvitationScope });
    }

    public IResult RevokeCampaignInvitation(string projectId, string id, DeviceSession owner)
    {
        if (!owner.Owner) return Results.StatusCode(403);
        if (!TaskIdPattern.IsMatch(projectId) || !TaskIdPattern.IsMatch(id)) return Results.BadRequest();
        lock (gate)
        {
            using var db = Open(); using var command = db.CreateCommand();
            command.CommandText = "UPDATE campaign_invitations SET revoked_at=COALESCE(revoked_at,$now) " +
                "WHERE id=$id AND project_id=$project AND accepted_at IS NULL";
            command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$project", projectId);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            return command.ExecuteNonQuery() == 1 ? Results.Ok() : Results.Conflict(new { error = "This invitation is unavailable or already accepted. Remove an existing member through campaign access." });
        }
    }

    public async Task<IResult> UseCampaignInvitation(JsonElement input, DeviceSession actor, Security security,
        bool accept, CancellationToken cancellation)
    {
        // Authenticate first, then reveal the scope only to the intended verified identity.
        if (actor.AccountId == null || security.ActiveDevice(actor.Id) == null) return Results.StatusCode(403);
        var account = security.Account(actor.AccountId);
        if (account == null || account.Owner) return InvitationUnavailable();
        var token = RequiredString(input, "token", 64);
        if (!Regex.IsMatch(token, "\\A[a-f0-9]{64}\\z")) return InvitationUnavailable();
        await sharedGatewayGate.WaitAsync(cancellation);
        try
        {
            lock (gate)
            {
                using var db = Open(); using var transaction = db.BeginTransaction();
                CampaignInvitation? item;
                using (var read = db.CreateCommand())
                {
                    read.Transaction = transaction;
                    read.CommandText = $"SELECT {InvitationColumns} FROM campaign_invitations WHERE token_hash=$hash";
                    read.Parameters.AddWithValue("$hash", Wire.Hash(token));
                    using var reader = read.ExecuteReader(); item = reader.Read() ? ReadInvitation(reader) : null;
                }
                if (item == null || item.RevokedAt != null || item.AcceptedAt != null ||
                    DateTimeOffset.Parse(item.ExpiresAt) <= DateTimeOffset.UtcNow || account.Issuer != item.Issuer ||
                    !account.Subject.StartsWith(item.SubjectPrefix, StringComparison.Ordinal) ||
                    (item.TargetAccountId != null
                        ? account.Id != item.TargetAccountId || account.Subject != item.TargetSubject
                        : !account.EmailVerified || !string.Equals(account.Email, item.Email, StringComparison.OrdinalIgnoreCase))) return InvitationUnavailable();
                if (!accept) return Results.Ok(new { projectId = item.ProjectId, campaignName = item.CampaignName,
                    item.Email, item.Provider, item.ExpiresAt, recipient = item.TargetName ?? item.Email,
                    targetKind = item.TargetAccountId == null ? "email" : "account", scope = InvitationScope });
                BindNativeDevice(db, transaction, actor.PrincipalId, item.InvitedBy);
                using var save = db.CreateCommand(); save.Transaction = transaction;
                save.CommandText = "INSERT INTO campaign_memberships(project_id,device_id,granted_by,granted_at,revoked_at) " +
                    "VALUES($project,$person,$owner,$now,NULL) ON CONFLICT(project_id,device_id) DO UPDATE SET granted_by=$owner,granted_at=$now,revoked_at=NULL; " +
                    "UPDATE campaign_invitations SET accepted_at=$now,accepted_by=$person WHERE id=$id";
                save.Parameters.AddWithValue("$project", item.ProjectId); save.Parameters.AddWithValue("$person", actor.PrincipalId);
                save.Parameters.AddWithValue("$owner", item.InvitedBy); save.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                save.Parameters.AddWithValue("$id", item.Id); save.ExecuteNonQuery(); transaction.Commit();
                return Results.Ok(new { projectId = item.ProjectId });
            }
        }
        finally { sharedGatewayGate.Release(); }
    }

    private static IResult InvitationUnavailable() => Results.Conflict(new {
        error = "This invitation is expired, revoked, already used, or belongs to a different verified sign-in account. Ask the owner for a new link if needed." });
}
