using System.Net;
using System.Net.Sockets;
using System.Text;
using Thaddeus.Host;

namespace Thaddeus.Tests;

public sealed class ResearchReaderTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact] public async Task TheRenderingProxyReachesPublicHttpsOnly()
    {
        foreach (var refused in new[] { "127.0.0.1:443", "localhost:443", "10.1.2.3:443", "192.168.1.10:443", "169.254.169.254:443", "[::1]:443", "1.1.1.1:80", "1.1.1.1" })
            Assert.Null(await PublicOnlyProxy.Target(refused, CancellationToken.None));
        Assert.Equal(IPAddress.Parse("1.1.1.1"), await PublicOnlyProxy.Target("1.1.1.1:443", CancellationToken.None));

        using var proxy = PublicOnlyProxy.Start();
        foreach (var request in new[] { "CONNECT 127.0.0.1:443 HTTP/1.1\r\nHost: 127.0.0.1:443\r\n\r\n", "CONNECT localhost:5190 HTTP/1.1\r\n\r\n", "GET http://example.com/ HTTP/1.1\r\nHost: example.com\r\n\r\n" })
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
            var buffer = new byte[256];
            var read = await stream.ReadAsync(buffer);
            Assert.StartsWith("HTTP/1.1 403", Encoding.ASCII.GetString(buffer, 0, read));
        }
    }

    [Fact] public void TheExcerptStartsNearThePricesWhenMenusComeFirst()
    {
        var (title, body) = SiteReader.Extract("<html><head><title>Plans &amp; pricing</title><script>var x='$1';</script></head><body><template>$9</template>" +
            string.Join(" ", Enumerable.Repeat("Menu item", 400)) + " Pro $ 69 month/ seat Business custom pricing</body></html>");
        Assert.Equal("Plans & pricing", title);
        Assert.DoesNotContain("$1", body); Assert.DoesNotContain("$9", body);
        Assert.Contains("Pro $ 69 month/ seat", SiteReader.Excerpt(body));
        Assert.StartsWith("Short page", SiteReader.Excerpt("Short page, Pro $ 69 a month."));
        // A long page with several plans: the prices lead, so trimming the packet later cuts prose first.
        var plans = "Intro. " + string.Join(" ", Enumerable.Repeat("Feature detail text.", 300)) + " Creator $49 per month billed yearly. " + string.Join(" ", Enumerable.Repeat("More feature text.", 200)) + " Pro $69/mo per seat. Business: talk to sales.";
        var excerpt = SiteReader.Excerpt(plans);
        Assert.StartsWith("Prices on this page: ", excerpt);
        Assert.Contains("Creator $49 per month billed yearly", excerpt[..600]);
        Assert.Contains("Pro $69/mo per seat", excerpt[..600]);
    }

    /// <summary>Opt-in: FE_LIVE_WEB=1 renders a real JavaScript-built pricing page with the local browser.</summary>
    [Fact] public async Task ARealJavaScriptPricingPageRendersWhenAllowed()
    {
        if (Environment.GetEnvironmentVariable("FE_LIVE_WEB") != "1" || PageRenderer.Browser() == null) return;
        foreach (var (url, site) in new[] { ("https://www.jasper.ai/pricing", "jasper.ai"), ("https://www.hubspot.com/pricing/marketing", "hubspot.com"), ("https://www.lindy.ai/pricing", "lindy.ai"), ("https://www.artisan.co/pricing", "artisan.co") })
        {
            var (_, title, text) = await SiteReader.Read(url, [site], CancellationToken.None);
            output.WriteLine($"{url}: {title} | {text.Length} chars | {text[..Math.Min(240, text.Length)]}");
            if (site != "artisan.co") Assert.Matches(@"\$\s?\d", text);  // Artisan publishes no prices: "Get a demo"
        }
    }
}
