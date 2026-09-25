using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

/// <summary>Workspace roles the owner assigns to teammates. Each role includes the ones before it.
/// Approvals, assignments, grants, team access and backups always stay with the owner, so every
/// decision in the audit record remains an owner receipt.</summary>
public enum MemberRole { Viewer, Reviewer, Contributor, Manager }

public enum Capability
{
    SeeSharedCampaigns, CommentOnCampaigns,
    ReadWorkspace, WorkOnTasks, EditWiki, EditAssets,
    ChatWithEmployee, EditBrief, EditTeamFiles, PublishPages
}

public record MemberRoleEntry(string PrincipalId, string Role, string SetBy, DateTimeOffset SetAt);
public record MemberRoleChange(string Role);

public sealed class MemberRoles(Store store)
{
    private const string Key = "member-roles-v1";
    private MemberRoleEntry[] Read() => store.Setting(Key) is { } json ? Wire.Unpack<MemberRoleEntry[]>(json) : [];

    public static string Name(MemberRole role) => role.ToString().ToLowerInvariant();
    public static MemberRole? Parse(string? value) =>
        Enum.TryParse<MemberRole>(value, true, out var role) && Enum.IsDefined(role) && !int.TryParse(value, out _) ? role : null;

    public MemberRoleEntry[] List() { lock (store) return Read(); }
    public MemberRole? Explicit(string principalId) { lock (store) return Parse(Read().FirstOrDefault(entry => entry.PrincipalId == principalId)?.Role); }

    public MemberRoleEntry Set(string principalId, string role, string author)
    {
        if (Parse(role) is not { } parsed) throw new ArgumentException("Choose viewer, reviewer, contributor or manager.");
        if (string.IsNullOrWhiteSpace(principalId) || principalId.Length > 120) throw new ArgumentException("Choose a teammate.");
        lock (store)
        {
            var entry = new MemberRoleEntry(principalId, Name(parsed), author, DateTimeOffset.UtcNow);
            store.Setting(Key, Wire.Pack(Read().Where(item => item.PrincipalId != principalId).Append(entry).ToArray()));
            return entry;
        }
    }

    public static bool Grants(MemberRole role, Capability capability) => capability switch
    {
        Capability.SeeSharedCampaigns => true,
        Capability.CommentOnCampaigns => role >= MemberRole.Reviewer,
        Capability.ReadWorkspace or Capability.WorkOnTasks or Capability.EditWiki or Capability.EditAssets => role >= MemberRole.Contributor,
        Capability.ChatWithEmployee or Capability.EditBrief or Capability.EditTeamFiles or Capability.PublishPages => role >= MemberRole.Manager,
        _ => false
    };

    /// <summary>The routes a role-assigned teammate may reach at all. Endpoints then check capabilities.</summary>
    public static bool Reaches(MemberRole role, string method, string path)
    {
        var read = method is "GET" or "HEAD";
        if (path is "/api/session" or "/api/auth/logout" or "/api/auth/customer" or "/api/auth/customer/login" or "/api/marketing/state" or "/api/organization") return true;
        if (path.StartsWith("/api/marketing/campaigns/", StringComparison.Ordinal))
            return read || !path.EndsWith("/inputs", StringComparison.Ordinal) || Grants(role, Capability.CommentOnCampaigns);
        if (path.StartsWith("/api/marketing/runway/", StringComparison.Ordinal) && path.EndsWith("/shared", StringComparison.Ordinal)) return read;
        if (path.StartsWith("/api/marketing/runway/", StringComparison.Ordinal) && path.EndsWith("/shared/suggestions", StringComparison.Ordinal))
            return Grants(role, Capability.CommentOnCampaigns);
        if (!Grants(role, Capability.ReadWorkspace)) return path == "/api/state" && read;
        return path == "/api/state" && read
            || path.StartsWith("/api/marketing/tasks", StringComparison.Ordinal)
            || path is "/api/marketing/history" or "/api/marketing/chat" or "/api/marketing/profile"
            || path.StartsWith("/api/company-wiki", StringComparison.Ordinal)
            || path.StartsWith("/api/workspace-library", StringComparison.Ordinal)
            || path.StartsWith("/api/scorecard", StringComparison.Ordinal)
            || path.StartsWith("/api/objectives", StringComparison.Ordinal)
            || path == "/api/feedback"
            || path == "/api/listening" && read
            || path == "/api/shifts" && read
            || path.StartsWith("/api/organization/agents/", StringComparison.Ordinal)
            || path.StartsWith("/api/artifacts/", StringComparison.Ordinal)
            || path.StartsWith("/api/uploads", StringComparison.Ordinal)
            || path.StartsWith("/api/published-pages", StringComparison.Ordinal);
    }
}

public static class Access
{
    public static DeviceSession? Session(HttpContext context) => context.Items["session"] as DeviceSession;
    public static MemberRole? Role(HttpContext context) => context.Items["role"] as MemberRole?;
    /// <summary>The owner can do everything; a teammate can do what their role grants.</summary>
    public static bool Can(HttpContext context, Capability capability) =>
        Session(context) is { } session && (session.Owner || Role(context) is { } role && MemberRoles.Grants(role, capability));
    public static string Actor(HttpContext context) =>
        Session(context) is { } session ? (session.Owner ? "Owner " : "Member ") + session.PrincipalId : "Unknown";
}
