using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class OpenClawBackendTests
{
    private const string Id = "0123456789abcdef0123456789abcdef";
    private static ExecutionIdentity Identity(string? runId = null) => new("openclaw", "thaddeus-"+Id, "agent:thaddeus:"+Id, OpenClawBackend.PinnedVersion, runId);
    private sealed class TransportFixture(string output) : ISandboxBackend
    {
        public string? Input; public int Calls;
        public Task<SandboxCommandResult> Execute(string id,IReadOnlyList<string> command,string? input,CancellationToken cancellation)
        { Calls++; Input=input; Assert.Equal("python3",command[0]); return Task.FromResult(new SandboxCommandResult(0,output,"")); }
        public Task<SandboxInspection> Inspect(CancellationToken cancellation)=>throw new NotSupportedException();
        public Task Create(SandboxSpec spec,CancellationToken cancellation)=>throw new NotSupportedException();
        public Task Stop(string id,CancellationToken cancellation)=>throw new NotSupportedException();
        public Task Remove(string id,CancellationToken cancellation)=>throw new NotSupportedException();
        public Task PutText(string id,string path,string content,CancellationToken cancellation)=>throw new NotSupportedException();
        public Task<SandboxText> GetText(string id,string path,CancellationToken cancellation)=>throw new NotSupportedException();
    }
    [Fact] public async Task StartPinsTheModelAndSessionAndNeverTreatsWorkerOkAsVerifiedCompletion()
    {
        var transport=new TransportFixture("{\"runId\":\"runtime-1\",\"status\":\"ok\"}");
        var result=await new OpenClawBackend(transport).Start(new(Id,Identity(),"A literal $(objective)",new("compatible","glimmer-task-dev","high"),new()),default);
        Assert.Equal("worker-reported",result.Authority);
        var request=JsonDocument.Parse(transport.Input!).RootElement;
        Assert.Equal("agent",request.GetProperty("method").GetString());
        var parameters=request.GetProperty("parameters");
        Assert.Equal("glimmer-task-dev",parameters.GetProperty("model").GetString());
        Assert.Equal(Id,parameters.GetProperty("idempotencyKey").GetString());
        Assert.False(parameters.GetProperty("deliver").GetBoolean());
        Assert.Equal(1,transport.Calls);
    }
    [Theory][InlineData("{}")] [InlineData("not json")] [InlineData("{\"runId\":\"other-run\",\"status\":\"ok\"}")]
    public async Task MissingOrWrongCorrelationNeverCreatesSuccessOrAutomaticRetry(string response)
    {
        var transport=new TransportFixture(response);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new OpenClawBackend(transport).Inspect(Identity("expected-run"),default));
        Assert.Equal(1,transport.Calls);
    }
    [Fact] public async Task UnknownAdmissionCannotResumeThroughInspection()
    {
        var transport=new TransportFixture("{}");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new OpenClawBackend(transport).Inspect(Identity(),default));
        Assert.Equal(0,transport.Calls);
    }
}
