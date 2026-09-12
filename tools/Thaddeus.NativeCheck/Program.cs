using System.Net;
using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

// Explicit developer integration fixture. This executable is not shipped or reachable through the product host.
if (args.Length != 3 || args[1] is not ("scripted" or "luna") ||
    !System.Text.RegularExpressions.Regex.IsMatch(args[2], @"\Athaddeus-[a-f0-9]{32}\z"))
    throw new ArgumentException("Usage: NativeCheck ARTIFACT_DIRECTORY scripted|luna OWNED_CONTAINER_NAME");
var artifacts = Path.GetFullPath("artifacts") + Path.DirectorySeparatorChar;
var root = Path.GetFullPath(args[0]);
if (!root.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
    throw new ArgumentException("Use a fresh private directory under this checkout's artifacts.");
const int port = 5182;
var mode = args[1]; var container = args[2];
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:" + port);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 150000);
builder.Services.AddSingleton(_ => new Store(root));
builder.Services.AddSingleton<IValidator, PlanValidator>();
builder.Services.AddSingleton<IAgentPolicy, EvidencePolicy>();
builder.Services.AddSingleton<Func<ProviderSnapshot, IModelProvider>>(_ => _ => throw new InvalidOperationException("The native engine owns this test's loop."));
builder.Services.AddSingleton<Runtime>();
builder.Services.AddSingleton<IModelAccessGate, ModelAccessGate>();
builder.Services.AddSingleton<IInferenceTransport>(services => mode == "luna" ? new CompatibleInference(
    new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromMinutes(5) }, null)
    : new ScriptedNativeModel(services.GetRequiredService<Store>()));
WorkerMcp.Register(builder.Services);
await using var app = builder.Build();
var store = app.Services.GetRequiredService<Store>();
var runtime = app.Services.GetRequiredService<Runtime>();
var authorization = app.Services.GetRequiredService<WorkerAuthorization>();
var controlToken = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
var transport = new FixtureContainer(container, root);
var backend = new OpenClawBackend(transport);
store.Write("notes/source.md", "# Workshop\nA fictional workshop lasts 45 minutes. Its audience has not been selected.\n", "absent");
var run = new Run
{
    Goal = new("Read notes/source.md using thaddeus_read_note, then ask which audience to use with thaddeus_ask_user (Developers or Beginners). Stop until the answer arrives. After the answer, write summary.md in your private artifact directory and propose its exact contents for plans/summary.md with thaddeus_propose_import. Mention the workshop duration and source path. This is a fictional integration fixture.",
        ["notes/source.md"], "plans/", [], new(ModelCalls: 6, ToolCalls: 12, Seconds: 600, MaxTotalTokens: 96000),
        new("compatible", "gpt-5.6-luna", "high", "http://127.0.0.1:5181/v1"), "research"),
    Profile = PolicyProfile.Evidence
};
run.Execution = new("openclaw", container, "agent:thaddeus:" + run.Id, OpenClawBackend.PinnedVersion);
store.Save(run, "fixture.created", new { mode, isolationQualification = false, modelUsage = mode == "scripted" ? "synthetic" : "provider-reported" });
var context = await runtime.PrepareExecutionContext(run.Id, default);
var grant = authorization.Issue(run.Id, TimeSpan.FromMinutes(20));
var binding = new { schemaVersion = 1, runId = run.Id, brokerOrigin = "http://127.0.0.1:" + port,
    model = run.Goal.Provider.Model, reasoning = "high", context, grantToken = grant };

