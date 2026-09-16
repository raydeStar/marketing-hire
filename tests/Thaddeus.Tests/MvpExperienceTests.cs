using System.Text;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class MvpExperienceTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"thaddeus-mvp-"+Guid.NewGuid().ToString("N"));
    private readonly Store store;
    public MvpExperienceTests()=>store=new(root);
    private sealed class Provider(Func<Observation,ModelReply> reply):IModelProvider
    {public Task<ModelReply> Respond(Observation o,Func<string,Task> delta,CancellationToken cancellation)=>Task.FromResult(reply(o));}
    [Fact] public async Task UploadedContentIsAdmittedExplicitlyAndSurvivesReversibleTrash()
    {
        var file=store.AddUpload("../../fictional.md",Encoding.UTF8.GetBytes("A fictional moon garden."));
        Assert.Equal("fictional.md",file.Name);
        var runtime=new Runtime(store,_=>new Provider(o=>{Assert.Equal("A fictional moon garden.",Assert.Single(o.Attachments!).Content);return new(null,"Read the attached note.",30,5);}),new PlanValidator(),new EvidencePolicy());
        var run=runtime.Converse("Read this file",new(),uploadIds:[file.Id]);
        var archived=store.EditUpload(file.Id,new(file.Version,true));
        Assert.Throws<ArgumentException>(()=>store.Attachments([file.Id]));
        await runtime.Execute(run.Id);Assert.Equal(RunState.Succeeded,store.Get(run.Id)!.State);
        Assert.Throws<InvalidOperationException>(()=>store.EditUpload(file.Id,new(file.Version,false)));
        store.EditUpload(file.Id,new(archived.Version,false));
        Assert.Equal("A fictional moon garden.",Assert.Single(store.Attachments([file.Id])).Content);
        Assert.Throws<ArgumentException>(()=>store.Attachments([file.Id,file.Id]));
    }
    [Fact] public void UploadTypeSizeAndTextBoundariesFailBeforeStorage()
    {
        foreach(var name in new[]{"script.svg","page.html","document.pdf","video.mp4","audio.wav"})Assert.Throws<ArgumentException>(()=>store.AddUpload(name,[1,2,3]));
        Assert.Throws<ArgumentException>(()=>store.AddUpload("wrong.png",Encoding.UTF8.GetBytes("not an image")));
        Assert.Throws<ArgumentException>(()=>store.AddUpload("bad.txt",[255,255]));
        Assert.Throws<ArgumentException>(()=>store.AddUpload("null.txt",[0]));
        Assert.Throws<ArgumentException>(()=>store.AddUpload("large.md",Encoding.UTF8.GetBytes(new string('a',60001))));
        Assert.Throws<ArgumentException>(()=>store.AddUpload("large.png",new byte[Store.MaxUploadBytes+1]));
        Assert.Empty(store.Uploads());
    }
    [Fact] public void FollowUpMetadataSurvivesEditsAndStaleChangesAreRejected()
    {
        var id=Guid.NewGuid().ToString("N");var plan=new TrackingPlan("tracked","weekly",1,5,"walks","Choose a nearby trail",new(2026,9,20));
        var edit=new LibraryEdit("todo","Walks","Fictional habit","open",null,null,"absent",plan);
        var item=store.EditLibrary(id,edit);Assert.Equal(plan,item.Tracking);
        var changed=store.EditLibrary(id,edit with {Version=item.Version,Tracking=plan with {Current=2,LastCheckIn=new(2026,9,15)}});
        Assert.Equal(2,changed.Tracking!.Current);
        Assert.Throws<InvalidOperationException>(()=>store.EditLibrary(id,edit with {Version=item.Version}));
        Assert.Throws<ArgumentException>(()=>store.EditLibrary(id,edit with {Version=changed.Version,Tracking=plan with {Cadence="hourly"}}));
        Assert.Equal(changed,Assert.Single(store.Library()));
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task IdeaGenerationRequiresASaveAndNeverExecutesTheSuggestedWork(bool save)
    {
        var suggestions=new[]{new IdeaSuggestion("A reading journal","Keep notes as you read","Reading","Build a reading journal")};
        var runtime=new Runtime(store,_=>new Provider(o=>{Assert.True(o.SuggestIdeas);return new(save?new ToolRequest("ideas_save","",Wire.Pack(new IdeaBatch(suggestions))):null,"I saved ideas",20,10);}),new PlanValidator(),new EvidencePolicy());
        var run=runtime.Converse("Suggest ideas",new(),suggestIdeas:true);await runtime.Execute(run.Id);
        Assert.Equal(save?RunState.Succeeded:RunState.Failed,store.Get(run.Id)!.State);
        Assert.Equal(save?1:0,store.Library().Length);Assert.Empty(store.Artifacts());
        if(save){var item=Assert.Single(store.Library());Assert.Equal("Reading",item.Category);Assert.Equal("Build a reading journal",item.Prompt);}
    }
    [Fact] public void TemporarySearchReservationsShareTheLimitWithoutSavingQueryData()
    {
        store.SetSearchBudget(new(store.SearchBudget().Version,2));store.ReserveTemporarySearch();store.ReserveTemporarySearch();
        Assert.Equal(2,store.SearchBudget().Used);Assert.Throws<InvalidOperationException>(store.ReserveTemporarySearch);
        Assert.Empty(store.AllEvents());Assert.Empty(store.Chats());Assert.Empty(store.List());
        store.SetSearchBudget(new(store.SearchBudget().Version,999));Assert.Equal(997,store.SearchBudget().Remaining);
    }
    public void Dispose(){store.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
}
