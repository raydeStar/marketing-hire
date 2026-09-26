using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record PieceClaim(string Text, string? Url = null, string? SourceKey = null);
public record PieceRecord(string Week, string? Channel, PieceClaim[] Claims);
public record CampaignPiece(string Key, string? Week, string? Channel, string? Grade, PieceClaim[] Claims, string[] Blockers);
public record CampaignPiecesView(string CampaignId, string? Angle, CampaignPiece[] Pieces);

/// <summary>What each piece of a campaign is for: the week it serves, its channel, and the claims it makes with their sources,
/// recorded when the employee makes it; with its grade and what still holds it back from the review record. The campaign's
/// angle is its latest recommendation, else the plan's own "Angle:" or core message.</summary>
public sealed class CampaignPieces(Store store, Campaigns campaigns, EmployeeMemory memory, EmployeeExperience experience, CompanyWiki wiki, MarketingBackend marketing)
{
    const string Key = "campaign-pieces-v1";
    Dictionary<string, PieceRecord> Read() => store.Setting(Key) is { } json ? Wire.Unpack<Dictionary<string, PieceRecord>>(json) : [];

    /// <summary>The Monday of the week a date falls in, as yyyy-MM-dd.</summary>
    public static string Week(DateTimeOffset at) { var day = DateOnly.FromDateTime(at.UtcDateTime); return day.AddDays(-(((int)day.DayOfWeek + 6) % 7)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }

    /// <summary>Records the pieces a priority made for a campaign: the week the writer named (else this week), the channel, and
    /// each claim with the source its [n] points to.</summary>
    public void Record(IEnumerable<string> keys, JsonElement reply, IReadOnlyList<ResearchSource> sources)
    {
        var piece = reply.TryGetProperty("piece", out var given) && given.ValueKind == JsonValueKind.Object ? given : default;
        var week = piece.ValueKind == JsonValueKind.Object && piece.TryGetProperty("week", out var named) && named.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(named.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? Week(new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)) : Week(DateTimeOffset.UtcNow);
        var claims = piece.ValueKind == JsonValueKind.Object && piece.TryGetProperty("claims", out var listed) && listed.ValueKind == JsonValueKind.Array
            ? listed.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!.Trim()).Where(text => text.Length is > 3 and <= 300).Take(8)
                .Select(text => Regex.Match(text, @"\[(\d{1,2})\]") is { Success: true } cited && int.Parse(cited.Groups[1].Value, CultureInfo.InvariantCulture) is var n && n >= 1 && n <= sources.Count
                    ? new PieceClaim(Regex.Replace(text, @"\s*\[\d{1,2}\]", "").Trim(), sources[n - 1].Url) : new PieceClaim(text)).ToArray()
            : [];
        var channel = reply.TryGetProperty("channel", out var where) && where.ValueKind == JsonValueKind.String && where.GetString() is { Length: > 0 and <= 40 } name ? name : null;
        lock (store)
        {
            var all = Read();
            foreach (var key in keys.Where(key => key.Length is > 0 and <= 120)) all[key] = new PieceRecord(week, channel, claims);
            store.Setting(Key, Wire.Pack(all.Count > 2000 ? all.Skip(all.Count - 2000).ToDictionary() : all));
        }
    }

    /// <summary>A piece's record carries over to its better version (a polished draft, a document's new version).</summary>
    public void Carry(string from, string to)
    {
        lock (store) { var all = Read(); if (all.TryGetValue(from, out var record)) { all[to] = record; store.Setting(Key, Wire.Pack(all)); } }
    }

    public async Task<CampaignPiecesView> View(string campaignId)
    {
        var campaign = campaigns.Find(campaignId) ?? throw new KeyNotFoundException("That campaign doesn't exist.");
        var records = Read();
        var quality = memory.Quality();
        var snapshot = await marketing.ShiftHire(null, "snapshot");
        var drafts = snapshot.Value is { } work && work.TryGetProperty("drafts", out var list) ? list.EnumerateArray().ToDictionary(item => "draft:" + item.GetProperty("id").GetRawText()) : [];
        var pieces = campaigns.View().Items.Where(item => item.Value == campaignId).Select(item => item.Key).Select(key =>
        {
            records.TryGetValue(key, out var record);
            var graded = quality.LastOrDefault(entry => entry.Keys?.Contains(key) == true);
            var channel = record?.Channel ?? (drafts.TryGetValue(key, out var draft) && draft.TryGetProperty("channel", out var on) ? on.GetString() : null)
                ?? (key.StartsWith("pagecopy:", StringComparison.Ordinal) ? "Page" : key.StartsWith("wiki:", StringComparison.Ordinal) ? "Document" : null);
            return new CampaignPiece(key, record?.Week, channel, graded is { Scores.Count: > 0 } ? MarketingRubric.Grade(graded.Scores.Values.Average()) : null,
                record?.Claims ?? [], graded?.Unmet ?? []);
        }).ToArray();
        var angle = experience.View().Recommendations.Where(item => item.CampaignId == campaignId && item.Status == "ready").OrderByDescending(item => item.UpdatedAt).FirstOrDefault()?.Recommendation
            ?? (campaign.PlanWikiId is { } plan && wiki.List().FirstOrDefault(page => page.Id == plan)?.Body is { } body
                && Regex.Match(body, @"^\s*(?:[-*]\s*)?\**(?:Central angle|Angle|Core message)\**\s*:?\**\s*:?\s*(.+)$|^##\s*Core message\s*\n+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline) is { Success: true } found
                ? Regex.Replace(found.Groups[1].Success && found.Groups[1].Value.Length > 0 ? found.Groups[1].Value : found.Groups[2].Value, @"[*_`]", "").Trim() : null);
        return new CampaignPiecesView(campaignId, angle, pieces);
    }
}
