using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Runtime
{
    // True means the app was read for one remaining reply, not that the requested work is done.
    private bool HandleAppAction(Run run, ToolRequest action)
    {
        var context = run.ArtifactContext ?? throw new ArgumentException("This conversation has no app capability.");
        if (action.Name is not ("artifact_create" or "artifact_update" or "artifact_open" or "artifact_delete") || action.Path != "" || action.Content == null)
            throw new ArgumentException("Conversation can only use its advertised app capabilities.");
        if (run.ToolCalls >= run.Goal.Limits.ToolCalls) throw new BudgetException("App-action allowance exhausted. No app was changed.");
        if (context.Continuing && action.Name is not ("artifact_update" or "artifact_delete")) throw new ArgumentException("After selecting an app for this request, finish the requested change or ask a question. A second selection or new app is not authorized.");
        switch (action.Name)
        {
            case "artifact_create":
                var create = ArtifactChatTools.Parse<ArtifactChatTools.Create>(action.Content);
                if (create.Definition == null || create.Entries == null) throw new ArgumentException("Creating an app requires a definition and entries array.");
                ReserveTool(run, action);
                store.CompleteArtifactConversation(run, run.Id, new(run.Id, "absent", create.Definition, create.Entries));
                break;
            case "artifact_update":
                var update = ArtifactChatTools.Parse<ArtifactChatTools.Update>(action.Content);
                if (context.Selected == null || context.Selected.Id != update.ArtifactId || context.Selected.Version != update.Version)
                    throw new ArgumentException("The update must target the selected app and the version read for this message.");
                if (update.Upserts == null || update.DeleteIds == null) throw new ArgumentException("An update needs explicit entry and removal arrays.");
                var visibleIds = context.Selected.Entries.Select(entry => entry.Id).ToHashSet();
                if (update.DeleteIds.Any(id => !visibleIds.Contains(id)) || update.Upserts.Any(entry => entry == null || !string.IsNullOrEmpty(entry.Id) && !visibleIds.Contains(entry.Id)))
                    throw new ArgumentException("An update may only edit or remove entries included in this message's app context.");
                ReserveTool(run, action);
                store.CompleteArtifactConversation(run, update.ArtifactId, new(run.Id, update.Version, update.Definition, update.Upserts, update.DeleteIds));
                break;
            case "artifact_delete":
                var delete = ArtifactChatTools.Parse<ArtifactChatTools.Delete>(action.Content);
                if (context.Selected == null || context.Selected.Id != delete.ArtifactId || context.Selected.Version != delete.Version)
                    throw new ArgumentException("Deletion must target the selected app and the version read for this message.");
                ReserveTool(run, action);
                store.CompleteArtifactConversation(run, delete.ArtifactId, new(run.Id, delete.Version, Archived: true));
                break;
            case "artifact_open":
                var open = ArtifactChatTools.Parse<ArtifactChatTools.Open>(action.Content);
                if (!context.Apps.Any(app => app.Id == open.ArtifactId)) throw new ArgumentException("The app was not in this conversation's catalog.");
                if (open.ContinueTask)
                {
                    if (run.ModelCalls >= Math.Min(2, run.Goal.Limits.ModelCalls) || run.ToolCalls + 1 >= run.Goal.Limits.ToolCalls)
                        throw new BudgetException("The app lookup needs one remaining model call and app action to finish your request. No redesign or data change was made.");
                    var selected = store.ArtifactContext(open.ArtifactId, context.LocalDate);
                    ReserveTool(run, action);
                    run.ArtifactContext = selected with { Apps = context.Apps, Continuing = true };
                    run.ArtifactResult = new(open.ArtifactId, selected.Selected!.Version, "Selected app · no change saved yet", false);
                    run.DraftText = "";
                    run.Summary = "App selected · continuing your request";
                    store.Save(run, "artifact.context.selected", new { artifactId = open.ArtifactId, version = selected.Selected!.Version, continuing = true });
                    return true;
                }
                var app = store.Artifact(open.ArtifactId) ?? throw new ArgumentException("App not found.");
                if (app.Archived) throw new InvalidOperationException("This app was archived after the message was sent.");
                ReserveTool(run, action);
                run.ArtifactResult = new(app.Id, app.Version, "Opened the app · no data changed", false);
                run.State = RunState.Succeeded; run.Summary = run.ArtifactResult.Description;
                run.DraftText = $"Opened **{app.Definition.Title}**. It is selected for our next message; no entries have been changed yet.";
                run.Validation = new(true, ["Opened an app from the admitted catalog"], []);
                store.Save(run, "artifact.opened", run.ArtifactResult, new(run.Id + "-assistant", "assistant", run.DraftText, DateTimeOffset.UtcNow));
                break;
        }
        return false;
    }
}
