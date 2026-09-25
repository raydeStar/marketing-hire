using System.Text;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class EmployeeFilesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "employee-files-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private static EmployeeFileChange Change(string name, string content, int version = 0, bool deleted = false) =>
        new(Guid.NewGuid().ToString("N"), name, version, content, deleted);

    [Fact] public void FilesAreVersionedPerMemberAndDeletionKeepsHistory()
    {
        using var store = new Store(root); var files = new EmployeeFiles(store, new OrganizationDirectory(store));
        var agents = files.Save("marketing-main", Change("AGENTS.md", "# Role\r\nResearch and draft."), "owner");
        Assert.Equal(1, agents.Version);
        Assert.Equal("# Role\nResearch and draft.", agents.Content);
        files.Save("marketing-main", Change("SOUL.md", "Warm and direct."), "owner");
        var edited = files.Save("marketing-main", Change("AGENTS.md", "# Role\nResearch only.", 1), "owner");
        Assert.Equal(2, edited.Version);
        Assert.Throws<InvalidOperationException>(() => files.Save("marketing-main", Change("AGENTS.md", "Stale edit", 1), "owner"));
        Assert.Equal(["AGENTS.md", "SOUL.md"], files.List("marketing-main").Select(file => file.Name));

        files.Save("marketing-main", Change("SOUL.md", "", 1, deleted: true), "owner");
        Assert.Equal(["AGENTS.md"], files.List("marketing-main").Select(file => file.Name));
        Assert.Equal(2, files.History("marketing-main", "soul.md").Length);
        var recreated = files.Save("marketing-main", Change("SOUL.md", "Calm and curious."), "owner");
        Assert.Equal(3, recreated.Version);
    }

    [Fact] public void RequestsReplayAndInputsAreValidated()
    {
        using var store = new Store(root); var files = new EmployeeFiles(store, new OrganizationDirectory(store));
        var change = Change("NOTES.md", "First");
        var saved = files.Save("marketing-main", change, "owner");
        Assert.Equal(saved, files.Save("marketing-main", change, "owner"));
        Assert.Throws<ArgumentException>(() => files.Save("marketing-main", change with { Content = "Different" }, "owner"));
        Assert.Throws<ArgumentException>(() => files.Save("marketing-main", Change("../escape.md", "x"), "owner"));
        Assert.Throws<ArgumentException>(() => files.Save("marketing-main", Change("notes.txt", "x"), "owner"));
        Assert.Throws<ArgumentException>(() => files.Save("marketing-main", Change("BIG.md", new string('a', EmployeeFiles.MaxCharacters + 1)), "owner"));
        Assert.Throws<ArgumentException>(() => files.Save("nobody", Change("AGENTS.md", "x"), "owner"));
        Assert.Throws<ArgumentException>(() => files.List("nobody"));
    }

    [Fact] public void CampaignMediaUploadsAreCheckedAndNeverAttachedToModels()
    {
        using var store = new Store(root);
        var mp4 = new byte[64]; Encoding.ASCII.GetBytes("ftypisom").CopyTo(mp4, 4);
        var video = store.AddUpload("launch.mp4", mp4);
        Assert.Equal("video/mp4", video.MediaType);
        Assert.Equal("image/gif", store.AddUpload("loop.gif", Encoding.ASCII.GetBytes("GIF89a....")).MediaType);
        Assert.Throws<ArgumentException>(() => store.AddUpload("fake.mp4", Encoding.ASCII.GetBytes("not a video at all")));
        Assert.Throws<ArgumentException>(() => store.AddUpload("fake.webm", [1, 2, 3, 4]));
        var large = new byte[Store.MaxUploadBytes + 1]; Encoding.ASCII.GetBytes("ftypisom").CopyTo(large, 4);
        Assert.Equal("video/mp4", store.AddUpload("long.mp4", large).MediaType);
        Assert.Throws<ArgumentException>(() => store.AddUpload("large.png", new byte[Store.MaxUploadBytes + 1]));
        Assert.Throws<ArgumentException>(() => store.Attachments([video.Id]));
    }
}
