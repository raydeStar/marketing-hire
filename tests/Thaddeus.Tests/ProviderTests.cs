using System.Net;
using Thaddeus.Core;
using Thaddeus.Infrastructure;
namespace Thaddeus.Tests;
public sealed class ProviderTests
{
    private static Observation Observe()=>new(new("Plan",["notes/a.md"],"plans/",[],new(),new("compatible","test","high","http://localhost:1234/v1")),[],null,1);
    [Fact]public async Task StreamingFragments_AssembleTypedActionAndActualUsage()
    {
        var handler=new Handler("data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"name\":\"knowledge_write\",\"arguments\":\"{\\\"path\\\":\\\"plans/a.md\\\",\"}}]}}]}\n\ndata: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"content\\\":\\\"hello\\\"}\"}}]}}]}\n\ndata: {\"choices\":[],\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":9}}\n\ndata: [DONE]\n");
        var o=Observe();var provider=new CompatibleProvider(o.Goal.Provider,"server-only-key",new HttpClient(handler));var result=await provider.Respond(o,_=>Task.CompletedTask,default);
        Assert.Equal("plans/a.md",result.Action!.Path);Assert.Equal("hello",result.Action.Content);Assert.Equal(11,result.InputTokens);Assert.Equal(9,result.OutputTokens);
        Assert.Contains("\"stream\":true",handler.Body);Assert.Contains("\"max_completion_tokens\":4096",handler.Body);Assert.Equal("Bearer server-only-key",handler.Authorization);
    }
    [Theory][InlineData("{\"path\":\"plans/a.md\",\"content\":42}")][InlineData("{\"path\":\"plans/a.md\",\"content\":\"ok\",\"shell\":\"bad\"}")]
    public async Task MalformedArguments_AreNotExecutable(string args)
    {
        var line="data: "+Wire.Pack(new{choices=new[]{new{delta=new{tool_calls=new[]{new{index=0,function=new{name="knowledge_write",arguments=args}}}}}}})+"\n\ndata: [DONE]\n";
        var o=Observe();var p=new CompatibleProvider(o.Goal.Provider,null,new HttpClient(new Handler(line)));
        await Assert.ThrowsAsync<ArgumentException>(()=>p.Respond(o,_=>Task.CompletedTask,default));
    }
    [Theory][InlineData("http://remote.example/v1")][InlineData("https://key:secret@example.com/v1")][InlineData("https://example.com/v1?key=secret")]
    public void UnsafeEndpoints_Reject(string endpoint)=>Assert.Throws<ArgumentException>(()=>CompatibleProvider.Endpoint(new("compatible","test","high",endpoint)));

    [Fact] public async Task Conversation_StreamsTextWithFrozenHistoryAndNoTools()
    {
        var handler = new Handler("data: {\"choices\":[{\"delta\":{\"content\":\"Hello \"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"Juniper\"}}]}\n\ndata: [DONE]\n");
        var o = Observe(); o = o with { Goal = o.Goal with { Kind = "conversation", ReadScope = [] }, History = [new("earlier", "user", "Juniper is my raven", DateTimeOffset.UtcNow)] };
        var chunks = new List<string>();
        var reply = await new CompatibleProvider(o.Goal.Provider, null, new HttpClient(handler)).Respond(o, d => { chunks.Add(d); return Task.CompletedTask; }, default);
        Assert.Equal(["Hello ", "Juniper"], chunks); Assert.Equal("Hello Juniper", reply.Text); Assert.Null(reply.Action);
        Assert.Contains("Juniper is my raven", handler.Body); Assert.DoesNotContain("tool_choice", handler.Body);
    }
    [Fact] public async Task Conversation_AdvertisesOnlyFrozenConnectedToolsWithoutCredentials()
    {
        const string modelName = "mcp_12345678_events_list";
        var delta = new { tool_calls = new[] { new { index = 0, function = new { name = modelName, arguments = "{\"day\":\"tomorrow\"}" } } } };
        var payload = "data: " + Wire.Pack(new { choices = new[] { new { delta } } }) + "\n\ndata: [DONE]\n";
        var handler = new Handler(payload);
        var tool = new ConnectedToolDefinition("connector", "Calendar", "events_list", modelName, "List events.",
            System.Text.Json.JsonSerializer.SerializeToElement(new { type = "object", properties = new { day = new { type = "string" } }, required = new[] { "day" } }), "read external data", "v1");
        var o = Observe() with { Goal = Observe().Goal with { Kind = "conversation", ReadScope = [] }, ConnectedTools = new([tool], [], true) };
        var reply = await new CompatibleProvider(o.Goal.Provider, "model-key", new HttpClient(handler)).Respond(o, _ => Task.CompletedTask, default);
        Assert.Equal(modelName, reply.Action!.Name); Assert.Contains("\"day\":\"tomorrow\"", reply.Action.Content);
        Assert.Contains(modelName, handler.Body); Assert.Contains("Every call is a proposal", handler.Body);
        Assert.DoesNotContain("model-key", handler.Body); Assert.DoesNotContain("bearer", handler.Body, StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public async Task Conversation_AdvertisesOnlyTheFrozenTodoBatchSource()
    {
        var source = new TodoBatchSource("upload", "upload:1234567890abcdef1234567890abcdef", new string('a', 64), "tasks.txt");
        var arguments = Wire.Pack(new TodoBatchProposal(source.Reference, source.Version,
            [new("Call the dentist", "Use the number in the supplied note.", null, "The note gives no date.")]));
        var delta = new { tool_calls = new[] { new { index = 0, function = new { name = TodoBatchConversation.ToolName, arguments } } } };
        var handler = new Handler("data: " + Wire.Pack(new { choices = new[] { new { delta } } }) + "\n\ndata: [DONE]\n");
        var o = Observe() with { Goal = Observe().Goal with { Kind = "conversation", ReadScope = [] }, Todos = new([source], [], true) };

        var reply = await new CompatibleProvider(o.Goal.Provider, null, new HttpClient(handler)).Respond(o, _ => Task.CompletedTask, default);

        Assert.Equal(TodoBatchConversation.ToolName, reply.Action!.Name);
        using var body = System.Text.Json.JsonDocument.Parse(handler.Body);
        var tool = Assert.Single(body.RootElement.GetProperty("tools").EnumerateArray(), item =>
            item.GetProperty("function").GetProperty("name").GetString() == TodoBatchConversation.ToolName);
        var properties = tool.GetProperty("function").GetProperty("parameters").GetProperty("properties");
        Assert.Equal(source.Reference, Assert.Single(properties.GetProperty("sourceReference").GetProperty("enum").EnumerateArray()).GetString());
        Assert.Equal(source.Version, Assert.Single(properties.GetProperty("sourceVersion").GetProperty("enum").EnumerateArray()).GetString());
        Assert.Contains("writes deterministic editable To-dos", handler.Body);
    }
    [Fact] public async Task Conversation_AdvertisesVersionBoundDelegationManagementWithoutReminderPayload()
    {
        var job = new DelegationJobSummary("1234567890abcdef1234567890abcdef", 7, "reminder", "Call dentist", "scheduled",
            "once", new DateTimeOffset(2026, 9, 17, 18, 0, 0, TimeSpan.Zero), "America/Denver", null, false);
        var arguments = Wire.Pack(new DelegationCancelProposal(job.Id, job.Version));
        var delta = new { tool_calls = new[] { new { index = 0, function = new { name = DelegationManagementConversation.CancelTool, arguments } } } };
        var handler = new Handler("data: " + Wire.Pack(new { choices = new[] { new { delta } } }) + "\n\ndata: [DONE]\n");
        var o = Observe() with
        {
            Goal = Observe().Goal with { Kind = "conversation", ReadScope = [] },
            Delegation = new([], false, true, new DateTimeOffset(2026, 9, 16, 16, 0, 0, TimeSpan.Zero), "America/Denver", [job])
        };

        var reply = await new CompatibleProvider(o.Goal.Provider, null, new HttpClient(handler)).Respond(o, _ => Task.CompletedTask, default);

        Assert.Equal(DelegationManagementConversation.CancelTool, reply.Action!.Name);
        using var body = System.Text.Json.JsonDocument.Parse(handler.Body);
        var names = body.RootElement.GetProperty("tools").EnumerateArray().Select(item => item.GetProperty("function").GetProperty("name").GetString()).ToArray();
        Assert.Contains(DelegationManagementConversation.CancelTool, names);
        Assert.Contains(DelegationManagementConversation.RescheduleTool, names);
        Assert.DoesNotContain(DelegationConversation.ToolName, names);
        Assert.Contains(job.Id, handler.Body);
        Assert.DoesNotContain("Call the dentist.", handler.Body);
    }
    [Fact] public async Task TruncatedStream_DoesNotClaimCompletion()
    {
        var o = Observe() with { Goal = Observe().Goal with { Kind = "conversation", ReadScope = [] } };
        var p = new CompatibleProvider(o.Goal.Provider, null, new HttpClient(new Handler("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n")));
        await Assert.ThrowsAsync<IOException>(() => p.Respond(o, _ => Task.CompletedTask, default));
    }
    [Fact] public async Task OversizedLine_IsRejectedBeforeJsonParsing()
    {
        var o = Observe(); var p = new CompatibleProvider(o.Goal.Provider, null, new HttpClient(new Handler("data: " + new string('x', 150001))));
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => p.Respond(o, _ => Task.CompletedTask, default));
        Assert.Contains("line exceeds", ex.Message);
    }
    [Fact] public async Task NegativeUsage_IsRejected()
    {
        var o = Observe(); var p = new CompatibleProvider(o.Goal.Provider, null, new HttpClient(new Handler("data: {\"usage\":{\"prompt_tokens\":-1,\"completion_tokens\":2}}\n\ndata: [DONE]\n")));
        await Assert.ThrowsAsync<ArgumentException>(() => p.Respond(o, _ => Task.CompletedTask, default));
    }
    private sealed class Handler(string payload):HttpMessageHandler
    {
        public string Body="",Authorization="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c){Body=await r.Content!.ReadAsStringAsync(c);Authorization=r.Headers.Authorization?.ToString()??"";return new(HttpStatusCode.OK){Content=new StringContent(payload)};}
    }
}
