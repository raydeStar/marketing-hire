using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record SiteAuditRequest(string Site);
public record AuditPage(string Url, int Status, string Title, string Description, int H1s, string? Canonical, bool NoIndex, int Images, int ImagesWithoutAlt, int Words, string[] Links);
public record AuditIssue(string Severity, string Check, string Url, string Detail);
public record SiteAuditResult(string Site, DateTimeOffset At, int Pages, bool Robots, bool Sitemap, AuditIssue[] Issues, string? ReportWikiId);

/// <summary>A technical SEO check of the owner's own site: up to 25 pages from its sitemap (or its homepage links),
/// plus up to 40 internal links, read politely one at a time with the research guards (HTTPS, public addresses, the site only).
/// It reports what a person would fix: missing or overlong titles and descriptions, missing or repeated H1s, noindex,
/// images without alt text, thin pages, duplicates and broken links. The report is a Library document; nothing on the site changes.</summary>
public sealed partial class SiteAudit(CompanyObjectives objectives, CompanyWiki wiki, WorkspaceLibrary library, Store store)
{
    const string Key = "site-audits";
    public const int MaxPages = 25, MaxLinkChecks = 40;
    static readonly SemaphoreSlim One = new(1, 1);

    /// <summary>Fetches one URL of the site; replaceable in tests.</summary>
    public Func<string, IReadOnlyCollection<string>, CancellationToken, Task<(int Status, Uri Url, string MediaType, string Body, int Redirects)>> Probe { get; set; } = SiteReader.Probe;
    public TimeSpan Pause { get; set; } = TimeSpan.FromMilliseconds(250);

    public SiteAuditResult? Latest(string? site = null)
    {
        var all = store.Setting(Key) is { } json ? Wire.Unpack<SiteAuditResult[]>(json) : [];
        return all.Where(item => site == null || item.Site == site).OrderByDescending(item => item.At).FirstOrDefault();
    }

    /// <summary>The owner's sites the audit may crawl: those on the research allowlist.</summary>
    public string[] Sites() => objectives.Current().Content.ResearchSites ?? [];

    public async Task<SiteAuditResult> Run(string requested, string author, CancellationToken cancellation)
    {
        var site = SiteReader.NormalizeSite(requested) ?? throw new ArgumentException("Enter the site's address, e.g. example.com.");
        if (!Sites().Contains(site)) throw new ArgumentException($"Add {site} to the research sites (Library → Company → Objectives & positioning) first; the check only reads sites you listed.");
        if (!await One.WaitAsync(0, cancellation)) throw new InvalidOperationException("A site check is already running.");
        try
        {
            string[] sites = [site];
            var home = $"https://{site}/";
            var robots = await Safe($"https://{site}/robots.txt", sites, cancellation);
            var sitemapUrls = new List<string>();
            var sitemap = await Safe($"https://{site}/sitemap.xml", sites, cancellation);
            if (sitemap is { Status: 200 } map)
            {
                sitemapUrls.AddRange(Locations(map.Body));
                // A sitemap index points at more sitemaps; read the first one.
                if (sitemapUrls.Count > 0 && sitemapUrls.All(url => url.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) && await Safe(sitemapUrls[0], sites, cancellation) is { Status: 200 } child)
                    sitemapUrls = Locations(child.Body).ToList();
            }
            var queue = new Queue<(string Url, string? From)>([(home, null), .. sitemapUrls.Where(url => SiteReader.Allowed(new Uri(url), sites)).Select(url => (url, (string?)null))]);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pages = new List<AuditPage>();
            var issues = new List<AuditIssue>();
            while (queue.Count > 0 && pages.Count < MaxPages)
            {
                var (next, from) = queue.Dequeue();
                var url = Clean(next);
                if (url == null || !seen.Add(url)) continue;
                var got = await Safe(url, sites, cancellation);
                if (got is not { Status: 200 })
                {
                    var answer = got == null ? "could not be reached" : $"answers {got.Value.Status}";
                    issues.Add(from != null ? new("error", "Broken link", from, $"Links to {url}, which {answer}.") : new("error", "Broken page", url, $"It {answer}{(sitemapUrls.Contains(url) ? " (listed in the sitemap)" : "")}."));
                    continue;
                }
                if (!got.Value.MediaType.Contains("html", StringComparison.Ordinal)) continue;
                var page = Read(got.Value.Url, got.Value.Body);
                pages.Add(page);
                if (sitemapUrls.Count == 0) foreach (var link in page.Links) queue.Enqueue((link, page.Url));
            }
            // Internal links not already crawled: do they work?
            var checkedLinks = 0;
            foreach (var link in pages.SelectMany(page => page.Links.Select(link => (page.Url, link))).Where(item => !seen.Contains(item.link)).DistinctBy(item => item.link))
            {
                if (checkedLinks++ >= MaxLinkChecks) break;
                seen.Add(link.link);
                var got = await Safe(link.link, sites, cancellation);
                if (got is not { Status: < 400 }) issues.Add(new("error", "Broken link", link.Url, $"Links to {link.link}, which {(got == null ? "could not be reached" : $"answers {got.Value.Status}")}."));
            }
            issues.AddRange(Check(pages));
            if (robots is not { Status: 200 }) issues.Add(new("notice", "robots.txt", home, "There is no robots.txt; search engines crawl everything, which is usually fine."));
            if (sitemap is not { Status: 200 }) issues.Add(new("warning", "Sitemap", home, "There is no /sitemap.xml; a sitemap helps search engines find every page."));
            var result = new SiteAuditResult(site, DateTimeOffset.UtcNow, pages.Count, robots is { Status: 200 }, sitemap is { Status: 200 }, [.. issues], null);
            var id = Save(result, author);
            result = result with { ReportWikiId = id };
            lock (store)
            {
                var all = store.Setting(Key) is { } json ? Wire.Unpack<SiteAuditResult[]>(json) : [];
                store.Setting(Key, Wire.Pack(all.Append(result).OrderByDescending(item => item.At).Take(20).ToArray()));
            }
            return result;
        }
        finally { One.Release(); }
    }

