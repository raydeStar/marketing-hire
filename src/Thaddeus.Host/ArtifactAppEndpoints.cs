using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public static class ArtifactAppEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/artifacts/{id}", (string id, Store store) => store.Artifact(id) is { } artifact ? Results.Ok(artifact) : Results.NotFound());
        app.MapGet("/api/artifacts/{id}/page", (string id, HttpContext context, Store store) => {
            if (store.Artifact(id)?.Definition.Page is not { } page) return Results.NotFound();
            context.Response.Headers["Content-Security-Policy"] = ArtifactPageDocument.Policy;
            context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), fullscreen=()";
            return Results.Content(ArtifactPageDocument.Render(PageMedia.Inline(page, store)), "text/html; charset=utf-8");
        });
        app.MapPut("/api/artifacts/{id}", (string id, AppEdit edit, Store store) => store.EditArtifact(id, edit));
        app.MapGet("/api/artifacts/{id}/history", (string id, Store store) => store.ArtifactRevisions(id).Select(revision => new {
            revision.Id, revision.Description, revision.Source, revision.At, version = revision.Snapshot.Version,
            title = revision.Snapshot.Definition.Title, entryCount = revision.Snapshot.Entries.Length
        }));
        app.MapPost("/api/artifacts/{id}/restore", (string id, AppRestore request, Store store) => store.RestoreArtifact(id, request));
        app.MapGet("/api/artifacts/{id}/export", (string id, Store store) => store.Artifact(id) is { } artifact
            ? Results.File(System.Text.Encoding.UTF8.GetBytes(Wire.Pack(new { schemaVersion = 1, artifact, history = store.ArtifactRevisions(id) })), "application/json", "thaddeus-app-" + id + ".json") : Results.NotFound());
    }
}
