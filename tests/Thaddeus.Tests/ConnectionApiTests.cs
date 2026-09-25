using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class ConnectionApiTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-connection-api-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> factory;
    private readonly Vault vault = new();
    private Store? store;
    private const string Key = "fictional-only-api-credential";
    private int searchRequests;
    private sealed class SearchHandler(Action sent):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            sent();Assert.Equal(Key,request.Headers.GetValues("X-Subscription-Token").Single());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new {web=new {results=new[]{new {url="https://example.com/fictional-garden",title="Fictional garden",description="Only a test response"}}}})});
        }
    }
    private sealed class Vault : ICredentialVault
    {
        public Dictionary<(string, string), string> Entries = [];
        public Task<string?> Execute(string operation, string scope, string id, string? value, CancellationToken cancellation)
        {
            if (operation == "write") Entries[(scope, id)] = value!;
            if (operation == "forget") Entries.Remove((scope, id));
            return Task.FromResult(operation == "read" ? Entries.GetValueOrDefault((scope, id)) : null);
        }
    }
    private sealed class Address : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, proceed) => { context.Connection.RemoteIpAddress = context.Request.Headers.ContainsKey("Fixture-Remote") ? IPAddress.Parse("192.0.2.7") : IPAddress.Loopback; return proceed(); }); next(app);
        };
    }
    public ConnectionApiTests()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.ConfigureServices(services => { services.AddSingleton<ICredentialVault>(vault); services.AddSingleton<IStartupFilter, Address>(); services.AddSingleton<TemporaryPublicSearch>(s=>new(s.GetRequiredService<SearchConnections>(),()=>new SearchHandler(()=>searchRequests++))); });
        });
    }
    private HttpClient Client(bool? owner = true, bool csrf = true)
    {
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        store ??= factory.Services.GetRequiredService<Store>(); client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        if (owner != null)
        {
            var context = new DefaultHttpContext(); var session = factory.Services.GetRequiredService<Security>().Issue(context, "Fixture browser", owner.Value);
            client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
            if (csrf) client.DefaultRequestHeaders.Add("X-CSRF", session.Csrf);
        }
        return client;
    }
    private static async Task<JsonElement> View(HttpClient client) => await client.GetFromJsonAsync<JsonElement>("/api/settings/connection");
    private static ConnectionEdit Edit(string version, string mode = "system") => new(version, new("compatible", "fixture-model", "high", "https://provider.invalid/v1"), mode, mode is "system" or "session" ? Key : null);
    [Theory]
    [InlineData(null, true, 401)]
    [InlineData(false, true, 403)]
    [InlineData(true, false, 403)]
    public async Task KeyManagementRequiresOwnerAndCsrf(bool? owner, bool csrf, int code)
    {
        using var client = Client(owner, csrf);
        Assert.Equal((HttpStatusCode)code, (await client.PutAsJsonAsync("/api/settings/connection", Edit("unknown"))).StatusCode);
        Assert.Empty(vault.Entries); Assert.Null(store!.Setting("provider-credentials"));
        Assert.Equal((HttpStatusCode)code, (await client.PostAsJsonAsync("/api/settings/connection/credentials/missing/remove", new { version = "unknown" })).StatusCode);
    }
    [Fact] public async Task RemoteOwnerSessionCannotManageKeys()
    {
        using var client = Client(); client.DefaultRequestHeaders.Add("Fixture-Remote", "true");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/settings/connection")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/settings/connection", Edit("unknown"))).StatusCode);
        Assert.Empty(vault.Entries);
    }
    [Theory]
    [InlineData(null, true, false, 401)]
    [InlineData(false, true, false, 403)]
    [InlineData(true, false, false, 403)]
    [InlineData(true, true, true, 403)]
    public async Task SearchLimitRequiresLocalOwnerAndCsrf(bool? owner, bool csrf, bool remote, int code)
    {
        using var client = Client(owner, csrf);
        if (remote) client.DefaultRequestHeaders.Add("Fixture-Remote", "true");
        var current = store!.SearchBudget();
        using var result = await client.PutAsJsonAsync("/api/settings/search/budget", new SearchBudgetEdit(current.Version, 0));
        Assert.Equal((HttpStatusCode)code, result.StatusCode);
        Assert.Equal(100, store.SearchBudget().MonthlyLimit); Assert.Empty(vault.Entries);
    }
    [Fact] public async Task SearchLimitIsVisibleAndStaleEditsFailWithoutResettingTheSetting()
    {
        using var client = Client(); var before = store!.SearchBudget();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/settings/search/budget", new SearchBudgetEdit(before.Version, 25))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/settings/search/budget", new SearchBudgetEdit(before.Version, 100))).StatusCode);
        var state = await client.GetFromJsonAsync<JsonElement>("/api/state");
        Assert.Equal(25, state.GetProperty("search").GetProperty("budget").GetProperty("monthlyLimit").GetInt32());
        Assert.Equal(0, store.SearchBudget().Used); Assert.Empty(vault.Entries); Assert.Empty(store.List());
    }
    [Fact] public async Task StandardSearchIsTemporaryAndStopsAtTheSharedAllowance()
    {
        using var client=Client();var current=await client.GetFromJsonAsync<JsonElement>("/api/settings/search");
        Assert.Equal(HttpStatusCode.OK,(await client.PutAsJsonAsync("/api/settings/search",new SearchConnectionEdit(current.GetProperty("version").GetString()!,"session",Key,false))).StatusCode);
        store!.SetSearchBudget(new(store.SearchBudget().Version,1));
        var result=await client.PostAsJsonAsync("/api/search/temporary",new {query="fictional-garden-private-query"});Assert.Equal(HttpStatusCode.OK,result.StatusCode);
        Assert.True(result.Headers.CacheControl!.NoStore);Assert.Contains("Fictional garden",await result.Content.ReadAsStringAsync());
        Assert.Equal(1,searchRequests);Assert.Equal(1,store.SearchBudget().Used);
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/search/temporary",new {query="over allowance"})).StatusCode);Assert.Equal(1,searchRequests);
        var export=await client.GetStringAsync("/api/export");Assert.DoesNotContain("fictional-garden",export);Assert.DoesNotContain(Key,export);
        Assert.Empty(store.AllEvents());Assert.Empty(store.List());Assert.Empty(store.Chats());
        using var noCsrf=Client(csrf:false);Assert.Equal(HttpStatusCode.Forbidden,(await noCsrf.PostAsJsonAsync("/api/search/temporary",new {query="blocked"})).StatusCode);
    }
    [Fact] public async Task UploadsOfARealSizeGetThroughWhileOtherCallsStaySmall()
    {
        using var client=Client();using var form=new MultipartFormDataContent();var image=new byte[1024*1024];new byte[]{0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A}.CopyTo(image,0);form.Add(new ByteArrayContent(image),"file","screenshot.png");
        var response=await client.PostAsync("/api/uploads",form);
        Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());Assert.Equal(1024*1024,(await response.Content.ReadFromJsonAsync<UploadFile>(Wire.Json))!.Bytes);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,(await client.PostAsJsonAsync("/api/search/temporary",new {query=new string('q',200_000)})).StatusCode);
        // Recorded narration: a real WAV is media; a file that only claims to be one is refused.
        var wav=new byte[3*1024*1024];System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(wav,0);System.Text.Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(wav,8);
        using var voice=new MultipartFormDataContent();voice.Add(new ByteArrayContent(wav),"file","narration-scene-1.wav");
        var clip=await client.PostAsync("/api/uploads",voice);Assert.True(clip.IsSuccessStatusCode,await clip.Content.ReadAsStringAsync());
        Assert.Equal("audio/wav",(await clip.Content.ReadFromJsonAsync<UploadFile>(Wire.Json))!.MediaType);
        using var fake=new MultipartFormDataContent();fake.Add(new ByteArrayContent(new byte[2048]),"file","narration-scene-2.wav");
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/api/uploads",fake)).StatusCode);
    }
    [Fact] public async Task UploadedTextIsServedAsInertContentWithAuthenticatedSoftDeletion()
    {
        using var client=Client();using var form=new MultipartFormDataContent();form.Add(new StringContent("<script>not executable</script>"),"file","fictional.txt");
        var response=await client.PostAsync("/api/uploads",form);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var file=(await response.Content.ReadFromJsonAsync<UploadFile>(Wire.Json))!;
        var content=await client.GetAsync("/api/uploads/"+file.Id+"/content");Assert.True(content.Headers.CacheControl!.NoStore);Assert.Equal("text/plain",content.Content.Headers.ContentType!.MediaType);Assert.Contains("sandbox",content.Headers.GetValues("Content-Security-Policy").Single());
        using var guest=Client(null);Assert.Equal(HttpStatusCode.Unauthorized,(await guest.GetAsync("/api/uploads/"+file.Id+"/content")).StatusCode);
        using var noCsrf=Client(csrf:false);Assert.Equal(HttpStatusCode.Forbidden,(await noCsrf.PutAsJsonAsync("/api/uploads/"+file.Id,new UploadEdit(file.Version,true))).StatusCode);
        var archived=(await (await client.PutAsJsonAsync("/api/uploads/"+file.Id,new UploadEdit(file.Version,true))).Content.ReadFromJsonAsync<UploadFile>(Wire.Json))!;
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/uploads/"+file.Id+"/content")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PutAsJsonAsync("/api/uploads/"+file.Id,new UploadEdit(archived.Version,false))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/uploads/"+file.Id+"/content")).StatusCode);
    }
    [Fact] public async Task ConnectionNeverEchoesKeyAndRemovedKeyStopsChatBeforeChargingTokens()
    {
        using var client = Client(); var before = await View(client);
        using var save = await client.PutAsJsonAsync("/api/settings/connection", Edit(before.GetProperty("version").GetString()!));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode); Assert.DoesNotContain(Key, await save.Content.ReadAsStringAsync());
        var saved = await View(client); var id = saved.GetProperty("provider").GetProperty("credentialId").GetString()!;
        Assert.Single(vault.Entries); Assert.DoesNotContain(Key, store!.Setting("provider-credentials")!);
        foreach (var route in new[] { "/api/state", "/api/export", "/api/settings/connection", "/api/settings/diagnostics" })
            Assert.DoesNotContain(Key, await client.GetStringAsync(route));
        using var removal = await client.PostAsJsonAsync($"/api/settings/connection/credentials/{id}/remove", new { version = saved.GetProperty("version").GetString() });
        Assert.Equal(HttpStatusCode.OK, removal.StatusCode); Assert.Empty(vault.Entries);
        using var submit = await client.PostAsJsonAsync("/api/chat", new { mode = "chat", content = "A fictional admission check." });
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var run = await submit.Content.ReadFromJsonAsync<Run>(Wire.Json) ?? throw new InvalidOperationException();
        for (var i = 0; i < 100 && store.Get(run.Id)!.State is RunState.Queued or RunState.Running; i++) await Task.Delay(20);
        var failed = store.Get(run.Id)!; Assert.Equal(RunState.Failed, failed.State);
        Assert.Equal(0, failed.ModelCalls); Assert.Equal(0, failed.ChargedTokens); Assert.Equal(0, failed.ReservedTokens);
        Assert.DoesNotContain(Key, Wire.Pack(store.AllEvents()));
    }
    [Fact] public async Task StaleOwnerSaveHasNoCredentialSideEffect()
    {
        using var client = Client(); var before = await View(client); var version = before.GetProperty("version").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/settings/connection", Edit(version))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/settings/connection", Edit(version))).StatusCode);
        Assert.Single(vault.Entries);
    }
    [Fact] public async Task GoogleOAuthSetupIsLocalOwnerOnlyAndUnknownCallbacksCannotMutateConnections()
    {
        using var owner = Client();
        var before = await owner.GetFromJsonAsync<JsonElement>("/api/settings/mcp");
        var version = before.GetProperty("version").GetString()!;
        Assert.Equal("http://127.0.0.1:5179/api/settings/mcp/google/callback", before.GetProperty("google").GetProperty("redirectUri").GetString());
        Assert.Equal("Desktop app", before.GetProperty("google").GetProperty("clientType").GetString());
        var products = before.GetProperty("google").GetProperty("products");
        Assert.Equal(3, products.GetArrayLength());
        var gmailRead = products.EnumerateArray().Single(item => item.GetProperty("id").GetString() == "gmail-read");
        Assert.Contains("gmail.readonly", gmailRead.GetProperty("scopes").EnumerateArray().Select(item => item.GetString()).Single(scope => scope!.Contains("gmail.")));
        Assert.DoesNotContain(gmailRead.GetProperty("scopes").EnumerateArray(), item => item.GetString()!.Contains("gmail.send"));
        var gmailSend = products.EnumerateArray().Single(item => item.GetProperty("id").GetString() == "gmail-send");
        Assert.Contains(gmailSend.GetProperty("scopes").EnumerateArray(), item => item.GetString()!.Contains("gmail.send"));
        Assert.DoesNotContain(gmailSend.GetProperty("scopes").EnumerateArray(), item => item.GetString()!.Contains("gmail.readonly"));
        using var guest = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsJsonAsync("/api/settings/mcp/google/start",
            new GoogleMcpStart(version, "calendar", "fixture-client", "fixture-secret"))).StatusCode);
        using var remote = Client(); remote.DefaultRequestHeaders.Add("Fixture-Remote", "true");
        Assert.Equal(HttpStatusCode.Forbidden, (await remote.PostAsJsonAsync("/api/settings/mcp/google/start",
            new GoogleMcpStart(version, "calendar", "fixture-client", "fixture-secret"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/settings/mcp/google/start",
            new GoogleMcpStart(version, "drive", "fixture-client", "fixture-secret"))).StatusCode);
        using var callback = factory.CreateClient(new() { BaseAddress = new("http://127.0.0.1:5179"), HandleCookies = false });
        callback.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        Assert.Equal(HttpStatusCode.BadRequest, (await callback.GetAsync("/api/settings/mcp/google/callback?code=fictional&state=unknown")).StatusCode);
        Assert.Empty(vault.Entries); Assert.Null(store!.Setting("mcp-connectors"));
    }
    [Fact] public async Task ChatCanOpenSecureConnectionSetupWithoutProviderDispatch()
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync("/api/chat", new { mode = "chat", content = "Connect my Google Calendar" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = (await response.Content.ReadFromJsonAsync<Run>(Wire.Json))!;
        Assert.Equal(RunState.Succeeded, run.State); Assert.Equal("google", run.ConnectionSetup);
        Assert.Equal(0, run.ModelCalls); Assert.Equal(0, run.ToolCalls); Assert.Equal(2, store!.Chats().Count);
        Assert.Empty(vault.Entries);
    }
    [Fact] public async Task MissingGoogleCapabilityOffersConnectionAndFollowUpReadsTheHostState()
    {
        using var client = Client();
        using var missingResponse = await client.PostAsJsonAsync("/api/chat", new { mode = "chat", content = "Check my Gmail for me" });
        Assert.Equal(HttpStatusCode.OK, missingResponse.StatusCode);
        var missing = (await missingResponse.Content.ReadFromJsonAsync<Run>(Wire.Json))!;
        Assert.Equal("google", missing.ConnectionSetup); Assert.Equal("gmail-read", missing.ConnectionSetupProduct);
        Assert.Contains("don’t have read-only mail connected", missing.DraftText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, missing.ModelCalls); Assert.Equal(0, missing.ToolCalls);
        using var followUpResponse = await client.PostAsJsonAsync("/api/chat", new { mode = "chat", content = "Did that work?" });
        var followUp = (await followUpResponse.Content.ReadFromJsonAsync<Run>(Wire.Json))!;
        Assert.Equal("google", followUp.ConnectionSetup); Assert.Equal("gmail-read", followUp.ConnectionSetupProduct);
        Assert.Contains("don’t have read-only mail connected", followUp.DraftText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, followUp.ModelCalls); Assert.Empty(vault.Entries);
    }
    [Theory]
    [InlineData(null, true, false, 401)]
    [InlineData(false, true, false, 403)]
    [InlineData(true, false, false, 403)]
    [InlineData(true, true, true, 403)]
    public async Task GoogleAppImportAndRemovalRequireLocalOwnerAndCsrf(bool? owner, bool csrf, bool remote, int status)
    {
        using var client = Client(owner, csrf);
        if (remote) client.DefaultRequestHeaders.Add("Fixture-Remote", "true");
        Assert.Equal((HttpStatusCode)status, (await client.PutAsJsonAsync("/api/settings/mcp/google/client", new GoogleClientImport("unknown", GoogleClientSetupTests.Credentials))).StatusCode);
        Assert.Equal((HttpStatusCode)status, (await client.PostAsJsonAsync("/api/settings/mcp/google/client/remove", new McpConnectorChange("unknown"))).StatusCode);
        Assert.Empty(vault.Entries); Assert.Null(store!.Setting("mcp-connectors"));
    }

    [Fact]
    public async Task GoogleSetupImportNeverReturnsOrExportsTheCredentials()
    {
        using var client = Client();
        var before = await client.GetFromJsonAsync<JsonElement>("/api/settings/mcp");
        var saved = await client.PutAsJsonAsync("/api/settings/mcp/google/client", new GoogleClientImport(before.GetProperty("version").GetString()!, GoogleClientSetupTests.Credentials));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var body = await saved.Content.ReadAsStringAsync();
        var view = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.True(view.GetProperty("google").GetProperty("clientSetup").GetProperty("configured").GetBoolean());
        Assert.DoesNotContain(GoogleClientSetupTests.Secret, body + await client.GetStringAsync("/api/export") + await client.GetStringAsync("/api/state"));
        Assert.Single(vault.Entries);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/settings/mcp/google/client/remove", new McpConnectorChange(view.GetProperty("version").GetString()!))).StatusCode);
        Assert.Empty(vault.Entries);
    }

    [Fact]
    public async Task SoulSettingsRequireOwnerAndRejectStaleSaves()
    {
        using var guest = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/settings/soul")).StatusCode);
        using var paired = Client(false);
        Assert.Equal(HttpStatusCode.Forbidden, (await paired.GetAsync("/api/settings/soul")).StatusCode);

        using var owner = Client();
        var before = await owner.GetFromJsonAsync<JsonElement>("/api/settings/soul");
        var soul = before.GetProperty("soul");
        var version = soul.GetProperty("version").GetString()!;
        var content = soul.GetProperty("content").GetString()! + "\n\nA fictional API edit.";
        var saved = await owner.PutAsJsonAsync("/api/settings/soul", new SoulEditRequest(content, version));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(content, (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("soul").GetProperty("content").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync("/api/settings/soul", new SoulEditRequest("stale", version))).StatusCode);
        Assert.Equal(content, store!.Soul().Content);
        Assert.Contains(Store.SoulFileName, await owner.GetStringAsync("/api/export"));
    }

    [Fact]
    public async Task UserSettingsRequireOwnerAndRejectStaleSaves()
    {
        using var guest = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/settings/user")).StatusCode);
        using var paired = Client(false);
        Assert.Equal(HttpStatusCode.Forbidden, (await paired.GetAsync("/api/settings/user")).StatusCode);

        using var owner = Client();
        var before = await owner.GetFromJsonAsync<JsonElement>("/api/settings/user");
        var user = before.GetProperty("user");
        var version = user.GetProperty("version").GetString()!;
        const string content = "# User\n\n- Prefers concise fictional fixtures.";
        var saved = await owner.PutAsJsonAsync("/api/settings/user", new UserEditRequest(content, version));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(content, (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("user").GetProperty("content").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync("/api/settings/user", new UserEditRequest("stale", version))).StatusCode);
        Assert.Equal(content, store!.User().Content);
        Assert.Contains(Store.UserFileName, await owner.GetStringAsync("/api/export"));
    }

    [Fact]
    public async Task IdentitySettingsRequireOwnerAndRejectStaleSaves()
    {
        using var guest = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/settings/identity")).StatusCode);
        using var paired = Client(false);
        Assert.Equal(HttpStatusCode.Forbidden, (await paired.GetAsync("/api/settings/identity")).StatusCode);

        using var owner = Client();
        var before = await owner.GetFromJsonAsync<JsonElement>("/api/settings/identity");
        var identity = before.GetProperty("identity");
        var version = identity.GetProperty("version").GetString()!;
        const string content = "# Identity\n\n**Role:** A fictional API test butler.";
        var saved = await owner.PutAsJsonAsync("/api/settings/identity", new IdentityEditRequest(content, version));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(content, (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("identity").GetProperty("content").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync("/api/settings/identity", new IdentityEditRequest("stale", version))).StatusCode);
        Assert.Equal(content, store!.Identity().Content);
        Assert.Contains(Store.IdentityFileName, await owner.GetStringAsync("/api/export"));
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        await factory.DisposeAsync(); store?.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
