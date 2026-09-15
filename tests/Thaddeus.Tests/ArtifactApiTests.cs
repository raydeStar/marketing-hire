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
        if (!authenticated) foreach (var route in new[] { "", "/history", "/export", "/page" }) Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/artifacts/" + id + route)).StatusCode);
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
        Assert.Equal(7, full.GetProperty("schemaVersion").GetInt32()); Assert.Single(full.GetProperty("artifacts").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/artifacts/" + id + "/restore", new AppRestore(Guid.NewGuid().ToString("N"), current.Version, created.Version))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<ArtifactApp>("/api/artifacts/" + id, Wire.Json))!.Entries);
        Assert.Empty(store!.List());
    }
    [Fact] public async Task GeneratedPageIsAnAuthenticatedSandboxedDocumentWithoutEmbeddedStudyData()
    {
        using var client = Client(); var id = Guid.NewGuid().ToString("N");
        var create = Create();
        var page = new AppPage("<main>Custom reading room</main>", "main{padding:20px}", "window.thaddeus.onChange(state=>{});");
        var entry = new AppEntry("", new() { ["book"] = JsonSerializer.SerializeToElement("Private fixture title") });
        var response = await client.PutAsJsonAsync("/api/artifacts/" + id, create with { Definition = create.Definition! with { Page = page }, Upserts = [entry] });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await client.GetAsync("/api/artifacts/" + id + "/page");
        Assert.Equal("text/html", document.Content.Headers.ContentType!.MediaType);
        var policy = document.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("sandbox allow-scripts allow-forms;", policy); Assert.Contains("form-action 'none'", policy); Assert.DoesNotContain("allow-same-origin", policy);
        Assert.Contains("connect-src 'none'", policy); Assert.Contains("frame-src 'none'", policy);
        Assert.Contains("frame-ancestors 'self'", policy); Assert.Contains("no-store", document.Headers.CacheControl!.ToString());
        var html = await document.Content.ReadAsStringAsync();
        Assert.Contains(page.Html, html); Assert.Contains(page.JavaScript, html); Assert.DoesNotContain("Private fixture title", html);
        Assert.DoesNotContain("thaddeus-session", html); Assert.DoesNotContain("X-CSRF", html);
    }
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        await factory.DisposeAsync(); store?.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
