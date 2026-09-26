using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record MarketDataSettings(string Contact = "");
public record MarketDataSettingsEdit(string? Contact);

/// <summary>Public numbers for sizing a market, as citable sources:
/// the size of an industry (BLS Quarterly Census of Employment and Wages: US private establishments, employment, pay; no key),
/// and what public competitors actually earn (SEC EDGAR XBRL revenue from 10-K filings). The SEC asks every requester to
/// identify themselves, so SEC data needs a contact the owner sets; nothing is sent under the owner's name without it.</summary>
public sealed partial class MarketData(Store store)
{
    const string Key = "market-data";
    static readonly string[] Bls = ["bls.gov"], Sec = ["sec.gov"];
    static readonly string[] RevenueConcepts = ["RevenueFromContractWithCustomerExcludingAssessedTax", "Revenues", "SalesRevenueNet"];
    readonly Dictionary<string, (DateTimeOffset At, string Body)> cache = [];

    /// <summary>Fetches a public file; replaceable in tests.</summary>
    public Func<string, IReadOnlyCollection<string>, string?, CancellationToken, Task<string>> Fetch { get; set; } =
        (url, sites, agent, cancellation) => SiteReader.FetchData(url, sites, cancellation, agent);

    public MarketDataSettings Settings() => store.Setting(Key) is { } json ? Wire.Unpack<MarketDataSettings>(json) : new();

    public MarketDataSettings Save(MarketDataSettingsEdit edit)
    {
        var contact = Regex.Replace(edit.Contact ?? "", @"\s+", " ").Trim();
        if (contact.Length > 0 && (contact.Length > 120 || !ContactShape().IsMatch(contact)))
            throw new ArgumentException("Use a name and an email address, for example “Acme Research ops@acme.com”.");
        var settings = new MarketDataSettings(contact);
        store.Setting(Key, Wire.Pack(settings));
        return settings;
    }

    [GeneratedRegex(@"^[\p{L}\p{N} .,&'()-]{2,80} [^@\s]+@[^@\s]+\.[a-z]{2,24}$", RegexOptions.IgnoreCase)] private static partial Regex ContactShape();
    [GeneratedRegex(@"^\d{2,6}$")] private static partial Regex Naics();
    [GeneratedRegex(@"^[A-Z][A-Z.-]{0,9}$")] private static partial Regex Ticker();

    /// <summary>Everything asked for, as sources; each lookup that fails is reported in the notes, never invented.</summary>
    public async Task<ResearchSource[]> Look(IEnumerable<string> industries, IEnumerable<string> companies, List<string> notes, CancellationToken cancellation, bool smallBusinesses = false)
    {
        var found = new List<ResearchSource>();
        if (smallBusinesses)
        {
            try { if (await SmallBusinesses(cancellation) is { } source) found.Add(source); else notes.Add("BLS size-class figures were not found."); }
            catch (Exception error) when (error is IOException or HttpRequestException or InvalidOperationException or OperationCanceledException) { notes.Add("BLS size-class figures were unavailable."); }
        }
        foreach (var code in industries.Select(item => item.Trim()).Where(item => Naics().IsMatch(item)).Distinct().Take(3))
        {
            try { if (await Industry(code, cancellation) is { } source) found.Add(source); else notes.Add($"BLS has no national figures for NAICS {code}."); }
            catch (Exception error) when (error is IOException or HttpRequestException or InvalidOperationException or OperationCanceledException) { notes.Add($"BLS data for NAICS {code} was unavailable."); }
        }
        var names = companies.Select(item => item.Trim()).Where(item => item.Length is > 0 and <= 60).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToArray();
        if (names.Length > 0 && Settings().Contact is not { Length: > 0 } contact) notes.Add("Public-company revenue needs a contact for SEC requests: Settings → Research data.");
        else
            foreach (var name in names)
            {
                try { if (await Company(name, Settings().Contact, cancellation) is { } source) found.Add(source); else notes.Add($"No SEC revenue filing found for “{name}” (it may be private)."); }
                catch (Exception error) when (error is IOException or HttpRequestException or InvalidOperationException or JsonException or OperationCanceledException) { notes.Add($"SEC data for “{name}” was unavailable."); }
            }
        return [.. found];
    }

    async Task<string> Cached(string url, IReadOnlyCollection<string> sites, string? agent, CancellationToken cancellation)
    {
        lock (cache) if (cache.TryGetValue(url, out var hit) && hit.At > DateTimeOffset.UtcNow.AddHours(-12)) return hit.Body;
        var body = await Fetch(url, sites, agent, cancellation);
        lock (cache) { if (cache.Count > 64) cache.Clear(); cache[url] = (DateTimeOffset.UtcNow, body); }
        return body;
    }

