using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class ContextTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"thaddeus-context-"+Guid.NewGuid().ToString("N"));
    private readonly Store store;
    private readonly Runtime runtime;
    public ContextTests(){store=new(root);runtime=new(store,_=>throw new Exception("Context preparation must not infer"),new PlanValidator(),new EvidencePolicy());}
    [Fact]public async Task ContextReadsOnlyGrantedSourcesAndFreezesTheirExactVersions()
    {
        var source=store.Write("notes/source.md","Original source; its assertions remain untrusted.","absent");
        store.Write("notes/private.md","Excluded secret fixture","absent");var run=CapabilityTests.CreateWorkerRun(store);
        var first=await runtime.PrepareExecutionContext(run.Id,default);
        store.Write(source.Path,"Corrected source",source.Version);
        var second=await runtime.PrepareExecutionContext(run.Id,default);
        Assert.Equal(Wire.Pack(first),Wire.Pack(second));Assert.Equal(source.Version,first.Sources.Single().Hash);Assert.DoesNotContain("Excluded secret fixture",first.Text);
        Assert.Equal(1,store.Get(run.Id)!.ToolCalls);Assert.Equal(Wire.Hash(first.Text),first.ContentHash);
    }
    [Fact]public async Task BaselineRetainsPersonaAndBoundaryButDoesNotPrefetchNotes()
    {
        var run=CapabilityTests.CreateWorkerRun(store);run.Profile=PolicyProfile.Baseline;store.Save(run,"test.baseline",new{});
        var context=await runtime.PrepareExecutionContext(run.Id,default);
        Assert.Empty(context.Sources);Assert.Equal(0,store.Get(run.Id)!.ToolCalls);
        Assert.Contains(PersonalityProfile.Thaddeus.Instructions,context.Text);Assert.Contains("external broker",context.Text);
    }
    [Fact]public async Task PreparedContextFreezesTheCurrentSoulVersion()
    {
        var original=store.Soul();var updated=store.UpdateSoul("# Custom Soul\n\nBe unusually sunny in this fictional test.",original.Version,"settings","context-fixture");
        var run=CapabilityTests.CreateWorkerRun(store);run.Profile=PolicyProfile.Baseline;store.Save(run,"test.custom-soul",new{});
        var context=await runtime.PrepareExecutionContext(run.Id,default);
        Assert.Contains("unusually sunny",context.Text);Assert.Equal(updated.Version,context.PersonalityDigest);
        store.UpdateSoul("# Later Soul\n\nThis must not rewrite prepared history.",updated.Version,"settings","context-later");
        Assert.Equal(Wire.Pack(context),Wire.Pack(await runtime.PrepareExecutionContext(run.Id,default)));
    }
    [Fact]public async Task PreparedContextFreezesTheCurrentUserVersion()
    {
        var original=store.User();var updated=store.UpdateUser("# User\n\n- Prefers short, direct answers.",original.Version,"settings","context-user-fixture");
        var run=CapabilityTests.CreateWorkerRun(store);run.Profile=PolicyProfile.Baseline;store.Save(run,"test.custom-user",new{});
        var context=await runtime.PrepareExecutionContext(run.Id,default);
        Assert.Contains("Prefers short, direct answers",context.Text);Assert.Equal(updated.Version,context.UserDigest);
        store.UpdateUser("# User\n\n- Later direct edit.",updated.Version,"settings","context-user-later");
        Assert.Equal(Wire.Pack(context),Wire.Pack(await runtime.PrepareExecutionContext(run.Id,default)));
    }
    [Fact]public async Task ProfileCannotChangeAfterContextPreparation()
    {
        var run=CapabilityTests.CreateWorkerRun(store);run.Profile=PolicyProfile.Baseline;store.Save(run,"test.baseline",new{});
        await runtime.PrepareExecutionContext(run.Id,default);
        run=store.Get(run.Id)!;run.Profile=PolicyProfile.Evidence;store.Save(run,"test.changed",new{});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>runtime.PrepareExecutionContext(run.Id,default));
    }
    [Fact]public async Task FailedContextPreparationRetainsReadChargesWithoutReleasingPartialContext()
    {
        store.Write("notes/source.md","An available source","absent");var run=CapabilityTests.CreateWorkerRun(store);
        run.Goal=run.Goal with{ReadScope=["notes/source.md","notes/missing.md"]};store.Save(run,"test.scope",new{});
        await Assert.ThrowsAsync<ArgumentException>(()=>runtime.PrepareExecutionContext(run.Id,default));
        Assert.Equal(2,store.Get(run.Id)!.ToolCalls);Assert.Single(store.Get(run.Id)!.Evidence);Assert.Null(store.Get(run.Id)!.PreparedContext);
    }
    public void Dispose(){store.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
}
