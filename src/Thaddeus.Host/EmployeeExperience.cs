using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record PreparedSource(string Url, string Title, string Coverage);
public record PreparedRecommendation(string Id, int Version, string Title, string WhyNow, string Recommendation, string NextStep,
    string Hypothesis, string Measurement, string Uncertainty, string[] Outputs, PreparedSource[] Sources, string? CampaignId,
    string ShiftId, bool Simulated, string Status, string? DecisionReason, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public record ExperienceLedger(PreparedRecommendation[] Recommendations);
public record RecommendationDecision(int ExpectedVersion, string Status, string? Reason);

/// <summary>Judgment attached to work the host actually saved. It never approves, publishes or dispatches work.</summary>
public sealed class EmployeeExperience(Store store)
{
    const string Key = "employee-experience-v1";
    ExperienceLedger Read() => store.Setting(Key) is { } json ? Wire.Unpack<ExperienceLedger>(json) : new([]);
    public ExperienceLedger View() { lock (store) return Read(); }

    /// <summary>A better version took a piece's place (a draft's revision, a replacing proposal): what was prepared points to it.</summary>
    public void Replace(string key, string next)
    {
        if (key == next) return;
        lock (store)
        {
            var ledger = Read();
            if (!ledger.Recommendations.Any(item => item.Outputs.Contains(key))) return;
            store.Setting(Key, Wire.Pack(ledger with { Recommendations = [.. ledger.Recommendations.Select(item => item.Outputs.Contains(key)
                ? item with { Outputs = [.. item.Outputs.Select(output => output == key ? next : output)], Version = item.Version + 1, UpdatedAt = DateTimeOffset.UtcNow } : item)] }));
        }
    }

    static string Text(JsonElement json, string name, int limit, string fallback = "")
    {
        var text = json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!.Trim() : fallback;
        if (string.IsNullOrWhiteSpace(text)) text = fallback;
        return text.Length > limit ? text[..limit] : text;
    }

    public PreparedRecommendation? Capture(string shiftId, bool simulated, JsonElement reply, JsonElement priority,
        IEnumerable<string> outputs, IEnumerable<ResearchSource> sources, string? campaignId)
    {
        // An eloquent promise isn't an artifact. Only the host's successful save receipts enter the package.
        var keys = outputs.Select(output => output.Split(' ')[0]).Where(key => Regex.IsMatch(key, @"^(wiki|draft|media|pagecopy|exp):[A-Za-z0-9_-]{1,100}$"))
            .Distinct().Take(12).ToArray();
        if (keys.Length == 0) return null;
        var detail = reply.ValueKind == JsonValueKind.Object && reply.TryGetProperty("recommendation", out var given) && given.ValueKind == JsonValueKind.Object ? given : default;
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shiftId + ":" + string.Join("|", keys))))[..20].ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var item = new PreparedRecommendation(id, 1, Text(reply, "title", 160, Text(priority, "title", 160, "Prepared work")),
            Text(detail, "whyNow", 600, Text(priority, "reason", 600, EmployeeShifts.AssignedReason)),
            Text(detail, "choice", 600, Text(reply, "rationale", 600)),
            Text(detail, "nextStep", 400),
            Text(detail, "hypothesis", 400), Text(detail, "measurement", 400),
            Text(detail, "uncertainty", 400), keys,
            sources.Where(source => Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) && uri.Scheme == "https")
                .GroupBy(source => source.Url).Select(group => group.First()).Take(8)
                .Select(source => new PreparedSource(source.Url, source.Title.Length > 160 ? source.Title[..160] : source.Title,
                    source.Via == "Google News" ? "Headline only" : source.Via == "Customer notes" ? "Customer notes; limited coverage" : "Read for this assignment; limited coverage")).ToArray(),
            campaignId, shiftId, simulated, "ready", null, now, now);
        lock (store)
        {
            var ledger = Read();
            if (ledger.Recommendations.FirstOrDefault(existing => existing.Id == id) is { } existing) return existing;
            store.Setting(Key, Wire.Pack(new ExperienceLedger(ledger.Recommendations.TakeLast(149).Append(item).ToArray())));
        }
        return item;
    }

    public PreparedRecommendation Decide(string id, RecommendationDecision decision)
    {
        if (decision.Status is not ("ready" or "parked")) throw new ArgumentException("Choose ready or parked.");
        var reason = (decision.Reason ?? "").Trim();
        if (reason.Length > 600) throw new ArgumentException("Keep the reason under 600 characters.");
        lock (store)
        {
            var ledger = Read();
            var item = ledger.Recommendations.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException("That recommendation is no longer available.");
            if (item.Version != decision.ExpectedVersion) throw new InvalidOperationException("This recommendation changed. Refresh and review it again.");
            var next = item with { Version = item.Version + 1, Status = decision.Status, DecisionReason = reason.Length > 0 ? reason : null, UpdatedAt = DateTimeOffset.UtcNow };
            store.Setting(Key, Wire.Pack(new ExperienceLedger(ledger.Recommendations.Select(item => item.Id == id ? next : item).ToArray())));
            return next;
        }
    }
}
