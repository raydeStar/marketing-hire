using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class CapabilityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-capability-" + Guid.NewGuid().ToString("N"));
    private Store store;
    private Runtime runtime;
    public CapabilityTests() { store = new(root); runtime = NewRuntime(); }
    private Runtime NewRuntime() => new(store, _ => throw new InvalidOperationException("This test must never dispatch a model."), new PlanValidator(), new EvidencePolicy());
    internal static Run CreateWorkerRun(Store store, int tools = 8)
    {
        var run = new Run
        {
            Goal = new("Prepare a research note", ["notes/source.md"], "plans/", [], new(ToolCalls:tools), new("compatible", "fixture-model"), "research"),
            State = RunState.Running, Profile = PolicyProfile.Evidence,
            Execution = new("openclaw", "thaddeus-0123456789abcdef0123456789abcdef", "agent:thaddeus:fixture", "2026.9.4")
        };
        store.Save(run, "test.worker-admitted", new { simulatedWorker = true }); return run;
    }
    private Task<CapabilityResult> Call(Run run, string id, string name, object args) => runtime.Call(run.Id, new(id,name,JsonSerializer.SerializeToElement(args)), default);
    [Fact] public async Task ReadScopeCannotBeBroadenedByToolArguments()
    {
        store.Write("notes/private.md", "Never granted", "absent"); var run = CreateWorkerRun(store);
        var result = await Call(run,"read-1","thaddeus_read_note",new{path="notes/private.md"});
        Assert.True(result.IsError); Assert.DoesNotContain("Never granted", result.Value.GetRawText());
        Assert.Empty(store.Get(run.Id)!.Evidence);
    }
    [Fact] public async Task StableOperationRetryAfterRestartDoesNotAskTwiceOrSpendAgain()
    {
        var run = CreateWorkerRun(store); var args = new { question = "Which audience?", choices = new[] { "Beginners", "Developers" } };
        var first = await Call(run,"question-1","thaddeus_ask_user",args);
        store.Dispose(); store = new(root); runtime = NewRuntime(); runtime.Recover();
        var retry = await Call(run,"question-1","thaddeus_ask_user",args);
        Assert.Equal(first.Value.GetRawText(), retry.Value.GetRawText());
        Assert.Equal(1, store.Get(run.Id)!.ToolCalls); Assert.Single(store.Get(run.Id)!.Capabilities);
        Assert.Equal(RunState.AwaitingInput, store.Get(run.Id)!.State);
        await runtime.AnswerQuestion(run.Id, "question-1", "Beginners, please", default);
        Assert.Equal("Beginners, please", store.Get(run.Id)!.Question!.Answer);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>runtime.AnswerQuestion(run.Id,"question-1","Different answer",default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>runtime.Execute(run.Id));
    }
    [Fact] public async Task ReusedOperationIdWithDifferentContentIsRejected()
    {
        var run = CreateWorkerRun(store);
        await Call(run,"q1","thaddeus_ask_user",new{question="First?",choices=Array.Empty<string>()});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Call(run,"q1","thaddeus_ask_user",new{question="Changed?",choices=Array.Empty<string>()}));
    }
    [Fact] public async Task ProposalHasNoHostWriteUntilExactApprovalAndReadback()
    {
        var run = CreateWorkerRun(store); const string content = "# Research\nA draft with explicit uncertainties.";
        var result = await Call(run,"import-1","thaddeus_propose_import",new{path="plans/research.md",content,artifact="research.md"});
        Assert.False(result.IsError); Assert.Equal("absent",store.Version("plans/research.md"));
        var approval = store.Get(run.Id)!.Approval!;
        var completed = await runtime.Decide(run.Id,approval.Id,approval.Digest,true);
        Assert.Equal(Wire.Hash(content),store.Version("plans/research.md")); Assert.True(completed.Validation!.Passed);
        Assert.Contains(completed.Goal.Criteria,c=>c.Status=="unverified");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>runtime.Decide(run.Id,approval.Id,approval.Digest,true));
        Assert.Single(store.Revisions("plans/research.md"));
    }
    [Fact] public async Task SourceDriftPreventsPreviouslyApprovedImport()
    {
        store.Write("notes/source.md","Original source","absent"); var run = CreateWorkerRun(store);
        await Call(run,"read-1","thaddeus_read_note",new{path="notes/source.md"});
        await Call(run,"import-1","thaddeus_propose_import",new{path="plans/research.md",content="# Draft",artifact="research.md"});
        var approval=store.Get(run.Id)!.Approval!;
        store.Write("notes/source.md","Changed source",store.Version("notes/source.md"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>runtime.Decide(run.Id,approval.Id,approval.Digest,true));
        Assert.Equal("absent",store.Version("plans/research.md"));
    }
    [Fact] public async Task ToolBudgetReservesRoomForApprovedEffect()
    {
        var run=CreateWorkerRun(store,1);
        Assert.True((await Call(run,"import-1","thaddeus_propose_import",new{path="plans/research.md",content="# Draft",artifact="research.md"})).IsError);
        Assert.Null(store.Get(run.Id)!.Approval); Assert.Equal("absent",store.Version("plans/research.md"));
    }
    [Fact] public void WorkerTokenIsScopedHashedRevocableAndStoppedOnRecovery()
    {
        var first=CreateWorkerRun(store);var second=CreateWorkerRun(store);var auth=new WorkerAuthorization(store);
        var token=auth.Issue(first.Id,TimeSpan.FromMinutes(5));
        Assert.True(auth.Authenticate(first.Id,token)); Assert.False(auth.Authenticate(second.Id,token));
        Assert.DoesNotContain(token,store.Setting("worker-grant:"+first.Id)!);
        runtime.Recover(); Assert.False(auth.Authenticate(first.Id,token));
        var restored=store.Get(first.Id)!;restored.State=RunState.Running;store.Save(restored,"test.resume",new{});
        auth.Revoke(first.Id); Assert.False(auth.Authenticate(first.Id,token));
    }
    public void Dispose(){store.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
}
