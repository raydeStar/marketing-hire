using System.Text.RegularExpressions;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

/// <summary>Pages refer to uploaded images as <c>media:{uploadId}</c>. The host inlines them as data URIs at
/// render time, so stored pages stay small and sandboxed pages never need network or cookie access.</summary>
public static partial class PageMedia
{
    public const int MaxInlinedBytes = 8 * 1024 * 1024;
    [GeneratedRegex("media:([a-f0-9]{32})")] private static partial Regex Reference();

    public static AppPage Inline(AppPage page, Store store)
    {
        var budget = MaxInlinedBytes;
        var cache = new Dictionary<string, string?>();
        string Replace(string text) => Reference().Replace(text, match =>
        {
            var id = match.Groups[1].Value;
            if (!cache.TryGetValue(id, out var uri))
            {
                uri = null;
                var file = store.Upload(id);
                if (file is { Archived: false } && file.MediaType is "image/png" or "image/jpeg" or "image/webp" or "image/gif" && file.Bytes <= budget)
                {
                    budget -= (int)file.Bytes;
                    uri = "data:" + file.MediaType + ";base64," + Convert.ToBase64String(store.UploadContent(id));
                }
                cache[id] = uri;
            }
            // An unknown, deleted or non-image reference stays as written and simply does not load.
            return uri ?? match.Value;
        });
        return page with { Html = Replace(page.Html), Css = Replace(page.Css) };
    }
}
