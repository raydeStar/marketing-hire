using System.Net;
using System.Text.RegularExpressions;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record PublishedPage(string Slug, string ArtifactId, string ArtifactVersion, string Title, string Description,
    AppPage Page, string Digest, string PublishedBy, DateTimeOffset PublishedAt);
public record PublishRequest(string RequestId, string Slug, string ExpectedVersion);
public record PublishReceipt(string RequestId, string Digest, string Slug);
public record PublishLedger(PublishedPage[] Pages, PublishReceipt[] Receipts);

/// <summary>Owner-published campaign pages. Publishing freezes the exact page version at /p/{slug};
/// later edits change nothing until the owner publishes again.</summary>
public sealed partial class PublishedPages(Store store)
{
    private const string Key = "published-pages-v1";
    public const int MaxPages = 32;
    [GeneratedRegex("\\A[a-z0-9](?:[a-z0-9-]{0,58}[a-z0-9])?\\z")] private static partial Regex SlugPattern();
    private PublishLedger Read() => store.Setting(Key) is { } json ? Wire.Unpack<PublishLedger>(json) : new([], []);
    private void Write(PublishLedger value) => store.Setting(Key, Wire.Pack(value));

    public PublishedPage[] List() { lock (store) return Read().Pages.OrderByDescending(page => page.PublishedAt).ToArray(); }
    public PublishedPage? Find(string slug) { lock (store) return Read().Pages.FirstOrDefault(page => page.Slug == slug); }

    public PublishedPage Publish(string artifactId, PublishRequest request, string author)
    {
        lock (store)
        {
            if (string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 120) throw new ArgumentException("A request ID is required.");
            var ledger = Read(); var digest = Wire.Hash(Wire.Pack(new { artifactId, request }));
            if (ledger.Receipts.FirstOrDefault(receipt => receipt.RequestId == request.RequestId) is { } replay)
            {
                if (replay.Digest != digest) throw new ArgumentException("That request ID belongs to another publish action.");
                return ledger.Pages.FirstOrDefault(page => page.Slug == replay.Slug) ?? throw new InvalidOperationException("That page was unpublished after this request.");
            }
            var slug = request.Slug?.Trim().ToLowerInvariant() ?? "";
            if (!SlugPattern().IsMatch(slug)) throw new ArgumentException("Use 1–60 lowercase letters, numbers and dashes for the page address.");
            var app = store.Artifact(artifactId) ?? throw new ArgumentException("That page no longer exists.");
            if (app.Archived) throw new InvalidOperationException("Restore this page from Trash before publishing it.");
            if (app.Definition.Page is not { } page) throw new ArgumentException("Only pages with HTML can be published.");
            if (string.IsNullOrWhiteSpace(request.ExpectedVersion) || request.ExpectedVersion != app.Version)
                throw new InvalidOperationException("This page changed. Refresh and review the current version before publishing.");
            if (ledger.Pages.FirstOrDefault(item => item.Slug == slug) is { } taken && taken.ArtifactId != artifactId)
                throw new InvalidOperationException("That address is already used by another published page.");
            // One address per page: republishing under a new address moves it.
            var others = ledger.Pages.Where(item => item.ArtifactId != artifactId).ToArray();
            if (others.Length >= MaxPages) throw new InvalidOperationException($"Up to {MaxPages} pages can be published at once.");
            var published = new PublishedPage(slug, artifactId, app.Version, app.Definition.Title, app.Definition.Description, page,
                Wire.Hash(Wire.Pack(page)), author, DateTimeOffset.UtcNow);
            Write(new([.. others, published], [.. ledger.Receipts.TakeLast(255), new(request.RequestId, digest, slug)]));
            return published;
        }
    }

    public bool Unpublish(string slug)
    {
        lock (store)
        {
            var ledger = Read();
            if (!ledger.Pages.Any(page => page.Slug == slug)) return false;
            Write(ledger with { Pages = ledger.Pages.Where(page => page.Slug != slug).ToArray() });
            return true;
        }
    }

    // Published pages run on their own, sandboxed: no host bridge, no same-origin access, no network.
    public const string Policy = "sandbox allow-scripts allow-popups allow-popups-to-escape-sandbox; default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; media-src data:; font-src data:; connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    // Pages written for the workspace bridge still load; saving is unavailable on a published copy.
    private const string ReadOnlyBridge = "window.thaddeus=Object.freeze({onChange(){return()=>{};},save(){return Promise.reject(new Error('This published page is read-only.'));}});";

    public static string Render(PublishedPage published) =>
        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
        "<title>" + WebUtility.HtmlEncode(published.Title) + "</title>" +
        (published.Description.Length > 0 ? "<meta name=\"description\" content=\"" + WebUtility.HtmlEncode(published.Description) + "\">" : "") +
        "<style>*{box-sizing:border-box}body{margin:0;font:16px/1.55 system-ui,-apple-system,'Segoe UI',sans-serif}img,video{max-width:100%}</style>" +
        "<style>" + published.Page.Css.Replace("</style", "<\\/style", StringComparison.OrdinalIgnoreCase) + "</style></head><body>" +
        published.Page.Html + "<script>" + ReadOnlyBridge + "</script><script>" + published.Page.JavaScript.Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase) + "</script></body></html>";
}
