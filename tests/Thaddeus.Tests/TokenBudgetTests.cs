using Thaddeus.Core;
using Thaddeus.Infrastructure;
namespace Thaddeus.Tests;
public sealed class TokenBudgetTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"thaddeus-budget-"+Guid.NewGuid().ToString("N"));
    private readonly Store store;
    public TokenBudgetTests()=>store=new(root);
    private Runtime Runtime(IModelProvider p)=>new(store,_=>p,new PlanValidator(),new EvidencePolicy());
    [Fact]public async Task StrictMode_RejectsUncertifiedProviderBeforeDispatch()
    {
        var p=new MeteredProvider(false);var rt=Runtime(p);var r=rt.Converse("hello",new(),new(RequireCertifiedTokenBound:true));await rt.Execute(r.Id);
        Assert.Equal(0,p.Calls);Assert.Equal(RunState.Failed,store.Get(r.Id)!.State);Assert.Contains("No inference",store.Get(r.Id)!.Summary);
    }
    [Fact]public async Task CertifiedQuote_ReservesBeforeDispatchAndSettlesActual()
    {
        var p=new MeteredProvider(true);var rt=Runtime(p);var r=rt.Converse("hello",new(),new(MaxTotalTokens:20,RequireCertifiedTokenBound:true));await rt.Execute(r.Id);
        Assert.Equal(1,p.Calls);Assert.Equal(14,store.Get(r.Id)!.ChargedTokens);Assert.Equal(0,store.Get(r.Id)!.ReservedTokens);
    }
    [Fact]public async Task InsufficientBudget_NeverCallsProvider()
    {
        var p=new MeteredProvider(true);var rt=Runtime(p);var r=rt.Converse("hello",new(),new(MaxTotalTokens:10));await rt.Execute(r.Id);Assert.Equal(0,p.Calls);
    }
    [Fact]public async Task UnknownUsage_ConsumesFullReservation()
    {
        var p=new MeteredProvider(false,unknown:true);var rt=Runtime(p);var r=rt.Converse("hello",new(),new(MaxTotalTokens:100));await rt.Execute(r.Id);
        Assert.Equal(100,store.Get(r.Id)!.ChargedTokens);Assert.Null(store.Get(r.Id)!.InputTokens);
    }
    [Fact]public async Task FailedDispatch_DoesNotRefundUnknownUsage()
    {
        var p=new MeteredProvider(false,fail:true);var rt=Runtime(p);var r=rt.Converse("hello",new(),new(MaxTotalTokens:100));await rt.Execute(r.Id);
        Assert.Equal(100,store.Get(r.Id)!.ChargedTokens);Assert.Equal(RunState.Failed,store.Get(r.Id)!.State);
    }
    [Fact]public async Task ReportedOverrun_StopsBeforeSuccess()
    {
        var p=new MeteredProvider(false);var rt=Runtime(p);var r=rt.Converse("hello",new(),new(MaxTotalTokens:10));await rt.Execute(r.Id);
        Assert.Equal(14,store.Get(r.Id)!.ChargedTokens);Assert.Equal(RunState.Failed,store.Get(r.Id)!.State);Assert.Single(store.Chats());
    }
    public void Dispose(){store.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
    private sealed class MeteredProvider(bool certified,bool unknown=false,bool fail=false):IModelProvider
    {
        public int Calls;
        public TokenQuote Quote(Observation o)=>certified?new(10,true,"Deterministic test provider ceiling",10):new(null,false,"Uncertified");
        public Task<ModelReply> Respond(Observation o,Func<string,Task> delta,CancellationToken c){Calls++;if(fail)throw new HttpRequestException();return Task.FromResult(new ModelReply(null,"hello",unknown?null:10,unknown?null:4));}
    }
}
