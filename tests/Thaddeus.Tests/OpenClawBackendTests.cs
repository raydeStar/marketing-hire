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
    [Fact] public async Task StartUsesBoundSessionAndConfiguredRouteWithoutPrivilegedModelOverrides()
    {
        var transport=new TransportFixture("{\"runId\":\"runtime-1\",\"status\":\"ok\"}");
        var result=await new OpenClawBackend(transport).Start(new(Id,Identity(),"A literal $(objective)",new("compatible","glimmer-task-dev","high"),new()),default);
        Assert.Equal("worker-reported",result.Authority);
        var request=JsonDocument.Parse(transport.Input!).RootElement;
        Assert.Equal("agent",request.GetProperty("method").GetString());
        var parameters=request.GetProperty("parameters");
        Assert.False(parameters.TryGetProperty("model", out _));
        Assert.False(parameters.TryGetProperty("provider", out _));
        Assert.Equal("agent:thaddeus:" + Id, parameters.GetProperty("sessionKey").GetString());
        Assert.Equal(Id,parameters.GetProperty("idempotencyKey").GetString());
        Assert.False(parameters.GetProperty("deliver").GetBoolean());
        Assert.Equal(1,transport.Calls);
    }
    [Fact] public async Task SteeringUsesPublicUserInputWithoutRequestingSystemProvenanceAuthority()
    {
        var transport = new TransportFixture("{\"runId\":\"runtime-1\",\"status\":\"steered\"}");
        var result = await new OpenClawBackend(transport).Steer(Identity("runtime-1"), "Keep the current task and include the new guidance.", "guidance-1", default);
        using var request = JsonDocument.Parse(transport.Input!);
        var parameters = request.RootElement.GetProperty("parameters");
        Assert.Equal("chat.send", request.RootElement.GetProperty("method").GetString());
        Assert.Equal("agent:thaddeus:" + Id, parameters.GetProperty("sessionKey").GetString());
        Assert.Equal("steer", parameters.GetProperty("queueMode").GetString());
        Assert.Equal("guidance-1", parameters.GetProperty("idempotencyKey").GetString());
        Assert.False(parameters.GetProperty("deliver").GetBoolean());
        Assert.False(parameters.TryGetProperty("suppressCommandInterpretation", out _));
        Assert.False(parameters.TryGetProperty("systemInputProvenance", out _));
        Assert.False(parameters.TryGetProperty("systemProvenanceReceipt", out _));
        Assert.Equal("runtime-1", result.RuntimeRunId);
        Assert.Equal("worker-reported", result.Authority);
        Assert.Equal(1, transport.Calls);
    }
    [Theory][InlineData("{}")] [InlineData("not json")]
    public async Task UnconfirmedSteeringIsNotRetriedOrReportedAsSuccess(string response)
    {
        var transport = new TransportFixture(response);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new OpenClawBackend(transport).Steer(Identity("runtime-1"), "Additional guidance.", "guidance-1", default));
        Assert.Equal(1, transport.Calls);
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
    [Theory] [InlineData(null)] [InlineData("previous-turn")]
    public async Task CancellationStopsOnlyTheBoundTaskSessionIncludingQueuedContinuations(string? runId)
    {
        var transport = new TransportFixture("{\"ok\":true,\"status\":\"aborted\",\"thaddeusFilesystemCheckpoint\":\"syncfs\"}");
        await new OpenClawBackend(transport).Cancel(Identity(runId), default);
        using var request = JsonDocument.Parse(transport.Input!);
        Assert.Equal("sessions.abort", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("parameters");
        Assert.Equal("agent:thaddeus:" + Id, parameters.GetProperty("key").GetString());
        Assert.True(parameters.GetProperty("clearQueued").GetBoolean());
        Assert.False(parameters.TryGetProperty("runId", out _));
        Assert.Contains("class GatewayControl", request.RootElement.GetProperty("controller").GetString());
        Assert.Equal(1, transport.Calls);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task StopRequiresAStorageCheckpointAndNeverRetriesAnUncertainReply(bool checkpoint)
    {
        var response = checkpoint ? "{\"ok\":true,\"status\":\"aborted\",\"thaddeusFilesystemCheckpoint\":\"syncfs\"}" : "{\"ok\":true,\"status\":\"aborted\"}";
        var transport = new TransportFixture(response);
        var backend = new OpenClawBackend(transport);
        if (checkpoint) Assert.Equal("worker-reported", (await backend.Cancel(Identity("run-1"), default)).Authority);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => backend.Cancel(Identity("run-1"), default));
        Assert.Equal(1, transport.Calls);
    }
}
