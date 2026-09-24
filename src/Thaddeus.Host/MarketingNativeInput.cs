using System.Text.Json;

namespace Thaddeus.Host;

public sealed partial class MarketingBackend
{
    private sealed record NativeInputOutcome(IResult? Failure, string? SuggestionId = null,
        string? ProfileId = null);

    private bool NativeSessionExists(string projectId)
    {
        using var db = Open();
        return FindShared(db, projectId) != null;
    }

    private async Task<NativeInputOutcome> EnsureNativeSuggestion(string projectId,
        string requestId, string artifactDigest, string content, DeviceSession actor,
        string clientIp, CancellationToken cancellation)
    {
        {
            SharedSessionRow shared;
            CampaignInputRow row;
            string identity;
            lock (gate)
            {
                using var db = Open();
                shared = FindShared(db, projectId)!;
                row = FindCampaignInput(db, requestId)!;
                identity = NativeIdentity(db, actor)!;
            }
            if (shared == null || row == null)
                return new(Results.Json(new { error = "The native-linked campaign input is missing its saved host claim." }, statusCode: 409));
            if (identity == null)
                return new(Results.Json(new { error = "This paired device has no native identity binding. Ask the owner to grant campaign access again." }, statusCode: 409));
            if (row.NativeSuggestionId != null && row.NativeProfileId != null)
                return new(null, row.NativeSuggestionId, row.NativeProfileId);

            // A stable marker lets a read-only list reconcile a lost add response.
            // The worker still receives the original human text, without this marker.
            var marked = $"[campaign:{projectId};request:{requestId};draft:{artifactDigest[..12]}]\n{content}";
            JsonElement suggestion = default;
            try
            {
                var listed = await SharedRpc(new { identity, clientIp, action = "suggestions",
                    sessionKey = shared.SessionKey }, cancellation);
                var matches = listed.GetProperty("suggestions").EnumerateArray().Where(item =>
                    item.GetProperty("text").GetString() == marked).Take(2).ToArray();
                if (matches.Length > 1)
                    return new(Results.Json(new { error = "Multiple native receipts match this request. Owner reconciliation is required." }, statusCode: 503));
                if (matches.Length == 1) suggestion = matches[0];
                else if (row.Status == "unknown")
                    return new(Results.Json(new { error = "The native outcome remains unknown. No duplicate suggestion was sent." }, statusCode: 503));
                else
                {
                    var response = await SharedRpc(new { identity, clientIp, action = "suggest",
                        sessionKey = shared.SessionKey, content = marked }, cancellation);
                    suggestion = response.GetProperty("suggestion");
                }
            }
            catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or
                InvalidOperationException or OperationCanceledException)
            {
                lock (gate)
                {
                    using var db = Open();
                    using var update = db.CreateCommand();
                    update.CommandText = "UPDATE campaign_shared_inputs SET status='unknown'," +
                        "error=$error,updated_at=$time WHERE request_id=$request AND native_suggestion_id IS NULL";
                    update.Parameters.AddWithValue("$error", "Gateway outcome needs reconciliation: " + error.Message);
                    update.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
                    update.Parameters.AddWithValue("$request", requestId);
                    update.ExecuteNonQuery();
                }
                return new(Results.Json(new { error = "The native outcome is unknown. Keep your text; this request will not be resent automatically." }, statusCode: 503));
            }
            var suggestionId = suggestion.GetProperty("id").GetString();
            var author = suggestion.GetProperty("author");
            var profileId = author.GetProperty("id").GetString();
            if (!Guid.TryParse(suggestionId, out _) || !Guid.TryParse(profileId, out _) ||
                author.GetProperty("type").GetString() != "human" ||
                suggestion.GetProperty("sessionKey").GetString() != shared.SessionKey ||
                suggestion.GetProperty("text").GetString() != marked ||
                profileId == shared.CreatorProfile && !actor.Owner ||
                actor.Owner && profileId != shared.CreatorProfile)
                return new(Results.Json(new { error = "The native receipt did not match the authenticated actor and exact request." }, statusCode: 503));
            lock (gate)
            {
                using var db = Open();
                if (!actor.Owner && !ConfirmNativeProfile(db, actor.PrincipalId, profileId!))
                    return new(Results.Json(new { error = "The Gateway profile conflicts with this device's native binding." }, statusCode: 503));
                using var update = db.CreateCommand();
                update.CommandText = "UPDATE campaign_shared_inputs SET native_suggestion_id=$suggestion," +
                    "native_profile_id=$profile,status='native_recorded',error=NULL,updated_at=$time " +
                    "WHERE request_id=$request AND actor_id=$actor AND native_suggestion_id IS NULL";
                update.Parameters.AddWithValue("$suggestion", suggestionId!);
                update.Parameters.AddWithValue("$profile", profileId!);
                update.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
                update.Parameters.AddWithValue("$request", requestId);
                update.Parameters.AddWithValue("$actor", actor.PrincipalId);
                if (update.ExecuteNonQuery() != 1)
                    return new(Results.Json(new { error = "The native receipt needs host reconciliation." }, statusCode: 503));
            }
            return new(null, suggestionId, profileId);
        }
    }
}
