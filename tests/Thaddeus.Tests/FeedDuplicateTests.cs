using Microsoft.Data.Sqlite;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class FeedDuplicateTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-feed-duplicates-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026,9,16,12,0,0,TimeSpan.Zero);
    private const string Source = "https://news.example.org/rss";
    private const string Url = "https://news.example.org/story";
    private static FeedFetch Page(params FeedEntryPreview[] entries) => new(Source,new("Gazette",entries,false,null));
    private static FeedEntryPreview Story(string key,string? url=Url,string title="One story") => new(key,title,"An excerpt",url,Now);
    private static FeedEntry Seed(Store store){store.Subscribe(Source,Now);store.FinishFeedRefresh(store.ClaimFeedRefresh(Now)!,Page(Story("original")),Now);return store.Feeds().Entries.Single();}
    private void Insert(FeedEntry entry)
    {
        using var db=new SqliteConnection("Data Source="+Path.Combine(root,"ledger.sqlite")+";Pooling=False");db.Open();using var command=db.CreateCommand();
        command.CommandText="INSERT INTO feed_entries VALUES($id,$subscription,$body)";command.Parameters.AddWithValue("$id",entry.Id);command.Parameters.AddWithValue("$subscription",entry.SubscriptionId);command.Parameters.AddWithValue("$body",Wire.Pack(entry));command.ExecuteNonQuery();
    }
    [Fact] public void PublisherIdentityChangesKeepOneStoryAndItsReadingState()
    {
        using var store=new Store(root);var original=Seed(store);
        store.ReadFeedEntry(original.Id,original.Version,true);original=store.Feeds().Entries.Single();var saved=store.SaveFeedEntry(original.Id,original.Version);store.RecordFeedInteraction(original.Id,"absent","more",Now);
        store.FinishFeedRefresh(store.ClaimFeedRefresh(Now.AddHours(2))!,Page(Story("replacement",title:"Updated headline")),Now.AddHours(2));
        var updated=Assert.Single(store.Feeds().Entries);Assert.Equal(original.Id,updated.Id);Assert.Equal("Updated headline",updated.Title);Assert.True(updated.Read);Assert.Equal(saved.Id,updated.SavedItemId);Assert.Equal(1,updated.Engagement!.Preference);
        store.FinishFeedRefresh(store.ClaimFeedRefresh(Now.AddHours(4))!,Page(Story("third"),Story("fourth")),Now.AddHours(4));
        Assert.Single(store.Feeds().Entries);Assert.Single(store.Library());Assert.Empty(store.List());
    }
    [Fact] public void LegacyDuplicatesRemainStoredButShareReadingFeedbackAndSavedLinks()
    {
        using var store=new Store(root);var original=Seed(store);
        var duplicate=original with {Id=new string('a',64),Key="old-alias",Read=true,Engagement=new(Opened:Now,Preference:-1,Preferred:Now)};Insert(duplicate);
        var saved=store.EditLibrary(duplicate.Id[..32],new("feed",duplicate.Title,"Keep my saved note","open",Url,null,"absent"));
        var visible=Assert.Single(store.Feeds().Entries);Assert.True(visible.Read);Assert.Equal(-1,visible.Engagement!.Preference);Assert.Equal(saved.Id,visible.SavedItemId);
        Assert.Equal(saved.Id,store.SaveFeedEntry(visible.Id,visible.Version).Id);store.RecordFeedInteraction(visible.Id,"absent","save",Now);store.RecordFeedInteraction(visible.Id,"absent","clear",Now.AddMinutes(1));
        visible=store.Feeds().Entries.Single();Assert.Equal(0,visible.Engagement!.Preference);Assert.NotNull(visible.Engagement.Saved);
        store.ReadFeedEntry(visible.Id,visible.Version,false);Assert.False(store.Feeds().Entries.Single().Read);
        Assert.Throws<InvalidOperationException>(()=>store.ReadFeedEntry(visible.Id,visible.Version,true));
        using var db=new SqliteConnection("Data Source="+Path.Combine(root,"ledger.sqlite")+";Pooling=False");db.Open();using var command=db.CreateCommand();command.CommandText="SELECT count(*) FROM feed_entries";Assert.Equal(2L,command.ExecuteScalar());
        Assert.Equal("Keep my saved note",store.Library().Single().Content);
    }
    [Fact] public void RefreshKeepsLegacyReadingAndSavedLinkWhenOldAliasesAgeOut()
    {
        using var store=new Store(root);var original=Seed(store);
        var duplicate=original with {Id=new string('f',64),Key="old-alias",Read=true,Engagement=new(Opened:Now,Preference:1,Preferred:Now)};Insert(duplicate);
        var saved=store.EditLibrary(duplicate.Id[..32],new("feed",duplicate.Title,"Keep this saved note","open",Url,null,"absent"));
        var incoming=Enumerable.Range(1,FeedParser.MaxEntries-1).Select(index=>Story("new-"+index,"https://news.example.org/"+index)).Append(Story("replacement")).ToArray();
        store.FinishFeedRefresh(store.ClaimFeedRefresh(Now.AddHours(2))!,Page(incoming),Now.AddHours(2));
        var visible=store.Feeds().Entries.Single(row=>row.Url==Url);Assert.Equal(original.Id,visible.Id);Assert.True(visible.Read);Assert.Equal(1,visible.Engagement!.Preference);Assert.Equal(saved.Id,visible.SavedItemId);
        Assert.Equal(saved.Id,store.SaveFeedEntry(visible.Id,visible.Version).Id);store.RecordFeedInteraction(visible.Id,"absent","save",Now.AddHours(2));Assert.Single(store.Library());Assert.Equal(FeedParser.MaxEntries,store.Feeds().Entries.Length);
    }
    [Fact] public void MissingUrlsAndSeparateSubscriptionsAreNotCollapsed()
    {
        using var store=new Store(root);var original=Seed(store);
        Insert(original with {Id=new string('b',64),Key="missing-one",Url=null});Insert(original with {Id=new string('c',64),Key="missing-two",Url=null});
        var other=store.Subscribe("https://other.example.org/rss",Now);Insert(original with {Id=new string('d',64),SubscriptionId=other.Id,Key="separate-source"});
        Assert.Equal(4,store.Feeds().Entries.Length);
    }
    public void Dispose(){SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
}
