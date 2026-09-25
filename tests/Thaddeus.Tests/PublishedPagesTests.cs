using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class PublishedPagesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "published-pages-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private static string Id() => Guid.NewGuid().ToString("N");
    private static AppDefinition Page(string title, string html) =>
        new(title, "Campaign landing page", [new AppField("note", "Note", "text")], [], null, new AppPage(html, "h1{color:#5b4cdb}", ""));
    private static ArtifactApp Create(Store store, AppDefinition definition) =>
        store.EditArtifact(Id(), new AppEdit(Id(), "absent", definition, [], [], null));

    [Fact] public void PublishingFreezesTheExactVersionUntilRepublished()
    {
        using var store = new Store(root); var pages = new PublishedPages(store);
        var app = Create(store, Page("Spring launch", "<h1>Spring</h1>"));
        var published = pages.Publish(app.Id, new(Id(), "spring-launch"), "owner");
        Assert.Equal(app.Version, published.ArtifactVersion);
        var edited = store.EditArtifact(app.Id, new AppEdit(Id(), app.Version, Page("Spring launch", "<h1>Edited</h1>"), [], [], null));
        Assert.Contains("<h1>Spring</h1>", PublishedPages.Render(pages.Find("spring-launch")!));
        var republished = pages.Publish(app.Id, new(Id(), "spring"), "owner");
        Assert.Equal(edited.Version, republished.ArtifactVersion);
        Assert.Null(pages.Find("spring-launch"));
        Assert.Contains("<h1>Edited</h1>", PublishedPages.Render(pages.Find("spring")!));
        Assert.True(pages.Unpublish("spring"));
        Assert.Empty(pages.List());
        Assert.False(pages.Unpublish("spring"));
    }

    [Fact] public void AddressesAreValidatedUniqueAndRequestsReplay()
    {
        using var store = new Store(root); var pages = new PublishedPages(store);
        var first = Create(store, Page("One", "<p>One</p>"));
        var second = Create(store, Page("Two", "<p>Two</p>"));
        var request = new PublishRequest(Id(), "launch");
        var published = pages.Publish(first.Id, request, "owner");
        Assert.Equal(published, pages.Publish(first.Id, request, "owner"));
        Assert.Throws<InvalidOperationException>(() => pages.Publish(second.Id, new(Id(), "launch"), "owner"));
        foreach (var slug in new[] { "", "-bad", "bad-", "Has Space", "../up", new string('a', 61) })
            Assert.Throws<ArgumentException>(() => pages.Publish(second.Id, new(Id(), slug), "owner"));
        Assert.Throws<ArgumentException>(() => pages.Publish(Id(), new(Id(), "missing"), "owner"));
        var tool = Create(store, new AppDefinition("Log", "Decisions", [new AppField("note", "Note", "text")], []));
        Assert.Throws<ArgumentException>(() => pages.Publish(tool.Id, new(Id(), "log"), "owner"));
        store.EditArtifact(second.Id, new AppEdit(Id(), second.Version, null, [], [], true));
        Assert.Throws<InvalidOperationException>(() => pages.Publish(second.Id, new(Id(), "two"), "owner"));
    }

    [Fact] public void RenderedPagesAreSandboxedAndEscapeTheirMetadata()
    {
        using var store = new Store(root); var pages = new PublishedPages(store);
        var app = Create(store, Page("<script>alert(1)</script>", "<main>Hi</main>"));
        var html = PublishedPages.Render(pages.Publish(app.Id, new(Id(), "hi"), "owner"));
        Assert.Contains("<title>&lt;script&gt;alert(1)&lt;/script&gt;</title>", html);
        Assert.Contains("window.thaddeus=Object.freeze", html);
        Assert.StartsWith("sandbox allow-scripts", PublishedPages.Policy);
        Assert.DoesNotContain("allow-same-origin", PublishedPages.Policy);
        Assert.Contains("connect-src 'none'", PublishedPages.Policy);
    }
}
