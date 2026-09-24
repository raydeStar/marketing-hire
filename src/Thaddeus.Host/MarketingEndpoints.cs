using System.Text.Json;

namespace Thaddeus.Host;

public static class MarketingEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/meetings", (CompanyMeetings meetings, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } ? Results.Ok(meetings.List()) : Results.StatusCode(403));
        app.MapPost("/api/meetings", (CompanyMeetings meetings, MeetingCommand body, HttpContext context) =>
            Task.FromResult<IResult>(context.Items["session"] is DeviceSession { Owner: true }
                ? Results.Json(new { error = "New meetings are paused for the single-employee MVP. Past records remain available." }, statusCode: 409)
                : Results.StatusCode(403)));
        app.MapPost("/api/meetings/{id}", (CompanyMeetings meetings, string id, MeetingCommand body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? body.Action is "veto" or "accept-artifact"
                    ? MeetingResult(meetings.Change(id, body, "Owner " + owner.Id, app.Lifetime.ApplicationStopping))
                    : Task.FromResult<IResult>(Results.Json(new { error = "Meeting turns and plan approval are paused for the single-employee MVP." }, statusCode: 409))
                : Task.FromResult<IResult>(Results.StatusCode(403)));
        // Program.cs protects every /api route with the existing session and CSRF checks.
        app.MapGet("/api/marketing/state", (MarketingBackend marketing, HttpContext context) =>
            marketing.State(context.Items["session"] is DeviceSession { Owner: true }, context.RequestAborted));
        app.MapGet("/api/marketing/history", (MarketingBackend marketing, string? before, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true }
                ? marketing.History(before) : Results.StatusCode(403));
        app.MapPost("/api/marketing/tasks", (MarketingBackend marketing, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true }
                ? marketing.CreateTask(body, context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPut("/api/marketing/tasks/{id}", (MarketingBackend marketing, string id, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true }
                ? marketing.UpdateTask(id, body, context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
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
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.Chat(body, owner, context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapGet("/api/marketing/runway", (MarketingBackend marketing, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true }
                ? marketing.RunwayState(context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapGet("/api/marketing/runways", (MarketingBackend marketing, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true }
                ? marketing.RunwayArchive(context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapGet("/api/marketing/runways/{id}", (MarketingBackend marketing, string id, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true }
                ? marketing.InspectRunway(id, context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/runway", (MarketingBackend marketing, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.StartRunway(body, owner, context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/runway/{action}", (MarketingBackend marketing, string action, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true }
                ? marketing.ChangeRunway(action, body, context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/runway/{id}/input", (MarketingBackend marketing, string id, JsonElement body, HttpContext context) =>
            marketing.AddRunwayInput(id, body, (DeviceSession)context.Items["session"]!, context.RequestAborted));
        app.MapPost("/api/marketing/runway/{id}/review", (MarketingBackend marketing, string id, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.ReviewRunway(id, body, owner, context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapGet("/api/marketing/runway/{id}/shared", (MarketingBackend marketing, string id, HttpContext context) =>
            marketing.SharedConversation(id, (DeviceSession)context.Items["session"]!));
        app.MapPost("/api/marketing/runway/{id}/shared", (MarketingBackend marketing, string id, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.StartSharedConversation(id, owner, context.Connection.RemoteIpAddress, context.RequestAborted)
                : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/runway/{id}/shared/suggestions", (MarketingBackend marketing, string id, JsonElement body, HttpContext context) =>
            marketing.AddSharedSuggestion(id, body, (DeviceSession)context.Items["session"]!,
                context.Connection.RemoteIpAddress, context.RequestAborted));
        app.MapPost("/api/marketing/runway/{id}/shared/reconcile", (MarketingBackend marketing,
            string id, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true }
                ? marketing.ReconcileSharedSuggestion(id, body, context.RequestAborted)
                : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/runway/{id}/shared/collaborator", (MarketingBackend marketing, Security security,
            string id, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.ApproveSharedCollaborator(id, body, security, owner)
                : Results.StatusCode(403));
    }
    private static async Task<IResult> MeetingResult(Task<CompanyMeeting> result) => Results.Ok(await result);
}
