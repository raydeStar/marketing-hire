using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public sealed record AllowanceWindow(string Key, string Label, double UsedPercent, int? WindowMinutes, long? ResetsAt);
public sealed record AllowanceSample(string AccountKey, string AccountLabel, string? Plan, double ObservedAt,
    int? ResetCredits, AllowanceWindow[] Windows);

// Only metadata crosses this boundary. The CLI keeps its credentials in its own cabinet.
internal static class CodexAllowanceReader
{
    internal static AllowanceSample Parse(JsonElement account, JsonElement limits, DateTimeOffset observedAt)
    {
        static string? Text(JsonElement e, string key) => e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        if (Text(account, "type") != "chatgpt" || Text(account, "email") is not { Length: > 0 } email)
            throw new InvalidDataException("A signed-in ChatGPT account is required.");
        var windows = new List<AllowanceWindow>();
        void Bucket(JsonElement bucket, string key)
        {
            if (bucket.ValueKind != JsonValueKind.Object) return;
            foreach (var slot in new[] { "primary", "secondary" })
            {
                if (!bucket.TryGetProperty(slot, out var window) || window.ValueKind != JsonValueKind.Object) continue;
                if (!window.TryGetProperty("usedPercent", out var percent) || percent.ValueKind != JsonValueKind.Number || !percent.TryGetDouble(out var used) ||
                    !double.IsFinite(used) || used is < 0 or > 100) continue;
                int? duration = window.TryGetProperty("windowDurationMins", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out var minutes) && minutes > 0 ? minutes : null;
                long? reset = window.TryGetProperty("resetsAt", out var r) && r.ValueKind == JsonValueKind.Number && r.TryGetInt64(out var seconds) && seconds is > 0 and < 253402300800 ? seconds : null;
                var name = Text(bucket, "limitName");
                var label = duration switch { 10080 => "Weekly", 300 => "5 hours", null => slot == "primary" ? "Primary window" : "Secondary window", _ => $"{duration} minutes" };
                windows.Add(new(key + ":" + slot, (name ?? (key == "codex" ? "Codex" : key)) + " · " + label, used, duration, reset));
            }
        }
        // When the newer map exists it is authoritative, including an empty map.
        if (limits.TryGetProperty("rateLimitsByLimitId", out var map) && map.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in map.EnumerateObject().Take(16)) Bucket(entry.Value, entry.Name);
        }
        else if (limits.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object) Bucket(legacy, Text(legacy, "limitId") ?? "codex");
        if (windows.Count == 0) throw new InvalidDataException("No allowance window was reported.");
        int? credits = limits.TryGetProperty("rateLimitResetCredits", out var c) && c.ValueKind == JsonValueKind.Object &&
            c.TryGetProperty("availableCount", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var count) && count >= 0 ? count : null;
        var normalized = email.Trim().ToLowerInvariant();
        var identity = Text(limits, "accountId") ?? normalized;
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        var at = normalized.IndexOf('@');
        var labelEmail = at > 0 ? normalized[..Math.Min(2, at)] + "•••" + normalized[at..] : "Signed-in account";
        return new(keyHash, labelEmail, Text(account, "planType"), observedAt.ToUnixTimeMilliseconds() / 1000d, credits, windows.ToArray());
    }

    internal static async Task<AllowanceSample> Read(string executable, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var ct = timeout.Token;
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) } };
        process.StartInfo.ArgumentList.Add("app-server");
        process.StartInfo.ArgumentList.Add("--listen");
        process.StartInfo.ArgumentList.Add("stdio://");
        process.Start();
        // Drain, but never log or retain CLI diagnostics that could contain account data.
        var stderr = Task.Run(async () => {
            var buffer = new char[2048];
            while (await process.StandardError.ReadAsync(buffer.AsMemory(), ct) > 0) { }
        }, CancellationToken.None);
        try
        {
            async Task<JsonElement> Request(int id, string method, object parameters)
            {
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { id, method, @params = parameters }).AsMemory(), ct);
                await process.StandardInput.FlushAsync(ct);
                // The stream is bounded and we never start a thread, turn, login or reset.
                for (var i = 0; i < 100; i++)
                {
                    var line = await process.StandardOutput.ReadLineAsync(ct);
                    if (line == null || line.Length > 1_000_000) throw new InvalidDataException("Allowance reader returned no usable result.");
                    using var doc = JsonDocument.Parse(line);
                    if (!doc.RootElement.TryGetProperty("id", out var replyId) || replyId.ValueKind != JsonValueKind.Number || replyId.GetInt32() != id) continue;
                    if (!doc.RootElement.TryGetProperty("result", out var result)) throw new InvalidDataException("Allowance read was unavailable.");
                    return result.Clone();
                }
                throw new InvalidDataException("Allowance reader exceeded its response limit.");
            }
            await Request(0, "initialize", new { clientInfo = new { name = "marketing_allowance", title = "Marketing allowance history", version = "1.0.0" } });
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\",\"params\":{}}".AsMemory(), ct);
            var before = (await Request(1, "account/read", new { refreshToken = false })).GetProperty("account");
            if (before.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Codex is not signed in.");
            var limits = await Request(2, "account/rateLimits/read", new { });
            var after = (await Request(3, "account/read", new { refreshToken = false })).GetProperty("account");
            if (before.GetRawText() != after.GetRawText()) throw new InvalidDataException("The signed-in account changed during this read.");
            return Parse(before, limits, DateTimeOffset.UtcNow);
        }
        finally
        {
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException) { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            timeout.Cancel();
            try { await stderr; } catch (OperationCanceledException) { }
        }
    }
}

