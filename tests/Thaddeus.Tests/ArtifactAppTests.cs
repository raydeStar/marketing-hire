using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class ArtifactAppTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-apps-" + Guid.NewGuid().ToString("N"));
    private Store store;
    private bool failCommit;
    public ArtifactAppTests() => store = new(root, point => { if (failCommit && point == "before-artifact-conversation-commit") throw new IOException("Fictional commit failure."); });
    private static string Id() => Guid.NewGuid().ToString("N");
    private static AppEntry Entry(object values, string id = "") => new(id, Wire.Unpack<Dictionary<string, JsonElement>>(Wire.Pack(values)));
    private Runtime Runtime(IModelProvider provider) => new(store, _ => provider, new PlanValidator(), new EvidencePolicy());
    public static AppDefinition Definition(string kind) => kind switch
    {
        "mood" => new("Mood diary", "How today felt", [new("date", "Date", "date"), new("mood", "Mood", "select", Options: ["Good", "Okay", "Low"]), new("notes", "Notes", "text")], [], "date"),
        "todo" => new("Internal list", "My own tasks", [new("task", "Task", "text"), new("done", "Done", "checkbox")], ["done"]),
        _ => new("Food & caffeine", "Only supplied quantities", [new("date", "Date", "date"), new("item", "Item", "text"), new("calories", "Calories", "number", "kcal"), new("caffeine", "Caffeine", "number", "mg")], ["calories", "caffeine"], "date")
    };
    private ArtifactApp Create(string kind = "todo") => store.EditArtifact(Id(), new(Id(), "absent", Definition(kind)));
    private sealed class Provider(Func<Observation, ToolRequest?> action, int output = 40) : IModelProvider
    {
        public List<Observation> Seen { get; } = [];
        public Task<ModelReply> Respond(Observation observation, Func<string, Task> delta, CancellationToken cancellation)
        { Seen.Add(observation); return Task.FromResult(new ModelReply(action(observation), "A fictional model reply", 100, output)); }
    }
    [Theory][InlineData("mood")][InlineData("todo")][InlineData("food")]
    public async Task OrdinaryChatCreatesDifferentAppsWithAnAtomicReceipt(string kind)
    {
        var definition = Definition(kind);
        var provider = new Provider(_ => new("artifact_create", "", Wire.Pack(new { definition, entries = Array.Empty<AppEntry>() })));
        var runtime = Runtime(provider); var run = runtime.Converse("Build the app I described", new()); await runtime.Execute(run.Id);
        var saved = store.Get(run.Id)!; var app = Assert.Single(store.Artifacts());
        Assert.Equal(RunState.Succeeded, saved.State); Assert.Equal(definition.Title, app.Definition.Title); Assert.Empty(app.Entries);
        Assert.Equal(app.Id, saved.ArtifactResult!.Id); Assert.Equal(2, store.Chats().Count); Assert.Single(store.ArtifactRevisions(app.Id));
        Assert.Contains(store.Events(0, run.Id), e => e.Type == "artifact.conversation.completed"); Assert.Equal(140, saved.ChargedTokens); Assert.Equal(1, saved.ToolCalls);
        Assert.Empty(store.Pages()); Assert.Empty(store.Library());
    }
    [Fact] public async Task ManualCheckboxAndChatEditsUseTheSameRecordsAndCanBeRestoredAfterRestart()
    {
        var app = Create(); var id = app.Id;
        app = store.EditArtifact(id, new(Id(), app.Version, Upserts: [Entry(new { task = "Buy tea", done = false })]));
        var before = app.Version; var entry = app.Entries.Single();
        var provider = new Provider(o => new("artifact_update", "", Wire.Pack(new { artifactId = id, version = o.Artifacts!.Selected!.Version,
            upserts = new[] { Entry(new { task = "Buy tea", done = true }, entry.Id) }, deleteIds = Array.Empty<string>() })));
        var runtime = Runtime(provider); var run = runtime.Converse("Mark Buy tea done", new(), artifactId: id); await runtime.Execute(run.Id);
        Assert.True(store.Artifact(id)!.Entries.Single().Values["done"].GetBoolean());
        Assert.Equal(1, provider.Seen[0].Artifacts!.TotalEntries);
        store.Dispose(); store = new(root);
        app = store.Artifact(id)!; Assert.True(app.Entries.Single().Values["done"].GetBoolean());
        var restored = store.RestoreArtifact(id, new(Id(), app.Version, before));
        Assert.False(restored.Entries.Single().Values["done"].GetBoolean()); Assert.NotEqual(before, restored.Version);
        Assert.Contains(store.ArtifactRevisions(id), item => item.Snapshot.Version == app.Version);
    }
    [Fact] public void RetriesDoNotDuplicateEntriesAndConflictingOperationIdsAreRejected()
    {
        var app = Create(); var edit = new AppEdit(Id(), app.Version, Upserts: [Entry(new { task = "Only once", done = false })]);
        var result = store.EditArtifact(app.Id, edit); var repeat = store.EditArtifact(app.Id, edit);
        Assert.Equal(result.Version, repeat.Version); Assert.Single(store.Artifact(app.Id)!.Entries); Assert.Equal(2, store.ArtifactRevisions(app.Id).Length);
        Assert.Throws<InvalidOperationException>(() => store.EditArtifact(app.Id, edit with { Archived = true }));
    }
    [Fact] public async Task AConcurrentManualEditRejectsTheStaleModelChangeWithoutLosingData()
    {
        var app = Create(); var runtime = Runtime(new Provider(o => new("artifact_update", "", Wire.Pack(new { artifactId = app.Id, version = o.Artifacts!.Selected!.Version,
            upserts = new[] { Entry(new { task = "Stale addition", done = false }) }, deleteIds = Array.Empty<string>() }))));
        var run = runtime.Converse("Add something", new(), artifactId: app.Id);
        store.EditArtifact(app.Id, new(Id(), app.Version, Upserts: [Entry(new { task = "Newer manual addition", done = false })]));
        await runtime.Execute(run.Id);
        Assert.Equal(RunState.Failed, store.Get(run.Id)!.State); Assert.Equal("Newer manual addition", store.Artifact(app.Id)!.Entries.Single().Values["task"].GetString());
        Assert.Single(store.Chats()); Assert.Equal(140, store.Get(run.Id)!.ChargedTokens);
    }
    [Fact] public async Task FailedCommitDoesNotLeaveAnAppOrFalseSuccessMessage()
    {
        failCommit = true;
        var runtime = Runtime(new Provider(_ => new("artifact_create", "", Wire.Pack(new { definition = Definition("todo"), entries = Array.Empty<AppEntry>() }))));
        var run = runtime.Converse("Create an app", new()); await runtime.Execute(run.Id);
        Assert.Empty(store.Artifacts()); Assert.Empty(store.ArtifactRevisions()); Assert.Single(store.Chats());
        Assert.Equal(RunState.Failed, store.Get(run.Id)!.State); Assert.Null(store.Get(run.Id)!.ArtifactResult); Assert.Equal(140, store.Get(run.Id)!.ChargedTokens);
    }
    [Fact] public void ContextIncludesOnlySelectedRecentEntriesAndKeepsTheOmittedCount()
    {
        var other = Create(); store.EditArtifact(other.Id, new(Id(), other.Version, Upserts: [Entry(new { task = "Private other app detail" })]));
        var app = Create(); app = store.EditArtifact(app.Id, new(Id(), app.Version, Upserts: Enumerable.Range(0,60).Select(i => Entry(new { task = "Task " + i })).ToArray()));
        var context = store.ArtifactContext(app.Id, "2026-09-15");
        Assert.Equal(60, context.TotalEntries); Assert.Equal(40, context.Selected!.Entries.Length); Assert.DoesNotContain("Private other app detail", Wire.Pack(context));
        Assert.Null(store.ArtifactContext(null, "2026-09-15").Selected);
    }
    [Theory][InlineData("cross-app")][InlineData("missing-entry")][InlineData("no-budget")]
    public async Task AnAppActionCannotEscapeItsAdmittedTargetOrAllowance(string mode)
    {
        var app = Create(); var other = Create();
        var runtime = Runtime(new Provider(o => new("artifact_update", "", Wire.Pack(new { artifactId = mode == "cross-app" ? other.Id : app.Id,
            version = o.Artifacts!.Selected!.Version, upserts = new[] { Entry(new { task = "No" }, mode == "missing-entry" ? Id() : "") }, deleteIds = Array.Empty<string>() }))));
        var run = runtime.Converse("Edit", new(), new(ModelCalls: 1, ToolCalls: mode == "no-budget" ? 0 : 1), app.Id);
        await runtime.Execute(run.Id); Assert.Equal(RunState.Failed, store.Get(run.Id)!.State); Assert.Empty(store.Artifact(app.Id)!.Entries); Assert.Empty(store.Artifact(other.Id)!.Entries);
    }
    [Fact] public async Task ReportedTokenOverrunPreventsAnOtherwiseValidAppWrite()
    {
        var runtime = Runtime(new Provider(_ => new("artifact_create", "", Wire.Pack(new { definition = Definition("todo"), entries = Array.Empty<AppEntry>() })), 5000));
        var run = runtime.Converse("Build", new()); await runtime.Execute(run.Id);
        Assert.Equal(RunState.Failed, store.Get(run.Id)!.State); Assert.Equal(5100, store.Get(run.Id)!.ChargedTokens); Assert.Empty(store.Artifacts());
    }
    [Fact] public void SchemaChangesPreserveCompatibleDataAndRejectUnknownFieldsOrExecutableContent()
    {
        var app = Create(); app = store.EditArtifact(app.Id, new(Id(), app.Version, Upserts: [Entry(new { task = "Keep this", done = true })]));
        var expanded = app.Definition with { Fields = [..app.Definition.Fields, new("notes", "Notes", "text")] };
        app = store.EditArtifact(app.Id, new(Id(), app.Version, expanded)); Assert.Equal("Keep this", app.Entries.Single().Values["task"].GetString());
        Assert.Throws<ArgumentException>(() => store.EditArtifact(app.Id, new(Id(), app.Version, expanded with { Fields = [new("notes", "Notes", "text")] })));
        Assert.Throws<ArgumentException>(() => Store.ValidateAppDefinition(expanded with { Fields = [new("code", "Code", "javascript")] }));
        Assert.Throws<ArgumentException>(() => store.EditArtifact(app.Id, new(Id(), app.Version, Upserts: [Entry(new { script = "not a supported field" })])));
        Assert.Throws<ArgumentException>(() => ArtifactChatTools.Parse<ArtifactChatTools.Open>("{\"artifactId\":\"one\",\"artifactId\":\"two\"}"));
        Assert.Throws<JsonException>(() => ArtifactChatTools.Parse<ArtifactChatTools.Open>("{\"artifactId\":\"one\",\"shell\":\"no\"}"));
    }
    [Fact] public void RevisionRetentionAndArchivingAreBoundedAndReversible()
    {
        var app = Create(); for(var i=0;i<25;i++) app=store.EditArtifact(app.Id,new(Id(),app.Version,Definition:app.Definition with { Description="Revision "+i }));
        Assert.Equal(20,store.ArtifactRevisions(app.Id).Length);
        app=store.EditArtifact(app.Id,new(Id(),app.Version,Archived:true)); Assert.True(app.Archived);
        Assert.Throws<InvalidOperationException>(()=>store.ArtifactContext(app.Id,"2026-09-15"));
        app=store.EditArtifact(app.Id,new(Id(),app.Version,Archived:false)); Assert.False(app.Archived);
    }
    [Fact] public async Task BackupRestoreIncludesAppsAndTheirRevisions()
    {
        var app = Create("mood"); app=store.EditArtifact(app.Id,new(Id(),app.Version,Upserts:[Entry(new { date="2026-09-15",mood="Good",notes="A fictional morning" })]));
        store.Dispose(); var backup=root+"-backup";var destination=root+"-restored";
        try{
            await StudyBackup.Create(root,backup);await StudyBackup.Restore(backup,destination);
            using var restored=new Store(destination);Assert.Equal(app.Version,restored.Artifact(app.Id)!.Version);Assert.Equal(2,restored.ArtifactRevisions(app.Id).Length);
        }finally{if(Directory.Exists(backup))Directory.Delete(backup,true);if(Directory.Exists(destination))Directory.Delete(destination,true);}
    }
    [Fact] public void SchemaFiveUpgradePreservesExistingRows()
    {
        store.Chat(new("old","user","A saved message",DateTimeOffset.UtcNow));store.Dispose();
        using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(root,"ledger.sqlite"),Pooling=false}.ToString())){
            db.Open();using var command=db.CreateCommand();command.CommandText="DROP TABLE artifact_revisions; DROP TABLE artifact_apps; DELETE FROM schema_migrations WHERE version=6; PRAGMA user_version=5;";command.ExecuteNonQuery();
        }
        store=new(root);Assert.Equal("A saved message",store.Chats().Single().Content);Assert.Empty(store.Artifacts());
    }
    public void Dispose(){store.Dispose();if(Directory.Exists(root))Directory.Delete(root,true);}
}

