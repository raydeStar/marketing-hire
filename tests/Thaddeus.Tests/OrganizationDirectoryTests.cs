using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class OrganizationDirectoryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "company-directory-" + Guid.NewGuid().ToString("N"));
    public void Dispose() => Directory.Delete(root, true);

    [Fact] public void TeamPersistsAndRetriesDoNotDuplicateSeats()
    {
        using (var store = new Store(root))
        {
            var service = new OrganizationDirectory(store);
            var initial = service.Read();
            var change = new CompanyDirectoryChange(initial.Version, "add-team",
                [.. initial.Departments, new("operations", "Operations", "Keep work moving")],
                [.. initial.Agents, new("ceo", "CEO manager", "Coordinate departments", null, "manager", null),
                    new("ops", "Operations assistant", "Plan delivery", "operations", "employee", null)]);
            var saved = service.Update(change);
            Assert.Equal(2, saved.Version);
            Assert.Equal(3, service.Update(change).Agents.Length);
            Assert.Throws<ArgumentException>(() => service.Update(change with { Agents = initial.Agents }));
            Assert.Throws<InvalidOperationException>(() => service.Update(change with { RequestId = "stale" }));
        }
        using var reopened = new Store(root);
        var restored = new OrganizationDirectory(reopened).Read();
        Assert.Equal(3, restored.Agents.Length);
        Assert.Null(restored.Agents.Single(agent => agent.Id == "ceo").RuntimeKey);
    }

    [Fact] public void OwnershipAndRuntimeBindingsAreValidated()
    {
        using var store = new Store(root);
        var service = new OrganizationDirectory(store);
        var initial = service.Read();
        CompanyDirectoryChange Change(CompanyAgent[] agents) => new(initial.Version, Guid.NewGuid().ToString(), initial.Departments, agents);
        Assert.Throws<ArgumentException>(() => service.Update(Change([])));
        Assert.Throws<ArgumentException>(() => service.Update(Change([.. initial.Agents, new("copy", "Copy", "", "marketing", "employee", "marketing")])));
        Assert.Throws<ArgumentException>(() => service.Update(Change([.. initial.Agents, new("orphan", "Orphan", "", "missing", "employee", null)])));
        Assert.Throws<ArgumentException>(() => service.Update(Change([initial.Agents[0], initial.Agents[0]])));
        Assert.Equal(1, service.Read().Version);
    }
}
