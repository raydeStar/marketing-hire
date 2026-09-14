using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

/// <summary>An owner-only local screen stays available after the product store and worker services have closed.</summary>
public static class MaintenanceScreen
{
    public static bool Authorized(HttpContext context, MaintenancePlan plan)
    {
        var origin = $"{context.Request.Scheme}://{context.Request.Host}";
        var token = context.Request.Cookies["thaddeus-session"];
        return string.Equals(origin, plan.Origin, StringComparison.OrdinalIgnoreCase) && NetworkBoundary.IsLocalOwnerOrigin(context, plan.Origin) &&
            !context.Request.Headers.ContainsKey("Tailscale-Funnel-Request") && context.Request.Headers["Sec-Fetch-Site"] != "cross-site" &&
            (!context.Request.Headers.TryGetValue("Origin", out var supplied) || string.Equals(supplied, origin, StringComparison.OrdinalIgnoreCase)) &&
            plan.Owner.Owner && !plan.Owner.Revoked && plan.Owner.Expires > DateTimeOffset.UtcNow && token != null && Wire.Hash(token) == plan.Owner.TokenHash;
    }

    public static async Task<bool> Run(MaintenancePlan plan)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = plan.Package });
        // Maintenance uses only the reviewed local origin, including when the ordinary host has phone endpoints.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.WebHost.UseUrls(plan.Origin);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 4000);
        var app = builder.Build();
        var restore = new GuidedRestore(plan);
        await using var picker = new ApplicationFolderPicker(new NativeApplicationFolderDialog(plan.Package));
        var sync = new object(); var reopen = false; var ending = false;
        var state = new MaintenanceView(plan.Mode == "backup" ? "copying" : "stopped", plan.Id, plan.Source, plan.BackupRoot,
            plan.Mode == "backup" ? plan.Destination : null,
            plan.Mode == "backup" ? "The study is closed. Creating and verifying your backup…" : "The study is closed. No new backup was requested.", false);
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            if (!Authorized(context, plan)) { context.Response.StatusCode = 403; return; }
            if (context.Request.Method is not ("GET" or "HEAD") &&
                (!string.Equals(context.Request.Headers["Origin"], plan.Origin, StringComparison.OrdinalIgnoreCase) || !context.Request.HasJsonContentType() || context.Request.Headers["X-CSRF"] != plan.Owner.Csrf))
            { context.Response.StatusCode = 403; return; }
            try { await next(); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or JsonException or IOException or UnauthorizedAccessException)
            {
                if (!context.Response.HasStarted) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = error is JsonException ? "Invalid maintenance request." : error.Message }); }
            }
        });
        app.UseDefaultFiles(); app.UseStaticFiles();
        app.MapGet("/api/session", () => new { plan.Owner.Id, plan.Owner.Csrf, plan.Owner.Owner });
        app.MapGet("/api/maintenance", () => { lock (sync) return state; });
        app.MapGet("/api/maintenance/backups", async (CancellationToken cancellation) => await restore.Backups(cancellation));
        app.MapGet("/api/maintenance/restore", () => restore.View);
        app.MapGet("/api/maintenance/application-folder", () => picker.View);
        app.MapPost("/api/maintenance/application-folder/start", (MaintenanceRequest request) =>
        {
            lock (sync)
            {
                if (request.Version != plan.Id || request.Mode != "choose") throw new ArgumentException("Refresh this maintenance screen before choosing an application.");
                if (ending || state.Phase == "copying" || restore.Busy) throw new InvalidOperationException("Wait for the current maintenance operation to finish.");
                var opened = picker.Begin(app.Lifetime.ApplicationStopping);
                restore.ClearReview();
                return opened;
            }
        });
        app.MapPost("/api/maintenance/application-folder/cancel", (FolderPickerCancellation request) => picker.Cancel(request.Id));
        app.MapPost("/api/maintenance/restore/review", async (HttpContext context, RestoreSelection selection) =>
        {
            Task<GuidedRestoreView> review;
            lock (sync)
            {
                if (ending || state.Phase == "copying" || restore.Busy || picker.Busy) throw new InvalidOperationException("Wait for the current maintenance operation to finish.");
                // Review claims its busy state synchronously before releasing the shared admission lock.
                review = restore.Review(selection.BackupId, context.RequestAborted, selection.PackageDirectory);
            }
            return await review;
        });
        app.MapPost("/api/maintenance/restore/start", (RestoreConfirmation confirmation) =>
        {
            lock (sync)
            {
                if (ending || state.Phase == "copying" || picker.Busy) throw new InvalidOperationException("Wait for the current maintenance operation to finish.");
                return restore.Begin(confirmation.ReviewId, app.Lifetime.ApplicationStopping);
            }
        });
        app.MapPost("/api/maintenance/finish", async (HttpContext context, MaintenanceRequest request) =>
        {
            lock (sync)
            {
                if (request.Version != plan.Id || request.Mode is not ("reopen" or "close")) throw new ArgumentException("Refresh this maintenance screen before continuing.");
                if (state.Phase == "copying" || restore.Busy || picker.Busy || ending) throw new InvalidOperationException("Finish or cancel the current maintenance operation before closing maintenance.");
                ending = true; reopen = request.Mode == "reopen";
            }
            try { await context.Response.WriteAsJsonAsync(new { accepted = true, action = request.Mode }); await context.Response.CompleteAsync(); }
            finally { app.Lifetime.StopApplication(); }
        });
        app.Map("/api/{**path}", () => Results.Json(new { error = "The study is closed for maintenance." }, statusCode: 503));
        app.Map("/worker/{**path}", () => Results.StatusCode(403));
        app.MapGet("/", () => Results.Redirect("/maintenance"));
        app.MapFallbackToFile("index.html");
        await app.StartAsync();
        // No Store, model transport, worker factory or research pump exists in this service provider.
        var copy = Task.Run(async () =>
        {
            if (plan.Mode != "backup") return;
            try
            {
                PrivateWorkerDirectory.OpenOrCreate(plan.BackupRoot);
                var receipt = await StudyBackup.Create(plan.Source, plan.Destination, app.Lifetime.ApplicationStopping);
                var json = System.Text.Encoding.UTF8.GetBytes(Wire.Pack(receipt));
                using (var output = new FileStream(Path.Combine(plan.BackupRoot, plan.Id + ".receipt.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { await output.WriteAsync(json); output.Flush(flushToDisk: true); }
                lock (sync) state = state with { Phase = "verified", Message = "Backup verified. Your original study and its history remain in place.", Receipt = receipt };
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or OperationCanceledException or Microsoft.Data.Sqlite.SqliteException)
            {
                lock (sync) state = state with { Phase = "failed", Message = "A completed backup could not be confirmed. Keep the original study and any incomplete copy. You can reopen the study and try again." };
                app.Logger.LogWarning("Study backup was not confirmed ({Kind}). The original remains in place.", error.GetType().Name);
            }
        });
        // A forgotten maintenance tab is not a permanent credential service. Interruption retains the incomplete copy.
        using var expiry = new CancellationTokenSource(plan.Owner.Expires - DateTimeOffset.UtcNow > TimeSpan.Zero ? plan.Owner.Expires - DateTimeOffset.UtcNow : TimeSpan.Zero);
        using var expired = expiry.Token.Register(app.Lifetime.StopApplication);
        try { await app.WaitForShutdownAsync(); await copy; await restore.Completion; }
        finally { await app.DisposeAsync(); }
        return reopen;
    }
}
