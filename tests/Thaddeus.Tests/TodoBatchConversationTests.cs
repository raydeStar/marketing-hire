using System.Text;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class TodoBatchConversationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-todo-batch-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    private static readonly ProviderSnapshot Profile = new("compatible", "fixture", "high", "http://localhost:1234/v1");

    public TodoBatchConversationTests() => store = new(root);

    private sealed class Model : IModelProvider
    {
        public int Calls;
        public TokenQuote Quote(Observation observation) => new(0, true, "fixture", 0);
        public Task<ModelReply> Respond(Observation observation, Func<string, Task> onDelta, CancellationToken cancellation)
        {
            Calls++;
            var context = Assert.IsType<TodoBatchToolContext>(observation.Todos);
            var source = Assert.Single(context.Sources);
            Assert.True(context.CanPropose);
            Assert.Equal("upload", source.Kind);
            var proposal = new TodoBatchProposal(source.Reference, source.Version,
            [
                new("Send the application", "Apply through the employer portal.", "2026-09-18", null),
                new("Ask about the interview", "Follow up after the application.", null, "The source says next week but gives no exact date.")
            ]);
            return Task.FromResult(new ModelReply(new(TodoBatchConversation.ToolName, "", Wire.Pack(proposal)), null));
        }
    }

    private sealed class Reader : IPublicWebReader
    {
        public int Calls;
        public Task<PublicWebResult> Read(string url, PublicWebScope scope, CancellationToken cancellation)
        {
            Calls++;
            return Task.FromResult(new PublicWebResult(
                new(url, "Fixture checklist", DateTimeOffset.UtcNow, "text/html", 120, "body-hash",
                    "Send the application by September 18. Follow up next week.", "text-hash", false),
                [new(url, 200)]));
        }
    }

    private sealed class WebModel : IModelProvider
    {
        public int Calls;
        public TokenQuote Quote(Observation observation) => new(0, true, "fixture", 0);
        public Task<ModelReply> Respond(Observation observation, Func<string, Task> onDelta, CancellationToken cancellation)
        {
            Calls++;
            if (Calls == 1)
            {
                Assert.Null(observation.Todos);
                var web = Assert.IsType<ConversationWebContext>(observation.Web);
                return Task.FromResult(new ModelReply(new(ConversationWeb.ToolName, "", Wire.Pack(new { url = Assert.Single(web.Urls) })), null));
            }
            var source = Assert.Single(Assert.IsType<TodoBatchToolContext>(observation.Todos).Sources);
            return Task.FromResult(new ModelReply(new(TodoBatchConversation.ToolName, "", Wire.Pack(new TodoBatchProposal(source.Reference, source.Version,
                [new("Send the application", "Apply through the employer portal.", "2026-09-18", null)]))), null));
        }
    }

    private sealed class NoteModel : IModelProvider
    {
        public TokenQuote Quote(Observation observation) => new(0, true, "fixture", 0);
        public Task<ModelReply> Respond(Observation observation, Func<string, Task> onDelta, CancellationToken cancellation)
        {
            var evidence = Assert.Single(observation.Evidence);
            Assert.Equal("notes/checklist.md", evidence.Path);
            Assert.Contains("renew the library card", evidence.Content, StringComparison.OrdinalIgnoreCase);
            var source = Assert.Single(Assert.IsType<TodoBatchToolContext>(observation.Todos).Sources);
            Assert.Equal("saved-note", source.Kind);
            Assert.Equal(evidence.Path, source.Reference);
            Assert.Equal(evidence.Hash, source.Version);
            return Task.FromResult(new ModelReply(new(TodoBatchConversation.ToolName, "", Wire.Pack(new TodoBatchProposal(source.Reference, source.Version,
                [new("Renew the library card", "Use the renewal page named in the saved note.", null, null)]))), null));
        }
    }

    [Fact]
    public async Task UploadedReadingBecomesOneReviewedIdempotentEditableBatch()
    {
        var upload = store.AddUpload("role.txt", Encoding.UTF8.GetBytes("Apply by September 18. Follow up next week."));
        var model = new Model();
        var runtime = new Runtime(store, _ => model, new PlanValidator(), new EvidencePolicy());
        var run = runtime.Converse("Turn this into To-dos.", Profile, uploadIds: [upload.Id]);

        await runtime.Execute(run.Id);
        var review = store.Get(run.Id)!;
        Assert.Equal(RunState.AwaitingApproval, review.State);
        Assert.Equal(TodoBatchConversation.ToolName, review.Approval!.Action.Name);
        Assert.Empty(store.Library());

        var completed = await runtime.Decide(review.Id, review.Approval.Id, review.Approval.Digest, true);
        Assert.Equal(RunState.Succeeded, completed.State);
        Assert.Equal(1, model.Calls);
        var items = store.Library().OrderBy(item => item.Title).ToArray();
        Assert.Equal(2, items.Length);
        Assert.All(items, item => Assert.Equal("todo", item.Kind));
        Assert.Contains(items, item => item.Due == new DateOnly(2026, 9, 18));
        var unresolved = Assert.Single(items, item => item.Due == null);
        Assert.Contains("Unresolved:", unresolved.Content);
        Assert.All(items, item => Assert.Contains("Source: upload:" + upload.Id, item.Content));
        var operation = Assert.Single(store.TodoBatchOperations());
        var replay = store.CreateTodoBatch(operation.Id, new(operation.SourceReference, operation.SourceVersion,
        [
            new("Send the application", "Apply through the employer portal.", "2026-09-18", null),
            new("Ask about the interview", "Follow up after the application.", null, "The source says next week but gives no exact date.")
        ]));
        Assert.Equal(operation.Id, replay.Operation.Id);
        Assert.Equal(2, store.Library().Length);
        Assert.Contains(store.Chats(), message => message.Role == "assistant" && message.Content.Contains("Created 2 editable To-dos"));
    }

    [Fact]
    public async Task DenialAndChangedSourceCreateNothing()
    {
        var upload = store.AddUpload("role.txt", Encoding.UTF8.GetBytes("Apply by September 18. Follow up next week."));
        var model = new Model();
        var runtime = new Runtime(store, _ => model, new PlanValidator(), new EvidencePolicy());
        var denied = runtime.Converse("Turn this into To-dos.", Profile, uploadIds: [upload.Id]);
        await runtime.Execute(denied.Id);
        var review = store.Get(denied.Id)!;
        await runtime.Decide(review.Id, review.Approval!.Id, review.Approval.Digest, false);
        Assert.Equal(RunState.Denied, store.Get(review.Id)!.State);
        Assert.Empty(store.Library());

        var second = runtime.Converse("Turn this into To-dos.", Profile, uploadIds: [upload.Id]);
        await runtime.Execute(second.Id);
        var changed = store.Get(second.Id)!;
        store.EditUpload(upload.Id, new(upload.Version, true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Decide(changed.Id, changed.Approval!.Id, changed.Approval.Digest, true));
        Assert.Empty(store.Library());
    }

    [Fact]
    public async Task PublicLinkIsReadBeforeItsActionsAreProposed()
    {
        const string url = "https://example.org/checklist";
        var reader = new Reader(); var model = new WebModel();
        var runtime = new Runtime(store, _ => model, new PlanValidator(), new EvidencePolicy(), reader);
        var run = runtime.Converse("Read " + url + " and turn it into To-dos.", Profile);

        await runtime.Execute(run.Id);
        var review = store.Get(run.Id)!;
        Assert.Equal(RunState.AwaitingApproval, review.State);
        Assert.Equal(1, reader.Calls);
        Assert.Equal(2, model.Calls);
        Assert.Equal(url, review.Approval!.Action.Path);
        Assert.Empty(store.Library());

        await runtime.Decide(review.Id, review.Approval.Id, review.Approval.Digest, true);
        var item = Assert.Single(store.Library());
        Assert.Equal(url, item.Url);
        Assert.Contains("Source: " + url, item.Content);
        Assert.Equal(2, store.Get(run.Id)!.ToolCalls);
    }

    [Fact]
    public async Task ExplicitSavedNoteBecomesFrozenReviewedSourceAndChangedNoteRefusesApproval()
    {
        var note = store.Write("notes/checklist.md", "Remember to renew the library card.", "absent");
        var runtime = new Runtime(store, _ => new NoteModel(), new PlanValidator(), new EvidencePolicy());
        var run = runtime.Converse("Read notes/checklist.md and add what I need to do to my list.", Profile);

        await runtime.Execute(run.Id);
        var review = store.Get(run.Id)!;
        Assert.Equal(RunState.AwaitingApproval, review.State);
        Assert.Equal("notes/checklist.md", review.Approval!.Action.Path);
        Assert.Empty(store.Library());

        store.Write(note.Path, note.Content + "\nThis note changed after review.", note.Version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Decide(review.Id, review.Approval.Id, review.Approval.Digest, true));
        Assert.Empty(store.Library());
    }

    [Fact]
    public async Task UnchangedSavedNoteCreatesEditableSourceLinkedTodo()
    {
        store.Write("notes/checklist.md", "Remember to renew the library card.", "absent");
        var runtime = new Runtime(store, _ => new NoteModel(), new PlanValidator(), new EvidencePolicy());
        var run = runtime.Converse("Turn notes/checklist.md into a To-do.", Profile);
        await runtime.Execute(run.Id); var review = store.Get(run.Id)!;

        await runtime.Decide(run.Id, review.Approval!.Id, review.Approval.Digest, true);

        var item = Assert.Single(store.Library());
        Assert.Equal("todo", item.Kind);
        Assert.Contains("Source: notes/checklist.md", item.Content);
        Assert.Contains(store.Chats(), message => message.Role == "assistant" && message.Content.Contains("Created 1 editable To-do"));
    }

    [Fact]
    public void InterruptedOperationRecoversWithoutDuplicatingItems()
    {
        store.Dispose();
        var operationId = Guid.NewGuid().ToString("N");
        var sourceVersion = new string('a', 64);
        var proposal = new TodoBatchProposal("https://example.org/list", sourceVersion,
            [new("One bounded task", "Do the supported thing.", null, null)]);
        using (var interrupted = new Store(root, point => { if (point == "before-todo-batch-operation") throw new IOException("fixture interruption"); }))
        {
            Assert.Throws<IOException>(() => interrupted.CreateTodoBatch(operationId, proposal));
            Assert.Single(interrupted.Library());
            Assert.Empty(interrupted.TodoBatchOperations());
        }
        using (var recovered = new Store(root))
        {
            var result = recovered.CreateTodoBatch(operationId, proposal);
            Assert.Single(result.Items);
            Assert.Single(recovered.Library());
            Assert.Single(recovered.TodoBatchOperations());
            Assert.Throws<InvalidOperationException>(() => recovered.CreateTodoBatch(operationId,
                proposal with { Items = [new("Different task", "Different content.", null, null)] }));
        }
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