public sealed class CodexAllowanceHistory
{
    private readonly string database;
    private readonly object gate = new();
    private double? lastAttemptAt;
    private string? error;
    public CodexAllowanceHistory(Store store) : this(store.Root) { }
    internal CodexAllowanceHistory(string root)
    {
        database = new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "codex-allowance.sqlite"), Pooling = false }.ToString();
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS samples(observed_at REAL PRIMARY KEY,account_key TEXT NOT NULL,payload TEXT NOT NULL);" +
            "CREATE INDEX IF NOT EXISTS samples_account ON samples(account_key,observed_at);";
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var db = new SqliteConnection(database); db.Open(); return db; }
    public void Save(AllowanceSample sample)
    {
        lock (gate)
        {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO samples VALUES($time,$account,$payload)";
            cmd.Parameters.AddWithValue("$time", sample.ObservedAt);
            cmd.Parameters.AddWithValue("$account", sample.AccountKey);
            cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(sample));
            cmd.ExecuteNonQuery(); lastAttemptAt = sample.ObservedAt; error = null;
        }
    }
    public void Failed(DateTimeOffset time)
    {
        lock (gate) { lastAttemptAt = time.ToUnixTimeMilliseconds() / 1000d; error = "Allowance check failed. Last saved observations remain available; no new usage is assumed."; }
    }
    public object View(bool configured, DateTimeOffset now)
    {
        lock (gate)
        {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT payload FROM samples ORDER BY observed_at DESC LIMIT 1";
            var latest = cmd.ExecuteScalar() is string raw ? JsonSerializer.Deserialize<AllowanceSample>(raw) : null;
            var samples = new List<AllowanceSample>();
            if (latest != null)
            {
                cmd.CommandText = "SELECT payload FROM samples WHERE account_key=$account AND observed_at >= $since ORDER BY observed_at LIMIT 10000";
                cmd.Parameters.AddWithValue("$account", latest.AccountKey);
                cmd.Parameters.AddWithValue("$since", now.AddDays(-30).ToUnixTimeSeconds());
                using var rows = cmd.ExecuteReader();
                while (rows.Read()) samples.Add(JsonSerializer.Deserialize<AllowanceSample>(rows.GetString(0))!);
            }
            return new { configured, latest, samples, lastAttemptAt, error,
                stale = latest == null || now.ToUnixTimeSeconds() - latest.ObservedAt > 600 || error != null || !configured,
                pollSeconds = 300, source = "codex_app_server", retention = "History begins with the first successful observation on this host." };
        }
    }
}

internal static class CodexAllowanceCapture
{
    // A single read can establish the baseline before the running web host is restarted.
    public static async Task<int> Run(string[] args)
    {
        if (args is not ["--capture-codex-allowance", "--data", var root, "--executable", var executable] ||
            !Path.IsPathFullyQualified(root) || !Directory.Exists(root) || !Path.IsPathFullyQualified(executable) || !File.Exists(executable)) return 2;
        try
        {
            var sample = await CodexAllowanceReader.Read(executable, CancellationToken.None);
            new CodexAllowanceHistory(root).Save(sample);
            Console.WriteLine(JsonSerializer.Serialize(new { recorded = true, observedAt = sample.ObservedAt,
                windows = sample.Windows, note = "One read-only observation saved. The butler has left the reset lever alone." }));
            return 0;
        }
        catch { Console.Error.WriteLine("Allowance observation unavailable; no model work started. The ledger awaits better evidence."); return 1; }
    }
}

public sealed class CodexAllowanceMonitor(CodexAllowanceHistory history, IConfiguration config) : BackgroundService
{
    private readonly string? executable = config["Marketing:CodexUsageExecutable"];
    public bool Configured => !string.IsNullOrWhiteSpace(executable) && Path.IsPathFullyQualified(executable) && File.Exists(executable)
        && string.IsNullOrWhiteSpace(config["Marketing:FixtureLedger"]);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Configured) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { history.Save(await CodexAllowanceReader.Read(executable!, stoppingToken)); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException or SqliteException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
                or OperationCanceledException or KeyNotFoundException or ArgumentException) { history.Failed(DateTimeOffset.UtcNow); }
            try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
