using Thaddeus.Core;
using Thaddeus.Infrastructure;
namespace Thaddeus.Tests;
public sealed class ReconciliationTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"thaddeus-reconcile-"+Guid.NewGuid().ToString("N"));
    private Store store;
    private bool inject;
    private string stage="after-content-commit";
    public ReconciliationTests(){store=new(root,s=>{if(inject&&s==stage)throw new IOException("Injected crash");});store.Write("notes/a.md","# Fictional source\nUnresolved conflict.","absent");}
    private Runtime Runtime()=>new(store,_=>new ScriptedProvider(),new PlanValidator(),new EvidencePolicy());
    private async Task<Run> Interrupted()
    {
        var rt=Runtime();var r=rt.Create(new("Plan",["notes/a.md"],"plans/",[],new(),new()));await rt.Execute(r.Id);r=store.Get(r.Id)!;inject=true;
        await rt.Decide(r.Id,r.Approval!.Id,r.Approval.Digest,true);inject=false;return store.Get(r.Id)!;
    }
    [Fact]public async Task CrashBeforeProjection_CommitsOnceAndRequiresExplicitCompletion()
    {
        var r=await Interrupted();Assert.Equal(RunState.NeedsAttention,r.State);Assert.Equal("absent",store.Version(r.Approval!.Action.Path));Assert.Single(store.Revisions(r.Approval.Action.Path));
        store.Dispose();store=new(root);var rt=Runtime();rt.Recover();await rt.Execute(r.Id);Assert.Equal("absent",store.Version(r.Approval.Action.Path));
        var done=await rt.Reconcile(r.Id,"absent","complete");Assert.Equal(RunState.Succeeded,done.State);Assert.Single(store.Revisions(r.Approval.Action.Path));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>rt.Reconcile(r.Id,store.Version(r.Approval.Action.Path),"complete"));
    }
    [Fact]public async Task CrashAfterProjection_VerifiesWithoutAnotherWriteOrRevision()
    {
        stage="after-projection";var r=await Interrupted();var path=r.Approval!.Action.Path;var time=File.GetLastWriteTimeUtc(store.SafePath(path));
        var done=await Runtime().Reconcile(r.Id,store.Version(path),"verify");Assert.Equal(RunState.Succeeded,done.State);Assert.Equal(time,File.GetLastWriteTimeUtc(store.SafePath(path)));Assert.Single(store.Revisions(path));
    }
    [Fact]public async Task ReconciliationCannotOverwriteNewUserContent()
    {
        var r=await Interrupted();var path=r.Approval!.Action.Path;store.Write(path,"User changed this.","absent");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Runtime().Reconcile(r.Id,store.Version(path),"complete"));Assert.Equal("User changed this.",store.Page(path)!.Content);
        await Runtime().Reconcile(r.Id,store.Version(path),"abandon");Assert.Equal("User changed this.",store.Page(path)!.Content);
    }
    [Fact]public void ExplicitEditsHaveActivityReceiptsAndDoNotInheritAgentOff()
    {
        store.Setting("writes","off");var page=Runtime().EditPage("notes/a.md","User edit",store.Version("notes/a.md"));
        var run=Assert.Single(store.List());Assert.Equal("edit",run.Goal.Kind);Assert.Equal(RunState.Succeeded,run.State);Assert.Equal(page.Path,run.OutputPath);Assert.Contains(store.Events(0,run.Id),e=>e.Type=="user.edit.completed");
    }
    [Fact]public async Task OffPolicyIsRecheckedBeforeReconciliationWrite()
    {
        var r=await Interrupted();store.Setting("writes","off");await Assert.ThrowsAsync<InvalidOperationException>(()=>Runtime().Reconcile(r.Id,"absent","complete"));Assert.Equal("absent",store.Version(r.Approval!.Action.Path));
    }
    public void Dispose(){store.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
}
