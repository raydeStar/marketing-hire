using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record WorkspaceRoleInfo(string Role, string Person, string Offer, string Disclosure, string UpdatedBy, DateTimeOffset UpdatedAt);
public record WorkspaceRoleChange(string Role, string? Person, string? Offer, string? Disclosure);

/// <summary>Whose marketing this is. An owner or an in-house marketer speaks as the company; a salesperson or an affiliate
/// has plenty on the company and little on themselves, and the employee works for them: in their voice, to their
/// prospects or audience, never as the company's official channel, and (for an affiliate) always disclosed.</summary>
public sealed class WorkspaceRole(Store store, Playbooks playbooks)
{
    private const string Key = "workspace-role-v1";
    public static readonly string[] Roles = ["owner", "marketer", "sales", "affiliate"];
    public WorkspaceRoleInfo Current() { lock (store) return store.Setting(Key) is { } json ? Wire.Unpack<WorkspaceRoleInfo>(json) : new("owner", "", "", "", "", DateTimeOffset.MinValue); }

    public WorkspaceRoleInfo Save(WorkspaceRoleChange change, string author)
    {
        var role = (change.Role ?? "").Trim().ToLowerInvariant();
        if (!Roles.Contains(role)) throw new ArgumentException("Choose owner, marketer, sales or affiliate.");
        static string Text(string? value, int limit, string field) => (value ?? "").Trim() is var text && text.Length <= limit ? text : throw new ArgumentException($"{field} can be up to {limit} characters.");
        var offer = Text(change.Offer, 300, "The link or code");
        if (offer.Length > 0 && offer.Contains("://") && !offer.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use an https link.");
        var saved = new WorkspaceRoleInfo(role, Text(change.Person, 600, "About you"), offer, Text(change.Disclosure, 200, "The disclosure"), author, DateTimeOffset.UtcNow);
        lock (store) store.Setting(Key, Wire.Pack(saved));
        return saved;
    }

    public const string DefaultDisclosure = "I may earn a commission if you buy through my link.";

    /// <summary>What every shift and chat turn reads about whose marketing this is.</summary>
    public string Guidance() => RoleGuidance() + playbooks.Guidance();

    string RoleGuidance()
    {
        var current = Current();
        var who = current.Person.Length > 0 ? $" About them: {current.Person}" : "";
        return current.Role switch
        {
            "sales" => "This workspace belongs to a salesperson for the company, not the company itself." + who +
                " Write as them, in the first person, to their prospects: personal outreach, follow-ups, posts from their own profile, call prep and battlecards that help book meetings." +
                " The brief describes the company's product; its audience, voice and goals are the salesperson's. Never pose as the company's official accounts, and never promise pricing, terms or features the company hasn't published." +
                (current.Offer.Length > 0 ? $" Their booking or referral link: {current.Offer}." : ""),
            "affiliate" => "This workspace belongs to an independent affiliate who earns a commission on the company's product, not to the company." + who +
                " Write as them to their own audience: honest reviews, fair comparisons, how-tos and recommendations." +
                $" Every public post discloses the relationship in plain words (\"{(current.Disclosure.Length > 0 ? current.Disclosure : DefaultDisclosure)}\")" +
                (current.Offer.Length > 0 ? $" and uses their link or code ({current.Offer})" : "") +
                ". Never pose as the company, invent discounts, or make claims the company's own pages don't support.",
            "marketer" => "This workspace belongs to the company's marketing lead; write as the company, in its official voice.",
            _ => "This workspace belongs to the business owner; write as the company."
        };
    }
}