    async Task<ResearchSource?> Industry(string naics, CancellationToken cancellation)
    {
        var titles = await Cached("https://data.bls.gov/cew/doc/titles/industry/industry_titles.csv", Bls, null, cancellation);
        // The latest full year: last year's annual averages are published late the following summer.
        for (var year = DateTime.UtcNow.Year - 1; year >= DateTime.UtcNow.Year - 3; year--)
        {
            var url = $"https://data.bls.gov/cew/data/api/{year}/a/industry/{naics}.csv";
            string csv;
            try { csv = await Cached(url, Bls, null, cancellation); }
            catch (IOException) { continue; }
            if (ReadIndustry(csv, naics, IndustryTitle(titles, naics)) is { } summary)
                return new(url, $"BLS QCEW {year}: {summary.Label}", summary.Text, null, new DateTimeOffset(year, 12, 31, 0, 0, 0, TimeSpan.Zero), "BLS");
        }
        return null;
    }

    static readonly (string Code, string Label)[] SizeClasses = [("1", "fewer than 5 employees"), ("2", "5 to 9"), ("3", "10 to 19")];

    /// <summary>US private establishments by employee size (first-quarter QCEW size classes): the base for sizing a market of small businesses.</summary>
    async Task<ResearchSource?> SmallBusinesses(CancellationToken cancellation)
    {
        for (var year = DateTime.UtcNow.Year - 1; year >= DateTime.UtcNow.Year - 3; year--)
        {
            var counts = new List<(string Label, long Establishments, long Employees)>();
            foreach (var (code, label) in SizeClasses)
            {
                string csv;
                try { csv = await Cached($"https://data.bls.gov/cew/data/api/{year}/1/size/{code}.csv", Bls, null, cancellation); }
                catch (IOException) { break; }
                if (ReadSizeClass(csv, code) is { } row) counts.Add((label, row.Establishments, row.Employees));
            }
            if (counts.Count != SizeClasses.Length) continue;
            var text = $"In the first quarter of {year}, the US private sector had {counts[0].Establishments:N0} establishments with {counts[0].Label} ({counts[0].Employees:N0} employees in all), " +
                $"{counts[1].Establishments:N0} with {counts[1].Label} and {counts[2].Establishments:N0} with {counts[2].Label} employees (BLS Quarterly Census of Employment and Wages, size classes, all industries). " +
                "Establishments are locations, not firms, and businesses without employees (sole proprietors working alone) are not counted, so the smallest businesses are undercounted.";
            return new($"https://data.bls.gov/cew/data/api/{year}/1/size/1.csv", $"BLS QCEW {year}: US private establishments by employee size", text, null, new DateTimeOffset(year, 3, 31, 0, 0, 0, TimeSpan.Zero), "BLS");
        }
        return null;
    }

