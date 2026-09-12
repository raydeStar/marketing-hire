using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class ModelBrokerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-inference-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    private readonly Runtime runtime;
    private readonly ModelAccessGate access = new();
    public ModelBrokerTests() { store = new(root); runtime = new(store, _ => throw new Exception("No legacy inference"), new PlanValidator(), new EvidencePolicy()); }
    private Run Run(Budget? budget = null, ProviderSnapshot? provider = null)
    {
        var run = CapabilityTests.CreateWorkerRun(store);
        run.Goal = run.Goal with { Provider = provider ?? new("compatible", "gpt-5.6-luna", "high", "http://127.0.0.1:5181/v1"), Limits = budget ?? new() };
        store.Save(run,"test.provider-frozen",new{}); return run;
    }
    private static JsonElement Request(string? extra = null) => JsonDocument.Parse("{\"model\":\"gpt-5.6-luna\",\"messages\":[{\"role\":\"user\",\"content\":\"A protocol fixture\"}],\"stream\":true" + extra + "}").RootElement.Clone();
    private Task<InferenceReply> Infer(Run run, Transport transport, JsonElement? request = null) => runtime.Infer(run.Id,request ?? Request(),transport,access,default);
    private sealed class Transport(Func<JsonElement,Task<InferenceReply>> send) : IInferenceTransport
    {
        public int Calls;
        public Task<InferenceReply> Send(ProviderSnapshot provider,JsonElement body,CancellationToken cancellation) { Calls++; return send(body); }
    }
    private static InferenceReply Reply(int? input = 10, int? output = 5) => new(JsonSerializer.SerializeToElement(new
    {
        id="test-completion", model="gpt-5.6-luna", created=1, choices=new[] { new { index=0, message=new { role="assistant",content=(string?)null,
            tool_calls=new[]{new{id="call-1",type="function",function=new{name="read",arguments="{}"}}}}, finish_reason="tool_calls" } },
        usage=new {prompt_tokens=input,completion_tokens=output}
    }), input, output);
    [Fact] public async Task FrozenModelAndOutputCeilingAreEnforcedBeforeDispatch()
    {
        var run=Run(); var transport=new Transport(body =>
        {
            var saved=store.Get(run.Id)!;
            Assert.Equal(1,saved.ModelCalls); Assert.Equal(run.Goal.Limits.MaxTotalTokens,saved.ReservedTokens);
            Assert.False(body.GetProperty("stream").GetBoolean());Assert.False(body.GetProperty("store").GetBoolean());
            Assert.Equal(run.Goal.Limits.MaxOutputTokens,body.GetProperty("max_completion_tokens").GetInt32());
            return Task.FromResult(Reply());
        });
        await Infer(run,transport,Request(",\"max_tokens\":999999"));
        Assert.Equal(15,store.Get(run.Id)!.ChargedTokens); Assert.Equal("completed",store.Get(run.Id)!.ModelDispatches.Single().Status);
        var wrong=JsonNode.Parse(Request().GetRawText())!;wrong["model"]="different-model";
        await Assert.ThrowsAsync<ArgumentException>(()=>Infer(run,transport,JsonSerializer.SerializeToElement(wrong)));
        Assert.Equal(1,transport.Calls);
    }
    [Theory][InlineData("\"endpoint\":\"http://evil.invalid\"")][InlineData("\"n\":2")][InlineData("\"reasoning_effort\":\"low\"")]
    public async Task WorkerCannotBroadenProviderOrSampleCount(string extra)
    {
        var run=Run();var transport=new Transport(_=>Task.FromResult(Reply()));
        await Assert.ThrowsAsync<ArgumentException>(()=>Infer(run,transport,Request(","+extra)));
        Assert.Equal(0,transport.Calls);Assert.Empty(store.Get(run.Id)!.ModelDispatches);
    }
    [Fact] public async Task MissingUsageConsumesReservationAndPreventsAnotherDispatch()
    {
        var run=Run(new(MaxTotalTokens:100));var transport=new Transport(_=>Task.FromResult(Reply(null,null)));
        await Infer(run,transport);
        Assert.Null(store.Get(run.Id)!.InputTokens);Assert.Equal(100,store.Get(run.Id)!.ChargedTokens);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Infer(run,transport));Assert.Equal(1,transport.Calls);
    }
    [Fact] public async Task DisconnectRemainsUnknownAndCannotBeRetriedForFree()
    {
        var run=Run(new(MaxTotalTokens:100));var transport=new Transport(_=>throw new IOException("A secret-bearing upstream error must not escape"));
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>Infer(run,transport));
        Assert.DoesNotContain("secret-bearing",error.Message);Assert.Equal(100,store.Get(run.Id)!.ChargedTokens);
        Assert.Equal(RunState.NeedsAttention,store.Get(run.Id)!.State);Assert.Equal("outcome-unknown",store.Get(run.Id)!.ModelDispatches.Single().Status);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Infer(run,transport));Assert.Equal(1,transport.Calls);
    }
    [Fact] public async Task ReportedOverrunRetainsActualUsageAndDoesNotReleaseResponse()
    {
        var run=Run(new(MaxTotalTokens:10));var transport=new Transport(_=>Task.FromResult(Reply()));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Infer(run,transport));
        Assert.Equal(15,store.Get(run.Id)!.ChargedTokens);Assert.Equal(RunState.NeedsAttention,store.Get(run.Id)!.State);
    }
    [Fact] public async Task StrictAdmissionAndAwaitingInputDispatchNothing()
    {
        var run=Run(new(RequireCertifiedTokenBound:true));var transport=new Transport(_=>Task.FromResult(Reply()));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Infer(run,transport));
        run=store.Get(run.Id)!;run.State=RunState.AwaitingInput;store.Save(run,"test.pause",new{});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Infer(run,transport));Assert.Equal(0,transport.Calls);
    }
    [Fact] public void GlimmerRequiresARealResourceLease()
    {
        Assert.Throws<InvalidOperationException>(()=>access.Check(new("compatible","glimmer-task-dev","high","http://127.0.0.1:1235/v1")));
    }
    [Fact] public async Task AccumulatedActiveTimeStopsModelAndCapabilityDispatchAfterContinuation()
    {
        var run = Run(new(Seconds: 10));
        run.ExecutionActiveSeconds = 9; run.ExecutionDeadlineStart = DateTimeOffset.UtcNow.AddSeconds(-2);
        store.Save(run, "fixture.resumed-time", new { });
        var transport = new Transport(_ => Task.FromResult(Reply()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Infer(run, transport));
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Call(run.Id, new("expired-read", "thaddeus_read_note",
            JsonSerializer.SerializeToElement(new { path = "notes/source.md" })), default));
        Assert.Equal(0, transport.Calls); Assert.Empty(store.Get(run.Id)!.Capabilities);
    }
    [Fact] public void BufferedSsePreservesToolIdentityFinishAndUsage()
    {
        var frames=WorkerModels.Frames(Reply().Body).Select(s=>JsonDocument.Parse(s).RootElement.Clone()).ToArray();
        Assert.Equal("call-1",frames[0].GetProperty("choices")[0].GetProperty("delta").GetProperty("tool_calls")[0].GetProperty("id").GetString());
        Assert.Equal(0,frames[0].GetProperty("choices")[0].GetProperty("delta").GetProperty("tool_calls")[0].GetProperty("index").GetInt32());
        Assert.Equal("tool_calls",frames[1].GetProperty("choices")[0].GetProperty("finish_reason").GetString());
        Assert.Equal(10,frames[2].GetProperty("usage").GetProperty("prompt_tokens").GetInt32());
    }
    private sealed class HttpFixture(Func<HttpRequestMessage,Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>handle(request);
    }
    [Fact] public async Task HttpTransportUsesFrozenDestinationAndHostCredential()
    {
        using var client=new HttpClient(new HttpFixture(async request=>
        {
            Assert.Equal("https://provider.invalid/v1/chat/completions",request.RequestUri!.ToString());
            Assert.Equal("host-only-fixture",request.Headers.Authorization!.Parameter);
            Assert.DoesNotContain("host-only-fixture",await request.Content!.ReadAsStringAsync());
            return new(HttpStatusCode.OK){Content=new StringContent(Reply().Body.GetRawText(),System.Text.Encoding.UTF8,"application/json")};
        }));
        var response=await new CompatibleInference(client,"host-only-fixture").Send(new("compatible","gpt-5.6-luna","high","https://provider.invalid/v1"),Request(),default);
        Assert.Equal(10,response.InputTokens);
    }
    public void Dispose() { store.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true); }
}
