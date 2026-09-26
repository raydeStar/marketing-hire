using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record ScoreMetric(string Key, string Name, string Unit, string Good, bool Primary, string Source);
public record ScoreObservation(string Metric, string Date, double Value, string Source, DateTimeOffset ImportedAt);
public record ScoreRule(string Direction, double ThresholdPercent);
public record ScoreExperiment(string Id, string Title, string Hypothesis, string Metric, string StartDate, string ReviewDate,
    ScoreRule Rule, string Status, string? Outcome, string? OutcomeNote, string CreatedBy, DateTimeOffset CreatedAt);
public record ScoreImportReceipt(string RequestId, string Digest, int Rows, int Metrics, DateTimeOffset At);
public record ScoreLedger(int Version, ScoreMetric[] Metrics, ScoreObservation[] Observations, ScoreExperiment[] Experiments, ScoreImportReceipt[] Imports);
public record ScoreAnomaly(string Metric, string Name, string Date, double Value, double Baseline, double ChangePercent, double ZScore, string Severity, bool Good);
public record ScoreImportRequest(string RequestId, string? Csv, string? Url, string? Source);
public record ScoreMetricChange(string? Name, string? Unit, string? Good, bool? Primary);
public record ScoreExperimentRequest(string RequestId, string Title, string Hypothesis, string Metric, string StartDate, string ReviewDate, string Direction, double ThresholdPercent);
public record ScoreMeasurement(string ExperimentId, string Metric, double? Baseline, double? During, double? ChangePercent, int BaselinePoints, int DuringPoints);

/// <summary>The employee's scorecard: one primary KPI, leading indicators and experiments, fed by CSV or a
/// published Google Sheet. Anomalies are plain statistics so the morning signal scan costs no model turns.</summary>
public sealed partial class Scorecard(Store store)
{
    private const string Key = "scorecard-v1";
    public const int MaxObservations = 20000, MaxMetrics = 60, MaxCsvCharacters = 2_000_000;
    [GeneratedRegex("[^a-z0-9]+")] private static partial Regex NonKey();
    private ScoreLedger Read() => store.Setting(Key) is { } json ? Wire.Unpack<ScoreLedger>(json) : new(0, [], [], [], []);
    private void Write(ScoreLedger value) => store.Setting(Key, Wire.Pack(value));
    public ScoreLedger Ledger() { lock (store) return Read(); }

    public static string MetricKey(string name)
    {
        var key = NonKey().Replace(name.Trim().ToLowerInvariant(), "_").Trim('_');
        if (key.Length is 0 or > 60) throw new ArgumentException("Each metric needs a name of 1 to 60 letters or digits.");
        return key;
    }

