using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class CompanyWikiTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "company-wiki-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private static WikiChange Change(string scope, string scopeId, string title, string body, string status = "active", string? id = null, int version = 0) =>
        new(Guid.NewGuid().ToString("N"), id, version, scope, scopeId, title, body, "fact", status);

    [Fact] public void PublishedLayersAreIsolatedAndPinnedAcrossEdits()
    {
        using var store = new Store(root); var wiki = new CompanyWiki(store, new OrganizationDirectory(store));
        var shared = wiki.Save(Change("company", "company", "Company truth", "Only local drafts are permitted."), "owner");
        var marketing = wiki.Save(Change("department", "marketing", "Marketing playbook", "Ask founders about their first customer."), "owner");
        var privateMarketing = wiki.Save(Change("member", "marketing-main", "Private note", "Interview angles are uncertain."), "owner");
        wiki.Save(Change("member", "ceo", "CEO rubric", "Question unsupported claims."), "owner");
        wiki.Save(Change("company", "company", "Unpublished", "Never enter an agent prompt.", "draft"), "owner");
        var ceo = wiki.Capture("ceo", "ceo", "first customers");
        var employee = wiki.Capture("marketing-main", "marketing", "first customers");
        Assert.Contains(ceo.Pages, page => page.Id == shared.Id);
        Assert.DoesNotContain(ceo.Pages, page => page.Id == marketing.Id || page.Id == privateMarketing.Id);
        Assert.Contains(employee.Pages, page => page.Id == marketing.Id);
        Assert.Contains(employee.Pages, page => page.Id == privateMarketing.Id);
        Assert.DoesNotContain(wiki.Render(ceo), "Interview angles");
        Assert.DoesNotContain(wiki.Render(employee), "Question unsupported claims");
        Assert.DoesNotContain(wiki.Render(employee), "Never enter an agent prompt");
        wiki.Save(Change("company", "company", "Company truth", "Revised after the meeting.", id: shared.Id, version: 1), "owner");
        Assert.Contains("Only local drafts are permitted", wiki.Render(ceo));
        Assert.DoesNotContain("Revised after the meeting", wiki.Render(ceo));
        Assert.Equal(2, wiki.History(shared.Id).Length);
    }

    [Fact] public void VersionAndScopeChangesDoNotRewriteHistory()
    {
        using var store = new Store(root); var wiki = new CompanyWiki(store, new OrganizationDirectory(store));
        var page = wiki.Save(Change("company", "company", "Policy", "No spending."), "owner");
        Assert.Throws<InvalidOperationException>(() => wiki.Save(Change("company", "company", "Policy", "Spend freely.", id: page.Id, version: 0), "owner"));
        Assert.Throws<InvalidOperationException>(() => wiki.Save(Change("member", "ceo", "Policy", "Changed layer.", id: page.Id, version: 1), "owner"));
        Assert.Single(wiki.History(page.Id));
    }
}
