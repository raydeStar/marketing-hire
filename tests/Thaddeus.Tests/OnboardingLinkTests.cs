using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

/// <summary>Onboarding opens the website before learning from it: a mistyped address (hire-zero.com for hirezero.app) became the
/// owner's site and a brief of guesses, and the first shift planned a fix for a site that wasn't theirs.</summary>
public sealed class OnboardingLinkTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "onboarding-link-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (factory != null) { var store = factory.Services.GetService<Store>(); await factory.DisposeAsync(); store?.Dispose(); }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++) { try { Directory.Delete(root, true); } catch (IOException) when (attempt < 10) { await Task.Delay(200); } }
    }

    [Fact] public async Task AWebsiteThatDoesntOpenStopsOnboardingBeforeAnythingIsLearned()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "host")); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Marketing:ShiftPump", "off");
        });
        factory.Services.GetRequiredService<EmployeeShifts>().ReadSite = (url, sites, _) => url.Contains("hirezero.app", StringComparison.Ordinal)
            ? Task.FromResult((url, "HireZero", "An AI marketing employee."))
            : url.Contains("js-only", StringComparison.Ordinal) ? throw new IOException("The page had too little readable text (it may need JavaScript).")
            : throw new HttpRequestException("No such host is known.");
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var owner = factory.Services.GetRequiredService<Security>().Issue(context, "Owner", true);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        async Task<JsonElement> Check(string url)
        {
            using var response = await client.PostAsJsonAsync("/api/onboarding/check-link", new { url });
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }
        var typo = await Check("https://hire-zero.com");
        Assert.False(typo.GetProperty("ok").GetBoolean());
        Assert.Contains("No such host", typo.GetProperty("reason").GetString());
        Assert.True((await Check("hirezero.app")).GetProperty("ok").GetBoolean());   // a bare address is fine
        Assert.True((await Check("https://js-only.example")).GetProperty("ok").GetBoolean());   // it exists; its words need a browser
        Assert.False((await Check("hirezero")).GetProperty("ok").GetBoolean());   // not an address at all
    }
}