public sealed class ArtifactProviderTests
{
    private sealed class Handler(string result) : HttpMessageHandler
    {
        public string Body="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Body=await request.Content!.ReadAsStringAsync(token);return new(HttpStatusCode.OK){Content=new StringContent(result)};}
    }
    [Fact] public async Task ExistingCompatibleTransportAdvertisesAppToolsAndRetainsActualUsage()
    {
        var arguments=Wire.Pack(new {definition=ArtifactAppTests.Definition("mood"),entries=Array.Empty<AppEntry>()});
        var stream="data: "+Wire.Pack(new { choices=new[]{new{delta=new{tool_calls=new[]{new{index=0,function=new{name="artifact_create",arguments}}}}}}})+"\n\ndata: {\"choices\":[],\"usage\":{\"prompt_tokens\":120,\"completion_tokens\":80}}\n\ndata: [DONE]\n";
        var handler=new Handler(stream);var snapshot=new ProviderSnapshot("compatible","test","high","http://localhost:1234/v1");
        var observation=new Observation(new("Make a mood app",[],"plans/",[],new(),snapshot,"conversation"),[],null,1,[],new([],null,0,"2026-09-15"));
        var provider=new CompatibleProvider(snapshot,null,new HttpClient(handler));var reply=await provider.Respond(observation,_=>Task.CompletedTask,default);
        Assert.Equal("artifact_create",reply.Action!.Name);Assert.Equal(120,reply.InputTokens);Assert.Equal(80,reply.OutputTokens);
        using var body=JsonDocument.Parse(handler.Body);var tools=body.RootElement.GetProperty("tools").EnumerateArray().Select(tool=>tool.GetProperty("function").GetProperty("name").GetString()).ToArray();
        Assert.Contains("artifact_create",tools);Assert.Contains("artifact_open",tools);Assert.DoesNotContain("artifact_update",tools);Assert.DoesNotContain("knowledge_write",tools);
        Assert.False(body.RootElement.GetProperty("parallel_tool_calls").GetBoolean());
    }
}