    /// <summary>The US private, all-industries row of a QCEW size-class file.</summary>
    public static (long Establishments, long Employees)? ReadSizeClass(string csv, string sizeCode)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return null;
        var header = lines[0].Split(',').Select(item => item.Trim().Trim('"')).ToList();
        int Column(string name) => header.IndexOf(name);
        if (Column("qtrly_estabs") < 0 || Column("month3_emplvl") < 0) return null;
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split(',').Select(item => item.Trim().Trim('"')).ToArray();
            if (cells.Length < header.Count || cells[Column("area_fips")] != "US000" || cells[Column("own_code")] != "5" || cells[Column("industry_code")] != "10" || cells[Column("size_code")] != sizeCode) continue;
            return long.TryParse(cells[Column("qtrly_estabs")], out var establishments) && long.TryParse(cells[Column("month3_emplvl")], out var employees) ? (establishments, employees) : null;
        }
        return null;
    }

    public static string IndustryTitle(string titlesCsv, string naics)
    {
        foreach (var line in titlesCsv.Split('\n'))
        {
            var comma = line.IndexOf(',');
            if (comma > 0 && line[..comma].Trim('"') == naics)
            {
                var title = line[(comma + 1)..].Trim().Trim('"');
                return Regex.Replace(title, @"^NAICS\s*\d+\s*", "").Trim();
            }
        }
        return "NAICS " + naics;
    }

    /// <summary>The US private-sector row of a QCEW annual industry file, as one sentence of figures.</summary>
    public static (string Label, string Text)? ReadIndustry(string csv, string naics, string title)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return null;
        var header = lines[0].Split(',').Select(item => item.Trim().Trim('"')).ToList();
        int Column(string name) => header.IndexOf(name);
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split(',').Select(item => item.Trim().Trim('"')).ToArray();
            if (cells.Length < header.Count || cells[Column("area_fips")] != "US000" || cells[Column("own_code")] != "5" || cells[Column("industry_code")] != naics) continue;
            if (cells[Column("disclosure_code")] == "N") return null;
            long Number(string name) => long.TryParse(cells[Column(name)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
            var establishments = Number("annual_avg_estabs"); var employees = Number("annual_avg_emplvl"); var wages = Number("total_annual_wages"); var pay = Number("avg_annual_pay");
            if (establishments == 0) return null;
            var label = $"NAICS {naics} {title}";
            var text = $"In {cells[Column("year")]}, the US private sector had {establishments:N0} establishments in {label}, employing {employees:N0} people on average, " +
                $"with total annual wages of {Money(wages)} and average annual pay of {Money(pay)} (BLS Quarterly Census of Employment and Wages, annual averages). " +
                "Establishments are locations, not firms; one-person businesses without employees are not counted.";
            return (label, text);
        }
        return null;
    }

    async Task<ResearchSource?> Company(string name, string contact, CancellationToken cancellation)
    {
        var agent = "FirstEmployee market research " + contact;
        var tickers = await Cached("https://www.sec.gov/files/company_tickers.json", Sec, agent, cancellation);
        if (FindCompany(tickers, name) is not { } company) return null;
        foreach (var concept in RevenueConcepts)
        {
            string json;
            try { json = await Cached($"https://data.sec.gov/api/xbrl/companyconcept/CIK{company.Cik:D10}/us-gaap/{concept}.json", Sec, agent, cancellation); }
            catch (IOException) { continue; }
            if (LatestAnnualRevenue(json) is { } revenue)
                return new($"https://www.sec.gov/cgi-bin/browse-edgar?action=getcompany&CIK={company.Cik:D10}&type=10-K", $"SEC 10-K: {company.Title} ({company.Ticker}) revenue",
                    $"{company.Title} ({company.Ticker}) reported revenue of {Money(revenue.Value)} for {revenue.Frame[2..]} in its annual report (10-K, filed {revenue.Filed}; XBRL {concept})." +
                    (revenue.Prior is { } prior ? $" The year before: {Money(prior)}, so {((double)revenue.Value / prior - 1):P0} growth." : ""),
                    null, DateTimeOffset.Parse(revenue.Filed, CultureInfo.InvariantCulture), "SEC EDGAR");
        }
        return null;
    }

    public static (long Cik, string Ticker, string Title)? FindCompany(string tickersJson, string name)
    {
        using var document = JsonDocument.Parse(tickersJson);
        var wanted = name.Trim();
        var all = document.RootElement.EnumerateObject().Select(item => item.Value)
            .Select(item => (Cik: item.GetProperty("cik_str").GetInt64(), Ticker: item.GetProperty("ticker").GetString() ?? "", Title: item.GetProperty("title").GetString() ?? "")).ToArray();
        if (Ticker().IsMatch(wanted) && all.FirstOrDefault(item => item.Ticker.Equals(wanted, StringComparison.OrdinalIgnoreCase)) is { Cik: > 0 } byTicker) return byTicker;
        static string Plain(string text) => Regex.Replace(text.ToLowerInvariant(), @"[,.]|\b(inc|corp|corporation|co|ltd|plc|holdings|group|the)\b", " ").Trim();
        var plain = Regex.Replace(Plain(wanted), @"\s+", " ");
        var match = all.Where(item => Regex.Replace(Plain(item.Title), @"\s+", " ") == plain).FirstOrDefault();
        return match.Cik > 0 ? match : null;
    }

    /// <summary>The latest calendar-year revenue reported in a 10-K, with the year before when both are there.</summary>
    public static (long Value, string Frame, string Filed, long? Prior)? LatestAnnualRevenue(string conceptJson)
    {
        using var document = JsonDocument.Parse(conceptJson);
        if (!document.RootElement.TryGetProperty("units", out var units) || !units.TryGetProperty("USD", out var usd)) return null;
        var years = usd.EnumerateArray()
            .Where(item => item.TryGetProperty("form", out var form) && form.GetString() is "10-K" or "10-K/A" && item.TryGetProperty("frame", out var frame) && Regex.IsMatch(frame.GetString() ?? "", @"^CY\d{4}$"))
            .Select(item => (Value: item.GetProperty("val").GetInt64(), Frame: item.GetProperty("frame").GetString()!, Filed: item.GetProperty("filed").GetString() ?? ""))
            .GroupBy(item => item.Frame).Select(group => group.OrderByDescending(item => item.Filed, StringComparer.Ordinal).First())
            .OrderBy(item => item.Frame, StringComparer.Ordinal).ToArray();
        if (years.Length == 0) return null;
        var last = years[^1];
        var prior = years.Length > 1 && years[^2].Frame == "CY" + (int.Parse(last.Frame[2..], CultureInfo.InvariantCulture) - 1) ? years[^2].Value : (long?)null;
        return (last.Value, last.Frame, last.Filed, prior);
    }

    static string Money(long value) => value >= 1_000_000_000 ? $"${value / 1e9:0.##} billion" : value >= 1_000_000 ? $"${value / 1e6:0.#} million" : $"${value:N0}";
}
