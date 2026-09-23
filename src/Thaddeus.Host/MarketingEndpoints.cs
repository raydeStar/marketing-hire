using System.Text.Json;

namespace Thaddeus.Host;

public static class MarketingEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/meetings", (CompanyMeetings meetings) => meetings.List());
        app.MapPost("/api/meetings", (CompanyMeetings meetings, MeetingCommand body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? MeetingResult(meetings.Change(null, body, "Owner " + owner.Id, app.Lifetime.ApplicationStopping))
                : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/meetings/{id}", (CompanyMeetings meetings, string id, MeetingCommand body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? MeetingResult(meetings.Change(id, body, "Owner " + owner.Id, app.Lifetime.ApplicationStopping))
                : Task.FromResult<IResult>(Results.StatusCode(403)));
        // Program.cs protects every /api route with the existing session and CSRF checks.
        app.MapGet("/api/marketing/state", (MarketingBackend marketing, HttpContext context) =>
            marketing.State(context.Items["session"] is DeviceSession { Owner: true }, context.RequestAborted));
        app.MapGet("/api/marketing/history", (MarketingBackend marketing, string? before) => marketing.History(before));
        app.MapPost("/api/marketing/tasks", (MarketingBackend marketing, JsonElement body, HttpContext context) =>
            marketing.CreateTask(body, context.RequestAborted));
        app.MapPut("/api/marketing/tasks/{id}", (MarketingBackend marketing, string id, JsonElement body, HttpContext context) =>
            marketing.UpdateTask(id, body, context.RequestAborted));
        app.MapPut("/api/marketing/profile", (MarketingBackend marketing, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true }
                ? marketing.UpdateProfile(body, context.RequestAborted)
                : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/tasks/{id}/evidence", (MarketingBackend marketing, string id, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true }
                ? marketing.AddEvidence(id, body, context.RequestAborted)
                : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/drafts/{id:int}/decision", (MarketingBackend marketing, int id, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.DecideDraft(id, body, owner, context.RequestAborted)
                : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/chat", (MarketingBackend marketing, JsonElement body, HttpContext context) =>
            marketing.Chat(body, context.RequestAborted));
    }
    private static async Task<IResult> MeetingResult(Task<CompanyMeeting> result) => Results.Ok(await result);
}
