using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public sealed record BackupChoice(string Id, string? Directory, int? Files, long? Bytes, bool Available, string Message);
public sealed record RestoreReview(string Id, string BackupId, string BackupDirectory, string Destination,
    StudyBackupPreview Backup, bool CanPrepareLauncher, VerifiedApplicationPackage? Application = null);
public sealed record GuidedRestoreView(string Phase, string Message, RestoreReview? Review = null,
    StudyBackupReceipt? Receipt = null, RestoredLauncher? Launcher = null, RestoredLauncher? ReturnLauncher = null);
public sealed record RestoreSelection(string BackupId, string? PackageDirectory = null);
public sealed record RestoreConfirmation(string ReviewId);

/// <summary>A closed study can restore a reviewed backup into a new sibling. The original estate stays put.</summary>
public sealed class GuidedRestore(MaintenancePlan plan)
{
    private readonly object sync = new();
    private GuidedRestoreView state = new("idle", "Choose a backup created through this study's maintenance screen.");
    private Task completion = Task.CompletedTask;
    public GuidedRestoreView View { get { lock (sync) return state; } }
    public Task Completion { get { lock (sync) return completion; } }
    public bool Busy => View.Phase is "restoring" or "reviewing" or "opening";

    public async Task<PreparedStudyOpen> PrepareOpen(OpenStudyRequest request, CancellationToken cancellation)
    {
        GuidedRestoreView previous;
        lock (sync)
        {
            if (Busy) throw new InvalidOperationException("Wait for the current maintenance operation to finish.");
            previous = state;
            state = state with { Phase = "opening", Message = "Checking the prepared application and study before opening them…" };
        }
        try
        {
            var prepared = await ApplicationHandoff.Prepare(plan, previous, request, cancellation);
            cancellation.ThrowIfCancellationRequested();
            Save(prepared.Id, "open-intent", new { prepared, instruction = "This launch is attempted once. Never replay it automatically after interruption." });
            return prepared;
        }
        catch { lock (sync) state = previous; throw; }
    }
    public void OpeningFailed(string message)
    {
        lock (sync) state = state with { Phase = "restored", Message = message };
    }

    public void ClearReview()
    {
        lock (sync)
        {
            if (Busy) throw new InvalidOperationException("Wait for the current restore operation to finish.");
            state = new("idle", "Choose a backup and review the selected application before restoring.");
        }
    }

