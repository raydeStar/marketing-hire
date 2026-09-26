using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class PageWatchTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "page-watch-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++) { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }

    [Fact] public void PricesAreReadInPageOrderAndChangesAreSaidPlainly()
    {
        Assert.Equal(["$29/month", "$69/seat", "€1,200"], PageWatch.Prices("Starter $ 29 / month. Pro $69 per seat. Enterprise €1,200. Starter $29 / month again."));
        var before = new WatchedPage("https://rival.test/pricing", "Pricing", DateTimeOffset.UtcNow.AddDays(-1), ["$29/month", "$69/month"], PageWatch.Words("Starter Pro plans for small teams"), "", null);
        var after = before with { CheckedAt = DateTimeOffset.UtcNow, Prices = ["$29/month", "$79/month"] };
        var change = Assert.Single(PageWatch.Compare(before, after));
        Assert.Equal("prices", change.Kind);
        Assert.Equal("Prices changed on rival.test/pricing: No longer shown: $69/month; now shown: $79/month. Before: $29/month, $69/month. Now: $29/month, $79/month.", change.Summary);
        Assert.Empty(PageWatch.Compare(before, before with { CheckedAt = DateTimeOffset.UtcNow }));
        var reworded = before with { Words = PageWatch.Words("Completely different wording about enterprise security compliance governance") };
        Assert.Equal("copy", Assert.Single(PageWatch.Compare(before, reworded)).Kind);
    }

    [Fact] public async Task AWatchedPageStartsABaselineThenAPriceChangeReachesTheShift()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host")); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:ShiftPump", "off");
        });
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var owner = factory.Services.GetRequiredService<Security>().Issue(context, "Owner", true);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        object Content(string[] pages) => new { expectedVersion = 0, content = new { objectives = Array.Empty<object>(), competitors = Array.Empty<object>(), currentFocus = "", nonGoals = Array.Empty<string>(),
            ownSite = "https://www.acme.test/", researchSites = new[] { "rival.test" }, watchPages = pages } };

        // Only pages on the research sites can be watched; the owner's own site joins the research sites.
        using (var refused = await client.PutAsJsonAsync("/api/objectives", Content(["https://elsewhere.test/pricing"])))
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using (var saved = await client.PutAsJsonAsync("/api/objectives", Content(["https://rival.test/pricing"])))
        {
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var content = JsonDocument.Parse(await saved.Content.ReadAsStringAsync()).RootElement.GetProperty("revision").GetProperty("content");
            Assert.Equal("acme.test", content.GetProperty("ownSite").GetString());
            Assert.Equal(["acme.test", "rival.test"], content.GetProperty("researchSites").EnumerateArray().Select(item => item.GetString()!));
        }

        var watch = factory.Services.GetRequiredService<PageWatch>();
        var listening = factory.Services.GetRequiredService<MarketListening>();
        var now = DateTimeOffset.UtcNow; watch.Clock = () => now;
        var page = "Pricing. Starter $29 / month for small teams. Pro $69 / month with approvals and receipts.";
        var reads = 0; var fail = false;
        watch.ReadPage = (url, sites, _) => { reads++; Assert.Contains("rival.test", sites); return fail ? throw new IOException("It could not be read (503).") : Task.FromResult((url, "Rival pricing", page)); };

        await listening.Scan(CancellationToken.None);
        Assert.Equal(1, reads);
        Assert.Empty(listening.Signals());  // the first read is the baseline
        await listening.Scan(CancellationToken.None);
        Assert.Equal(1, reads);              // read at most once a day

        // A failed read keeps the baseline; the next good read still compares against it.
        now = now.AddHours(21); fail = true;
        await listening.Scan(CancellationToken.None);
        now = now.AddHours(2); fail = false; page = page.Replace("$69", "$79");
        await listening.Scan(CancellationToken.None);
        var signal = Assert.Single(listening.Signals());
        Assert.Equal("competitor_change", signal.Kind);
        Assert.Contains("now shown: $79/month", signal.Detail);
        var source = listening.PageSource("https://rival.test/pricing")!;
        Assert.Equal("Watched page", source.Via); Assert.Contains("Before: $29/month, $69/month", source.Excerpt);

        using var view = await client.GetAsync("/api/listening");
        var watched = JsonDocument.Parse(await view.Content.ReadAsStringAsync()).RootElement.GetProperty("watch")[0];
        Assert.Equal(["$29/month", "$79/month"], watched.GetProperty("prices").EnumerateArray().Select(item => item.GetString()!));
        Assert.Equal("prices", watched.GetProperty("lastChange").GetProperty("kind").GetString());
    }
}
