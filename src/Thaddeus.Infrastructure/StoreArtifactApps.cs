using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Store
{
    public ArtifactApp? Artifact(string id)
    {
        AppId(id);
        lock (gate) return Query("SELECT body FROM artifact_apps WHERE id=$id", ("$id", id)).Select(Wire.Unpack<ArtifactApp>).SingleOrDefault();
    }
    public AppSummary[] ArtifactSummaries()
    {
        lock (gate) return Artifacts().Select(app => new AppSummary(app.Id, app.Definition.Title, app.Definition.Description,
            app.Version, app.Entries.Length, app.Archived)).ToArray();
    }
    public ArtifactApp[] Artifacts()
    {
        lock (gate) return Query("SELECT body FROM artifact_apps ORDER BY rowid DESC").Select(Wire.Unpack<ArtifactApp>).ToArray();
    }
    public AppRevision[] ArtifactRevisions(string? id = null)
    {
        if (id != null) AppId(id);
        lock (gate) return Query("SELECT body FROM artifact_revisions WHERE ($id IS NULL OR artifactId=$id) ORDER BY rowid DESC", ("$id", id)).Select(Wire.Unpack<AppRevision>).ToArray();
    }
    public string ArtifactCursor() => Setting("artifact-revision") ?? "absent";
    public ArtifactChatContext ArtifactContext(string? id, string localDate)
    {
        if (!DateOnly.TryParseExact(localDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw new ArgumentException("Use a calendar date for the current day.");
        lock (gate)
        {
            var selected = id == null ? null : Artifact(id) ?? throw new ArgumentException("That app no longer exists.");
            if (selected?.Archived == true) throw new InvalidOperationException("Restore this app before using it in chat.");
            // Send one app's recent entries, not the entire filing cabinet, with every greeting.
            var entries = selected?.Entries.TakeLast(40).ToArray() ?? [];
            while (entries.Length > 0 && Wire.Pack(entries).Length > 16000) entries = entries.Skip(1).ToArray();
            return new(ArtifactSummaries().Where(app => !app.Archived).ToArray(), selected == null ? null : selected with { Entries = entries }, selected?.Entries.Length ?? 0, localDate);
        }
    }
    public ArtifactApp EditArtifact(string id, AppEdit edit)
    {
        lock (gate)
        {
            using var transaction = db.BeginTransaction();
            var app = EditArtifactInTransaction(id, edit, "user");
            transaction.Commit(); return app;
        }
    }
    public ArtifactApp RestoreArtifact(string id, AppRestore restore)
    {
        AppId(id); AppId(restore.OperationId);
        lock (gate)
        {
            var digest = Wire.Hash(Wire.Pack(new { id, restore, kind = "restore" }));
            if (RepeatedArtifactOperation(id, restore.OperationId, digest) is { } repeated) return repeated;
            var current = Artifact(id) ?? throw new ArgumentException("App not found.");
            if (current.Version != restore.Version) throw new InvalidOperationException("The app changed. Refresh before restoring a revision.");
            var target = ArtifactRevisions(id).FirstOrDefault(revision => revision.Snapshot.Version == restore.TargetVersion)
                ?? throw new ArgumentException("That revision is no longer retained.");
            var app = target.Snapshot with { Version = Guid.NewGuid().ToString("N"), Updated = DateTimeOffset.UtcNow };
            using var transaction = db.BeginTransaction();
            WriteArtifact(app, restore.OperationId, digest, "Restored a previous revision", "user");
            transaction.Commit(); return app;
        }
    }
    public void CompleteArtifactConversation(Run run, string id, AppEdit edit)
    {
        lock (gate)
        {
            var previousVersion = run.Version;
            try
            {
                using var transaction = db.BeginTransaction();
                var app = EditArtifactInTransaction(id, edit, "chat");
                var receipt = ArtifactRevisions(id).Single(revision => revision.Id == edit.OperationId);
                run.ArtifactResult = new(app.Id, app.Version, receipt.Description, true);
                run.State = RunState.Succeeded; run.Summary = receipt.Description;
                run.DraftText = edit.Version == "absent" ? $"Created **{app.Definition.Title}**. Your app is ready; tell me what you would like to add or change."
                    : $"Updated **{app.Definition.Title}**. {receipt.Description}. You can undo this from the app's history.";
                run.Validation = new(true, ["App schema and values validated", "Expected version matched", "App, revision and chat receipt saved together"], ["User-supplied values are not independently verified"]);
                SaveRunInTransaction(run, "artifact.conversation.completed", run.ArtifactResult, new(run.Id + "-assistant", "assistant", run.DraftText, DateTimeOffset.UtcNow));
                testFault?.Invoke("before-artifact-conversation-commit");
                transaction.Commit();
            }
            catch { run.Version = previousVersion; run.ArtifactResult = null; throw; }
        }
    }
    private ArtifactApp EditArtifactInTransaction(string id, AppEdit edit, string source)
    {
        AppId(id); AppId(edit.OperationId);
        var digest = Wire.Hash(Wire.Pack(new { id, edit, source }));
        if (RepeatedArtifactOperation(id, edit.OperationId, digest) is { } repeated) return repeated;
        var current = Artifact(id);
        if ((current?.Version ?? "absent") != edit.Version) throw new InvalidOperationException("The app changed in another window or message. Refresh before making this change.");
        if (current == null && Artifacts().Length >= 32) throw new InvalidOperationException("This study supports up to 32 apps.");
        var definition = edit.Definition ?? current?.Definition ?? throw new ArgumentException("A new app needs a definition.");
        ValidateAppDefinition(definition);
        var upserts = edit.Upserts ?? []; var deleteIds = edit.DeleteIds ?? [];
        if (upserts.Length + deleteIds.Length > 100) throw new ArgumentException("Change at most 100 entries at a time.");
        var entries = (current?.Entries ?? []).ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var touched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entryId in deleteIds)
        {
            AppId(entryId);
            if (!touched.Add(entryId) || !entries.Remove(entryId)) throw new ArgumentException("An entry to remove is missing or listed twice.");
        }
        foreach (var entry in upserts)
        {
            if (entry == null) throw new ArgumentException("An entry cannot be empty.");
            var entryId = string.IsNullOrEmpty(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id;
            AppId(entryId);
            if (!touched.Add(entryId)) throw new ArgumentException("Change each entry only once per request.");
            entries[entryId] = entry with { Id = entryId };
        }
        if (entries.Count > 1000) throw new ArgumentException("An app supports up to 1,000 entries. Export your data before trimming it.");
        foreach (var entry in entries.Values) ValidateAppEntry(definition, entry);
        var now = DateTimeOffset.UtcNow;
        var app = new ArtifactApp(id, definition, entries.Values.ToArray(), Guid.NewGuid().ToString("N"), current?.Created ?? now, now, edit.Archived ?? current?.Archived ?? false);
        if (Wire.Pack(app).Length > 250_000) throw new ArgumentException("This app exceeds the saved-data limit. Use shorter entries.");
        var description = current == null ? "Created an app" : edit.Archived != null ? app.Archived ? "Archived the app" : "Restored the app"
            : $"Saved {upserts.Length} entr{(upserts.Length == 1 ? "y" : "ies")}, removed {deleteIds.Length}" + (edit.Definition != null ? ", updated the layout" : "");
        WriteArtifact(app, edit.OperationId, digest, description, source); return app;
    }
    private ArtifactApp? RepeatedArtifactOperation(string id, string operationId, string digest)
    {
        var previous = Query("SELECT body FROM artifact_revisions WHERE id=$id", ("$id", operationId)).Select(Wire.Unpack<AppRevision>).SingleOrDefault();
        if (previous == null) return null;
        if (previous.ArtifactId != id || previous.RequestDigest != digest) throw new InvalidOperationException("This operation ID was already used for a different change.");
        return previous.Snapshot;
    }
    private void WriteArtifact(ArtifactApp app, string operationId, string digest, string description, string source)
    {
        Exec("INSERT INTO artifact_apps VALUES($id,$body) ON CONFLICT(id) DO UPDATE SET body=$body", ("$id", app.Id), ("$body", Wire.Pack(app)));
        var revision = new AppRevision(operationId, app.Id, digest, description, source, app.Updated, app);
        Exec("INSERT INTO artifact_revisions VALUES($id,$artifact,$body)", ("$id", revision.Id), ("$artifact", app.Id), ("$body", Wire.Pack(revision)));
        Exec("DELETE FROM artifact_revisions WHERE artifactId=$id AND rowid NOT IN (SELECT rowid FROM artifact_revisions WHERE artifactId=$id ORDER BY rowid DESC LIMIT 20)", ("$id", app.Id));
        Setting("artifact-revision", Guid.NewGuid().ToString("N"));
        testFault?.Invoke("before-artifact-commit");
    }
    private static void AppId(string id)
    {
        if (id == null || !Regex.IsMatch(id, "\\A[a-f0-9]{32}\\z")) throw new ArgumentException("Invalid app, entry or operation ID.");
    }
    public static void ValidateAppDefinition(AppDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Title) || definition.Title.Length > 80 || definition.Description == null || definition.Description.Length > 400)
            throw new ArgumentException("Give the app a title up to 80 characters and a description up to 400 characters.");
        if (definition.Fields == null || definition.Fields.Length is < 1 or > 12 || definition.Summaries == null || definition.Summaries.Length > 6)
            throw new ArgumentException("Use 1–12 fields and up to six summaries.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in definition.Fields)
        {
            if (field == null || field.Key == null || !Regex.IsMatch(field.Key, "\\A[a-z][a-z0-9_]{0,31}\\z") || !keys.Add(field.Key) ||
                string.IsNullOrWhiteSpace(field.Label) || field.Label.Length > 60 || field.Unit?.Length > 20 || field.Kind is not ("text" or "number" or "date" or "checkbox" or "select"))
                throw new ArgumentException("Use unique field keys and supported text, number, date, checkbox or select fields.");
            if (field.Kind == "select" && (field.Options == null || field.Options.Length is < 1 or > 12 || field.Options.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 80) || field.Options.Distinct().Count() != field.Options.Length))
                throw new ArgumentException("A select field needs 1–12 distinct choices.");
            if (field.Kind != "select" && field.Options is { Length: > 0 }) throw new ArgumentException("Only select fields have choices.");
        }
        if (definition.Summaries.Distinct().Count() != definition.Summaries.Length || definition.Summaries.Any(key => !definition.Fields.Any(field => field.Key == key && field.Kind is "number" or "checkbox")))
            throw new ArgumentException("Summaries must name number or checkbox fields.");
        if (definition.DateField != null && !definition.Fields.Any(field => field.Key == definition.DateField && field.Kind == "date")) throw new ArgumentException("The day filter must name a date field.");
    }
    private static void ValidateAppEntry(AppDefinition definition, AppEntry entry)
    {
        if (entry.Values == null || entry.Values.Count == 0 || entry.Values.Keys.Any(key => !definition.Fields.Any(field => field.Key == key))) throw new ArgumentException("Entry values must use this app's fields.");
        foreach (var field in definition.Fields)
        {
            if (!entry.Values.TryGetValue(field.Key, out var value) || value.ValueKind == JsonValueKind.Null) continue;
            var valid = field.Kind switch
            {
                "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && number is >= -1_000_000_000m and <= 1_000_000_000m,
                "checkbox" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "text" => value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 1000,
                "select" => value.ValueKind == JsonValueKind.String && field.Options!.Contains(value.GetString()),
                "date" => value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                _ => false
            };
            if (!valid) throw new ArgumentException($"Invalid value for {field.Label}.");
        }
    }
}
