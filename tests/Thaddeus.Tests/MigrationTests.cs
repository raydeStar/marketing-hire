using Microsoft.Data.Sqlite;
using System.Text.Json.Nodes;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class MigrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-migration-" + Guid.NewGuid().ToString("N"));
    private SqliteConnection Open()
    {
        Directory.CreateDirectory(root);
        var db=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=Path.Combine(root,"ledger.sqlite") }.ToString());db.Open();return db;
    }
    [Fact] public void LegacyRowsRemainByteIdenticalAndNewFieldsHaveSafeDefaults()
    {
        var oldRun=new Run { Goal=new("An old plan",["notes/source.md"],"plans/",[],new(),new()),State=RunState.Paused };
        var json=JsonNode.Parse(Wire.Pack(oldRun))!.AsObject();
        foreach(var field in new[]{"execution","question","capabilities","profile","modelDispatches","executionDeadlineStart"})json.Remove(field);
        var original=json.ToJsonString();
        using(var db=Open())
        {
            using var command=db.CreateCommand();command.CommandText="CREATE TABLE runs(id TEXT PRIMARY KEY,version INTEGER NOT NULL,body TEXT NOT NULL); INSERT INTO runs VALUES($id,0,$body);";
            command.Parameters.AddWithValue("$id",oldRun.Id);command.Parameters.AddWithValue("$body",original);command.ExecuteNonQuery();
        }
        using(var store=new Store(root))
        {
            var migrated=store.Get(oldRun.Id)!;
            Assert.Null(migrated.Execution);Assert.Null(migrated.Question);Assert.Empty(migrated.Capabilities);Assert.Empty(migrated.ModelDispatches);
            Assert.Equal(RunState.Paused,migrated.State);
        }
        using(var db=Open())
        {
            using var command=db.CreateCommand();command.CommandText="SELECT body FROM runs";Assert.Equal(original,command.ExecuteScalar());
            command.CommandText="PRAGMA user_version";Assert.Equal(2L,command.ExecuteScalar());
            command.CommandText="SELECT COUNT(*) FROM schema_migrations";Assert.Equal(2L,command.ExecuteScalar());
        }
        using var reopened=new Store(root);Assert.Equal(oldRun.Id,reopened.Get(oldRun.Id)!.Id);
    }
    [Fact] public void NewerDatabaseIsRefusedWithoutChangingItOrKeepingTheLease()
    {
        using(var db=Open()) {using var command=db.CreateCommand();command.CommandText="PRAGMA user_version=99";command.ExecuteNonQuery();}
        Assert.Throws<InvalidOperationException>(()=>new Store(root));
        using(var db=Open())
        {
            using var command=db.CreateCommand();command.CommandText="PRAGMA user_version";Assert.Equal(99L,command.ExecuteScalar());
            command.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table'";Assert.Equal(0L,command.ExecuteScalar());
            command.CommandText="PRAGMA user_version=0";command.ExecuteNonQuery();
        }
        using var store=new Store(root);Assert.Empty(store.List());
    }
    [Fact] public void RecoveryChargesInterruptedModelReservationOnce()
    {
        using var store=new Store(root);var run=CapabilityTests.CreateWorkerRun(store);
        run.ReservedTokens=100;run.ModelCalls=1;run.ModelDispatches.Add(new("interrupted","hash",DateTimeOffset.UtcNow,"dispatched-outcome-unknown",100));
        store.Save(run,"test.interrupted",new{});
        var runtime=new Runtime(store,_=>throw new Exception("Recovery cannot infer"),new PlanValidator(),new EvidencePolicy());
        runtime.Recover();runtime.Recover();
        var recovered=store.Get(run.Id)!;Assert.Equal(100,recovered.ChargedTokens);Assert.Equal(0,recovered.ReservedTokens);
        Assert.Equal("outcome-unknown",recovered.ModelDispatches.Single().Status);Assert.Equal(RunState.NeedsAttention,recovered.State);
    }
    public void Dispose() { SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true); }
}
