using Thaddeus.Core;
using Thaddeus.Infrastructure;
namespace Thaddeus.Tests;

public sealed class ReceiptPaginationTests
{
    [Fact] public void LongHistory_PagesWithoutLossAndExportIncludesEveryRun()
    {
        var root=Path.Combine(Path.GetTempPath(),"thaddeus-receipts-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var store=new Store(root);
            var run=new Run { Goal=new("Long receipt history",[],"plans/",[],new(),new()) };
            var other=new Run { Goal=new("Interleaved run",[],"plans/",[],new(),new()) };
            for(var i=0;i<2005;i++) { store.Save(run,"test.receipt",new{i});if(i==1000)store.Save(other,"test.other",new{}); }
            var first=store.Events(0,run.Id);var second=store.Events(first[^1].Cursor,run.Id);
            Assert.Equal(2000,first.Count);Assert.Equal(5,second.Count);
            Assert.Equal(Enumerable.Range(1,2005).Select(i=>(long)i),first.Concat(second).Select(e=>e.Sequence));
            var exported=store.AllEvents();Assert.Equal(2006,exported.Count);
            Assert.Equal(2006,exported.Select(e=>e.EventId).Distinct().Count());
            Assert.Single(exported,e=>e.RunId==other.Id);
            Assert.Empty(store.Pages());Assert.Equal(2005,store.Get(run.Id)!.Version);
        }
        finally {Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
    }
}
