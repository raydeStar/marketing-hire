using System.Net;
using System.Text;
using System.Xml;
using Microsoft.Data.Sqlite;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class FeedTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-feed-" + Guid.NewGuid().ToString("N"));
    private static readonly Uri Source = new("https://news.example.org/rss");
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private static string Rss(string items, string channel = "") => $"<rss version=\"2.0\"><channel><title>The Gazette</title>{channel}{items}</channel></rss>";
    private static FeedFetch Page(params int[] ids) => new(Source.AbsoluteUri, new("Gazette", ids.Select(id => new FeedEntryPreview(id.ToString(), "Story " + id, "Untrusted source excerpt", $"https://news.example.org/{id}", null)).ToArray(), false, null));
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) => send(request, cancellation); }
    private static HttpResponseMessage Response(string text, string type = "application/rss+xml") => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, type) };
    private sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes)
    { public override bool CanSeek => false; }
    private sealed class WaitingReader : IPublicFeedReader
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<FeedFetch> Read(string url, bool discover, string? validatorUrl, string? etag, DateTimeOffset? modified, CancellationToken cancellation)
        { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellation); return new(url); }
    }
    [Fact] public async Task RefreshAndPreviewShareOneRequestSlotAndShutdownAwaitsCancellation()
    {
        using var store = new Store(root); var reader = new WaitingReader(); using var refresh = new FeedRefresh(store, reader);
        var now = DateTimeOffset.UtcNow; store.Subscribe(Source.AbsoluteUri, now);
        using var stop = new CancellationTokenSource(); var pending = refresh.Tick(now, stop.Token);
        await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => refresh.Preview(Source.AbsoluteUri, default));
        await refresh.Tick(now, default); // An occupied slot is skipped, never queued without a bound.
        stop.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(store.Feeds().Entries); Assert.Null(store.Feeds().Subscriptions.Single().LastChecked);
        Assert.Null(store.ClaimFeedRefresh(now.AddMinutes(1)));
    }
    [Fact] public async Task SchemaFourUpgradeAndBackupPreserveSavedReadingAndFeedState()
    {
        var data = Path.Combine(root, "data"); var now = DateTimeOffset.UtcNow; LibraryItem saved;
        using (var store = new Store(data)) saved = store.EditLibrary(Guid.NewGuid().ToString("N"), new("feed", "Existing reading", "Keep this note", "open", null, null, "absent"));
        using (var database = new SqliteConnection($"Data Source={Path.Combine(data, "ledger.sqlite")};Pooling=False"))
        {
            database.Open(); using var command = database.CreateCommand();
            command.CommandText = "DROP TABLE feed_entries; DROP TABLE feed_subscriptions; DELETE FROM schema_migrations WHERE version>=5; PRAGMA user_version=4;"; command.ExecuteNonQuery();
        }
        string before;
        using (var upgraded = new Store(data))
        {
            Assert.Equal(saved, upgraded.Library().Single()); Assert.Empty(upgraded.Feeds().Subscriptions);
            upgraded.Subscribe(Source.AbsoluteUri, now); upgraded.FinishFeedRefresh(upgraded.ClaimFeedRefresh(now)!, Page(1), now); before = Wire.Pack(upgraded.Feeds());
        }
        await StudyBackup.Create(data, Path.Combine(root, "backup"));
        await StudyBackup.Restore(Path.Combine(root, "backup"), Path.Combine(root, "restored"));
        using var restored = new Store(Path.Combine(root, "restored")); Assert.Equal(before, Wire.Pack(restored.Feeds())); Assert.Equal(saved, restored.Library().Single());
    }

    [Fact] public void RssSanitizesMarkupDeduplicatesIdentityAndBoundsRetention()
    {
        var story = "<item><guid isPermaLink=\"false\">first</guid><title>A &amp; B</title><link>/story</link><description><![CDATA[<p>Hello <b>reader</b></p><script>steal()</script><iframe src='https://evil.example/a'>hidden</iframe><div hidden>secret</div>]]></description><pubDate>Mon, 14 Sep 2026 12:00:00 GMT</pubDate></item>";
        var feed = FeedParser.Parse(Bytes(Rss(story + story + string.Concat(Enumerable.Range(2, 105).Select(i => $"<item><guid>{i}</guid><title>Story {i}</title></item>")), "<ttl>5</ttl>")), Source);
        Assert.Equal("The Gazette", feed.Title); Assert.Equal(60, feed.RefreshMinutes); Assert.True(feed.Truncated); Assert.Equal(99, feed.Entries.Length);
        var first = feed.Entries[0]; Assert.Equal("A & B", first.Title); Assert.Equal("Hello reader", first.Summary); Assert.Equal("https://news.example.org/story", first.Url);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero), first.Published);
    }
    [Fact] public void AtomHonorsXmlBaseAndTextTypesWithoutExecutingMarkup()
    {
        var xml = """
        <feed xmlns="http://www.w3.org/2005/Atom" xml:base="https://news.example.org/blog/">
          <title type="text">A &lt;b&gt;literal&lt;/b&gt; title</title>
          <entry xml:base="2026/"><id>urn:story:one</id><title type="html">A &lt;b&gt;bold&lt;/b&gt; title</title>
          <summary type="xhtml"><div xmlns="http://www.w3.org/1999/xhtml"><p>Visible</p><script>hidden</script><p>words</p></div></summary>
          <link href="one"/><updated>2026-09-14T10:00:00-06:00</updated></entry>
          <entry><id>urn:story:two</id><title>Unsafe link</title><link href="javascript:alert(1)"/></entry>
        </feed>
        """;
        var feed = FeedParser.Parse(Bytes(xml), Source);
        Assert.Equal("A <b>literal</b> title", feed.Title); Assert.Equal("A bold title", feed.Entries[0].Title);
        Assert.Equal("Visible words", feed.Entries[0].Summary); Assert.Equal("https://news.example.org/blog/2026/one", feed.Entries[0].Url);
        Assert.Null(feed.Entries[1].Url);
    }
    [Theory]
    [InlineData("<!DOCTYPE rss [<!ENTITY theft SYSTEM 'file:///C:/private'>]><rss version='2.0'><channel><title>&theft;</title></channel></rss>")]
    [InlineData("<rss version='1.0'><channel/></rss>")]
    [InlineData("<feed xmlns='urn:other'><title>Other XML</title></feed>")]
    public void UnsupportedXmlAndEntitiesAreRefused(string xml) => Assert.Throws<XmlException>(() => FeedParser.Parse(Bytes(xml), Source));
    [Fact] public void DepthBytesSummaryAndCancellationAreBounded()
    {
        Assert.Throws<XmlException>(() => FeedParser.Parse(Bytes(Rss(string.Concat(Enumerable.Repeat("<x>", 35)) + string.Concat(Enumerable.Repeat("</x>", 35)))), Source));
        Assert.Throws<ArgumentException>(() => FeedParser.Parse(new byte[FeedParser.MaxBytes + 1], Source));
        var feed = FeedParser.Parse(Bytes(Rss("<item><description>" + new string('a', 5000) + "</description></item>")), Source);
        Assert.Equal(1200, feed.Entries.Single().Summary.Length); Assert.Equal(160, feed.Entries.Single().Title.Length);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => FeedParser.Parse(Bytes(Rss("")), Source, cancel.Token));
    }
    [Fact] public void SiteDiscoveryOnlyReturnsExplicitSafeAlternatesWithoutFetchingThem()
    {
        var links = FeedParser.Discover("<base href='https://feeds.example.org/'><link rel='alternate' type='application/atom+xml' title='Weekly' href='weekly.xml'><link rel='ALTERNATE' type='application/atom+xml' href='weekly.xml'><link rel='alternate' type='application/rss+xml' href='http://insecure.example.org/rss'><link rel='alternate' type='application/rss+xml' href='https://localhost/rss'><script>fetch('/secret')</script>", Source);
        Assert.Single(links); Assert.Equal("https://feeds.example.org/weekly.xml", links[0].Url); Assert.Equal("Weekly", links[0].Title);
    }
    [Theory]
    [InlineData("http://news.example.org/rss")]
    [InlineData("https://127.0.0.1/rss")]
    [InlineData("https://host.local/rss")]
    [InlineData("https://user:pass@news.example.org/rss")]
    [InlineData("https://news.example.org:9443/rss")]
    public void InvalidSubscriptionDoesNotReachTransportOrStorage(string url)
    { using var store = new Store(root); Assert.Throws<ArgumentException>(() => store.Subscribe(url, DateTimeOffset.UtcNow)); Assert.Empty(store.Feeds().Subscriptions); }
    [Fact] public async Task ValidatorsBelongToTheFinalUrlAndRequestsCarryNoCredentials()
    {
        var visited = new List<string>();
        var reader = new PublicFeedReader(() => new Handler((request, _) =>
        {
            visited.Add(request.RequestUri!.AbsoluteUri); Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie"));
            if (request.RequestUri.AbsolutePath == "/rss")
            { Assert.Empty(request.Headers.IfNoneMatch); var redirect = new HttpResponseMessage(HttpStatusCode.MovedPermanently); redirect.Headers.Location = new("/final", UriKind.Relative); return Task.FromResult(redirect); }
            Assert.Equal("\"v1\"", request.Headers.IfNoneMatch.Single().ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        }));
        var result = await reader.Read(Source.AbsoluteUri, false, "https://news.example.org/final", "\"v1\"", null, default);
        Assert.True(result.NotModified); Assert.Null(result.Error); Assert.Equal(2, visited.Count);
    }
    [Theory]
    [InlineData("https://other.example.org/rss")]
    [InlineData("https://127.0.0.1/rss")]
    [InlineData("http://news.example.org/rss")]
    public async Task RedirectCannotExpandTheSubscriptionHost(string target)
    {
        var calls = 0; var reader = new PublicFeedReader(() => new Handler((_, _) =>
        { calls++; var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new(target); return Task.FromResult(response); }));
        Assert.NotNull((await reader.Read(Source.AbsoluteUri, true, null, null, null, default)).Error); Assert.Equal(1, calls);
    }
    [Fact] public async Task ReaderBoundsUnknownLengthBodiesAndDoesNotFollowDiscoveredSources()
    {
        var calls = 0; var reader = new PublicFeedReader(() => new Handler((_, _) =>
        { calls++; return Task.FromResult(Response("<link rel='alternate' type='application/rss+xml' href='https://other.example.org/rss'>", "text/html")); }));
        var discovered = await reader.Read(Source.AbsoluteUri, true, null, null, null, default);
        Assert.Single(discovered.Candidates!); Assert.Equal(1, calls);
        Assert.NotNull((await reader.Read(Source.AbsoluteUri, false, null, null, null, default)).Error);
        var oversized = new PublicFeedReader(() => new Handler((_, _) =>
        {
            var response = Response(""); response.Content = new StreamContent(new UnknownLengthStream(new byte[FeedParser.MaxBytes + 1]));
            Assert.Null(response.Content.Headers.ContentLength);
            response.Content.Headers.ContentType = new("application/rss+xml"); return Task.FromResult(response);
        }));
        Assert.NotNull((await oversized.Read(Source.AbsoluteUri, false, null, null, null, default)).Error);
    }
    [Fact] public async Task RetryAfterAndCancellationAreHonoredWithoutAutomaticRetries()
    {
        var calls = 0; var reader = new PublicFeedReader(() => new Handler((_, _) =>
        { calls++; var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new(TimeSpan.FromHours(6)); return Task.FromResult(response); }));
        var failed = await reader.Read(Source.AbsoluteUri, false, null, null, null, default);
        Assert.Equal(360, failed.RefreshMinutes); Assert.NotNull(failed.Error); Assert.Equal(1, calls);
        var pending = new PublicFeedReader(() => new Handler(async (_, cancellation) => { await Task.Delay(Timeout.Infinite, cancellation); return Response(""); }));
        using var cancel = new CancellationTokenSource(); var task = pending.Read(Source.AbsoluteUri, false, null, null, null, cancel.Token); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
    [Fact] public void EntriesAreBoundedReadStateSurvivesAndSavedNotesOutliveRemovalAndRestart()
    {
        var now = DateTimeOffset.UtcNow; string savedId;
        using (var store = new Store(root))
        {
            var subscription = store.Subscribe(Source.AbsoluteUri, now); var claim = store.ClaimFeedRefresh(now)!;
            Assert.True(store.FinishFeedRefresh(claim, Page(Enumerable.Range(0, 100).ToArray()), now));
            var first = store.Feeds().Entries.Single(entry => entry.Key == "99");
            first = store.ReadFeedEntry(first.Id, first.Version, true);
            savedId = store.SaveFeedEntry(first.Id, first.Version).Id;
            Assert.Equal(savedId, store.SaveFeedEntry(first.Id, first.Version).Id); Assert.Single(store.Library());
            claim = store.ClaimFeedRefresh(now.AddHours(1))!;
            store.FinishFeedRefresh(claim, Page(99, 100, 101), now.AddHours(1));
            Assert.Equal(100, store.Feeds().Entries.Length); Assert.True(store.Feeds().Entries.Single(entry => entry.Key == "99").Read);
            Assert.Equal(savedId, store.Feeds().Entries.Single(entry => entry.Key == "99").SavedItemId);
            store.RemoveSubscription(subscription.Id, store.Feeds().Subscriptions.Single().Version);
            Assert.Empty(store.Feeds().Entries); Assert.Single(store.Library()); Assert.Empty(store.List()); Assert.Empty(store.AllEvents());
        }
        using var reopened = new Store(root); Assert.Equal(savedId, reopened.Library().Single().Id); Assert.Empty(reopened.Feeds().Subscriptions);
        reopened.DeletePersonalData(); Assert.Empty(reopened.Library()); Assert.Empty(reopened.Feeds().Entries);
    }
    [Fact] public void LateResponsesCannotResurrectPausedRemovedOrDeletedSubscriptions()
    {
        using var store = new Store(root); var now = DateTimeOffset.UtcNow;
        var added = store.Subscribe(Source.AbsoluteUri, now); var claim = store.ClaimFeedRefresh(now)!;
        var paused = store.ChangeSubscription(added.Id, claim.Version, true, now);
        Assert.False(store.FinishFeedRefresh(claim, Page(1), now)); Assert.Null(store.ClaimFeedRefresh(now.AddDays(1)));
        store.RemoveSubscription(paused.Id, paused.Version); Assert.False(store.FinishFeedRefresh(claim, Page(1), now));
        store.Subscribe(Source.AbsoluteUri, now); claim = store.ClaimFeedRefresh(now)!; store.DeletePersonalData();
        Assert.False(store.FinishFeedRefresh(claim, Page(2), now)); Assert.Empty(store.Feeds().Subscriptions); Assert.Empty(store.Feeds().Entries);
    }
    [Fact] public void FailedRefreshKeepsEntriesAndValidatorsAndPersistsItsBackoff()
    {
        using var store = new Store(root); var now = DateTimeOffset.UtcNow; store.Subscribe(Source.AbsoluteUri, now);
        store.FinishFeedRefresh(store.ClaimFeedRefresh(now)!, Page(1) with { ETag = "\"one\"" }, now);
        var claim = store.ClaimFeedRefresh(now.AddHours(1))!;
        store.FinishFeedRefresh(claim, new(Source.AbsoluteUri, Error: "HTTP 429", RefreshMinutes: 360), now.AddHours(1));
        var item = store.Feeds().Subscriptions.Single(); Assert.Single(store.Feeds().Entries); Assert.Equal("\"one\"", item.ETag);
        Assert.Equal(now.AddHours(7), item.NextRefresh); Assert.Null(store.ClaimFeedRefresh(now.AddHours(6)));
        Assert.Throws<InvalidOperationException>(() => store.QueueFeedRefresh(item.Id, item.Version, now.AddHours(2)));
    }
    [Fact] public void FeedCommitRollsBackEntriesAndRefreshMetadataTogether()
    {
        using var store = new Store(root, point => { if (point == "before-feed-commit") throw new IOException("Fictional interruption"); });
        var now = DateTimeOffset.UtcNow; store.Subscribe(Source.AbsoluteUri, now); var claim = store.ClaimFeedRefresh(now)!; var before = store.Feeds();
        Assert.Throws<IOException>(() => store.FinishFeedRefresh(claim, Page(1), now));
        Assert.Equal(Wire.Pack(before), Wire.Pack(store.Feeds()));
    }
    [Fact] public void DuplicateLimitAndStaleSubscriptionChangesDoNotLoseData()
    {
        using var store = new Store(root); var now = DateTimeOffset.UtcNow; var original = store.Subscribe(Source.AbsoluteUri, now);
        Assert.Throws<InvalidOperationException>(() => store.Subscribe(Source.AbsoluteUri + "#fragment", now));
        var paused = store.ChangeSubscription(original.Id, original.Version, true, now);
        Assert.Throws<InvalidOperationException>(() => store.RemoveSubscription(original.Id, original.Version));
        for (var i = 1; i < Store.MaxSubscriptions; i++) store.Subscribe(Source.AbsoluteUri + "?feed=" + i, now);
        Assert.Throws<InvalidOperationException>(() => store.Subscribe(Source.AbsoluteUri + "?overflow", now));
        Assert.Equal(paused, store.Feeds().Subscriptions[0]);
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
}
