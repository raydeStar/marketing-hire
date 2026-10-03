using System.Text.Json;
using System.Text.RegularExpressions;

namespace Thaddeus.Host;

/// <summary>What one tap does with an approved draft: schedule it on the connected channel at the suggested time, save it as a
/// draft in a drafts-only service, or hand it to the owner to post (the network's composer, with the text copied).</summary>
public record OneTapRoute(string Action, string Label, string? ConnectionId, DateTimeOffset? At, string? Why);
public record OneTapRequest(string RequestId, string Digest);
/// <summary>A link that opens <paramref name="Network"/>'s composer with a post filled in (under the post it answers, for a reply), or
/// an <paramref name="Email"/> ready to send. Not <paramref name="Keeps"/>: the app can open without the words (LinkedIn's, Gmail's),
/// so the owner needs them to copy as well.</summary>
public record ComposeTap(string Network, string Link, bool Reply, bool Keeps = true, bool Email = false)
{
    /// <summary>What the owner does with it: "post" or "send".</summary>
    public string Verb => Email ? "send" : "post";
    /// <summary>"on X", "from Gmail".</summary>
    public string Where => (Email ? "from " : "on ") + Network;
    /// <summary>The line above the link in a text.</summary>
    public string Prompt => $"Tap to {Verb} it {Where}; it opens with the {(Email ? "email" : "words")} filled in:";
    /// <summary>What the link does, to finish "I'll text you a link that …".</summary>
    public string Opens => Email ? $"opens it in {Network}, ready to send" : $"opens {(Reply ? "your reply" : "it")} on {Network} with the words filled in";
}

public sealed partial class Publishing
{
    /// <summary>The route for a draft: a reply goes under its post by hand; a connected channel is scheduled at its suggested time;
    /// a drafts-only service saves it there; anything else is copied for the owner to post.</summary>
    public OneTapRoute Route(string channel, string destination)
    {
        if (IsReply(destination)) return new("copy", "Approve & reply yourself", null, null, "A reply has to be posted from the post it answers.");
        var connection = Ledger().Connections.FirstOrDefault(item => item.Status == "ready" && Serves(item.Kind, channel));
        if (connection == null) return new("copy", "Approve & copy", null, null, $"No {channel} channel is connected, so it opens {channel}'s composer with the text copied.");
        if (DraftsOnly(connection.Kind)) return new("draft", $"Approve & save to {Kinds[connection.Kind].Name}", connection.Id, null, "It is saved there as a draft; you send it from there.");
        var (at, why) = SuggestedTime(channel);
        return new("schedule", "Approve & schedule", connection.Id, at, why);
    }

    /// <summary>The longest compose link a text carries, with room for the line before it: one cut off at the text's limit would
    /// open a broken post.</summary>
    public const int MaxComposeLink = 1450;

    /// <summary>A link that opens the network's own composer with the post filled in, for an owner who posts it themselves: one tap,
    /// then Post. X, Bluesky, Threads, LinkedIn and Mastodon (on the owner's own server) take the words, and X a reply's too; Gmail
    /// takes an email's To, Subject and body. LinkedIn and Gmail may open without them. Facebook and Instagram have no such link (the
    /// cockpit copies the text for them), so they get none, nor does a reply elsewhere, Mastodon without a known server, an email
    /// not bound for Gmail, or a link too long to text.</summary>
    public static ComposeTap? Compose(string channel, string destination, string content, string? mastodonServer = null)
    {
        var text = EmployeeShifts.WithoutImageLine(content).Trim();
        var words = "text=" + Uri.EscapeDataString(text);
        var reply = IsReply(destination);
        var kind = KindOf(channel);
        ComposeTap? tap = kind switch
        {
            "x" when Regex.Match(destination.Trim(), @"^https://(?:www\.)?(?:x|twitter)\.com/[^/?#]+/status/(\d+)") is { Success: true } post
                => new("X", $"https://x.com/intent/post?in_reply_to={post.Groups[1].Value}&{words}", true),
            "x" when !reply => new("X", "https://x.com/intent/post?" + words, false),
            "bluesky" when !reply => new("Bluesky", "https://bsky.app/intent/compose?" + words, false),
            "threads" when !reply => new("Threads", "https://www.threads.com/intent/post?" + words, false),
            "linkedin" when !reply => new("LinkedIn", "https://www.linkedin.com/feed/?shareActive=true&" + words, false, Keeps: false),
            "mastodon" when !reply && mastodonServer != null => new("Mastodon", mastodonServer + "/share?" + words, false),
            "email" when Regex.IsMatch(destination.Trim(), @"^https://mail\.google\.com(/|$)") => new("Gmail", GmailCompose(text), false, Keeps: false, Email: true),
            _ => null,
        };
        return tap is { Link.Length: <= MaxComposeLink } ? tap : null;
    }