    async Task<(int Status, Uri Url, string MediaType, string Body, int Redirects)?> Safe(string url, IReadOnlyCollection<string> sites, CancellationToken cancellation)
    {
        await Task.Delay(Pause, cancellation);
        try { return await Probe(url, sites, cancellation); }
        catch (Exception error) when (error is IOException or HttpRequestException or InvalidOperationException or TaskCanceledException) { return null; }
    }

    static IEnumerable<string> Locations(string xml) => Regex.Matches(xml, @"<loc>\s*([^<\s]+)\s*</loc>", RegexOptions.IgnoreCase).Select(match => WebUtility.HtmlDecode(match.Groups[1].Value)).Take(200);

    /// <summary>A page URL without its fragment or query; null for anything that isn't a page worth crawling.</summary>
    static string? Clean(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        if (Regex.IsMatch(uri.AbsolutePath, @"\.(jpe?g|png|gif|webp|svg|pdf|zip|mp4|webm|css|js|ico|xml|txt)$", RegexOptions.IgnoreCase)) return null;
        return new UriBuilder(uri) { Fragment = "", Query = "" }.Uri.AbsoluteUri;
    }

    public static AuditPage Read(Uri url, string html)
    {
        string Meta(string name) => Regex.Match(html, $@"<meta[^>]+name\s*=\s*[""']{name}[""'][^>]*content\s*=\s*[""']([^""']*)[""']|<meta[^>]+content\s*=\s*[""']([^""']*)[""'][^>]*name\s*=\s*[""']{name}[""']", RegexOptions.IgnoreCase) is { Success: true } found
            ? WebUtility.HtmlDecode(found.Groups[1].Success ? found.Groups[1].Value : found.Groups[2].Value).Trim() : "";
        var (title, text) = SiteReader.Extract(html);
        var canonical = Regex.Match(html, @"<link[^>]+rel\s*=\s*[""']canonical[""'][^>]*href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase) is { Success: true } link ? link.Groups[1].Value : null;
        var images = Regex.Matches(html, @"<img\b[^>]*>", RegexOptions.IgnoreCase);
        var withoutAlt = images.Count(image => !Regex.IsMatch(image.Value, @"\balt\s*=\s*[""'][^""']+[""']", RegexOptions.IgnoreCase) && !Regex.IsMatch(image.Value, @"role\s*=\s*[""']presentation|aria-hidden\s*=\s*[""']true", RegexOptions.IgnoreCase));
        var links = Regex.Matches(html, @"<a\b[^>]*href\s*=\s*[""']([^""'#][^""']*)[""']", RegexOptions.IgnoreCase)
            .Select(match => Uri.TryCreate(url, WebUtility.HtmlDecode(match.Groups[1].Value), out var target) ? target : null)
            .Where(target => target != null && target.Scheme == Uri.UriSchemeHttps && (target.Host == url.Host || target.Host == "www." + url.Host || "www." + target.Host == url.Host))
            .Select(target => Clean(target!.AbsoluteUri)).OfType<string>().Distinct().Take(150).ToArray();
        return new(url.AbsoluteUri, 200, title, Meta("description"), Regex.Matches(html, @"<h1\b", RegexOptions.IgnoreCase).Count, canonical,
            Regex.IsMatch(Meta("robots"), "noindex", RegexOptions.IgnoreCase), images.Count, withoutAlt, text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, links);
    }

    /// <summary>What a person would fix on each page, and what repeats across pages.</summary>
    public static IEnumerable<AuditIssue> Check(IReadOnlyList<AuditPage> pages)
    {
        foreach (var page in pages)
        {
            if (page.Title.Length == 0) yield return new("error", "Title", page.Url, "The page has no title.");
            else if (page.Title.Length > 65) yield return new("warning", "Title", page.Url, $"The title is {page.Title.Length} characters; search results cut it near 60.");
            else if (page.Title.Length < 15) yield return new("warning", "Title", page.Url, $"The title “{page.Title}” is short; say what the page is.");
            if (page.Description.Length == 0) yield return new("warning", "Description", page.Url, "There is no meta description; search engines will pick a snippet themselves.");
            else if (page.Description.Length > 165) yield return new("notice", "Description", page.Url, $"The description is {page.Description.Length} characters; results show about 155.");
            if (page.H1s == 0) yield return new("warning", "H1", page.Url, "The page has no H1 heading.");
            else if (page.H1s > 1) yield return new("notice", "H1", page.Url, $"The page has {page.H1s} H1 headings; one is clearest.");
            if (page.NoIndex) yield return new("error", "Noindex", page.Url, "The page tells search engines not to index it. Intended?");
            if (page.ImagesWithoutAlt > 0) yield return new("warning", "Alt text", page.Url, $"{page.ImagesWithoutAlt} of {page.Images} images have no alt text.");
            if (page.Words < 250) yield return new("notice", "Thin content", page.Url, $"About {page.Words} words; pages with little text rarely rank.");
            if (page.Canonical is { } canonical && Uri.TryCreate(new Uri(page.Url), canonical, out var target) && !target.AbsoluteUri.TrimEnd('/').Equals(page.Url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                yield return new("notice", "Canonical", page.Url, $"The canonical points elsewhere ({target.AbsoluteUri}).");
        }
        foreach (var group in pages.Where(page => page.Title.Length > 0).GroupBy(page => page.Title, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            yield return new("warning", "Duplicate title", group.First().Url, $"{group.Count()} pages share the title “{group.Key}”: {string.Join(", ", group.Skip(1).Take(3).Select(page => page.Url))}.");
        foreach (var group in pages.Where(page => page.Description.Length > 0).GroupBy(page => page.Description, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            yield return new("notice", "Duplicate description", group.First().Url, $"{group.Count()} pages share the same meta description.");
    }

    string Save(SiteAuditResult result, string author)
    {
        var body = new StringBuilder();
        var errors = result.Issues.Count(item => item.Severity == "error"); var warnings = result.Issues.Count(item => item.Severity == "warning");
        body.AppendLine($"Checked {result.Pages} page{(result.Pages == 1 ? "" : "s")} of {result.Site} on {result.At:MMMM d, yyyy}: {errors} to fix, {warnings} worth improving, {result.Issues.Length - errors - warnings} to note.");
        body.AppendLine().AppendLine($"- robots.txt: {(result.Robots ? "present" : "missing")}").AppendLine($"- sitemap.xml: {(result.Sitemap ? "present" : "missing")}");
        foreach (var (severity, heading) in new[] { ("error", "To fix"), ("warning", "Worth improving"), ("notice", "To note") })
        {
            var items = result.Issues.Where(item => item.Severity == severity).ToArray();
            if (items.Length == 0) continue;
            body.AppendLine().AppendLine($"## {heading}").AppendLine();
            foreach (var group in items.GroupBy(item => item.Check))
            {
                body.AppendLine($"**{group.Key}** ({group.Count()})").AppendLine();
                foreach (var item in group.Take(12)) body.AppendLine($"- {item.Url}: {item.Detail}");
                if (group.Count() > 12) body.AppendLine($"- …and {group.Count() - 12} more.");
                body.AppendLine();
            }
        }
        body.AppendLine().AppendLine($"_Read with a polite crawler: at most {MaxPages} pages and {MaxLinkChecks} link checks, one request at a time. Pages built entirely with JavaScript may show fewer words than a visitor sees. Nothing on the site was changed._");
        var page = wiki.Save(new WikiChange(Guid.NewGuid().ToString("N"), null, 0, "company", "company", $"Site check: {result.Site} ({result.At:MMM d, yyyy})", body.ToString(), "fact", "draft"), author);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { library.SaveEntry("wiki:" + page.Id, new LibraryEntryChange(library.View("").Version, "Research/SEO", ["seo", "site-check"]), author, "employee"); break; }
            catch (InvalidOperationException) when (attempt < 2) { }
        }
        return page.Id;
    }

    /// <summary>A short summary for a shift: the counts and the first findings, to plan fixes from.</summary>
    public static string Summary(SiteAuditResult result) =>
        $"Site check of {result.Site} ({result.At:yyyy-MM-dd}), {result.Pages} pages: " +
        string.Join(" ", result.Issues.OrderBy(item => item.Severity switch { "error" => 0, "warning" => 1, _ => 2 }).Take(12).Select(item => $"[{item.Severity}] {item.Check} — {item.Url}: {item.Detail}"));
}
