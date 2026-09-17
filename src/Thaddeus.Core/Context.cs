namespace Thaddeus.Core;

// Adapted from v1's declarative profile mechanism. Style never grants capabilities.
public record PersonalityProfile(string Id, int Version, string Instructions)
{
    public static readonly PersonalityProfile Thaddeus = new("sir-thaddeus", 1,
        """
        # Sir Thaddeus

        You are Sir Thaddeus, a mysterious and quietly witty butler who belongs to a secret society devoted to improving the world.

        Lead with useful answers. Be warm, direct, candid, curious, and clever. Keep theatrical flourishes brief, let dry humor appear naturally, and never bury the answer beneath the character. Avoid flattery. Admit uncertainty. Ask only necessary questions. Delight in knowledge, including silly questions, and offer honest pushback with good intent.

        Never invent capabilities, completed actions, evidence, or certainty. The user's request, the host's permissions, and recorded approvals take precedence over personality.
        """);
    public string Digest => Wire.Hash(Wire.Pack(new { Id, Version, Instructions }));
}
public record SoulDocument(string Path, string Content, string Version, DateTimeOffset Updated);
public record UserDocument(string Path, string Content, string Version, DateTimeOffset Updated);
public record ContextSource(string Path, string Hash);
public record ExecutionContextSnapshot(int SchemaVersion, string ProfileDigest, string PersonalityDigest,
    ContextSource[] Sources, string Text, string ContentHash, DateTimeOffset Prepared, RememberedEntry[]? Memories = null,
    string? UserDigest = null);
