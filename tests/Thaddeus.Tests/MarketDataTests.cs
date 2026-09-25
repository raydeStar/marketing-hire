using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class MarketDataTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "fe-market-" + Guid.NewGuid().ToString("N"));
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
        var found = await new MarketData(new Store(root)).Look(["5418"], [], notes, CancellationToken.None);
        Assert.True(found.Length == 1, string.Join(" ", notes));
        Assert.Contains("establishments in NAICS 5418 Advertising", found[0].Excerpt);
    }

    [Fact] public async Task SecFiguresWaitForTheOwnersContactAndBlsNeedsNone()
    {
        var market = new MarketData(new Store(root));
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

        Assert.Throws<ArgumentException>(() => market.Save(new("just a name")));
        market.Save(new("Acme Research ops@acme.test"));
        notes.Clear();
        found = await market.Look([], ["HUBS", "Jasper"], notes, CancellationToken.None);
        var sec = Assert.Single(found);
        Assert.Equal("SEC EDGAR", sec.Via);
        Assert.Contains("$2.63 billion for 2024", sec.Excerpt); Assert.Contains("21% growth", sec.Excerpt);
        Assert.Contains(notes, note => note.Contains("“Jasper” (it may be private)"));
        Assert.All(asked.Where(item => item.Url.Contains("sec.gov")), item => Assert.Equal("FirstEmployee market research Acme Research ops@acme.test", item.Agent));
    }
}
