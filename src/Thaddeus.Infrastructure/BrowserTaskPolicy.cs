using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

// Chrome receives a task, not the keys to the manor. The host admits every operation.
public static class BrowserTaskPolicy
{
    public const int MaxSnapshotLength = 48000;
    public static Budget DefaultLimits => new(ModelCalls: 8, ToolCalls: 12, Seconds: 600, Repairs: 0);
    private static readonly Regex Reference = new(@"\Ae[0-9]{1,8}\z", RegexOptions.CultureInvariant);

    public static BrowserTaskScope ValidateScope(BrowserTaskScope scope)
    {
        if (string.IsNullOrWhiteSpace(scope.Objective) || scope.Objective.Length > 4000 || scope.Hosts is not { Length: > 0 and <= 8 })
            throw new ArgumentException("Describe one browser task and up to eight exact website hosts.");
        var hosts = scope.Hosts.Select(host =>
        {
            if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || host.Contains('/') || host.Contains(':') || host.Contains('*') || host.Contains('@'))
                throw new ArgumentException("Browser scope needs exact public website hosts, without wildcards, paths or credentials.");
            return PublicSearchAccess.ResultUrl("https://" + host.Trim()).IdnHost.ToLowerInvariant();
        }).Distinct(StringComparer.Ordinal).ToArray();
        var limits = scope.Limits ?? throw new ArgumentException("Review a browser allowance before starting.");
        if (limits.ModelCalls is < 1 or > 8 || limits.ToolCalls is < 1 or > 12 || limits.Seconds is < 1 or > 600 ||
            limits.MaxTotalTokens is < 1 or > 64000 || limits.MaxOutputTokens is < 128 or > 16000 || limits.Repairs != 0)
            throw new ArgumentException("Browser tasks support up to eight model calls, twelve browser actions, ten active minutes and 64,000 tokens, without automatic repairs.");
        var saved = scope with { Hosts = hosts };
        return saved with { StartUrl = AdmittedUrl(saved, scope.StartUrl).AbsoluteUri };
    }

    public static Uri AdmittedUrl(BrowserTaskScope scope, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048)
            throw new ArgumentException("Choose a public HTTPS website within this browser task.");
        var url = PublicSearchAccess.ResultUrl(value);
        if (!url.IsDefaultPort || !scope.Hosts.Contains(url.IdnHost, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("This website is outside the reviewed browser task. Start a newly reviewed task to include it.");
        return url;
    }

    public static bool NeedsReview(BrowserAction action) => action.Kind is "click" or "type" or "select" or "key";

    public static void ValidateAction(BrowserTaskScope scope, BrowserPage page, BrowserAction action)
    {
        if (action.PageVersion != page.Version) throw new InvalidOperationException("The browser page changed. Review the current page before acting.");
        AdmittedUrl(scope, page.Url);
        if (action.Kind is not ("snapshot" or "navigate" or "click" or "type" or "select" or "key"))
            throw new ArgumentException("That browser operation is unavailable.");
        if (action.Kind == "navigate") AdmittedUrl(scope, action.Url);
        else if (action.Url != null) throw new ArgumentException("Only navigation accepts a website address.");
        if (NeedsReview(action))
        {
            if (action.Target == null || !Reference.IsMatch(action.Target) || string.IsNullOrWhiteSpace(action.Description) || action.Description.Length > 240 ||
                !Regex.IsMatch(page.Snapshot, @"\[ref=" + Regex.Escape(action.Target) + @"\]"))
                throw new ArgumentException("Choose an exact element reference from the current page and describe the intended action.");
        }
        else if (action.Target != null || action.Description != null) throw new ArgumentException("Read operations cannot carry element actions.");
        if (action.Kind == "type")
        {
            if (action.Text == null || action.Text.Length > 4000) throw new ArgumentException("Review at most 4,000 characters of form text.");
        }
        else if (action.Text != null) throw new ArgumentException("Only typing accepts form text.");
        if (action.Kind == "select")
        {
            if (action.Values is not { Length: > 0 and <= 8 } || action.Values.Any(value => value == null || value.Length > 200))
                throw new ArgumentException("Review one to eight exact option values.");
        }
        else if (action.Values != null) throw new ArgumentException("Only selection accepts option values.");
        if (action.Kind == "key")
        {
            if (action.Key is not ("Enter" or "Space" or "Tab" or "Escape" or "ArrowUp" or "ArrowDown" or "ArrowLeft" or "ArrowRight"))
                throw new ArgumentException("Browser shortcuts, clipboard access and address-bar commands are unavailable.");
        }
        else if (action.Key != null) throw new ArgumentException("Only an element key action accepts a key.");
    }

    public static BrowserPage Page(BrowserTaskScope scope, string url, string title, string snapshot)
    {
        AdmittedUrl(scope, url);
        if (snapshot.Length > MaxSnapshotLength) throw new InvalidOperationException("This page exceeds the bounded browser reading limit. Take over or choose a smaller page.");
        // Queries and fragments may contain sign-in codes. They never belong on the chat card.
        var displayUrl = new Uri(url).GetLeftPart(UriPartial.Path);
        // Always omit editable values, even if a sign-in field appeared between the host's form check and snapshot.
        // Retain the raw snapshot only in the one-way version hash, so even hidden field changes stale an approval.
        var lines = new List<string>(); int? omittedIndent = null;
        foreach (var line in snapshot.Split('\n'))
        {
            var indent = line.TakeWhile(char.IsWhiteSpace).Count();
            if (omittedIndent is { } parent && indent > parent) continue;
            omittedIndent = null;
            var field = Regex.Match(line, @"^(\s*- (?:textbox|searchbox|spinbutton|combobox)\b.*?\[ref=e[0-9]+\])");
            if (field.Success) { lines.Add(field.Groups[1].Value + " [value omitted]"); omittedIndent = indent; }
            else lines.Add(line);
        }
        var visible = Regex.Replace(string.Join('\n', lines), "https?://[^\\s<>\"`]+", match =>
            Uri.TryCreate(match.Value, UriKind.Absolute, out var link) ? link.GetLeftPart(UriPartial.Path) : "[address omitted]");
        return new(displayUrl, title.Length > 240 ? title[..240] : title, visible, Wire.Hash(Wire.Pack(new { url, snapshot })));
    }
}
