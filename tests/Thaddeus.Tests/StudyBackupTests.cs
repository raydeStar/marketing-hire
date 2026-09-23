using Microsoft.Data.Sqlite;
using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class StudyBackupTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-backup-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(root, "study");
    private string Backup => Path.Combine(root, "backup");
    private string Restored => Path.Combine(root, "restored");
    public StudyBackupTests()
    {
        Directory.CreateDirectory(root);
        using var store = new Store(Data);
        store.Write("notes/fixture.md", "# Original\nCafé and a raven 🪶\n", "absent");
        store.Setting("provider", Wire.Pack(new ProviderSnapshot("compatible", "fictional-model", "high", "https://provider.invalid/v1", "fictional-opaque-reference")));
        store.Setting("provider-credentials", "fictional reference metadata; no provider key");
        File.WriteAllText(Path.Combine(Data, "host-key.txt"), "fictional-owner-key");
        File.WriteAllText(Path.Combine(Data, "launcher-instance.json"), "fictional stale process identity");
    }
    private async Task<StudyBackupManifest> Manifest()
    {
        await StudyBackup.Create(Data, Backup);
        return Wire.Unpack<StudyBackupManifest>(await File.ReadAllTextAsync(Path.Combine(Backup, "backup.json")));
    }
    private Task SaveManifest(StudyBackupManifest value) => File.WriteAllTextAsync(Path.Combine(Backup, "backup.json"), Wire.Pack(value));
    private sealed class CountingDispatcher : IDelegationDispatcher
    {
        public int Calls;
        public Task<DelegationDispatchResult> Dispatch(DelegationJob job, DelegationOccurrence occurrence, CancellationToken cancellation)
        { Calls++; return Task.FromResult(new DelegationDispatchResult("accepted", "Fixture accepted.", true)); }
    }
    [Fact] public async Task LongStudyPathsCanBeBackedUpRestoredAndReopened()
    {
        var parent = Path.Combine(root, new string('a', 100), new string('b', 100)); Directory.CreateDirectory(parent);
        var data = Path.Combine(parent, "long-study" + new string('c', 60));
        using (var store = new Store(data)) store.Write("notes/long.md", "A long address still belongs to the study.", "absent");
        var receipt = await StudyBackup.Create(data, Path.Combine(parent, "backup"));
        var restored = await StudyBackup.Restore(receipt.Directory, Path.Combine(parent, "restored" + new string('d', 60)));
        using var reopened = new Store(restored.Directory);
        Assert.Equal("A long address still belongs to the study.", File.ReadAllText(Path.Combine(restored.Directory, "knowledge/notes/long.md")));
    }
    [Fact] public async Task ActiveLauncherDiagnosticsStayOutsideTheRestorableStudy()
    {
        var logs = Path.Combine(Data, "launcher-logs"); Directory.CreateDirectory(logs);
        var log = Path.Combine(logs, "active.stdout.log");
        using var output = new FileStream(log, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await output.WriteAsync("The maintenance host is still reporting progress."u8.ToArray()); await output.FlushAsync();
        var manifest = await Manifest();
        Assert.DoesNotContain(manifest.Files, file => file.Path.StartsWith("launcher-logs/", StringComparison.Ordinal));
        Assert.True(File.Exists(log));
        var restored = await StudyBackup.Restore(Backup, Restored);
        Assert.False(Directory.Exists(Path.Combine(restored.Directory, "launcher-logs")));
    }
    [Fact] public async Task RestorePreservesTheSnapshotAndLaterOriginalEditsRemainUntouched()
    {
        var originalTime = File.GetLastWriteTimeUtc(Path.Combine(Data, "knowledge/notes/fixture.md"));
        const string customIdentity = "# Identity\n\n**Role:** A careful fictional backup butler.";
        const string customSoul = "# Backup Soul\n\nA calm fictional backup personality.";
        const string customUser = "# User\n\n- Prefers careful fictional backup tests.";
        using (var store = new Store(Data))
        {
            store.UpdateIdentity(customIdentity, store.Identity().Version, "backup-identity-fixture");
            store.UpdateSoul(customSoul, store.Soul().Version, "settings", "backup-soul-fixture");
            store.UpdateUser(customUser, store.User().Version, "settings", "backup-user-fixture");
        }
        var manifest = await Manifest();
        Assert.Contains(manifest.Files, file => file.Path == Store.IdentityFileName);
        Assert.Contains(manifest.Files, file => file.Path == Store.SoulFileName);
        Assert.Contains(manifest.Files, file => file.Path == Store.UserFileName);
        Assert.DoesNotContain(manifest.Files, file => file.Path is "host.lock" or "launcher-instance.json" or "ledger.sqlite-wal" or "ledger.sqlite-shm");
        using (var store = new Store(Data)) store.Write("notes/fixture.md", "Later edit", store.Version("notes/fixture.md"));
        var restored = await StudyBackup.Restore(Backup, Restored);
        Assert.Equal("restore", restored.Operation); Assert.Equal(Store.CurrentSchemaVersion, restored.DatabaseSchemaVersion);
        using (var store = new Store(Restored))
        {
            Assert.Contains("Café", store.Page("notes/fixture.md")!.Content);
            Assert.Single(store.Revisions("notes/fixture.md"));
            Assert.Contains("fictional-opaque-reference", store.Setting("provider"));
            Assert.Equal(customIdentity, store.Identity().Content);
            Assert.Single(store.IdentityHistory());
            Assert.Equal(customSoul, store.Soul().Content);
            Assert.Single(store.SoulHistory());
            Assert.Equal(customUser, store.User().Content);
            Assert.Single(store.UserHistory());
        }
        Assert.Equal("Later edit", await File.ReadAllTextAsync(Path.Combine(Data, "knowledge/notes/fixture.md")));
        Assert.Equal("fictional-owner-key", await File.ReadAllTextAsync(Path.Combine(Restored, "host-key.txt")));
        Assert.Equal(originalTime, File.GetLastWriteTimeUtc(Path.Combine(Restored, "knowledge/notes/fixture.md")));
        Assert.False(File.Exists(Path.Combine(Restored, "launcher-instance.json")));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Backup));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Restored));
        }
    }
    [Fact] public async Task OlderBackupReceiptReportsActualRestoredSchemaWithoutChangingBackup()
    {
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(Data, "ledger.sqlite"), Pooling = false }.ToString()))
        {
            db.Open(); using var command = db.CreateCommand();
            command.CommandText = "DELETE FROM schema_migrations WHERE version > 11; PRAGMA user_version=11;"; command.ExecuteNonQuery();
        }
        var saved = await StudyBackup.Create(Data, Backup);
        Assert.Equal(11, saved.DatabaseSchemaVersion);
        var file = Path.Combine(Backup, "data", "ledger.sqlite"); var before = await File.ReadAllBytesAsync(file);
        var restored = await StudyBackup.Restore(Backup, Restored);
        Assert.Equal(Store.CurrentSchemaVersion, restored.DatabaseSchemaVersion);
        using var dbRead = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(Restored, "ledger.sqlite"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        dbRead.Open(); using var read = dbRead.CreateCommand(); read.CommandText = "PRAGMA user_version";
        Assert.Equal((long)restored.DatabaseSchemaVersion, read.ExecuteScalar());
        Assert.Equal(before, await File.ReadAllBytesAsync(file));
        Assert.Equal(11, (await StudyBackup.Preview(Backup)).DatabaseSchemaVersion);
    }
    [Fact] public async Task RestoredBackupCannotRearmPendingOutboundEmailWithoutFreshReview()
    {
        DelegationJob sourceJob;
        using (var source = new Store(Data))
        {
            var tool = new ConnectedToolDefinition("mail-id", "Owner test mail", "send_email", "mcp_mail_send_email", "Send an email message.",
                JsonSerializer.SerializeToElement(new { type = "object", properties = new { to = new { type = "string" }, body = new { type = "string" } }, required = new[] { "to", "body" } }),
                "write or external action", "v1");
            var payload = new ScheduledEmailPayload(tool, JsonSerializer.SerializeToElement(new { to = "owner@example.invalid", body = "Fixture body." }),
                tool.ConnectorName, "owner@example.invalid", null, "Fixture body.");
            sourceJob = new DelegationScheduler(source, new CountingDispatcher()).CreateEmail(
                new(payload, DateTimeOffset.UtcNow.AddHours(2), TimeZoneInfo.Local.Id)).Job;
        }
        await StudyBackup.Create(Data, Backup);
        await StudyBackup.Restore(Backup, Restored);

        var dispatcher = new CountingDispatcher();
        using var restored = new Store(Restored);
        var job = Assert.Single(restored.DelegationJobs());
        var grant = restored.DelegationGrant(job.GrantId)!;
        Assert.Equal(sourceJob.Id, job.Id);
        Assert.Equal("needs-approval", job.State);
        Assert.Null(job.NextRunUtc);
        Assert.Contains("outbound authority disabled", job.LastSummary);
        Assert.True(grant.Revoked);
        Assert.Equal(0, await new DelegationScheduler(restored, dispatcher).Tick());
        Assert.Equal(0, dispatcher.Calls);
    }
    [Fact] public async Task LiveStoreAndLauncherLeasePreventBackupWithoutCreatingDestination()
    {
        using (var store = new Store(Data)) await Assert.ThrowsAsync<IOException>(() => StudyBackup.Create(Data, Backup));
        using (File.Open(Path.Combine(Data, "launcher.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAsync<IOException>(() => StudyBackup.Create(Data, Backup));
        Assert.False(Directory.Exists(Backup));
        Assert.Empty(Directory.GetDirectories(root, "*.incomplete-*"));
    }
    [Fact] public async Task NeitherBackupNorRestoreOverwritesAnExistingDestination()
    {
        await Manifest();
        var original = await File.ReadAllTextAsync(Path.Combine(Backup, "backup.json"));
        await Assert.ThrowsAsync<IOException>(() => StudyBackup.Create(Data, Backup));
        Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(Backup, "backup.json")));
        Directory.CreateDirectory(Restored); await File.WriteAllTextAsync(Path.Combine(Restored, "keep.txt"), "keep");
        await Assert.ThrowsAsync<IOException>(() => StudyBackup.Restore(Backup, Restored));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(Restored, "keep.txt")));
    }
    [Fact] public async Task BackupAndRestoreRejectNestedDestinations()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => StudyBackup.Create(Data, Path.Combine(Data, "backup")));
        await Manifest();
        await Assert.ThrowsAsync<ArgumentException>(() => StudyBackup.Restore(Backup, Path.Combine(Backup, "restored")));
    }
    [Theory] [InlineData("modified")] [InlineData("missing")] [InlineData("extra")]
    public async Task DamagedOrUnexpectedPayloadCannotBecomeARestoredStudy(string mode)
    {
        await Manifest(); var note = Path.Combine(Backup, "data/knowledge/notes/fixture.md");
        if (mode == "modified") await File.WriteAllTextAsync(note, "Changed payload");
        if (mode == "missing") File.Delete(note);
        if (mode == "extra") await File.WriteAllTextAsync(Path.Combine(Backup, "data/extra.txt"), "Unlisted");
        await Assert.ThrowsAsync<IOException>(() => StudyBackup.Restore(Backup, Restored));
        Assert.False(Directory.Exists(Restored)); Assert.True(File.Exists(Path.Combine(Data, "host-key.txt")));
    }
    [Theory] [InlineData("../outside.txt")] [InlineData("/absolute.txt")] [InlineData("stream:secret")]
    [InlineData("CON.txt")] [InlineData("folder\\outside.txt")] [InlineData("name. ")]
    public async Task HostileManifestPathsAreRefusedBeforeCopying(string relative)
    {
        var manifest = await Manifest();
        await SaveManifest(manifest with { Files = [.. manifest.Files, new(relative, 0, new('0', 64))] });
        await Assert.ThrowsAsync<ArgumentException>(() => StudyBackup.Restore(Backup, Restored));
        Assert.False(Directory.Exists(Restored));
    }
    [Fact] public async Task CaseCollisionsAndNullEntriesAreRejected()
    {
        var manifest = await Manifest();
        await SaveManifest(manifest with { Files = [.. manifest.Files, manifest.Files[0] with { Path = manifest.Files[0].Path.ToUpperInvariant() }] });
        await Assert.ThrowsAsync<ArgumentException>(() => StudyBackup.Restore(Backup, Restored));
        await SaveManifest(manifest with { Files = [null!] });
        await Assert.ThrowsAsync<ArgumentException>(() => StudyBackup.Restore(Backup, Restored));
    }
    [Fact] public async Task FutureDatabaseVersionIsRefusedWithoutMigratingTheSource()
    {
        using (var db = new SqliteConnection($"Data Source={Path.Combine(Data, "ledger.sqlite")};Pooling=False"))
        { db.Open(); using var command = db.CreateCommand(); command.CommandText = "PRAGMA user_version=999"; command.ExecuteNonQuery(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => StudyBackup.Create(Data, Backup));
        Assert.False(Directory.Exists(Backup));
        using var original = new SqliteConnection($"Data Source={Path.Combine(Data, "ledger.sqlite")};Mode=ReadOnly;Pooling=False"); original.Open();
        using var version = original.CreateCommand(); version.CommandText = "PRAGMA user_version"; Assert.Equal(999L, version.ExecuteScalar());
    }
    [Fact] public async Task CompletedSnapshotNeverContainsDatabaseSidecars()
    {
        // Deliberately retain a WAL using an independent test connection while the product's host lease is closed.
        using var db = new SqliteConnection($"Data Source={Path.Combine(Data, "ledger.sqlite")};Pooling=False"); db.Open();
        using (var write = db.CreateCommand()) { write.CommandText = "PRAGMA journal_mode=WAL; INSERT INTO settings VALUES('wal-only','durable journal value')"; write.ExecuteNonQuery(); }
        Assert.True(new FileInfo(Path.Combine(Data, "ledger.sqlite-wal")).Length > 0);
        var manifest = await Manifest(); Assert.DoesNotContain(manifest.Files, file => file.Path.EndsWith("-wal") || file.Path.EndsWith("-shm"));
        await StudyBackup.Restore(Backup, Restored);
        using var restored = new Store(Restored); Assert.Equal("durable journal value", restored.Setting("wal-only"));
    }
    [Fact] public async Task UnixLinksCannotPullExternalContentIntoABackupOrRestore()
    {
        if (OperatingSystem.IsWindows()) return; // Unix exercises actual links without requesting Windows symlink privileges.
        var external = Path.Combine(root, "external.txt"); await File.WriteAllTextAsync(external, "outside");
        var link = Path.Combine(Data, "linked.txt"); File.CreateSymbolicLink(link, external);
        await Assert.ThrowsAsync<IOException>(() => StudyBackup.Create(Data, Backup)); File.Delete(link);
        await Manifest();
        File.Move(Path.Combine(Backup, "backup.json"), Path.Combine(root, "manifest.json"));
        File.CreateSymbolicLink(Path.Combine(Backup, "backup.json"), Path.Combine(root, "manifest.json"));
        await Assert.ThrowsAsync<IOException>(() => StudyBackup.Restore(Backup, Restored));
        Assert.Equal("outside", await File.ReadAllTextAsync(external));
    }
    public void Dispose() { Directory.Delete(root, true); }
}
