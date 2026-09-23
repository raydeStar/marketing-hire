using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Store
{
    public MyPageSetting MyPage()
    {
        lock (gate) return Setting("my-page") is { } saved ? Wire.Unpack<MyPageSetting>(saved) : new();
    }

    public MyPageSetting EditMyPage(MyPageEdit edit)
    {
        lock (gate)
        {
            if (edit.Mode is not ("today" or "artifact") || (edit.Mode == "today" && edit.ArtifactId != null))
                throw new ArgumentException("Choose Today or one saved app for My page.");
            if (edit.Mode == "artifact" && (edit.ArtifactId == null || Artifact(edit.ArtifactId) is not { Archived: false }))
                throw new ArgumentException("Choose an available app. Restore an archived app before pinning it.");
            if (MyPage().Version != edit.Version)
                throw new InvalidOperationException("My page changed in another window. Reload before changing it again.");
            var saved = new MyPageSetting(edit.Mode, edit.ArtifactId, Guid.NewGuid().ToString("N"));
            Setting("my-page", Wire.Pack(saved));
            return saved;
        }
    }

    public void CompleteMyPageConversation(Run run, MyPageEdit edit)
    {
        lock (gate)
        {
            var previousVersion = run.Version;
            try
            {
                using var transaction = db.BeginTransaction();
                var saved = EditMyPage(edit);
                run.State = RunState.Succeeded;
                run.Summary = saved.Mode == "today" ? "My page shows Today" : "App pinned to My page";
                run.DraftText = saved.Mode == "today" ? "My page now shows **Today**: your tasks and recorded work."
                    : $"Pinned **{Artifact(saved.ArtifactId!)!.Definition.Title}** to My page. You can return to Today whenever you like.";
                run.Validation = new(true, ["My page version matched", "Preference and chat receipt saved together"], []);
                SaveRunInTransaction(run, "my-page.changed", saved, new(run.Id + "-assistant", "assistant", run.DraftText, DateTimeOffset.UtcNow));
                testFault?.Invoke("before-my-page-commit");
                transaction.Commit();
            }
            catch { run.Version = previousVersion; throw; }
        }
    }
}