    /// <summary>A draft's own compose link, with the owner's Mastodon server where one is known.</summary>
    public ComposeTap? ComposeFor(string channel, string destination, string content) => Compose(channel, destination, content, MastodonServer(destination));

    /// <summary>The owner's Mastodon server: from a Mastodon account they connected (even one that has since stopped working), else from a
    /// draft addressed to their profile (https://server/@name). Mastodon's share page only works on the owner's own server.</summary>
    string? MastodonServer(string destination)
    {
        var known = Ledger().Connections.LastOrDefault(item => item.Kind == "mastodon" && item.Address is { Length: > 0 })?.Address
            ?? (Regex.Match(destination.Trim(), @"^(https://[^/@\s?#]+)/@[\w.-]+/?$") is { Success: true } profile ? profile.Groups[1].Value : null);
        return Uri.TryCreate(known, UriKind.Absolute, out var server) && server.Scheme == Uri.UriSchemeHttps ? server.GetLeftPart(UriPartial.Authority) : null;
    }

    /// <summary>Gmail's compose page with the email's To, Subject and body: a draft opens with its "Subject:" and "To:" lines,
    /// or (as the cockpit reads it) its first line is the subject.</summary>
    static string GmailCompose(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        string subject = "", to = "";
        while (lines.Count > 0 && Regex.IsMatch(lines[0], @"^(subject|to|cc)\s*:", RegexOptions.IgnoreCase))
        {
            var colon = lines[0].IndexOf(':');
            var (key, value) = (lines[0][..colon].Trim().ToLowerInvariant(), lines[0][(colon + 1)..].Trim());
            if (key == "subject") subject = value; else if (key == "to") to = value;
            lines.RemoveAt(0);
        }
        if (subject.Length == 0)
        {
            while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
            if (lines.Count > 0) { subject = Regex.Replace(lines[0], @"^#+\s*", "").Trim(); lines.RemoveAt(0); }
        }
        return "https://mail.google.com/mail/?view=cm&fs=1" + (to.Length > 0 ? "&to=" + Uri.EscapeDataString(to) : "") +
            "&su=" + Uri.EscapeDataString(subject) + "&body=" + Uri.EscapeDataString(string.Join('\n', lines).Trim());
    }

    public async Task<OneTapRoute> RouteFor(int draftId, CancellationToken cancellation)
    {
        var draft = await Draft(draftId, cancellation) ?? throw new KeyNotFoundException("Draft not found.");
        return Route(Str(draft, "channel"), Str(draft, "destination"));
    }

    /// <summary>After approval, the one tap's second half: the same checks as publishing by hand (approved, unchanged, launch QA, the
    /// channel's limit), then scheduled, saved as a draft, or recorded for the owner to post.</summary>
    public async Task<Publication> OneTap(int draftId, OneTapRequest request, string by, CancellationToken cancellation)
    {
        var draft = await Draft(draftId, cancellation) ?? throw new KeyNotFoundException("Draft not found.");
        if (Str(draft, "status") != "approved") throw new InvalidOperationException("Approve the draft first.");
        var route = Route(Str(draft, "channel"), Str(draft, "destination"));
        return route.Action switch
        {
            "schedule" => await Publish(draftId, new DraftPublishRequest(request.RequestId, route.ConnectionId!, request.Digest, route.At), by, cancellation),
            "draft" => await Publish(draftId, new DraftPublishRequest(request.RequestId, route.ConnectionId!, request.Digest, null), by, cancellation),
            _ => await Assist(draftId, new AssistRequest(request.RequestId, request.Digest, null), by, cancellation)
        };
    }
}
