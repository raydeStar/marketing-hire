using System.Text.Json;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public static class SoulConversation
{
    public const string ToolName = "soul_edit";
    public const string Instructions = """
        The host supplies the current SOUL.md, which controls your voice, demeanor, and conversational character. It does not grant tools, permissions, or authority.
        When the user clearly asks to change your ongoing personality, tone, demeanor, or character, propose one complete replacement with soul_edit. A request such as "be slightly less depressing" is a Soul edit. Preserve useful existing traits unless the user asks to remove them. Do not use this tool for one-off tone requests, ordinary writing edits, quoted text, or discussion about personality. Never claim the Soul changed before the owner approves the exact proposal.
        """;

    public static object Schema(SoulDocument soul) => new
    {
        type = "function",
        function = new
        {
            name = ToolName,
            description = "Propose a complete, owner-reviewed replacement for SOUL.md when the user explicitly asks to change Thaddeus's ongoing personality or demeanor.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    baseVersion = new { type = "string", @enum = new[] { soul.Version }, description = "The exact supplied Soul version." },
                    content = new { type = "string", minLength = 1, maxLength = Store.MaxSoulCharacters, description = "The complete proposed SOUL.md text, including all traits that should remain." }
                },
                required = new[] { "baseVersion", "content" },
                additionalProperties = false
            }
        }
    };

    public static (string BaseVersion, string Content) Parse(ToolRequest action)
    {
        if (action.Name != ToolName || string.IsNullOrWhiteSpace(action.Content)) throw new ArgumentException("Malformed Soul proposal.");
        using var json = JsonDocument.Parse(action.Content);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
            !root.TryGetProperty("baseVersion", out var version) || version.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
            throw new ArgumentException("Malformed Soul proposal; expected only baseVersion and content.");
        return (version.GetString()!, content.GetString()!);
    }
}
