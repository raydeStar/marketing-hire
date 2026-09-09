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
    private sealed class Handler(string payload):HttpMessageHandler
    {
        public string Body="",Authorization="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c){Body=await r.Content!.ReadAsStringAsync(c);Authorization=r.Headers.Authorization?.ToString()??"";return new(HttpStatusCode.OK){Content=new StringContent(payload)};}
    }
}
