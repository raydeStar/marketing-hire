using System.Text.Json;

namespace Thaddeus.Host;

/// <summary>What one tap does with an approved draft: schedule it on the connected channel at the suggested time, save it as a
/// draft in a drafts-only service, or hand it to the owner to post (the network's composer, with the text copied).</summary>
public record OneTapRoute(string Action, string Label, string? ConnectionId, DateTimeOffset? At, string? Why);
public record OneTapRequest(string RequestId, string Digest);

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
