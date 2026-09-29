using System.Text.Json;
using System.Text.RegularExpressions;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

// The portal carries the invitation; this host remains the authority on its powers.
internal static class CompanionTeam
{
    private sealed record Grant(string Invitation, string Subject, string Name, string Role);
    private sealed record Receipt(string Invitation, string Subject, string Principal, string Role, string Inviter);
    private const string ReceiptsKey = "companion-invitation-grants-v1";

    internal static async Task<bool> Handle(HttpContext context, CompanionIngress? ingress, Security security, MemberRoles roles, Store store)
    {
        var path = context.Request.Path.Value;
        if (path is not ("/api/companion/team" or "/api/companion/team/grant")) return false;
        IResult result;
        var identity = ingress?.Actor(context);
        if (identity == null) result = Results.StatusCode(403);
        else
        {
            Grant? grant = null;
            if (path.EndsWith("/grant", StringComparison.Ordinal) && HttpMethods.IsPost(context.Request.Method))
            {
                try { grant = await context.Request.ReadFromJsonAsync<Grant>(context.RequestAborted); }
                catch (JsonException) { }
            }
            lock (store)
            {
                var owner = ingress!.IsOwner(identity);
                var account = security.CompanionAccount(identity.Subject);
                var rank = owner ? MemberRole.Manager : account is { Revoked: false, Owner: false }
                    ? roles.Explicit(account.Id) ?? MemberRole.Reviewer : (MemberRole?)null;
                if (rank is null || rank == MemberRole.Viewer) result = Results.StatusCode(403);
                else if (path == "/api/companion/team" && HttpMethods.IsGet(context.Request.Method))
                    result = Results.Ok(new { role = owner ? "owner" : MemberRoles.Name(rank.Value),
                        roles = Enum.GetValues<MemberRole>().Where(r => r <= rank).Select(MemberRoles.Name) });
                else if (grant == null || grant.Invitation == null || grant.Subject == null || !Regex.IsMatch(grant.Invitation, "^[a-f0-9]{32}$") ||
                    !Regex.IsMatch(grant.Subject, "^[A-Za-z0-9_-]{3,128}$") ||
                    string.IsNullOrWhiteSpace(grant.Name) || grant.Name.Length > 60 || MemberRoles.Parse(grant.Role) is not { } chosen)
                    result = Results.BadRequest(new { error = "Choose a valid teammate and access level." });
                else if (chosen > rank) result = Results.Json(new { error = "You cannot invite someone above your own access level." }, statusCode: 403);
                else
                {
                    var receipts = Wire.Unpack<Receipt[]>(store.Setting(ReceiptsKey) ?? "[]");
                    var prior = receipts.FirstOrDefault(r => r.Invitation == grant.Invitation);
                    var existing = security.CompanionAccount(grant.Subject);
                    if (prior != null)
                    {
                        // Retrying a lost reply must never undo a later demotion or revocation.
                        result = prior.Subject == grant.Subject && prior.Role == grant.Role && prior.Inviter == identity.Subject &&
                            security.Account(prior.Principal) is { Owner: false } && roles.Explicit(prior.Principal) == chosen
                            ? Results.Ok(new { role = prior.Role }) : Results.Conflict(new { error = "This invitation's access changed. Ask for a new invitation." });
                    }
                    else if (ingress.IsOwner(identity with { Subject = grant.Subject }) ||
                        existing != null && (existing.Owner || existing.Revoked || (roles.Explicit(existing.Id) ?? MemberRole.Reviewer) != chosen))
                        result = Results.Conflict(new { error = "This person already has an account here. The owner can change their existing access." });
                    else
                    {
                        // Rejoining at the same role is safe; an invitation never
                        // overrides the owner's existing role or account revocation.
                        var member = existing ?? security.AddCompanionMember(grant.Subject, grant.Name.Trim());
                        roles.Set(member.Id, grant.Role, "Invitation by " + identity.Subject);
                        store.Setting(ReceiptsKey, Wire.Pack(receipts.Append(new Receipt(grant.Invitation, grant.Subject, member.Id, grant.Role, identity.Subject))));
                        result = Results.Ok(new { role = grant.Role });
                    }
                }
            }
        }
        await result.ExecuteAsync(context);
        return true;
    }
}
