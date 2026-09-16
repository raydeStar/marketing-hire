using System.Globalization;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed record SearchBudgetEdit(string Version, int MonthlyLimit);
public sealed record SearchBudget(string Version, int MonthlyLimit, int Used, int Remaining, string Month, DateTimeOffset Resets);

public sealed partial class Store
{
    private const string SearchLimitKey = "search-monthly-limit";

    public SearchBudget SearchBudget(DateTimeOffset? at = null)
    {
        lock (gate)
        {
            var now = (at ?? DateTimeOffset.UtcNow).ToUniversalTime();
            var month = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var saved = Setting(SearchLimitKey);
            var limit = saved == null ? 100 : int.Parse(saved, CultureInfo.InvariantCulture);
            // Count the durable intent, including a request whose outcome we never heard back.
            // Read the ledger directly: the UI's paged replay is not an accounting source.
            var used = int.Parse(Query("""
                SELECT CAST(COUNT(*) AS TEXT) FROM events
                WHERE json_extract(body,'$.type')='public.search.intent'
                AND julianday(json_extract(body,'$.timestamp'))>=julianday($start)
                AND julianday(json_extract(body,'$.timestamp'))<julianday($end)
                """, ("$start", month.ToString("O")), ("$end", month.AddMonths(1).ToString("O")))[0], CultureInfo.InvariantCulture);
            used += int.Parse(Setting("temporary-search-count:"+month.ToString("yyyy-MM",CultureInfo.InvariantCulture))??"0",CultureInfo.InvariantCulture);
            return new(Wire.Hash(saved ?? ""), limit, used, Math.Max(0, limit - used),
                month.ToString("yyyy-MM", CultureInfo.InvariantCulture), month.AddMonths(1));
        }
    }

    public SearchBudget SetSearchBudget(SearchBudgetEdit edit)
    {
        if (edit.MonthlyLimit is < 0 or > 100_000)
            throw new ArgumentException("Monthly search limit must be between 0 and 100,000 requests. Zero pauses new searches.");
        lock (gate)
        {
            if (edit.Version != SearchBudget().Version)
                throw new InvalidOperationException("Search limit changed. Reload settings before saving.");
            Setting(SearchLimitKey, edit.MonthlyLimit.ToString(CultureInfo.InvariantCulture));
            return SearchBudget();
        }
    }


    public void ReserveTemporarySearch()
    {
        lock(gate)
        {
            var budget=SearchBudget();
            if(budget.Remaining==0)throw new InvalidOperationException("This study's monthly search limit is reached.");
            var key="temporary-search-count:"+budget.Month;
            Setting(key,(int.Parse(Setting(key)??"0",CultureInfo.InvariantCulture)+1).ToString(CultureInfo.InvariantCulture));
        }
    }

    public void ReservePublicSearch(Run run, CapabilityReceipt pending)
    {
        lock (gate)
        {
            if (SearchBudget().Remaining == 0)
                throw new InvalidOperationException("This study's monthly search limit is reached. Continue with supplied sources, or review the limit in Settings.");
            // Admission and the committed intent share the store lock across every task.
            run.Capabilities.Add(pending);
            Save(run, "public.search.intent", pending);
        }
    }
}
