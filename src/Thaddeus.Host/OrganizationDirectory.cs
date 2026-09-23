using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record CompanyDepartment(string Id, string Name, string Purpose);
public record CompanyAgent(string Id, string Name, string Role, string? DepartmentId, string Kind, string? RuntimeKey);
public record CompanyDirectory(int Version, CompanyDepartment[] Departments, CompanyAgent[] Agents, DateTimeOffset UpdatedAt);
public record CompanyDirectoryChange(int Version, string RequestId, CompanyDepartment[] Departments, CompanyAgent[] Agents);
public record CompanyDirectoryReceipt(string RequestId, string Digest, CompanyDirectory Result);
public record CompanyDirectoryEnvelope(CompanyDirectory Directory, CompanyDirectoryReceipt[] Receipts);

/// <summary>The team directory describes ownership. Adding a seat does not provision an agent.</summary>
public sealed class OrganizationDirectory(Store store)
{
    private const string Key = "company-directory-v1";
    private static readonly Regex IdPattern = new("^[a-zA-Z0-9_-]{1,80}$", RegexOptions.Compiled);

    private CompanyDirectoryEnvelope ReadEnvelope()
    {
        if (store.Setting(Key) is { } saved) return Wire.Unpack<CompanyDirectoryEnvelope>(saved);
        var initial = new CompanyDirectory(1,
            [new("marketing", "Marketing", "Build our personal brand and market its marketing agents.")],
            [new("marketing-main", "Marketing agent", "Research, positioning, and drafts for review", "marketing", "employee", "marketing")],
            DateTimeOffset.UtcNow);
        var envelope = new CompanyDirectoryEnvelope(initial, []);
        store.Setting(Key, Wire.Pack(envelope));
        return envelope;
    }

    public CompanyDirectory Read() { lock (store) return ReadEnvelope().Directory; }

    public CompanyDirectory Update(CompanyDirectoryChange change)
    {
        lock (store)
        {
            if (string.IsNullOrWhiteSpace(change.RequestId) || change.RequestId.Length > 120)
                throw new ArgumentException("A request ID is required.");
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Wire.Pack(change))));
            var envelope = ReadEnvelope();
            if (envelope.Receipts.FirstOrDefault(item => item.RequestId == change.RequestId) is { } replay)
            {
                if (replay.Digest != digest) throw new ArgumentException("That request ID belongs to another change.");
                return replay.Result;
            }
            if (change.Version != envelope.Directory.Version)
                throw new InvalidOperationException("The team changed. Refresh the directory before saving.");
            if (change.Departments == null || change.Agents == null || change.Departments.Length > 50 || change.Agents.Length > 100)
                throw new ArgumentException("The directory supports up to 50 departments and 100 agents.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var department in change.Departments)
            {
                if (department == null || !ValidId(department.Id) || !ids.Add(department.Id) || !Text(department.Name, 80) || !Text(department.Purpose, 500, true))
                    throw new ArgumentException("Departments need unique IDs, a name, and a short purpose.");
            }
            var agentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var agent in change.Agents)
            {
                if (agent == null || !ValidId(agent.Id) || !agentIds.Add(agent.Id) || !Text(agent.Name, 80) || !Text(agent.Role, 500, true) ||
                    agent.Kind is not ("employee" or "manager") || (agent.DepartmentId != null && !ids.Contains(agent.DepartmentId)))
                    throw new ArgumentException("Agents need unique IDs, a name, and an existing department or company role.");
                if (agent.RuntimeKey != null && (agent.RuntimeKey != "marketing" || agent.Id != "marketing-main"))
                    throw new ArgumentException("New agent entries need a separate runtime connection before they can work.");
            }
            if (!change.Agents.Any(agent => agent.Id == "marketing-main" && agent.RuntimeKey == "marketing"))
                throw new ArgumentException("The connected marketing employee must remain in the directory.");
            var next = new CompanyDirectory(envelope.Directory.Version + 1, change.Departments, change.Agents, DateTimeOffset.UtcNow);
            // One stored envelope commits the directory and its retry receipt together. The butler dislikes duplicates.
            store.Setting(Key, Wire.Pack(new CompanyDirectoryEnvelope(next,
                [.. envelope.Receipts.TakeLast(63), new(change.RequestId, digest, next)])));
            return next;
        }
    }

    private static bool ValidId(string? value) => value != null && IdPattern.IsMatch(value);
    private static bool Text(string? value, int max, bool empty = false) => value != null && value.Length <= max && (empty || !string.IsNullOrWhiteSpace(value));
}
