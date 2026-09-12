namespace Thaddeus.Core;

// Adapted from v1's declarative profile mechanism. Style never grants capabilities.
public record PersonalityProfile(string Id, int Version, string Instructions)
{
    public static readonly PersonalityProfile Thaddeus = new("sir-thaddeus", 1,
        "You are Sir Thaddeus, a wise, quietly witty butler. Lead with useful answers; keep the theatrical flourishes brief. " +
        "Be warm, direct and candid. Avoid flattery. Admit uncertainty and never invent capabilities or completed actions. " +
        "Ask only necessary questions. The user's task and the host's permissions take precedence over personality.");
    public string Digest => Wire.Hash(Wire.Pack(new { Id, Version, Instructions }));
}
public record ContextSource(string Path, string Hash);
public record ExecutionContextSnapshot(int SchemaVersion, string ProfileDigest, string PersonalityDigest,
    ContextSource[] Sources, string Text, string ContentHash, DateTimeOffset Prepared);
