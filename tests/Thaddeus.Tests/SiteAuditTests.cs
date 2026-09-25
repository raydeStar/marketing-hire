using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class SiteAuditTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "site-audit-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++) { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }

    static string Page(string title, string description, string body, string extra = "") =>
        $"<html><head><title>{title}</title>{(description.Length > 0 ? $"<meta name=\"description\" content=\"{description}\">" : "")}{extra}</head><body>{body}</body></html>";
    static readonly string Words = string.Join(" ", Enumerable.Repeat("Founders use the employee to plan and draft their marketing every week.", 30));

    static readonly Dictionary<string, (int Status, string Type, string Body)> Site = new()
    {
        ["https://acme.test/robots.txt"] = (200, "text/plain", "User-agent: *\nAllow: /"),
        ["https://acme.test/sitemap.xml"] = (404, "text/html", ""),
        ["https://acme.test/"] = (200, "text/html", Page("Acme — the marketing employee that asks first", "Acme drafts, researches and asks before anything goes out.",
            $"<h1>Acme</h1><p>{Words}</p><a href=\"/pricing\">Pricing</a> <a href=\"/about#team\">About</a> <a href=\"/old-page\">Old</a> <a href=\"https://elsewhere.test/\">Out</a> <img src=\"/a.png\" alt=\"Dashboard\">")),
        ["https://acme.test/pricing"] = (200, "text/html", Page("Acme — the marketing employee that asks first", "", $"<h1>Pricing</h1><h1>Plans</h1><p>{Words}</p><img src=\"/b.png\"><a href=\"/\">Home</a>")),
        ["https://acme.test/about"] = (200, "text/html", Page("About", "About Acme.", "<p>Short.</p>", "<meta name=\"robots\" content=\"noindex\">")),
        ["https://acme.test/old-page"] = (404, "text/html", ""),
    };

    [Fact] public async Task TheSiteCheckReportsWhatAPersonWouldFixAndReadsOnlyListedSites()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host")); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:ShiftPump", "off");
        });
        var audit = factory.Services.GetRequiredService<SiteAudit>();
        var asked = new List<string>();
        audit.Pause = TimeSpan.Zero;
        audit.Probe = (url, sites, _) =>
        {
            asked.Add(url);
            Assert.Equal(["acme.test"], sites);
            return Task.FromResult(Site.TryGetValue(url, out var page) ? (page.Status, new Uri(url), page.Type, page.Body, 0) : (404, new Uri(url), "text/html", "", 0));
        };
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var owner = factory.Services.GetRequiredService<Security>().Issue(context, "Owner", true);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);

        // Only sites the owner listed can be checked.
        using (var unlisted = await client.PostAsJsonAsync("/api/site-audit", new { site = "acme.test" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unlisted.StatusCode);
            Assert.Contains("research sites", await unlisted.Content.ReadAsStringAsync());
        }
        Assert.Empty(asked);
        using (var saved = await client.PutAsJsonAsync("/api/objectives", new { expectedVersion = 0, content = new { objectives = Array.Empty<object>(), competitors = Array.Empty<object>(), currentFocus = "", nonGoals = Array.Empty<string>(), researchSites = new[] { "https://acme.test/" } } }))
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());

        using var response = await client.PostAsJsonAsync("/api/site-audit", new { site = "acme.test" });
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        var result = JsonDocument.Parse(text).RootElement;
        var issues = result.GetProperty("issues").EnumerateArray().Select(item => (Check: item.GetProperty("check").GetString()!, Url: item.GetProperty("url").GetString()!, Detail: item.GetProperty("detail").GetString()!)).ToArray();
        Assert.Equal(3, result.GetProperty("pages").GetInt32());
        Assert.True(result.GetProperty("robots").GetBoolean()); Assert.False(result.GetProperty("sitemap").GetBoolean());
        Assert.Contains(issues, item => item.Check == "Broken link" && item.Detail.Contains("https://acme.test/old-page") && item.Detail.Contains("404"));
        Assert.Contains(issues, item => item.Check == "Duplicate title");
        Assert.Contains(issues, item => item.Check == "Description" && item.Url == "https://acme.test/pricing");
        Assert.Contains(issues, item => item.Check == "H1" && item.Detail.Contains("2 H1"));
        Assert.Contains(issues, item => item.Check == "Alt text" && item.Detail.StartsWith("1 of 1"));
        Assert.Contains(issues, item => item.Check == "Noindex" && item.Url == "https://acme.test/about");
        Assert.Contains(issues, item => item.Check == "Thin content" && item.Url == "https://acme.test/about");
        Assert.Contains(issues, item => item.Check == "Sitemap");
        Assert.DoesNotContain(asked, url => url.Contains("elsewhere.test"));

        // The report is a Library document under Research / SEO.
        var id = result.GetProperty("reportWikiId").GetString()!;
        var page = factory.Services.GetRequiredService<CompanyWiki>().List().Single(item => item.Id == id);
        Assert.StartsWith("Site check: acme.test", page.Title);
        Assert.Contains("## To fix", page.Body); Assert.Contains("Nothing on the site was changed", page.Body);
        var library = factory.Services.GetRequiredService<WorkspaceLibrary>().View("");
        Assert.Contains(library.Entries, entry => entry.Key == "wiki:" + id && entry.Folder == "Research/SEO");
    }
}
