using System.Text.Json;
using Microsoft.Data.Sqlite;
using Thaddeus.Host;

namespace Thaddeus.Tests;

public sealed class CodexAllowanceTests
{
    private static readonly JsonElement Account = JsonSerializer.SerializeToElement(new { type = "chatgpt", email = "owner@example.test", planType = "pro" });
    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);

    [Fact]
    public void MapWinsOverLegacyAndMissingWindowsNeverBecomeZero()
    {
        var sample = CodexAllowanceReader.Parse(Account, Json("""
            {"rateLimits":{"primary":{"usedPercent":99}},"rateLimitsByLimitId":{
              "codex":{"primary":{"usedPercent":97,"windowDurationMins":10080,"resetsAt":1800000000},"secondary":null},
              "other":{"primary":{"usedPercent":null},"secondary":{"usedPercent":12,"windowDurationMins":null,"resetsAt":null}}},
              "rateLimitResetCredits":{"availableCount":0}}
            """), DateTimeOffset.UtcNow);
        Assert.Equal(2, sample.Windows.Length);
        Assert.Equal(97, sample.Windows[0].UsedPercent);
        Assert.Equal("Codex · Weekly", sample.Windows[0].Label);
        Assert.Null(sample.Windows[1].WindowMinutes);
        Assert.Null(sample.Windows[1].ResetsAt);
        Assert.Equal(0, sample.ResetCredits);
        Assert.DoesNotContain("owner", sample.AccountKey);
        Assert.Equal("ow•••@example.test", sample.AccountLabel);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"rateLimits\":null}")]
    [InlineData("{\"rateLimits\":{\"primary\":{\"usedPercent\":-1}}}")]
    [InlineData("{\"rateLimits\":{\"primary\":{\"usedPercent\":101}}}")]
    [InlineData("{\"rateLimits\":{\"primary\":{\"usedPercent\":\"0\"}}}")]
    [InlineData("{\"rateLimitsByLimitId\":{},\"rateLimits\":{\"primary\":{\"usedPercent\":0}}}")]
    public void MissingOrInvalidAllowanceIsUnavailable(string raw) =>
        Assert.Throws<InvalidDataException>(() => CodexAllowanceReader.Parse(Account, Json(raw), DateTimeOffset.UtcNow));

    [Fact]
    public void ApiKeyAuthCannotBePresentedAsSubscriptionUsage() =>
        Assert.Throws<InvalidDataException>(() => CodexAllowanceReader.Parse(Json("""{"type":"apiKey"}"""), Json("{}"), DateTimeOffset.UtcNow));

    [Fact]
    public void HistorySurvivesRestartKeepsResetsAndSeparatesAccounts()
    {
        var root = Path.Combine(Path.GetTempPath(), "marketing-allowance-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var first = CodexAllowanceReader.Parse(Account, Json("""{"rateLimits":{"primary":{"usedPercent":97,"windowDurationMins":10080,"resetsAt":1800000000}}}"""), now.AddMinutes(-1));
            var history = new CodexAllowanceHistory(root);
            history.Save(first); history.Save(first);
            var after = first with { ObservedAt = now.ToUnixTimeMilliseconds()/1000d, Windows = [first.Windows[0] with { UsedPercent = 0, ResetsAt = 1800600000 }] };
            history.Save(after);
            var reloaded = new CodexAllowanceHistory(root);
            var view = JsonSerializer.SerializeToElement(reloaded.View(true, now));
            Assert.Equal(2, view.GetProperty("samples").GetArrayLength());
            Assert.Equal(97, view.GetProperty("samples")[0].GetProperty("Windows")[0].GetProperty("UsedPercent").GetInt32());
            Assert.Equal(0, view.GetProperty("latest").GetProperty("Windows")[0].GetProperty("UsedPercent").GetInt32());
            Assert.False(view.GetProperty("stale").GetBoolean());
            reloaded.Failed(now.AddMinutes(5));
            view = JsonSerializer.SerializeToElement(reloaded.View(true, now.AddMinutes(5)));
            Assert.True(view.GetProperty("stale").GetBoolean());
            Assert.Equal(2, view.GetProperty("samples").GetArrayLength());
            reloaded.Save(after with { AccountKey = "different-account", ObservedAt = after.ObservedAt + 400 });
            view = JsonSerializer.SerializeToElement(reloaded.View(true, now.AddMinutes(7)));
            Assert.Single(view.GetProperty("samples").EnumerateArray());
            Assert.True(JsonSerializer.SerializeToElement(reloaded.View(true, now.AddMinutes(20))).GetProperty("stale").GetBoolean());
            Assert.True(JsonSerializer.SerializeToElement(reloaded.View(false, now.AddMinutes(7))).GetProperty("stale").GetBoolean());
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task HistoryHoldsNoHandleThatBlocksAStudyBackup()
    {
        var root = Path.Combine(Path.GetTempPath(), "marketing-allowance-backup-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "study");
        Directory.CreateDirectory(root);
        try
        {
            new Thaddeus.Infrastructure.Store(data).Dispose();
            var history = new CodexAllowanceHistory(data);
            history.Save(CodexAllowanceReader.Parse(Account, Json("""{"rateLimits":{"primary":{"usedPercent":40}}}"""), DateTimeOffset.UtcNow));
            // No pool clearing here: maintenance copies the study while the host's allowance history is still alive.
            var receipt = await Thaddeus.Infrastructure.StudyBackup.Create(data, Path.Combine(root, "backup"));
            Assert.True(File.Exists(Path.Combine(receipt.Directory, "data", "codex-allowance.sqlite")));
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
