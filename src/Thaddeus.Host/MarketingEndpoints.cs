using System.Text.Json;

namespace Thaddeus.Host;

public static class MarketingEndpoints
{
    public static void Map(WebApplication app)
    {
        bool LocalOwner(HttpContext context) => NetworkBoundary.IsLocalOwnerOrigin(context,
            app.Configuration["Thaddeus:LocalOrigin"] ?? "http://localhost:5179");
        app.MapGet("/api/meetings", (CompanyMeetings meetings, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } ? Results.Ok(meetings.List()) : Results.StatusCode(403));
        app.MapPost("/api/meetings", (CompanyMeetings meetings, MeetingCommand body, HttpContext context) =>
            Task.FromResult<IResult>(context.Items["session"] is DeviceSession { Owner: true }
                ? Results.Json(new { error = "New meetings are paused for the single-employee MVP. Past records remain available." }, statusCode: 409)
                : Results.StatusCode(403)));
        app.MapPost("/api/meetings/{id}", (CompanyMeetings meetings, string id, MeetingCommand body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? body.Action is "veto" or "accept-artifact"
                    ? MeetingResult(meetings.Change(id, body, "Owner " + owner.PrincipalId, app.Lifetime.ApplicationStopping))
                    : Task.FromResult<IResult>(Results.Json(new { error = "Meeting turns and plan approval are paused for the single-employee MVP." }, statusCode: 409))
                : Task.FromResult<IResult>(Results.StatusCode(403)));
        // Program.cs protects every /api route with the existing session and CSRF checks.
        app.MapGet("/api/marketing/state", (MarketingBackend marketing, HttpContext context) =>
            marketing.State(context.Items["session"] is DeviceSession { Owner: true }, context.RequestAborted));
        app.MapGet("/api/marketing/history", (MarketingBackend marketing, string? before, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true }
                ? marketing.History(before) : Results.StatusCode(403));
        app.MapGet("/api/marketing/usage", (MarketingBackend marketing, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true }
                ? marketing.UsageHistory(context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
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
        app.MapPost("/api/marketing/runway/continue-pilot", (MarketingBackend marketing, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.ContinuePilot(body, owner, context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/runway/{id}/input", (MarketingBackend marketing, string id, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.AddRunwayInput(id, body, owner, context.RequestAborted)
                : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/runway/{id}/review", (MarketingBackend marketing, string id, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.ReviewRunway(id, body, owner, context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/runway/{id}/campaign-brief", (MarketingBackend marketing, string id, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.SaveCampaignBrief(id, body, owner, context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/runway/{id}/campaign-observation", (MarketingBackend marketing, string id, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.RecordCampaignObservation(id, body, owner, context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/runway/{id}/campaign-internal-action", (MarketingBackend marketing, string id, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.RecordInternalCampaignAction(id, body, owner, context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapGet("/api/marketing/campaign-lessons", (MarketingBackend marketing, string? audience, string? excludeCampaignId, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true }
                ? marketing.InternalCampaignLessons(audience, excludeCampaignId, context.RequestAborted)
                : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/runway/{id}/campaign-adopt-revision", (MarketingBackend marketing, string id, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.AdoptCampaignRevision(id, body, owner, context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/runway/fixture/seed", (MarketingBackend marketing, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.SeedCampaignFixture(body, owner, context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        // Disposable localhost fixture only: replace this browser's owner test cookie with
        // an independently authenticated collaborator cookie for the browser contract test.
        app.MapPost("/api/marketing/fixture/collaborator-session", (MarketingBackend marketing,
            Security security, HttpContext context) =>
        {
            var address = context.Connection.RemoteIpAddress;
            if (!marketing.FixtureCampaignEnabled || address == null ||
                !(System.Net.IPAddress.IsLoopback(address) ||
                  address.IsIPv4MappedToIPv6 && System.Net.IPAddress.IsLoopback(address.MapToIPv4())))
                return Results.NotFound();
            if (context.Items["session"] is not DeviceSession { Owner: true }) return Results.StatusCode(403);
            var issued = security.Issue(context, "Fixture collaborator", false, campaignOnly: true);
            return Results.Ok(new { issued.Id, issued.Csrf, issued.Owner, issued.Name });
        });
        app.MapPost("/api/marketing/fixture/campaigns/{id}/requests/{requestId}/linked-revision",
            (MarketingBackend marketing, string id, string requestId, HttpContext context) =>
                context.Items["session"] is DeviceSession { Owner: true } owner
                    ? marketing.RunFixtureLinkedRevision(id, requestId, owner, context.RequestAborted)
                    : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/runway/{id}/campaign-action", (MarketingBackend marketing, string id, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.CampaignFixtureAction(id, body, owner, context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/runway/fixture/lessons", (MarketingBackend marketing, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true }
                ? marketing.CampaignFixtureLessons(body, context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/runway/{id}/revision-grants", (MarketingBackend marketing, string id, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.PrepareRevisionGrant(id, body, owner, context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/revision-grants/{grantId}/release", (MarketingBackend marketing, string grantId, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.ReleaseRevisionGrant(grantId, owner, context.RequestAborted) : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapGet("/api/marketing/runway/{id}/shared", (MarketingBackend marketing, Security security, string id, HttpContext context) =>
            marketing.SharedConversation(id, (DeviceSession)context.Items["session"]!, security));
        app.MapPost("/api/marketing/runway/{id}/shared", (MarketingBackend marketing, string id, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.StartSharedConversation(id, owner, context.Connection.RemoteIpAddress,
                    context.Request.IsHttps, LocalOwner(context), context.RequestAborted)
                : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/runway/{id}/shared/suggestions", (MarketingBackend marketing, Security security, string id, JsonElement body, HttpContext context) =>
            marketing.AddSharedSuggestion(id, body, (DeviceSession)context.Items["session"]!, security,
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
        app.MapGet("/api/marketing/campaigns/shared", (MarketingBackend marketing, Security security, HttpContext context) =>
            marketing.SharedCampaignList((DeviceSession)context.Items["session"]!, security, context.RequestAborted));
        app.MapGet("/api/marketing/campaigns/{id}/invitations", (MarketingBackend marketing, string id, HttpContext context) =>
            marketing.CampaignInvitations(id, (DeviceSession)context.Items["session"]!));
        app.MapPost("/api/marketing/campaigns/{id}/invitations", (MarketingBackend marketing, string id, JsonElement body, HttpContext context) =>
            marketing.CreateCampaignInvitation(id, body, (DeviceSession)context.Items["session"]!,
                context.RequestServices.GetService<CustomerLoginSettings>(), context.RequestAborted));
        app.MapPost("/api/marketing/campaigns/{id}/invitations/{invitationId}/revoke", (MarketingBackend marketing,
            string id, string invitationId, HttpContext context) =>
            marketing.RevokeCampaignInvitation(id, invitationId, (DeviceSession)context.Items["session"]!));
        app.MapPost("/api/marketing/campaigns/invitations/preview", (MarketingBackend marketing,
            Security security, JsonElement body, HttpContext context) =>
            marketing.UseCampaignInvitation(body, (DeviceSession)context.Items["session"]!, security, false, context.RequestAborted));
        app.MapPost("/api/marketing/campaigns/invitations/accept", (MarketingBackend marketing,
            Security security, JsonElement body, HttpContext context) =>
            marketing.UseCampaignInvitation(body, (DeviceSession)context.Items["session"]!, security, true, context.RequestAborted));
        app.MapGet("/api/marketing/campaigns/{id}/review", (MarketingBackend marketing, Security security,
            string id, HttpContext context) =>
            marketing.SharedCampaignReview(id, (DeviceSession)context.Items["session"]!, security, context.RequestAborted));
        app.MapPost("/api/marketing/campaigns/{id}/native/start", (MarketingBackend marketing,
            string id, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.StartSharedConversation(id, owner, context.Connection.RemoteIpAddress,
                    context.Request.IsHttps, LocalOwner(context), context.RequestAborted)
                : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapGet("/api/marketing/campaigns/{id}/access", (MarketingBackend marketing, Security security,
            string id, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true }
                ? marketing.CampaignAccess(id, security) : Results.StatusCode(403));
        app.MapPost("/api/marketing/campaigns/{id}/access", (MarketingBackend marketing, Security security,
            string id, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.ChangeCampaignAccess(id, body, security, owner, context.RequestAborted)
                : Task.FromResult<IResult>(Results.StatusCode(403)));
        app.MapPost("/api/marketing/campaigns/{id}/inputs", (MarketingBackend marketing, Security security,
            string id, JsonElement body, HttpContext context) =>
            marketing.AddCampaignSharedInput(id, body, (DeviceSession)context.Items["session"]!,
                security, context.Connection.RemoteIpAddress, context.Request.IsHttps, LocalOwner(context),
                context.RequestAborted));
        app.MapPost("/api/marketing/campaigns/{id}/requests/{requestId}/authorize", (MarketingBackend marketing,
            string id, string requestId, JsonElement body, HttpContext context) =>
            context.Items["session"] is DeviceSession { Owner: true } owner
                ? marketing.AuthorizeCampaignRevisionRequest(id, requestId, body, owner, context.RequestAborted)
                : Task.FromResult<IResult>(Results.StatusCode(403)));
    }
    private static async Task<IResult> MeetingResult(Task<CompanyMeeting> result) => Results.Ok(await result);
}