    static string? ParseDate(string raw)
    {
        var value = raw.Trim().Trim('"');
        string[] formats = ["yyyy-MM-dd", "yyyy/MM/dd", "M/d/yyyy", "MM/dd/yyyy", "d/M/yyyy", "yyyyMMdd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "MMM d, yyyy", "d MMM yyyy"];
        return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }
    static double? ParseNumber(string raw)
    {
        var value = raw.Trim().Trim('"').Replace(",", "").Replace("$", "").Replace("€", "").Replace("£", "").Trim();
        var percent = value.EndsWith('%'); if (percent) value = value[..^1];
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : null;
    }

    /// <summary>Split one CSV line, honouring quoted fields.</summary>
    public static List<string> SplitCsv(string line)
    {
        var fields = new List<string>(); var current = new System.Text.StringBuilder(); var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted) { if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; } else if (c == '"') quoted = false; else current.Append(c); }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString());
        return fields;
    }

    /// <summary>Read wide (date, metric, metric…) or long (date, metric, value) CSV into observations.</summary>
    public static (List<(string Metric, string Name, string Date, double Value)> Rows, string Layout) ParseCsv(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) throw new ArgumentException("The CSV is empty.");
        if (csv.Length > MaxCsvCharacters) throw new ArgumentException("A CSV import can be up to 2 million characters.");
        var lines = csv.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Where(line => line.Trim().Length > 0).ToArray();
        if (lines.Length < 2) throw new ArgumentException("The CSV needs a header row and at least one data row.");
        var header = SplitCsv(lines[0]).Select(item => item.Trim().Trim('"')).ToList();
        var dateColumn = header.FindIndex(name => name.Equals("date", StringComparison.OrdinalIgnoreCase) || name.Equals("day", StringComparison.OrdinalIgnoreCase) || name.Equals("week", StringComparison.OrdinalIgnoreCase));
        if (dateColumn < 0) dateColumn = 0;
        var rows = new List<(string, string, string, double)>();
        var metricColumn = header.FindIndex(name => name.Equals("metric", StringComparison.OrdinalIgnoreCase) || name.Equals("kpi", StringComparison.OrdinalIgnoreCase));
        var valueColumn = header.FindIndex(name => name.Equals("value", StringComparison.OrdinalIgnoreCase));
        if (metricColumn >= 0 && valueColumn >= 0)
        {
            foreach (var line in lines.Skip(1))
            {
                var fields = SplitCsv(line);
                if (fields.Count <= Math.Max(dateColumn, Math.Max(metricColumn, valueColumn))) continue;
                if (ParseDate(fields[dateColumn]) is not { } date || ParseNumber(fields[valueColumn]) is not { } value) continue;
                var name = fields[metricColumn].Trim().Trim('"'); if (name.Length == 0) continue;
                rows.Add((MetricKey(name), name, date, value));
            }
            if (rows.Count == 0) throw new ArgumentException("No rows had a readable date and number.");
            return (rows, "long");
        }
        for (var index = 1; index < lines.Length; index++)
        {
            var fields = SplitCsv(lines[index]);
            if (fields.Count <= dateColumn || ParseDate(fields[dateColumn]) is not { } date) continue;
            for (var column = 0; column < header.Count && column < fields.Count; column++)
            {
                if (column == dateColumn || header[column].Length == 0) continue;
                if (ParseNumber(fields[column]) is { } value) rows.Add((MetricKey(header[column]), header[column], date, value));
            }
        }
        if (rows.Count == 0) throw new ArgumentException("No rows had a readable date and number. Put dates in the first column or a column named date.");
        return (rows, "wide");
    }

    public (ScoreLedger Ledger, int Rows, int Metrics) Import(ScoreImportRequest request, string csv, string author)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 120) throw new ArgumentException("A request ID is required.");
        var source = (request.Source ?? (request.Url != null ? "Google Sheet" : "CSV import")).Trim();
        if (source.Length is 0 or > 80) source = "CSV import";
        var digest = Wire.Hash(csv);
        var (rows, _) = ParseCsv(csv);
        lock (store)
        {
            var ledger = Read();
            if (ledger.Imports.FirstOrDefault(item => item.RequestId == request.RequestId) is { } replay)
            {
                if (replay.Digest != digest) throw new ArgumentException("That request ID belongs to another import.");
                return (ledger, replay.Rows, replay.Metrics);
            }
            var metrics = ledger.Metrics.ToDictionary(item => item.Key);
            foreach (var row in rows)
                if (!metrics.ContainsKey(row.Metric))
                {
                    if (metrics.Count >= MaxMetrics) throw new ArgumentException($"The scorecard holds up to {MaxMetrics} metrics.");
                    var name = row.Name.Trim();
                    var lower = name.ToLowerInvariant();
                    // Costs and churn are better when they fall; everything else when it rises.
                    var good = Regex.IsMatch(lower, @"\b(cost|cpa|cpc|cpm|spend|churn|bounce|unsubscribe|refund|complaint|position|rank)") ? "down" : "up";
                    metrics[row.Metric] = new ScoreMetric(row.Metric, name.Length > 80 ? name[..80] : name, lower.Contains('%') || lower.Contains("rate") ? "%" : "", good, metrics.Count == 0, source);
                }
            var now = DateTimeOffset.UtcNow;
            // A later import of the same metric and date replaces the earlier value.
            var incoming = rows.GroupBy(row => (row.Metric, row.Date)).Select(group => new ScoreObservation(group.Key.Metric, group.Key.Date, group.Last().Value, source, now)).ToArray();
            var replaced = incoming.Select(item => (item.Metric, item.Date)).ToHashSet();
            var observations = ledger.Observations.Where(item => !replaced.Contains((item.Metric, item.Date))).Concat(incoming)
                .OrderBy(item => item.Date, StringComparer.Ordinal).ToArray();
            if (observations.Length > MaxObservations) observations = observations[^MaxObservations..];
            var receipt = new ScoreImportReceipt(request.RequestId, digest, incoming.Length, incoming.Select(item => item.Metric).Distinct().Count(), now);
            var next = ledger with { Version = ledger.Version + 1, Metrics = [.. metrics.Values.OrderByDescending(item => item.Primary).ThenBy(item => item.Name)],
                Observations = observations, Imports = [.. ledger.Imports.TakeLast(199), receipt] };
            Write(next);
            return (next, receipt.Rows, receipt.Metrics);
        }
    }

    public ScoreLedger UpdateMetric(string key, ScoreMetricChange change)
    {
        lock (store)
        {
            var ledger = Read();
            var metric = ledger.Metrics.FirstOrDefault(item => item.Key == key) ?? throw new KeyNotFoundException("That metric is not on the scorecard.");
            if (change.Good is { } good && good is not ("up" or "down")) throw new ArgumentException("Choose whether up or down is good.");
            var name = change.Name?.Trim() is { Length: > 0 and <= 80 } named ? named : metric.Name;
            var updated = metric with { Name = name, Unit = change.Unit?.Trim() is { Length: <= 12 } unit ? unit : metric.Unit, Good = change.Good ?? metric.Good, Primary = change.Primary ?? metric.Primary };
            var metrics = ledger.Metrics.Select(item => item.Key == key ? updated : change.Primary == true ? item with { Primary = false } : item).ToArray();
            var next = ledger with { Version = ledger.Version + 1, Metrics = metrics };
            Write(next);
            return next;
        }
    }

    /// <summary>A person's experiment starts running; one the employee proposes waits as "proposed" until the owner starts it.</summary>
    public ScoreExperiment AddExperiment(ScoreExperimentRequest request, string author, bool proposed = false)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 160) throw new ArgumentException("Give the experiment a title of up to 160 characters.");
        if (string.IsNullOrWhiteSpace(request.Hypothesis) || request.Hypothesis.Length > 1000) throw new ArgumentException("State the hypothesis in up to 1,000 characters.");
        if (ParseDate(request.StartDate) is not { } start || ParseDate(request.ReviewDate) is not { } review || string.CompareOrdinal(review, start) <= 0)
            throw new ArgumentException("The review date must come after the start date.");
        if (request.Direction is not ("up" or "down")) throw new ArgumentException("Choose whether the metric should go up or down.");
        if (request.ThresholdPercent is <= 0 or > 1000) throw new ArgumentException("Set the decision threshold between 0 and 1,000 percent.");
        lock (store)
        {
            var ledger = Read();
            if (ledger.Metrics.All(item => item.Key != request.Metric)) throw new ArgumentException("Choose a metric that is on the scorecard.");
            if (ledger.Experiments.FirstOrDefault(item => item.Id == request.RequestId) is { } replay) return replay;
            if (request.RequestId is not { Length: > 0 and <= 120 }) throw new ArgumentException("A request ID is required.");
            var experiment = new ScoreExperiment(request.RequestId, request.Title.Trim(), request.Hypothesis.Trim(), request.Metric, start, review,
                new ScoreRule(request.Direction, request.ThresholdPercent), proposed ? "proposed" : "running", null, null, author, DateTimeOffset.UtcNow);
            Write(ledger with { Version = ledger.Version + 1, Experiments = [.. ledger.Experiments.TakeLast(199), experiment] });
            return experiment;
        }
    }

    /// <summary>The owner starts a proposed experiment: it runs from today for the length it was proposed with.</summary>
    public ScoreExperiment Start(string id, string by)
    {
        lock (store)
        {
            var ledger = Read();
            var experiment = ledger.Experiments.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException("That experiment does not exist.");
            if (experiment.Status == "running") return experiment;
            if (experiment.Status != "proposed") throw new InvalidOperationException("Only a proposed experiment can be started.");
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var days = Math.Max(1, DateOnly.ParseExact(experiment.ReviewDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber - DateOnly.ParseExact(experiment.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber);
            var started = experiment with { Status = "running", StartDate = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ReviewDate = today.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                OutcomeNote = "Started by " + by + "." };
            Write(ledger with { Version = ledger.Version + 1, Experiments = ledger.Experiments.Select(item => item.Id == id ? started : item).ToArray() });
            return started;
        }
    }

    public ScoreExperiment Decline(string id, string note)
    {
        lock (store)
        {
            var ledger = Read();
            var experiment = ledger.Experiments.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException("That experiment does not exist.");
            if (experiment.Status == "declined") return experiment;
            if (experiment.Status != "proposed") throw new InvalidOperationException("Only a proposed experiment can be declined.");
            var declined = experiment with { Status = "declined", OutcomeNote = note.Length > 1000 ? note[..1000] : note };
            Write(ledger with { Version = ledger.Version + 1, Experiments = ledger.Experiments.Select(item => item.Id == id ? declined : item).ToArray() });
            return declined;
        }
    }

    public ScoreExperiment Decide(string id, string outcome, string note)
    {
        lock (store)
        {
            var ledger = Read();
            var experiment = ledger.Experiments.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException("That experiment does not exist.");
            if (experiment.Status == "decided") return experiment;
            var decided = experiment with { Status = "decided", Outcome = outcome, OutcomeNote = note.Length > 1000 ? note[..1000] : note };
            Write(ledger with { Version = ledger.Version + 1, Experiments = ledger.Experiments.Select(item => item.Id == id ? decided : item).ToArray() });
            return decided;
        }
    }

    /// <summary>Material moves only: the latest value against its trailing 14-point baseline.</summary>
    public static ScoreAnomaly[] Anomalies(ScoreLedger ledger)
    {
        var list = new List<ScoreAnomaly>();
        foreach (var metric in ledger.Metrics)
        {
            var series = ledger.Observations.Where(item => item.Metric == metric.Key).OrderBy(item => item.Date, StringComparer.Ordinal).ToArray();
            if (series.Length < 8) continue;
            var latest = series[^1];
            var window = series[^Math.Min(15, series.Length)..^1].Select(item => item.Value).ToArray();
            var mean = window.Average();
            var sd = Math.Sqrt(window.Select(value => (value - mean) * (value - mean)).Sum() / Math.Max(1, window.Length - 1));
            var change = mean == 0 ? (latest.Value == 0 ? 0 : 100) : (latest.Value - mean) / Math.Abs(mean) * 100;
            var z = sd == 0 ? (latest.Value == mean ? 0 : 10) : (latest.Value - mean) / sd;
            if (Math.Abs(z) < 2.5 && Math.Abs(change) < 25) continue;
            var good = metric.Good == "up" ? latest.Value > mean : latest.Value < mean;
            var severity = Math.Abs(z) >= 4 || Math.Abs(change) >= 50 ? "high" : "medium";
            list.Add(new ScoreAnomaly(metric.Key, metric.Name, latest.Date, latest.Value, Math.Round(mean, 2), Math.Round(change, 1), Math.Round(z, 2), severity, good));
        }
        return [.. list.OrderByDescending(item => item.Severity == "high").ThenByDescending(item => Math.Abs(item.ChangePercent))];
    }

    /// <summary>The metric during an experiment against the same number of days before it started.</summary>
    public static ScoreMeasurement Measure(ScoreLedger ledger, ScoreExperiment experiment)
    {
        var start = DateTime.ParseExact(experiment.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var review = DateTime.ParseExact(experiment.ReviewDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var days = Math.Max(1, (review - start).Days);
        var baselineStart = start.AddDays(-days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var series = ledger.Observations.Where(item => item.Metric == experiment.Metric).ToArray();
        var before = series.Where(item => string.CompareOrdinal(item.Date, baselineStart) >= 0 && string.CompareOrdinal(item.Date, experiment.StartDate) < 0).Select(item => item.Value).ToArray();
        var during = series.Where(item => string.CompareOrdinal(item.Date, experiment.StartDate) >= 0 && string.CompareOrdinal(item.Date, experiment.ReviewDate) <= 0).Select(item => item.Value).ToArray();
        double? baseline = before.Length > 0 ? before.Average() : null, current = during.Length > 0 ? during.Average() : null;
        double? change = baseline is { } b && current is { } c ? (b == 0 ? null : Math.Round((c - b) / Math.Abs(b) * 100, 1)) : null;
        return new ScoreMeasurement(experiment.Id, experiment.Metric, baseline is { } bb ? Math.Round(bb, 2) : null, current is { } cc ? Math.Round(cc, 2) : null, change, before.Length, during.Length);
    }

    /// <summary>The pre-set decision rule, applied mechanically.</summary>
    public static (string Outcome, string Note) Rule(ScoreExperiment experiment, ScoreMeasurement measurement, string? metricName = null)
    {
        var metric = metricName ?? experiment.Metric;
        if (measurement.ChangePercent is not { } change || measurement.BaselinePoints < 3 || measurement.DuringPoints < 3)
            return ("insufficient_data", $"Not enough data to apply the rule ({measurement.BaselinePoints} baseline and {measurement.DuringPoints} test points; 3 each needed).");
        var moved = experiment.Rule.Direction == "up" ? change : -change;
        if (moved >= experiment.Rule.ThresholdPercent)
            return ("scale", $"{metric} moved {change:+0.0;-0.0}% against a {experiment.Rule.ThresholdPercent}% {experiment.Rule.Direction} threshold. The rule says scale it.");
        if (moved > 0) return ("iterate", $"{metric} moved {change:+0.0;-0.0}%, the right direction but short of the {experiment.Rule.ThresholdPercent}% threshold. The rule says iterate.");
        return ("stop", $"{metric} moved {change:+0.0;-0.0}%, not in the intended direction. The rule says stop.");
    }

    /// <summary>Fetch a published Google Sheet as CSV. Only docs.google.com over HTTPS, public addresses, 2 MB.</summary>
    public static async Task<string> FetchSheet(string url, CancellationToken cancellation)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "docs.google.com" ||
            !uri.AbsolutePath.StartsWith("/spreadsheets/", StringComparison.Ordinal) || !uri.Query.Contains("output=csv", StringComparison.Ordinal))
            throw new ArgumentException("Use a Google Sheet published as CSV: File → Share → Publish to web → CSV. The link contains output=csv.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false,
            ConnectCallback = async (context, token) => {
                if (context.DnsEndPoint.Host is not ("docs.google.com" or "doc-0s-sheets.googleusercontent.com") && !context.DnsEndPoint.Host.EndsWith(".googleusercontent.com", StringComparison.Ordinal))
                    throw new IOException("The sheet request left Google.");
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
                var address = addresses.FirstOrDefault(MeetingSourceReader.PublicIPv4) ?? throw new IOException("The sheet host has no public address.");
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(new IPEndPoint(address, 443), token); return new NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); throw; }
            } };
        using var client = new HttpClient(handler);
        var target = uri;
        for (var hop = 0; hop < 3; hop++)
        {
            using var response = await client.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect or HttpStatusCode.MovedPermanently && response.Headers.Location is { } next)
            {
                target = next.IsAbsoluteUri ? next : new Uri(target, next);
                if (target.Scheme != "https" || !(target.Host == "docs.google.com" || target.Host.EndsWith(".googleusercontent.com", StringComparison.Ordinal)))
                    throw new IOException("The sheet redirected outside Google.");
                continue;
            }
            if (response.StatusCode != HttpStatusCode.OK) throw new IOException("The sheet could not be read. Check that it is published to the web as CSV.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var bytes = new byte[MaxCsvCharacters + 1]; var length = 0;
            while (length < bytes.Length) { var read = await stream.ReadAsync(bytes.AsMemory(length), timeout.Token); if (read == 0) break; length += read; }
            if (length == bytes.Length) throw new IOException("The sheet is larger than 2 MB.");
            return System.Text.Encoding.UTF8.GetString(bytes, 0, length);
        }
        throw new IOException("The sheet redirected too many times.");
    }

    public object View()
    {
        var ledger = Ledger();
        var cutoff = DateTime.UtcNow.AddDays(-120).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new
        {
            version = ledger.Version,
            metrics = ledger.Metrics,
            series = ledger.Metrics.ToDictionary(metric => metric.Key, metric => ledger.Observations
                .Where(item => item.Metric == metric.Key && string.CompareOrdinal(item.Date, cutoff) >= 0).Select(item => new { item.Date, item.Value }).ToArray()),
            anomalies = Anomalies(ledger),
            experiments = ledger.Experiments.Select(item => new { experiment = item, measurement = Measure(ledger, item) }).Reverse().ToArray(),
            imports = ledger.Imports.TakeLast(10).Reverse().ToArray()
        };
    }
}
