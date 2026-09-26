using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class MarketDataTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "fe-market-" + Guid.NewGuid().ToString("N"));
    sealed class Vault : ICredentialVault
    {
        public Dictionary<(string, string), string> Entries { get; } = [];
        public Task<string?> Execute(string operation, string scope, string id, string? value, CancellationToken cancellation)
        {
            if (operation == "write") Entries[(scope, id)] = value!;
            if (operation == "forget") Entries.Remove((scope, id));
            return Task.FromResult(operation == "read" ? Entries.GetValueOrDefault((scope, id)) : null);
        }
    }
    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    const string Qcew = "\"area_fips\",\"own_code\",\"industry_code\",\"agglvl_code\",\"size_code\",\"year\",\"qtr\",\"disclosure_code\",\"annual_avg_estabs\",\"annual_avg_emplvl\",\"total_annual_wages\",\"taxable_annual_wages\",\"annual_contributions\",\"annual_avg_wkly_wage\",\"avg_annual_pay\"\n" +
        "\"US000\",\"1\",\"5418\",\"16\",\"0\",\"2024\",\"A\",\"\",45,166,10811624,0,0,1252,65130\n" +
        "\"US000\",\"5\",\"5418\",\"16\",\"0\",\"2024\",\"A\",\"\",80121,489153,52486950808,0,0,2063,107302\n" +
        "\"01000\",\"5\",\"5418\",\"26\",\"0\",\"2024\",\"A\",\"\",700,3000,200000000,0,0,1200,66000\n";
    const string Titles = "\"industry_code\",\"industry_title\"\n\"5418\",\"NAICS 5418 Advertising, PR, and related services\"\n";
    const string Tickers = "{\"0\":{\"cik_str\":1404655,\"ticker\":\"HUBS\",\"title\":\"HubSpot, Inc.\"},\"1\":{\"cik_str\":320193,\"ticker\":\"AAPL\",\"title\":\"Apple Inc.\"}}";
    const string Revenue = "{\"units\":{\"USD\":[" +
        "{\"val\":2170000000,\"form\":\"10-K\",\"fp\":\"FY\",\"frame\":\"CY2023\",\"filed\":\"2024-02-14\"}," +
        "{\"val\":2630000000,\"form\":\"10-K\",\"fp\":\"FY\",\"frame\":\"CY2024\",\"filed\":\"2025-02-12\"}," +
        "{\"val\":700000000,\"form\":\"10-Q\",\"fp\":\"Q3\",\"frame\":\"CY2024Q3\",\"filed\":\"2024-11-06\"}]}}";

    [Fact] public void IndustryFiguresComeFromTheNationalPrivateRow()
    {
        var title = MarketData.IndustryTitle(Titles, "5418");
        Assert.Equal("Advertising, PR, and related services", title);
        var (label, text) = MarketData.ReadIndustry(Qcew, "5418", title)!.Value;
        Assert.Equal("NAICS 5418 Advertising, PR, and related services", label);
        Assert.Contains("80,121 establishments", text); Assert.Contains("489,153 people", text); Assert.Contains("$52.49 billion", text);
        Assert.Contains("one-person businesses without employees are not counted", text);
        Assert.Null(MarketData.ReadIndustry(Qcew, "5419", title));
    }

    [Fact] public void SmallBusinessCountsComeFromTheAllIndustriesRow()
    {
        var csv = "\"area_fips\",\"own_code\",\"industry_code\",\"agglvl_code\",\"size_code\",\"year\",\"qtr\",\"disclosure_code\",\"qtrly_estabs\",\"month1_emplvl\",\"month2_emplvl\",\"month3_emplvl\"\n" +
            "\"US000\",\"5\",\"1011\",\"22\",\"1\",\"2024\",\"1\",\"\",100,200,200,200\n" +
            "\"US000\",\"5\",\"10\",\"21\",\"1\",\"2024\",\"1\",\"\",7782758,10698254,10430238,9966679\n";
        Assert.Equal((7782758L, 9966679L), MarketData.ReadSizeClass(csv, "1"));
        Assert.Null(MarketData.ReadSizeClass(csv, "2"));
    }

    [Fact] public void OnePersonBusinessesComeFromTheCensusAnswer()
    {
        Assert.Equal((29_800_000L, 1_700_000_000L), MarketData.ReadNonemployers("[[\"NESTAB\",\"NRCPTOT\",\"NAICS2022\",\"us\"],[\"29800000\",\"1700000000\",\"00\",\"1\"]]"));
        Assert.Null(MarketData.ReadNonemployers("[[\"NESTAB\"]]"));
    }

    [Fact] public async Task TheCensusKeyStaysInTheCredentialStoreAndOnlyGoesToTheCensus()
    {
        var vault = new Vault();
        var store = new Store(root);
        var market = new MarketData(store, vault);
        var asked = new List<string>();
        market.Fetch = (url, sites, _, _) =>
        {
            asked.Add(url);
            if (url.Contains("census.gov")) Assert.Equal(["census.gov"], sites);
            return Task.FromResult(url.Contains("/nonemp") ? (url.Contains("NAICS2022") ? "[[\"NESTAB\",\"NRCPTOT\",\"NAICS2022\",\"us\"],[\"29800000\",\"1700000000\",\"00\",\"1\"]]" : throw new IOException("400"))
                : "\"area_fips\",\"own_code\",\"industry_code\",\"size_code\",\"qtrly_estabs\",\"month3_emplvl\"\n\"US000\",\"5\",\"10\",\"1\",7782758,9966679\n\"US000\",\"5\",\"10\",\"2\",1533972,10000000\n\"US000\",\"5\",\"10\",\"3\",1094759,14000000\n");
        };
        var notes = new List<string>();
        var found = await market.Look([], [], notes, CancellationToken.None, smallBusinesses: true);
        Assert.Single(found); Assert.Contains(notes, note => note.Contains("free Census key"));
        Assert.DoesNotContain(asked, url => url.Contains("census.gov"));

        await Assert.ThrowsAsync<ArgumentException>(() => market.Save(new(null, "not a key!")));
        var saved = await market.Save(new(null, "abcdef0123456789abcdef0123456789abcdef01"));
        Assert.True(saved.Census);
        Assert.DoesNotContain("abcdef0123456789", store.Setting("market-data"));  // the key is in the credential store only
        notes.Clear();
        found = await market.Look([], [], notes, CancellationToken.None, smallBusinesses: true);
        var census = Assert.Single(found, item => item.Via == "Census");
        Assert.Contains("29,800,000 US businesses with no paid employees", census.Excerpt); Assert.Contains("$1.7 trillion", census.Excerpt);
        Assert.Contains(asked, url => url.StartsWith("https://api.census.gov/data/") && url.EndsWith("&key=abcdef0123456789abcdef0123456789abcdef01"));

        Assert.False((await market.Save(new(null, null, true))).Census);
        Assert.Empty(vault.Entries);
    }

    [Fact] public void CompanyRevenueIsTheLatestAnnualFilingWithGrowth()
    {
        Assert.Equal(1404655, MarketData.FindCompany(Tickers, "HUBS")!.Value.Cik);
        Assert.Equal(1404655, MarketData.FindCompany(Tickers, "HubSpot")!.Value.Cik);
        Assert.Null(MarketData.FindCompany(Tickers, "Jasper"));
        var revenue = MarketData.LatestAnnualRevenue(Revenue)!.Value;
        Assert.Equal((2630000000L, "CY2024", 2170000000L), (revenue.Value, revenue.Frame, revenue.Prior!.Value));
    }

    /// <summary>Opt-in: FE_LIVE_WEB=1 reads the real BLS files (no key, no contact needed).</summary>
    [Fact] public async Task RealBlsFiguresForAnIndustry()
    {
        if (Environment.GetEnvironmentVariable("FE_LIVE_WEB") != "1") return;
        var notes = new List<string>();
        var found = await new MarketData(new Store(root), new Vault()).Look(["5418"], [], notes, CancellationToken.None, smallBusinesses: true);
        Assert.True(found.Length == 2, string.Join(" ", notes));
        Assert.Contains("establishments with fewer than 5 employees", found[0].Excerpt);
        Assert.Contains("establishments in NAICS 5418 Advertising", found[1].Excerpt);
    }

    [Fact] public async Task SecFiguresWaitForTheOwnersContactAndBlsNeedsNone()
    {
        var vault = new Vault();
        var store = new Store(root);
        var market = new MarketData(store, vault);
        var asked = new List<(string Url, string? Agent)>();
        market.Fetch = (url, _, agent, _) =>
        {
            asked.Add((url, agent));
            return Task.FromResult(url.Contains("industry_titles") ? Titles : url.Contains("/industry/") ? Qcew : url.Contains("company_tickers") ? Tickers : url.Contains("RevenueFromContract") ? Revenue : throw new IOException("404"));
        };
        var notes = new List<string>();
        var found = await market.Look(["5418", "not-a-code"], ["HUBS"], notes, CancellationToken.None);
        Assert.Equal("BLS", Assert.Single(found).Via);
        Assert.Contains(notes, note => note.Contains("Settings → Research data"));
        Assert.DoesNotContain(asked, item => item.Url.Contains("sec.gov"));

        await Assert.ThrowsAsync<ArgumentException>(() => market.Save(new("just a name")));
        await market.Save(new("Acme Research ops@acme.test"));
        notes.Clear();
        found = await market.Look([], ["HUBS", "Jasper"], notes, CancellationToken.None);
        var sec = Assert.Single(found);
        Assert.Equal("SEC EDGAR", sec.Via);
        Assert.Contains("$2.63 billion for 2024", sec.Excerpt); Assert.Contains("21% growth", sec.Excerpt);
        Assert.Contains(notes, note => note.Contains("“Jasper” (it may be private)"));
        Assert.All(asked.Where(item => item.Url.Contains("sec.gov")), item => Assert.Equal("FirstEmployee market research Acme Research ops@acme.test", item.Agent));
    }
}
