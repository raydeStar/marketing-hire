using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class RecoveryApiTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-recovery-api-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> factory;
    public RecoveryApiTests() => factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
    });
    [Theory] [InlineData("inspect")] [InlineData("restore")]
    public async Task CheckpointEndpointsRequireOwnerAndCsrfBeforeAccessingAnyWorker(string action)
    {
        using var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        var url = "/api/runs/missing/recovery/" + action;
        var body = new { version = 1, digest = "not-a-review" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(url, body)).StatusCode);
        var context = new DefaultHttpContext(); var security = factory.Services.GetRequiredService<Security>();
        var paired = security.Issue(context, "Paired fixture", false);
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", paired.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(url, body)).StatusCode);
        context = new DefaultHttpContext(); var owner = security.Issue(context, "Owner fixture", true);
        client.DefaultRequestHeaders.Remove("Cookie"); client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(url, body)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF"); client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
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
