using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class ConversationRetryApiTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-retry-api-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> factory;
    private Store? store;
    public ConversationRetryApiTests() => factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
        builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
    });
    private HttpClient Client(bool authenticated = true, bool csrf = true)
    {
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        store ??= factory.Services.GetRequiredService<Store>();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        if (authenticated)
        {
            var context = new DefaultHttpContext(); var session = factory.Services.GetRequiredService<Security>().Issue(context, "Retry fixture", true);
            client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
            if (csrf) client.DefaultRequestHeaders.Add("X-CSRF", session.Csrf);
        }
        return client;
    }
    [Theory][InlineData(false,true,401)][InlineData(true,false,403)]
    public async Task RetryRequiresSessionAndCsrf(bool authenticated,bool csrf,int expected)
    {
        using var client = Client(authenticated,csrf);
        var response = await client.PostAsJsonAsync("/api/chat/unknown/retry", new { operationId = Guid.NewGuid().ToString("N") });
        Assert.Equal((HttpStatusCode)expected,response.StatusCode); Assert.Empty(store!.List());
    }
    [Fact] public async Task RouteQueuesOnceWithCurrentConnectionAndNoDuplicateTranscript()
    {
        using var client = Client(); var runtime = factory.Services.GetRequiredService<Runtime>();
        var source = runtime.Converse("Fixture request",new(),new(ModelCalls:0)); await runtime.Execute(source.Id);
        var provider = new ProviderSnapshot(Model:"current-fixture"); store!.Setting("provider",Wire.Pack(provider));
        var operationId = Guid.NewGuid().ToString("N"); var route = "/api/chat/" + source.Id + "/retry";
        using var response = await client.PostAsJsonAsync(route,new { operationId }); response.EnsureSuccessStatusCode();
        var retry = await response.Content.ReadFromJsonAsync<Run>(Wire.Json);
        using var repeated = await client.PostAsJsonAsync(route,new { operationId }); repeated.EnsureSuccessStatusCode();
        Assert.Equal(retry!.Id,(await repeated.Content.ReadFromJsonAsync<Run>(Wire.Json))!.Id);
        Assert.Equal(provider,retry.Goal.Provider); Assert.Equal(2,store.List().Count); Assert.Single(store.Chats());
        await runtime.Execute(retry.Id); // Join the same per-run gate before disposing the fixture.
        Assert.Equal(0,store.Get(retry.Id)!.ModelCalls);
    }
    public Task InitializeAsync()=>Task.CompletedTask;
    public async Task DisposeAsync(){await factory.DisposeAsync();store?.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
}