app.Use(async (http, next) =>
{
    if (WorkerMcp.IsWorkerRequest(http))
    {
        if (!WorkerMcp.Authenticate(http, authorization, "http://localhost:5179", port)) { http.Response.StatusCode = 403; return; }
    }
    else if (http.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip) ||
        http.Request.Headers.Authorization.ToString() != "Bearer " + controlToken ||
        http.Request.Headers.ContainsKey("Origin") || http.Request.Headers.ContainsKey("Cookie")) { http.Response.StatusCode = 403; return; }
    try { await next(); }
    catch (Exception error) { http.Response.StatusCode = 409; await http.Response.WriteAsJsonAsync(new { error = error.Message }); }
});
app.MapMcp("/worker/{runId}/mcp"); WorkerModels.Map(app);
app.MapGet("/fixture/state", () => Results.Json(new { run = store.Get(run.Id), events = store.AllEvents(), mode }, Wire.Json));
app.MapPost("/fixture/start", async () =>
{
    var current = store.Get(run.Id)!;
    if (current.State != RunState.Queued) throw new InvalidOperationException("Do not retry uncertain admission.");
    current.State = RunState.Running; current.ExecutionDeadlineStart = DateTimeOffset.UtcNow;
    store.Save(current, "fixture.start.intent", new { authority = "fixture-controller" });
    var observation = await backend.Start(new(current.Id, current.Execution!, current.Goal.Objective, current.Goal.Provider, current.Goal.Limits), default);
    RecordObservation(observation); return observation;
});
app.MapPost("/fixture/abort", async () => await backend.Cancel(store.Get(run.Id)!.Execution!, default));
app.MapPost("/fixture/resume", async () =>
{
    var current = store.Get(run.Id)!;
    if (current.Question is not { Answer: null } question) throw new InvalidOperationException("The fixture has no unanswered question.");
    await runtime.AnswerQuestion(current.Id, question.Id, "Developers", default);
    current = store.Get(run.Id)!; current.State = RunState.Running; current.ExecutionDeadlineStart = DateTimeOffset.UtcNow;
    store.Save(current, "fixture.resume.intent", new { answer = "Developers", authority = "fictional-test-input" });
    var observation = await backend.Resume(current.Execution!, "The user answered: Developers. Continue the original task. Write the artifact and propose its exact import, then stop.", Guid.NewGuid().ToString("N"), default);
    RecordObservation(observation); return observation;
});
app.MapPost("/fixture/approve", async () =>
{
    var current = store.Get(run.Id)!; var approval = current.Approval ?? throw new InvalidOperationException("No exact proposal.");
    var artifact = await transport.GetText(container, "summary.md", default);
    if (artifact.Content != approval.Action.Content) throw new InvalidOperationException("Native artifact differs from proposed import.");
    return await runtime.Decide(current.Id, approval.Id, approval.Digest, true);
});
app.MapPost("/fixture/shutdown", () => { app.Lifetime.StopApplication(); return Results.Ok(); });
await app.StartAsync();
await File.WriteAllTextAsync(Path.Combine(root, "controller.json"), Wire.Pack(new { controlToken, port, binding }));
Console.WriteLine("Native integration fixture ready. Fictional papers only; the family silver stays upstairs.");
try { await app.WaitForShutdownAsync(); }
finally
{
    authorization.Revoke(run.Id);
    await File.WriteAllTextAsync(Path.Combine(root, "final-state.json"), Wire.Pack(new { run = store.Get(run.Id), events = store.AllEvents(), mode }));
}

void RecordObservation(ExecutionObservation observation)
{
    // The model/tool broker may commit while the Gateway acknowledges admission. Preserve its newest row.
    for (var attempt = 0; ; attempt++)
    {
        var current = store.Get(run.Id)!;
        current.Execution = current.Execution! with { RuntimeRunId = observation.RuntimeRunId };
        try { store.Save(current, "fixture.native.acknowledged", observation); return; }
        catch (InvalidOperationException) when (attempt < 4) { }
    }
}

