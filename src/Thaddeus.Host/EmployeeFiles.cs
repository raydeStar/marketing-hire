using System.Text.RegularExpressions;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record EmployeeFileRevision(string AgentId, string Name, int Version, string Content, string Digest,
    string Author, bool Deleted, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public record EmployeeFileChange(string RequestId, string Name, int Version, string Content, bool Deleted = false);
public record EmployeeFileReceipt(string RequestId, string Digest, string AgentId, string Name, int Version);
public record EmployeeFileLedger(EmployeeFileRevision[] Revisions, EmployeeFileReceipt[] Receipts);

/// <summary>Owner-authored Markdown files for each team member, such as AGENTS.md or SOUL.md.
/// The host keeps every revision; delivering them into an agent runtime is a separate, explicit step.</summary>
public sealed partial class EmployeeFiles(Store store, OrganizationDirectory directory)
{
    private const string Key = "employee-files-v1";
    public const int MaxFiles = 16, MaxCharacters = 32000;
    [GeneratedRegex("\\A[A-Za-z0-9][A-Za-z0-9_.-]{0,59}\\.md\\z")] private static partial Regex FileName();
    private EmployeeFileLedger Read() => store.Setting(Key) is { } json ? Wire.Unpack<EmployeeFileLedger>(json) : new([], []);
    private void Write(EmployeeFileLedger value) => store.Setting(Key, Wire.Pack(value));
    private static IEnumerable<EmployeeFileRevision> Latest(EmployeeFileLedger ledger, string agentId) =>
        ledger.Revisions.Where(file => file.AgentId == agentId).GroupBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.MaxBy(file => file.Version)!);

    private void RequireMember(string agentId)
    {
        if (!directory.Read().Agents.Any(agent => agent.Id == agentId)) throw new ArgumentException("That team member does not exist.");
    }

    public EmployeeFileRevision[] List(string agentId)
    {
        RequireMember(agentId);
        lock (store) return Latest(Read(), agentId).Where(file => !file.Deleted).OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public EmployeeFileRevision[] History(string agentId, string name)
    {
        RequireMember(agentId);
        lock (store) return Read().Revisions.Where(file => file.AgentId == agentId && string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.Version).ToArray();
    }

    public EmployeeFileRevision Save(string agentId, EmployeeFileChange change, string author)
    {
        RequireMember(agentId);
        lock (store)
        {
            if (string.IsNullOrWhiteSpace(change.RequestId) || change.RequestId.Length > 120) throw new ArgumentException("A request ID is required.");
            var ledger = Read(); var digest = Wire.Hash(Wire.Pack(new { agentId, change }));
            if (ledger.Receipts.FirstOrDefault(receipt => receipt.RequestId == change.RequestId) is { } replay)
            {
                if (replay.Digest != digest) throw new ArgumentException("That request ID belongs to another file change.");
                return ledger.Revisions.Single(file => file.AgentId == replay.AgentId && file.Name == replay.Name && file.Version == replay.Version);
            }
            if (change.Name == null || !FileName().IsMatch(change.Name)) throw new ArgumentException("Use a Markdown file name such as AGENTS.md, with letters, numbers, dots, dashes or underscores.");
            if (change.Content == null || change.Content.Length > MaxCharacters || change.Content.Contains('\0'))
                throw new ArgumentException($"A file can hold up to {MaxCharacters:N0} characters of text.");
            var latest = Latest(ledger, agentId).ToArray();
            var previous = latest.FirstOrDefault(file => string.Equals(file.Name, change.Name, StringComparison.OrdinalIgnoreCase));
            var current = previous is { Deleted: false } ? previous : null;
            if (change.Version != (current?.Version ?? 0)) throw new InvalidOperationException("This file changed. Refresh before saving.");
            if (change.Deleted && current == null) throw new ArgumentException("That file does not exist.");
            if (current == null && latest.Count(file => !file.Deleted) >= MaxFiles) throw new InvalidOperationException($"Each team member can have up to {MaxFiles} files.");
            var now = DateTimeOffset.UtcNow;
            var name = previous?.Name ?? change.Name;
            var content = change.Deleted ? current!.Content : change.Content.Replace("\r\n", "\n");
            var file = new EmployeeFileRevision(agentId, name, (previous?.Version ?? 0) + 1, content, Wire.Hash(content), author,
                change.Deleted, current?.CreatedAt ?? now, now);
            Write(ledger with { Revisions = [.. ledger.Revisions, file],
                Receipts = [.. ledger.Receipts.TakeLast(255), new(change.RequestId, digest, agentId, file.Name, file.Version)] });
            return file;
        }
    }
}
