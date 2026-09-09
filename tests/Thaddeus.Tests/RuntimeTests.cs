using Thaddeus.Core;
using Thaddeus.Infrastructure;
namespace Thaddeus.Tests;

public sealed class RuntimeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-test-" + Guid.NewGuid().ToString("N"));
    private Store store;
    public RuntimeTests() { store = new(root); store.Write("notes/source.md", "# Fictional notes\nDeadline Friday. Unresolved conflict Thursday.", "absent"); }
    private Runtime Runtime(IModelProvider? provider = null) => new(store, _ => provider ?? new ScriptedProvider(), new PlanValidator(), new EvidencePolicy());
    private Goal Goal(Budget? budget = null) => new("Make a plan", ["notes/source.md"], "plans/", [new("Exact write", "deterministic")], budget ?? new(), new());
    private async Task<(Runtime Runtime, Run Run)> Draft(bool fault = false)
    {
        var rt = Runtime(); var run = rt.Create(Goal(), fault); await rt.Execute(run.Id); return (rt, store.Get(run.Id)!);
    }
    [Fact] public async Task EndToEnd_WaitsThenVerifiesExactContent()
    {
        var (rt,r) = await Draft(); Assert.Equal(RunState.AwaitingApproval,r.State); Assert.Equal("absent",store.Version(r.Approval!.Action.Path));
        var done=await rt.Decide(r.Id,r.Approval.Id,r.Approval.Digest,true);
        Assert.Equal(RunState.Succeeded,done.State); Assert.True(done.Validation!.Passed); Assert.Equal(r.Approval.Action.Content,store.Page(done.OutputPath!)!.Content);
        Assert.Null(done.InputTokens); Assert.Null(done.Cost); Assert.Contains(done.Goal.Criteria,c=>c.Status=="unverified");
    }
    [Fact] public async Task Denial_IsTerminalAndNonMutating()
    {
        var(rt,r)=await Draft(); await rt.Decide(r.Id,r.Approval!.Id,r.Approval.Digest,false); await rt.Execute(r.Id);
        Assert.Equal(RunState.Denied,store.Get(r.Id)!.State); Assert.Equal("absent",store.Version(r.Approval.Action.Path));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>rt.Decide(r.Id,r.Approval.Id,r.Approval.Digest,true));
    }
    [Fact] public async Task Tamper_Expiry_AndSourceRevisionReject()
    {
        var(rt,r)=await Draft(); var a=r.Approval!;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>rt.Decide(r.Id,a.Id,"tampered",true));
        store.Write("notes/source.md","Changed source",store.Version("notes/source.md"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>rt.Decide(r.Id,a.Id,a.Digest,true));
        Assert.Equal("absent",store.Version(a.Action.Path));
        r=store.Get(r.Id)!;r.Approval=a with {Expires=DateTimeOffset.UtcNow.AddSeconds(-1)};store.Save(r,"test.expired",new{});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>rt.Decide(r.Id,a.Id,a.Digest,true));
    }
    [Fact] public async Task ChangedTarget_AndPolicyOffReject()
    {
        var(rt,r)=await Draft();var a=r.Approval!;store.Setting("writes","off");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>rt.Decide(r.Id,a.Id,a.Digest,true));
        store.Setting("writes","ask");store.Write(a.Action.Path,"Direct user edit","absent");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>rt.Decide(r.Id,a.Id,a.Digest,true));
        Assert.Equal("Direct user edit",store.Page(a.Action.Path)!.Content);
    }
    [Fact] public async Task ConcurrentApprovals_WriteOnlyOnce()
    {
        var(rt,r)=await Draft();var a=r.Approval!;
        var outcomes=await Task.WhenAll(Enumerable.Range(0,8).Select(async _=>{try{await rt.Decide(r.Id,a.Id,a.Digest,true);return true;}catch(InvalidOperationException){return false;}}));
        Assert.Single(outcomes,x=>x);Assert.Single(store.Revisions(a.Action.Path));
    }
    [Fact] public async Task ConcurrentExecutors_UseOneModelCall()
    {
        var rt=Runtime();var r=rt.Create(Goal());await Task.WhenAll(Enumerable.Range(0,8).Select(_=>rt.Execute(r.Id)));
        Assert.Equal(1,store.Get(r.Id)!.ModelCalls);
    }
    [Fact] public async Task Repair_IsBoundedAndCounted()
    {
        var(_,r)=await Draft(true);Assert.Equal(RunState.AwaitingApproval,r.State);Assert.Equal(2,r.ModelCalls);Assert.Equal(1,r.Repairs);
        var rt=Runtime(new BadProvider());var bad=rt.Create(Goal());await rt.Execute(bad.Id);bad=store.Get(bad.Id)!;
        Assert.Equal(RunState.Failed,bad.State);Assert.True(bad.ModelCalls<=2);Assert.Equal("absent",store.Version("plans/bad.md"));
    }
    [Theory][InlineData(0,8)][InlineData(3,0)] public async Task Budgets_ReserveBeforeDispatch(int calls,int tools)
    {
        var rt=Runtime();var r=rt.Create(Goal(new(calls,tools)));await rt.Execute(r.Id);r=store.Get(r.Id)!;
        Assert.Equal(RunState.Failed,r.State);Assert.True(r.ModelCalls<=calls);Assert.True(r.ToolCalls<=tools);Assert.Null(r.Approval);
    }
    [Fact] public async Task Cancellation_StopsInFlightProvider()
    {
        var provider=new SlowProvider();var rt=Runtime(provider);var r=rt.Create(Goal());var work=rt.Execute(r.Id);await provider.Started.Task;await rt.Cancel(r.Id);await work;
        Assert.Equal(RunState.Cancelled,store.Get(r.Id)!.State);Assert.Null(store.Get(r.Id)!.Approval);
    }
    [Fact] public async Task Timeout_StopsInFlightProvider()
    {
        var rt=Runtime(new SlowProvider());var r=rt.Create(Goal(new(Seconds:1)));await rt.Execute(r.Id);
        Assert.Equal(RunState.NeedsAttention,store.Get(r.Id)!.State);
    }
    [Fact] public async Task ProviderFailure_IsNotSuccess()
    {
        var rt=Runtime(new ThrowProvider());var r=rt.Create(Goal());await rt.Execute(r.Id);
        Assert.Equal(RunState.Failed,store.Get(r.Id)!.State);Assert.DoesNotContain("secret",store.Get(r.Id)!.Summary);
    }
    [Fact] public async Task Restart_ReplayDoesNotExecuteAndHistorySurvives()
    {
        var(_,r)=await Draft();var before=store.Events(0,r.Id);store.Dispose();store=new(root);Runtime().Recover();
        Assert.Equal(RunState.AwaitingApproval,store.Get(r.Id)!.State);Assert.Equal(before.Count,store.Events(0,r.Id).Count);Assert.Equal("absent",store.Version(r.Approval!.Action.Path));
        Assert.Equal(before.Count-2,store.Events(before[1].Cursor,r.Id).Count);
        Assert.Equal(Enumerable.Range(1,before.Count).Select(i=>(long)i),before.Select(e=>e.Sequence));
        Assert.Equal(before.Count,before.Select(e=>e.EventId).Distinct().Count());
    }
    [Fact] public async Task UnknownWriteOutcome_RequiresReconciliationAfterRestart()
    {
        var(_,r)=await Draft();r.State=RunState.Running;store.Save(r,"tool.request",r.Approval!.Action);
        store.Write(r.Approval.Action.Path,r.Approval.Action.Content!,"absent");store.Dispose();store=new(root);var rt=Runtime();rt.Recover();await rt.Execute(r.Id);
        Assert.Equal(RunState.NeedsAttention,store.Get(r.Id)!.State);Assert.Single(store.Revisions(r.Approval.Action.Path));
    }
    [Fact] public async Task QueuedRestart_PausesAndCanResumeSafely()
    {
        var rt=Runtime();var r=rt.Create(Goal());rt.Recover();Assert.Equal(RunState.Paused,store.Get(r.Id)!.State);await rt.Execute(r.Id);Assert.Equal(RunState.AwaitingApproval,store.Get(r.Id)!.State);
    }
    [Theory][InlineData("../outside.md")][InlineData("plans/../../outside.md")][InlineData("C:/secrets.md")][InlineData("plans/a.md:stream")][InlineData("plans\\a.md")][InlineData("notes/<script>.md")][InlineData("plans/con.md")][InlineData("notes/com1.md")]
    public void TraversalAndInvalidPaths_Reject(string path)=>Assert.Throws<ArgumentException>(()=>store.SafePath(path));
    [Fact] public void LinkedDirectory_Rejects()
    {
        var target=Path.Combine(root,"target");Directory.CreateDirectory(target);var link=Path.Combine(root,"knowledge","plans");
        if(OperatingSystem.IsWindows())
        {
            var p=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe") {ArgumentList={"/c","mklink","/J",link,target},CreateNoWindow=true,RedirectStandardOutput=true});p!.WaitForExit();Assert.Equal(0,p.ExitCode);
        }
        else Directory.CreateSymbolicLink(link,target);
        Assert.Throws<ArgumentException>(()=>store.Write("plans/escape.md","bad","absent"));
        Directory.Delete(link); // Remove the verified test junction itself, never its target recursively.
    }
    [Fact] public void RevisionConflict_PreservesContent()
    {
        var original=store.Page("notes/source.md")!;store.Write(original.Path,"updated",original.Version);
        Assert.Throws<InvalidOperationException>(()=>store.Write(original.Path,"stale",original.Version));Assert.Equal(2,store.Revisions(original.Path).Count);
    }
    [Fact] public void RunCompareAndSwap_RejectsLostUpdate()
    {
        var rt=Runtime();var run=rt.Create(Goal());var stale=store.Get(run.Id)!;store.Save(run,"first",new{});Assert.Throws<InvalidOperationException>(()=>store.Save(stale,"stale",new{}));
    }
    public void Dispose(){store.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
    private sealed class BadProvider:IModelProvider{public Task<ModelReply> Respond(Observation o,Func<string,Task> delta,CancellationToken c)=>Task.FromResult(new ModelReply(new("knowledge.write","plans/bad.md","incomplete"),null));}
    private sealed class ThrowProvider:IModelProvider{public Task<ModelReply> Respond(Observation o,Func<string,Task> delta,CancellationToken c)=>throw new HttpRequestException("secret endpoint response");}
    private sealed class SlowProvider:IModelProvider
    {
        public TaskCompletionSource Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ModelReply> Respond(Observation o,Func<string,Task> delta,CancellationToken c){Started.SetResult();await Task.Delay(Timeout.Infinite,c);return new(null,null);}
    }
}
