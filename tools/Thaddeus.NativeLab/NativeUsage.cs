using System.Text;
using Microsoft.Data.Sqlite;
using Thaddeus.Core;

internal record NativeUsageRow(string Item, string State, int? Calls, int? ReportedInput, int? ReportedOutput,
    int? PendingOrUnreportedCalls, int? ChargedTokens, int? ReservedTokens, int? RemainingAllowance, string? ReadFailure = null);
internal record NativeUsageSnapshot(string Scope, string Mode, string Model, int TokenAllowance,
    int KnownInputSubtotal, int KnownOutputSubtotal, bool Incomplete, NativeUsageRow[] Tasks, string Note);

internal static class NativeUsage
{
    private static readonly Dictionary<string, string> lastPublished = new(StringComparer.OrdinalIgnoreCase);
    internal static NativeUsageSnapshot Read(string root, NativeRegistration registration)
    {
        registration.ValidatePlan();
        var rows = registration.Plan.Select(item => ReadRow(Path.Combine(root, item.Id, "ledger.sqlite"), item.Id, registration.Budget)).ToArray();
        return new("This registered Native Lab campaign only; excludes the main app, other campaigns, CLI work and benchmarks.",
            registration.Mode, registration.Provider.Model, registration.Plan.Length * registration.Budget.MaxTotalTokens,
            rows.Sum(row => row.ReportedInput ?? 0), rows.Sum(row => row.ReportedOutput ?? 0),
            rows.Any(row => row.ReadFailure != null || row.PendingOrUnreportedCalls != 0), rows,
            registration.Live ? "Provider-reported subtotals; missing calls stay explicit. Charges and reservations enforce host admission, not a certified remote ceiling or monetary bill. The butler counts before opening the purse."
                : "Synthetic test counts only; no inference or billing. The butler labels his stage money.");
    }
    private static NativeUsageRow ReadRow(string database, string item, Budget budget)
    {
        if (!File.Exists(database)) return new(item, "not-started", 0, 0, 0, 0, 0, 0, budget.MaxTotalTokens);
        try
        {
            // Never construct Store here: observing an old campaign must not migrate or recover it.
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private, Pooling = false, DefaultTimeout = 2 }.ToString());
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT body FROM runs LIMIT 2";
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return new(item, "preparing", 0, 0, 0, 0, 0, 0, budget.MaxTotalTokens);
            var run = Wire.Unpack<Run>(reader.GetString(0));
            if (reader.Read()) throw new InvalidOperationException("Multiple tasks in one registered item.");
            var known = run.ModelDispatches.Count(dispatch => dispatch.Status == "completed" && dispatch.InputTokens is >= 0 && dispatch.OutputTokens is >= 0);
            return new(item, run.State + " / " + run.Research?.Phase, run.ModelCalls,
                run.ModelDispatches.Where(dispatch => dispatch.InputTokens is >= 0).Sum(dispatch => dispatch.InputTokens!.Value),
                run.ModelDispatches.Where(dispatch => dispatch.OutputTokens is >= 0).Sum(dispatch => dispatch.OutputTokens!.Value),
                Math.Max(0, run.ModelCalls - known), run.ChargedTokens, run.ReservedTokens,
                Math.Max(0, budget.MaxTotalTokens - run.ChargedTokens - run.ReservedTokens));
        }
        catch (Exception error) when (error is SqliteException or IOException or System.Text.Json.JsonException or InvalidOperationException)
        { return new(item, "usage-unavailable", null, null, null, null, null, null, null, error.GetType().Name); }
    }
    internal static void Publish(string root, NativeRegistration registration)
    {
        lock (lastPublished)
        {
            var snapshot = Read(root, registration); var json = Wire.Pack(snapshot);
            if (lastPublished.TryGetValue(root, out var previous) && previous == json) return;
            static string N(int? value) => value?.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
            var text = new StringBuilder("# Native Lab token usage\n\n")
                .AppendLine(snapshot.Scope).AppendLine()
                .AppendLine($"Model: **{snapshot.Model}** · Mode: {snapshot.Mode} · Campaign allowance: **{N(snapshot.TokenAllowance)}** tokens.")
                .AppendLine().AppendLine($"Known reported subtotal: **{N(snapshot.KnownInputSubtotal)} input + {N(snapshot.KnownOutputSubtotal)} output**.")
                .AppendLine(snapshot.Incomplete ? "**Accounting is incomplete: inspect pending/unreported calls below.**" : "No pending or unreported calls in the observed ledger.")
                .AppendLine().AppendLine("| Task | State | Calls | Input reported | Output reported | Pending / unreported | Charged | Reserved | Remaining allowance |")
                .AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var row in snapshot.Tasks)
                text.AppendLine($"| {row.Item} | {row.State} | {N(row.Calls)} | {N(row.ReportedInput)} | {N(row.ReportedOutput)} | {N(row.PendingOrUnreportedCalls)} | {N(row.ChargedTokens)} | {N(row.ReservedTokens)} | {N(row.RemainingAllowance)} |");
            text.AppendLine().AppendLine(snapshot.Note).AppendLine().AppendLine("Updated: " + DateTimeOffset.UtcNow.ToString("O") + ". The display refreshes during this runner's work; use the read-only `usage` command for a fresh observation later.");
            WriteSnapshot(Path.Combine(root, "usage.json"), json); WriteSnapshot(Path.Combine(root, "usage.md"), text.ToString());
            lastPublished[root] = json;
        }
    }
    private static void WriteSnapshot(string destination, string text)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, text); File.Move(temporary, destination, overwrite: true);
    }
}
