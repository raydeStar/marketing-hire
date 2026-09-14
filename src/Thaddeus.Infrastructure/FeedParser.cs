using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using AngleSharp.Html.Parser;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public record FeedEntryPreview(string Key, string Title, string Summary, string? Url, DateTimeOffset? Published);
public record FeedContent(string Title, FeedEntryPreview[] Entries, bool Truncated, int? RefreshMinutes);
public record FeedCandidate(string Title, string Url);

// Feed contents are untrusted reading material. No image, enclosure, script or external entity gets an invitation.
public static class FeedParser
{
    public const int MaxBytes = 1_000_000, MaxEntries = 100, MaxSummary = 1200;
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Content = "http://purl.org/rss/1.0/modules/content/";

    public static Uri SourceUrl(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) throw new ArgumentException("Use a public HTTPS feed or website address.");
        return PublicWebNetwork.Destination(text, new([uri.IdnHost]));
    }

    public static FeedContent Parse(byte[] bytes, Uri source, CancellationToken cancellation = default)
    {
        if (bytes.Length > MaxBytes) throw new ArgumentException("The feed exceeds the one-megabyte limit.");
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes,
            IgnoreComments = true, IgnoreProcessingInstructions = true, CloseInput = true
        };
        // Check depth before constructing a tree. A small byte count alone does not bound nesting.
        using (var reader = XmlReader.Create(new MemoryStream(bytes, writable: false), settings))
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                if (reader.Depth > 32) throw new XmlException("Feed nesting exceeds the supported limit.");
            }
        using var input = XmlReader.Create(new MemoryStream(bytes, writable: false), settings);
        var document = XDocument.Load(input, LoadOptions.None);
        var root = document.Root ?? throw new XmlException("The feed has no document element.");
        var atom = root.Name == Atom + "feed";
        XElement channel;
        if (atom) channel = root;
        else if (root.Name == "rss" && (string?)root.Attribute("version") == "2.0" && root.Element("channel") is { } rss) channel = rss;
        else throw new XmlException("Use an RSS 2.0 or Atom 1.0 feed.");

        var title = atom ? AtomText(channel.Element(Atom + "title"), 160) : Text(channel.Element("title")?.Value ?? "", 160, html: true);
        if (title.Length == 0) title = source.IdnHost;
        var items = channel.Elements(atom ? Atom + "entry" : "item").Take(MaxEntries + 1).ToArray();
        var entries = new List<FeedEntryPreview>(); var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.Take(MaxEntries))
        {
            cancellation.ThrowIfCancellationRequested();
            var itemTitle = atom ? AtomText(item.Element(Atom + "title"), 160) : Text(item.Element("title")?.Value ?? "", 160, html: true);
            var summary = atom ? AtomText(item.Element(Atom + "summary") ?? item.Element(Atom + "content"), MaxSummary)
                : Text(item.Element("description")?.Value ?? item.Element(Content + "encoded")?.Value ?? "", MaxSummary, html: true);
            if (itemTitle.Length == 0 && summary.Length == 0) continue;
            if (itemTitle.Length == 0) itemTitle = Cut(summary, 160);
            XElement? linkElement = atom ? item.Elements(Atom + "link").FirstOrDefault(link =>
                ((string?)link.Attribute("rel") is null or "alternate") && ((string?)link.Attribute("type") is null or "text/html" or "application/xhtml+xml")) : item.Element("link");
            var link = Link(atom ? (string?)linkElement?.Attribute("href") : linkElement?.Value, Base(linkElement ?? item, source));
            var identity = atom ? item.Element(Atom + "id")?.Value : item.Element("guid")?.Value;
            if (!atom && link == null && item.Element("guid") is { } guid && !string.Equals((string?)guid.Attribute("isPermaLink"), "false", StringComparison.OrdinalIgnoreCase))
                link = Link(guid.Value, Base(guid, source));
            var when = atom ? item.Element(Atom + "published")?.Value ?? item.Element(Atom + "updated")?.Value : item.Element("pubDate")?.Value;
            DateTimeOffset? published = DateTimeOffset.TryParse(when, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.ToUniversalTime() : null;
            var key = Wire.Hash(!string.IsNullOrWhiteSpace(identity) ? "id:" + identity.Trim() : link != null ? "url:" + link : "text:" + itemTitle + "\n" + when + "\n" + summary);
            if (keys.Add(key)) entries.Add(new(key, itemTitle, summary, link, published));
        }
        int? refresh = !atom && int.TryParse(channel.Element("ttl")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var ttl) && ttl > 0 ? Math.Clamp(ttl, 60, 1440) : null;
        cancellation.ThrowIfCancellationRequested();
        return new(title, entries.ToArray(), items.Length > MaxEntries, refresh);
    }

    public static FeedCandidate[] Discover(string html, Uri source)
    {
        if (html.Length > MaxBytes) throw new ArgumentException("The discovery page exceeds the supported limit.");
        using var document = new HtmlParser(new() { IsScripting = false }).ParseDocument(html);
        var basis = Uri.TryCreate(source, document.QuerySelector("base[href]")?.GetAttribute("href"), out var htmlBase) ? htmlBase : source;
        var candidates = new List<FeedCandidate>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in document.QuerySelectorAll("link[href][rel][type]"))
        {
            if (!link.GetAttribute("rel")!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains("alternate", StringComparer.OrdinalIgnoreCase)) continue;
            if (link.GetAttribute("type")!.Split(';')[0].Trim().ToLowerInvariant() is not ("application/rss+xml" or "application/atom+xml")) continue;
            var url = Link(link.GetAttribute("href"), basis);
            if (url == null || !seen.Add(url)) continue;
            var title = Text(link.GetAttribute("title") ?? "", 160, html: false);
            candidates.Add(new(title.Length == 0 ? new Uri(url).IdnHost : title, url));
            if (candidates.Count == 8) break;
        }
        return candidates.ToArray();
    }

    private static string AtomText(XElement? element, int maximum)
    {
        if (element == null) return "";
        var type = (string?)element.Attribute("type") ?? "text";
        return type == "xhtml" ? Text(string.Concat(element.Nodes().Select(node => node.ToString())), maximum, html: true)
            : type is "text" or "html" ? Text(element.Value, maximum, html: type == "html") : "";
    }

    private static string Text(string value, int maximum, bool html)
    {
        if (html)
        {
            using var document = new HtmlParser(new() { IsScripting = false }).ParseDocument(value);
            foreach (var node in document.QuerySelectorAll("script,style,template,noscript,svg,iframe,object,embed,form,[hidden],[aria-hidden='true']")) node.Remove();
            foreach (var block in document.QuerySelectorAll("p,div,br,li,h1,h2,h3,h4,h5,h6,blockquote")) block.AppendChild(document.CreateTextNode(" "));
            value = document.Body?.TextContent ?? "";
        }
        var result = new StringBuilder(); var spacing = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character)) { spacing = result.Length > 0; continue; }
            if (char.IsControl(character)) continue;
            if (spacing) { result.Append(' '); spacing = false; }
            result.Append(character);
            if (result.Length > maximum) break;
        }
        return Cut(result.ToString(), maximum);
    }

    private static string Cut(string text, int maximum)
    {
        if (text.Length <= maximum) return text;
        var end = maximum - 1;
        if (end > 0 && char.IsHighSurrogate(text[end - 1])) end--;
        return text[..end].TrimEnd() + "…";
    }

    private static Uri Base(XElement element, Uri source)
    {
        var current = source;
        foreach (var ancestor in element.AncestorsAndSelf().Reverse())
            if (ancestor.Attribute(XNamespace.Xml + "base") is { } basis && Uri.TryCreate(current, basis.Value, out var next)) current = next;
        return current;
    }

    private static string? Link(string? text, Uri basis)
    {
        if (string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(basis, text.Trim(), out var uri)) return null;
        try { return SourceUrl(uri.AbsoluteUri).AbsoluteUri; } catch (ArgumentException) { return null; }
    }
}
