using System.Text.Json;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.ClearProviders();
var externalCallsAttempted = 0;
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithListToolsHandler((_, _) => ValueTask.FromResult(new ListToolsResult
    {
        Tools =
        [
            new Tool
            {
                Name = "send_email",
                Description = "Send an email message through the fictional owner test mailbox.",
                InputSchema = Schema(new
                {
                    type = "object",
                    properties = new
                    {
                        to = new { type = "string" },
                        subject = new { type = "string" },
                        body = new { type = "string" }
                    },
                    required = new[] { "to", "subject", "body" },
                    additionalProperties = false
                }),
                Annotations = new ToolAnnotations { ReadOnlyHint = false, DestructiveHint = false, OpenWorldHint = true }
            },
            new Tool
            {
                Name = "search_email",
                Description = "Read important email messages from the fictional owner test mailbox.",
                InputSchema = Schema(new
                {
                    type = "object",
                    properties = new
                    {
                        limit = new { type = "integer", minimum = 1, maximum = 50 },
                        since = new { type = "string" }
                    },
                    required = new[] { "limit", "since" },
                    additionalProperties = false
                }),
                Annotations = new ToolAnnotations { ReadOnlyHint = true, DestructiveHint = false, OpenWorldHint = true }
            },
            new Tool
            {
                Name = "list_calendar_events",
                Description = "Read calendar events from the fictional owner test calendar.",
                InputSchema = Schema(new
                {
                    type = "object",
                    properties = new
                    {
                        timeMin = new { type = "string" },
                        timeMax = new { type = "string" }
                    },
                    required = new[] { "timeMin", "timeMax" },
                    additionalProperties = false
                }),
                Annotations = new ToolAnnotations { ReadOnlyHint = true, DestructiveHint = false, OpenWorldHint = true }
            }
        ]
    }))
    .WithCallToolHandler((_, _) =>
    {
        Interlocked.Increment(ref externalCallsAttempted);
        return ValueTask.FromResult(new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = "Acceptance fixture discovery only; no external action was executed." }]
        });
    });

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new
{
    ready = true,
    externalCallsEnabled = false,
    externalCallsAttempted = Volatile.Read(ref externalCallsAttempted)
}));
app.MapMcp("/mcp");
app.Run();

static JsonElement Schema(object value) => JsonSerializer.SerializeToElement(value);
