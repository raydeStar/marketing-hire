using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public static class WorkerMcp
{
    public static void Register(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<WorkerAuthorization>();
        services.AddSingleton<ICapabilityBroker>(provider => provider.GetRequiredService<Runtime>());
        services.AddMcpServer().WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithListToolsHandler((context, cancellation) =>
            {
                var broker = context.Services!.GetRequiredService<ICapabilityBroker>();
                return ValueTask.FromResult(new ListToolsResult
                {
                    Tools = broker.Tools.Select(tool => new Tool
                    {
                        Name = tool.Name, Description = tool.Description,
                        InputSchema = WithOperationId(tool.InputSchema)
                    }).ToList()
                });
            })
            .WithCallToolHandler(async (context, cancellation) =>
            {
                var http = context.Services!.GetRequiredService<IHttpContextAccessor>().HttpContext
                    ?? throw new InvalidOperationException("Worker transport context is missing.");
                var runId = http.Items["worker-run-id"] as string ?? throw new InvalidOperationException("Worker identity is missing.");
                var parameters = context.Params ?? throw new ArgumentException("Tool parameters are required.");
                var arguments = parameters.Arguments?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) ?? [];
                if (!arguments.Remove("operationId", out var operation) || operation.ValueKind != JsonValueKind.String)
                    return Failure("A stable operationId is required for retry reconciliation.");
                try
                {
                    var broker = context.Services!.GetRequiredService<ICapabilityBroker>();
                    var result = await broker.Call(runId, new(operation.GetString()!, parameters.Name, JsonSerializer.SerializeToElement(arguments)), cancellation);
                    return new CallToolResult
                    {
                        IsError = result.IsError,
                        Content = [new TextContentBlock { Text = result.Value.GetRawText() }]
                    };
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                { return Failure(ex.Message); }
            });
    }
    private static CallToolResult Failure(string message) => new() { IsError = true, Content = [new TextContentBlock { Text = message }] };
    private static JsonElement WithOperationId(JsonElement original)
    {
        var schema = JsonNode.Parse(original.GetRawText())!;
        schema["properties"]!["operationId"] = new JsonObject
        {
            ["type"] = "string", ["pattern"] = "^[a-zA-Z0-9_-]{1,100}$",
            ["description"] = "Unique ID for this operation. Reuse exactly this ID and arguments when retrying the same request."
        };
        schema["required"]!.AsArray().Add("operationId");
        return JsonSerializer.SerializeToElement(schema);
    }
    public static bool IsWorkerRequest(HttpContext context) => context.Request.Path.StartsWithSegments("/worker");
    public static bool Authenticate(HttpContext context, WorkerAuthorization authorization, string localOrigin, int? workerPort = null)
    {
        // This transport never accepts a browser session as a worker credential.
        if (context.Request.Headers.ContainsKey("Origin") || context.Request.Headers.ContainsKey("Cookie") ||
            context.Request.QueryString.HasValue || context.Request.Headers.ContainsKey("X-Forwarded-For") ||
            context.Request.Headers.ContainsKey("Forwarded")) return false;
        if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote)) return false;
        if (workerPort == null)
        {
            if (!NetworkBoundary.IsLocalOwnerOrigin(context, localOrigin)) return false;
        }
        else if (context.Connection.LocalPort != workerPort || context.Request.Scheme != "http" ||
            context.Request.Host.Port != workerPort || context.Request.Host.Host is not ("127.0.0.1" or "localhost" or "host.docker.internal")) return false;
        var segments = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments is not ["worker", _, "mcp"] && segments is not ["worker", _, "v1", "chat", "completions"]) return false;
        var runId = segments[1];
        if (!System.Text.RegularExpressions.Regex.IsMatch(runId, @"\A[a-f0-9]{32}\z")) return false;
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || !authorization.Authenticate(runId, header[7..])) return false;
        context.Items["worker-run-id"] = runId; return true;
    }
}