    public async Task<BackupChoice[]> Backups(CancellationToken cancellation)
    {
        Store.AssertNoLinks(plan.BackupRoot);
        if (!Directory.Exists(plan.BackupRoot)) return [];
        var paths = Directory.EnumerateFiles(plan.BackupRoot, "*.receipt.json", SearchOption.TopDirectoryOnly).Take(101).ToArray();
        if (paths.Length > 100) throw new InvalidOperationException("This folder has more than 100 backup receipts. Use the offline restore command to select a backup.");
        var choices = new List<BackupChoice>();
        foreach (var path in paths.OrderByDescending(path => path, StringComparer.Ordinal))
        {
            var id = Path.GetFileName(path)[..^".receipt.json".Length];
            try
            {
                var receipt = await ReadReceipt(id, cancellation);
                choices.Add(new(id, receipt.Directory, receipt.Files, receipt.Bytes, true, "Recorded backup; its contents will be checked during restore."));
            }
            catch (Exception error) when (Expected(error))
            { choices.Add(new(id, null, null, null, false, "This backup receipt is unreadable or outside the study's backup folder.")); }
        }
        return choices.ToArray();
    }
    public async Task<GuidedRestoreView> Review(string id, CancellationToken cancellation, string? packageDirectory = null)
    {
        GuidedRestoreView previous;
        lock (sync)
        {
            if (Busy) throw new InvalidOperationException("Wait for the current restore operation to finish.");
            previous = state;
            state = new("reviewing", "Checking the selected backup and application files. No new study has been created.");
        }
        try
        {
        var recorded = await ReadReceipt(id, cancellation);
        var preview = await StudyBackup.Preview(recorded.Directory, cancellation);
        if (preview.ManifestSha256 != recorded.ManifestSha256 || preview.Files != recorded.Files || preview.Bytes != recorded.Bytes || preview.DatabaseSchemaVersion != recorded.DatabaseSchemaVersion)
            throw new InvalidOperationException("The backup manifest differs from its recorded receipt. It cannot be selected for guided restore.");
        VerifiedApplicationPackage? application = null;
        if (packageDirectory != null)
        {
            if (plan.Launch == null) throw new InvalidOperationException("Use a published application to prepare a launcher for another version.");
            application = await ApplicationPackage.Verify(packageDirectory, cancellation);
            ApplicationPackage.RequireStudyCompatibility(application, preview.DatabaseSchemaVersion);
        }
        var reviewId = Guid.NewGuid().ToString("N");
        var destination = Path.TrimEndingDirectorySeparator(plan.Source) + "-restored-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + reviewId;
        var review = new RestoreReview(reviewId, id, recorded.Directory, destination, preview, plan.Launch != null, application);
        lock (sync)
        {
            if (state.Phase == "restoring") throw new InvalidOperationException("Wait for the current restore to finish.");
            return state = new("review", "Review this backup and the separate destination. No files have been restored yet.", review);
        }
        }
        catch { lock (sync) state = previous; throw; }
    }
    public GuidedRestoreView Begin(string reviewId, CancellationToken cancellation)
    {
        lock (sync)
        {
            if (state.Review?.Id == reviewId && state.Phase is "restoring" or "restored" or "failed") return state;
            if (state.Phase != "review" || state.Review is not { } review || review.Id != reviewId)
                throw new InvalidOperationException("The restore review changed. Select and review the backup again.");
            Save(review.Id, "intent", new { review, status = "restore-intent", instruction = "Do not replay automatically if completion is unknown." });
            state = new("restoring", "Restoring and verifying a separate copy. Keep this maintenance screen open.", review);
            completion = Task.Run(() => Copy(review, cancellation), CancellationToken.None);
            return state;
        }
    }
    private async Task Copy(RestoreReview review, CancellationToken cancellation)
    {
        StudyBackupReceipt? receipt = null;
        try
        {
            if (review.Application is { } selected)
            {
                var current = await ApplicationPackage.Verify(selected.Directory, cancellation, selected.ManifestSha256);
                ApplicationPackage.RequireStudyCompatibility(current, review.Backup.DatabaseSchemaVersion);
            }
            receipt = await StudyBackup.Restore(review.BackupDirectory, review.Destination, cancellation, review.Backup.ManifestSha256);
            var launcher = plan.Launch == null ? null : await RestoredStudyLauncher.Create(plan.Launch, receipt.Directory, cancellation, review.Application);
            var returnLauncher = plan.Launch != null && review.Application != null ? await RestoredStudyLauncher.CreateOriginal(plan.Launch, cancellation) : null;
            var result = new GuidedRestoreView("restored", launcher == null
                ? "The separate restored study is verified. A published application package is needed to prepare its launcher."
                : "The separate restored study and its launcher are ready. Close Thaddeus before opening that launcher.", review, receipt, launcher, returnLauncher);
            Save(review.Id, "result", result);
            lock (sync) state = result;
        }
        catch (Exception error) when (Expected(error) || error is OperationCanceledException or Microsoft.Data.Sqlite.SqliteException)
        {
            var failure = new GuidedRestoreView("failed", receipt == null
                ? "Restore could not be verified. Keep the original study, backup and any incomplete copy. This attempt will not repeat automatically."
                : "The separate study was restored and verified, but its launcher or completion receipt could not be prepared. Keep that copy and the original study.", review, receipt);
            try { Save(review.Id, "failure", new { result = failure, error = error.GetType().Name }); }
            catch (Exception reporting) when (Expected(reporting)) { failure = failure with { Message = failure.Message + " The failure receipt could not be written." }; }
            lock (sync) state = failure;
        }
    }
    private async Task<StudyBackupReceipt> ReadReceipt(string id, CancellationToken cancellation)
    {
        if (!Regex.IsMatch(id ?? "", "\\A[a-f0-9]{32}\\z")) throw new ArgumentException("Choose a recorded backup from this study.");
        var path = Path.Combine(plan.BackupRoot, id + ".receipt.json"); Store.AssertNoLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 16000) throw new IOException("The backup receipt exceeds its limit.");
        var bytes = new byte[16001]; var read = 0;
        while (read < bytes.Length)
        {
            var count = await stream.ReadAsync(bytes.AsMemory(read), cancellation); if (count == 0) break; read += count;
        }
        if (read > 16000) throw new IOException("The backup receipt grew beyond its limit.");
        var receipt = Wire.Unpack<StudyBackupReceipt>(Encoding.UTF8.GetString(bytes, 0, read));
        if (receipt == null || receipt.Operation != "backup" || !Path.IsPathFullyQualified(receipt.Directory) ||
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(receipt.Directory))) != Path.TrimEndingDirectorySeparator(plan.BackupRoot) ||
            receipt.Files is < 1 or > 20000 || receipt.Bytes < 0 || !Regex.IsMatch(receipt.ManifestSha256 ?? "", "\\A[a-f0-9]{64}\\z"))
            throw new ArgumentException("The receipt is not a backup in this study's recorded folder.");
        Store.AssertNoLinks(receipt.Directory); return receipt;
    }
    private void Save(string id, string kind, object value)
    {
        PrivateWorkerDirectory.OpenOrCreate(plan.BackupRoot);
        var bytes = Encoding.UTF8.GetBytes(Wire.Pack(value));
        using var stream = new FileStream(Path.Combine(plan.BackupRoot, "restore-" + id + "." + kind + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes); stream.Flush(flushToDisk: true);
    }
    private static bool Expected(Exception error) => error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException;
}