sealed class ScriptedNativeModel(Store store) : IInferenceTransport
{
    public const string Summary = "# Workshop summary\nAudience: Developers\nDuration: 45 minutes\nSource: notes/source.md\n";
    public async Task<InferenceReply> Send(ProviderSnapshot provider, JsonElement body, CancellationToken cancellation)
    {
        var current = store.List().Single();
        await File.WriteAllTextAsync(Path.Combine(store.Root, $"synthetic-request-{current.ModelCalls}.json"), body.GetRawText(), cancellation);
        var stage = current.ModelCalls;
        var suffix = stage switch { 1 => "thaddeus_read_note", 2 => "thaddeus_ask_user", 3 => "write", 4 => "thaddeus_propose_import", _ => throw new InvalidOperationException("Unexpected extra scripted dispatch.") };
        var names = body.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("function").GetProperty("name").GetString()!).ToArray();
        var name = names.SingleOrDefault(n => n == suffix || n.EndsWith("_" + suffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Expected native tool is not advertised: " + suffix);
        object arguments = stage switch
        {
            1 => new { operationId = "native-read", path = "notes/source.md" },
            2 => new { operationId = "native-question", question = "Which audience should the workshop address?", choices = new[] { "Developers", "Beginners" } },
            3 => new { path = "/home/agent/thaddeus-artifacts/summary.md", content = Summary },
            _ => new { operationId = "native-import", path = "plans/summary.md", artifact = "summary.md", content = Summary }
        };
        var reply = JsonSerializer.SerializeToElement(new { id = "synthetic-native-" + stage, model = provider.Model, created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            choices = new[] { new { index = 0, message = new { role = "assistant", content = (string?)null,
                tool_calls = new[] { new { id = "call-native-" + stage, type = "function", function = new { name, arguments = Wire.Pack(arguments) } } } }, finish_reason = "tool_calls" } },
            usage = new { prompt_tokens = 100, completion_tokens = 30 } });
        return new(reply, 100, 30); // Explicitly synthetic protocol accounting, never model efficacy evidence.
    }
}

sealed class FixtureContainer(string owned, string evidenceRoot) : ISandboxBackend
{
    private readonly HostProcessRunner runner = new();
    public async Task<SandboxCommandResult> Execute(string id, IReadOnlyList<string> command, string? input, CancellationToken cancellation)
    {
        if (id != owned) throw new InvalidOperationException("This fixture cannot address another container.");
        var result = await runner.Run(new("docker", ["exec", "-i", owned, .. command], Environment.CurrentDirectory, TimeSpan.FromSeconds(55), input), cancellation);
        await File.WriteAllTextAsync(Path.Combine(evidenceRoot, "native-rpc-" + Guid.NewGuid().ToString("N") + ".json"), Wire.Pack(new { result.ExitCode, result.Failure, result.Output, result.Error }), cancellation);
        return new(result.ExitCode ?? -1, result.Output, result.Error);
    }
    public async Task<SandboxText> GetText(string id, string path, CancellationToken cancellation)
    {
        if (path != "summary.md") throw new ArgumentException("Only the fixture artifact is readable.");
        var result = await Execute(id, ["python3", "-c", "import os,json; fd=os.open('/home/agent/thaddeus-artifacts/summary.md',os.O_RDONLY|os.O_NOFOLLOW); data=os.read(fd,100001); os.close(fd); assert len(data)<=100000; print(json.dumps(data.decode('utf-8')))"], null, cancellation);
        if (result.ExitCode != 0) throw new InvalidOperationException("The native artifact was not readable.");
        var content = JsonSerializer.Deserialize<string>(result.Output)!; return new(path, content, Wire.Hash(content));
    }
    public Task<SandboxInspection> Inspect(CancellationToken cancellation) => throw new NotSupportedException("Fixture only; no qualification claim.");
    public Task Create(SandboxSpec spec, CancellationToken cancellation) => throw new NotSupportedException();
    public Task Stop(string id, CancellationToken cancellation) => throw new NotSupportedException();
    public Task Remove(string id, CancellationToken cancellation) => throw new NotSupportedException();
    public Task PutText(string id, string path, string content, CancellationToken cancellation) => throw new NotSupportedException();
}
