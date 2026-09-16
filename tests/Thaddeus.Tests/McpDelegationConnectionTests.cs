using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class McpDelegationConnectionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-mcp-delegation-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;

    public McpDelegationConnectionTests() => store = new(root);

    private sealed class Vault : ICredentialVault
    {
        public Task<string?> Execute(string operation, string scope, string id, string? value, CancellationToken cancellation) => Task.FromResult<string?>(null);
    }

    private sealed class Dispatcher : IDelegationDispatcher
    {
        public Task<DelegationDispatchResult> Dispatch(DelegationJob job, DelegationOccurrence occurrence, CancellationToken cancellation) =>
            Task.FromResult(new DelegationDispatchResult("accepted", "Fixture accepted.", true));
    }

    [Fact]
    public async Task PendingScheduledEmailPreventsConnectorRemovalUntilCancelled()
    {
        var schema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { to = new { type = "string" }, body = new { type = "string" } }, required = new[] { "to", "body" } });
        var record = new McpConnectorRecord("mail-id", "Owner test mail", "https://mail.invalid/mcp", "none", "ready",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            [new("send_email", "mcp_mail_send_email", "Send an email message.", schema, "write or external action")], "connector-v1");
        var packed = Wire.Pack(new McpConnectorCatalog("fixture-scope", [record]));
        store.Setting("mcp-connectors", packed);
        var connections = new McpConnections(store, new Vault());
        var tool = Assert.Single(connections.Snapshot());
        var payload = new ScheduledEmailPayload(tool,
            JsonSerializer.SerializeToElement(new { to = "owner@example.invalid", body = "Fixture only." }),
            tool.ConnectorName, "owner@example.invalid", null, "Fixture only.");
        var scheduler = new DelegationScheduler(store, new Dispatcher());
        var created = scheduler.CreateEmail(new(payload, DateTimeOffset.UtcNow.AddHours(2), TimeZoneInfo.Local.Id));
        var version = Wire.Hash(packed);

        var view = JsonSerializer.SerializeToElement(await connections.View(), Wire.Json);
        Assert.True(view.GetProperty("connectors")[0].GetProperty("inUse").GetBoolean());
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.Forget(record.Id, new(version), default));

        store.CancelDelegation(created.Job.Id, created.Job.Version, DateTimeOffset.UtcNow);
        await connections.Forget(record.Id, new(version), default);
        Assert.Empty(connections.Snapshot());
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
