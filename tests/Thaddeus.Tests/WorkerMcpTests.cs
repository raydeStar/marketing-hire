using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Thaddeus.Infrastructure;
using Thaddeus.Core;
using System.Net.Http.Json;
using System.Text.Json;

namespace Thaddeus.Tests;

public sealed class WorkerMcpTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-mcp-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> factory;
    private readonly InferenceFixture inference = new();
    private sealed class InferenceFixture : IInferenceTransport
    {
        public int Calls;
        public Task<InferenceReply> Send(ProviderSnapshot provider, JsonElement request, CancellationToken cancellation)
        {
            Calls++;
            return Task.FromResult(new InferenceReply(JsonSerializer.SerializeToElement(new
            {
                id="fixture",model=provider.Model,created=1,
                choices=new[]{new{index=0,message=new{role="assistant",content="HTTP contract fixture"},finish_reason="stop"}},
                usage=new{prompt_tokens=10,completion_tokens=5}
            }),10,5));
        }
    }
    private sealed class LoopbackTransport : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, dispatch) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return dispatch(context); });
            next(app);
        };
    }
    public WorkerMcpTests()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.ConfigureServices(services => { services.AddSingleton<IStartupFilter, LoopbackTransport>(); services.AddSingleton<IInferenceTransport>(inference); });
        });
    }
    private HttpClient Http() => factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false, AllowAutoRedirect = false });
    private async Task<HttpClient> OwnerHttp()
    {
        var http = Http(); http.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        using var login = await http.PostAsJsonAsync("/api/auth/login", new { key = File.ReadAllText(Path.Combine(root, "host-key.txt")).Trim() });
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        http.DefaultRequestHeaders.Add("X-CSRF", session.GetProperty("csrf").GetString());
        http.DefaultRequestHeaders.Add("Cookie", login.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        return http;
    }
    [Fact] public async Task BrowserCannotEnableResearchBySupplyingBackendOrAdmissionFlags()
    {
        using var http = await OwnerHttp();
        var store = factory.Services.GetRequiredService<Store>();
        using var state = await http.GetAsync("/api/state");
        Assert.False((await state.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("research").GetProperty("enabled").GetBoolean());
        using var response = await http.PostAsJsonAsync("/api/chat", new { content = "Research", mode = "research", readScope = Array.Empty<string>(), backend = "qemu-whpx", enabled = true, developmentOnly = true });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(store.List()); Assert.Empty(store.Chats()); Assert.Equal(0, inference.Calls);
    }
    [Theory] [InlineData("answer")] [InlineData("approve")]
    public async Task ManagedRoutesCannotBypassWorkerStopOrArtifactReadback(string action)
    {
        using var http = await OwnerHttp(); var store = factory.Services.GetRequiredService<Store>();
        var run = CapabilityTests.CreateWorkerRun(store);
        run.Research = new("awaiting-approval", "Controlled route fixture"); store.Save(run, "test.managed", new { });
        var runtime = factory.Services.GetRequiredService<Runtime>();
        if (action == "answer")
        {
            await runtime.Call(run.Id, new("q1", "thaddeus_ask_user", JsonSerializer.SerializeToElement(new { question = "Audience?", choices = Array.Empty<string>() })), default);
            using var response = await http.PostAsJsonAsync("/api/runs/" + run.Id + "/answer", new { questionId = "q1", answer = "Beginners" });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Null(store.Get(run.Id)!.Question!.Answer);
        }
        else
        {
            await runtime.Call(run.Id, new("a1", "thaddeus_propose_import", JsonSerializer.SerializeToElement(new { path = "plans/research.md", artifact = "research.md", content = "# Proposal" })), default);
            var approval = store.Get(run.Id)!.Approval!;
            using var response = await http.PostAsJsonAsync("/api/runs/" + run.Id + "/approve", new { approvalId = approval.Id, digest = approval.Digest, allow = true });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Equal("absent", store.Version("plans/research.md"));
        }
        Assert.Equal(0, inference.Calls);
    }
    [Fact] public async Task ComposerBudgetIsFrozenAndZeroCallAllowancePreventsDispatch()
    {
        using var http = await OwnerHttp();
        using var response = await http.PostAsJsonAsync("/api/chat", new { content = "Do not spend a model call", budget = new Budget(ModelCalls: 0, ToolCalls: 0, MaxTotalTokens: 2000) });
        response.EnsureSuccessStatusCode(); var accepted = await response.Content.ReadFromJsonAsync<Run>(Wire.Json);
        var store = factory.Services.GetRequiredService<Store>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (store.Get(accepted!.Id)!.State is RunState.Queued or RunState.Running) await Task.Delay(10, deadline.Token);
        var saved = store.Get(accepted!.Id)!;
        Assert.Equal(2000, saved.Goal.Limits.MaxTotalTokens); Assert.Equal(0, saved.ModelCalls); Assert.Equal(0, saved.ChargedTokens);
        Assert.NotEqual(RunState.Succeeded, saved.State);
    }
    [Fact] public async Task SdkClientNegotiatesCallsScopedToolAndReplaysDurableOperation()
    {
        using var http = Http();
        var store = factory.Services.GetRequiredService<Store>();
        store.Write("notes/source.md","# Source\nA verified transport fixture.","absent");
        var run = CapabilityTests.CreateWorkerRun(store);
        http.DefaultRequestHeaders.Authorization = new("Bearer",factory.Services.GetRequiredService<WorkerAuthorization>().Issue(run.Id,TimeSpan.FromMinutes(5)));
        await using var transport = new HttpClientTransport(new() { Endpoint = new(http.BaseAddress!,"/worker/"+run.Id+"/mcp"), EnableStandaloneGetStream = false }, http);
        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync();
        Assert.Equal(3,tools.Count); Assert.Contains(tools,t=>t.Name=="thaddeus_read_note");
        var args = new Dictionary<string,object?> { ["path"]="notes/source.md",["operationId"]="read-same-operation" };
        var first=await client.CallToolAsync("thaddeus_read_note",args);
        var second=await client.CallToolAsync("thaddeus_read_note",args);
        Assert.False(first.IsError);Assert.Equal(((TextContentBlock)first.Content[0]).Text,((TextContentBlock)second.Content[0]).Text);
        Assert.Equal(1,store.Get(run.Id)!.ToolCalls);
        args["path"]="notes/secret.md";args["operationId"]="unauthorized-read";
        Assert.True((await client.CallToolAsync("thaddeus_read_note",args)).IsError);
    }
    [Theory][InlineData("origin")][InlineData("cookie")][InlineData("wrong-run")][InlineData("missing-token")][InlineData("query")]
    public async Task BrowserAuthorityAndCrossRunTokensCannotEnterWorkerTransport(string mode)
    {
        using var http=Http();var store=factory.Services.GetRequiredService<Store>();var run=CapabilityTests.CreateWorkerRun(store);
        var token=factory.Services.GetRequiredService<WorkerAuthorization>().Issue(run.Id,TimeSpan.FromMinutes(5));
        var id=mode=="wrong-run"?CapabilityTests.CreateWorkerRun(store).Id:run.Id;
        using var request=new HttpRequestMessage(HttpMethod.Post,"/worker/"+id+"/mcp"+(mode=="query"?"?token=forbidden":""));
        request.Content=new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}",System.Text.Encoding.UTF8,"application/json");
        if(mode!="missing-token")request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        if(mode=="origin")request.Headers.Add("Origin","http://localhost:5179");
        if(mode=="cookie")request.Headers.Add("Cookie","thaddeus-session=fixture");
        using var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.Empty(store.Get(run.Id)!.Capabilities);
    }
    [Fact] public async Task ModelHttpPathRequiresWorkerGrantAndSettlesUsageBeforeSse()
    {
        using var http=Http();var store=factory.Services.GetRequiredService<Store>();var run=CapabilityTests.CreateWorkerRun(store);
        run.Goal=run.Goal with {Provider=new("compatible","gpt-5.6-luna","high","http://127.0.0.1:5181/v1")};store.Save(run,"test.model-profile",new{});
        var path="/worker/"+run.Id+"/v1/chat/completions";
        var body=new{model="gpt-5.6-luna",messages=new[]{new{role="user",content="A fixture"}},stream=true};
        using(var denied=await http.PostAsJsonAsync(path,body))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        Assert.Equal(0,inference.Calls);
        http.DefaultRequestHeaders.Authorization=new("Bearer",factory.Services.GetRequiredService<WorkerAuthorization>().Issue(run.Id,TimeSpan.FromMinutes(5)));
        using(var response=await http.PostAsJsonAsync(path,body))
        {
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("text/event-stream",response.Content.Headers.ContentType!.MediaType);
            Assert.EndsWith("data: [DONE]\n\n",await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(15,store.Get(run.Id)!.ChargedTokens);Assert.Equal(1,inference.Calls);
        run=store.Get(run.Id)!;run.State=RunState.AwaitingInput;store.Save(run,"test.waiting",new{});
        using(var paused=await http.PostAsJsonAsync(path,body))Assert.Equal(HttpStatusCode.Conflict,paused.StatusCode);
        Assert.Equal(1,inference.Calls);
    }
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        var ownedStore = factory.Services.GetRequiredService<Store>();
        await factory.DisposeAsync();
        // The test host can retain its service provider until its deferred entry point unwinds.
        // All requests have finished; release our fixture's exclusive file lease before cleanup.
        ownedStore.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if(Directory.Exists(root)) Directory.Delete(root,true);
    }
}
