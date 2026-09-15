using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class ArtifactApiTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-app-api-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> factory;
    private Store? store;
    public ArtifactApiTests() => factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
        builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
    });
    private HttpClient Client(bool authenticated = true, bool csrf = true)
    {
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        store ??= factory.Services.GetRequiredService<Store>();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        if (authenticated)
        {
            var context = new DefaultHttpContext(); var session = factory.Services.GetRequiredService<Security>().Issue(context, "Fixture app browser", true);
            client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
            if (csrf) client.DefaultRequestHeaders.Add("X-CSRF", session.Csrf);
        }
        return client;
    }
    private static AppEdit Create() => new(Guid.NewGuid().ToString("N"), "absent", new("Reading list", "Books I want to read", [new("book", "Book", "text"), new("done", "Read", "checkbox")], ["done"]), []);
    [Theory]
    [InlineData(false, true, 401)]
    [InlineData(true, false, 403)]
    public async Task AppWritesRequireSessionAndCsrf(bool authenticated, bool csrf, int expected)
    {
        using var client = Client(authenticated, csrf); var id = Guid.NewGuid().ToString("N");
        Assert.Equal((HttpStatusCode)expected, (await client.PutAsJsonAsync("/api/artifacts/" + id, Create())).StatusCode);
        Assert.Equal((HttpStatusCode)expected, (await client.PostAsJsonAsync("/api/artifacts/" + id + "/restore", new AppRestore(id, "absent", "missing"))).StatusCode);
        Assert.Empty(store!.Artifacts());
        if (!authenticated) foreach (var route in new[] { "", "/history", "/export" }) Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/artifacts/" + id + route)).StatusCode);
    }
    [Fact] public async Task AppRoutesPreserveVersionsAndExportDataWithoutPuttingEntriesInState()
    {
        using var client = Client(); var id = Guid.NewGuid().ToString("N"); var edit = Create();
        var created = await (await client.PutAsJsonAsync("/api/artifacts/" + id, edit)).Content.ReadFromJsonAsync<ArtifactApp>(Wire.Json);
        Assert.NotNull(created);
        var entry = new AppEntry("", new() { ["book"] = JsonSerializer.SerializeToElement("Fictional book"), ["done"] = JsonSerializer.SerializeToElement(false) });
        var update = new AppEdit(Guid.NewGuid().ToString("N"), created.Version, Upserts: [entry]);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/artifacts/" + id, update)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/artifacts/" + id, update with { OperationId = Guid.NewGuid().ToString("N") })).StatusCode);
        var current = await client.GetFromJsonAsync<ArtifactApp>("/api/artifacts/" + id, Wire.Json);
        Assert.Single(current!.Entries);
        var state = await client.GetStringAsync("/api/state"); Assert.DoesNotContain("Fictional book", state);
        using var export = await client.GetAsync("/api/artifacts/" + id + "/export");
        Assert.Equal("application/json", export.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Fictional book", await export.Content.ReadAsStringAsync());
        var full = await client.GetFromJsonAsync<JsonElement>("/api/export");
        Assert.Equal(6, full.GetProperty("schemaVersion").GetInt32()); Assert.Single(full.GetProperty("artifacts").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/artifacts/" + id + "/restore", new AppRestore(Guid.NewGuid().ToString("N"), current.Version, created.Version))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<ArtifactApp>("/api/artifacts/" + id, Wire.Json))!.Entries);
        Assert.Empty(store!.List());
    }
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        await factory.DisposeAsync(); store?.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
