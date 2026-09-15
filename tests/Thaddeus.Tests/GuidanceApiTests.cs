using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class GuidanceApiTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-guidance-api-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> factory;
    public GuidanceApiTests() => factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    { builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179"); });

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task OwnerAndPairedSessionsRequireOriginAndCsrfBeforeGuidanceAdmission(bool owner)
    {
        using var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        const string url = "/api/runs/missing/guidance";
        var body = new { operationId = Guid.NewGuid().ToString("N"), message = "A fictional guidance request." };
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(url, body)).StatusCode);
        var context = new DefaultHttpContext();
        var session = factory.Services.GetRequiredService<Security>().Issue(context, "Guidance fixture", owner);
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(url, body)).StatusCode);
        client.DefaultRequestHeaders.Add("X-CSRF", session.Csrf);
        client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", "https://untrusted.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(url, body)).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(url, body)).StatusCode);
        Assert.Empty(factory.Services.GetRequiredService<Store>().List());
    }
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        var store = factory.Services.GetRequiredService<Store>(); await factory.DisposeAsync(); store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true);
    }
}
