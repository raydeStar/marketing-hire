using System.Text.Json;

namespace Thaddeus.Host;

public static class MarketingEndpoints
{
    public static void Map(WebApplication app)
    {
        // Program.cs protects every /api route with the existing session and CSRF checks.
        app.MapGet("/api/marketing/state", (MarketingBackend marketing, HttpContext context) =>
            marketing.State(context.RequestAborted));
        app.MapPost("/api/marketing/tasks", (MarketingBackend marketing, JsonElement body, HttpContext context) =>
            marketing.CreateTask(body, context.RequestAborted));
        app.MapPut("/api/marketing/tasks/{id}", (MarketingBackend marketing, string id, JsonElement body, HttpContext context) =>
            marketing.UpdateTask(id, body, context.RequestAborted));
        app.MapPost("/api/marketing/chat", (MarketingBackend marketing, JsonElement body, HttpContext context) =>
            marketing.Chat(body, context.RequestAborted));
    }
}
