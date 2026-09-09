using System.Diagnostics;
using Thaddeus.Core;
using Thaddeus.Infrastructure;
if (args.FirstOrDefault() == "--recovery-fixture") { await RecoveryFixture.Create(args[1]); return; }
if (args.FirstOrDefault() == "--register-live") { await LiveComparison.Register(args[1]); return; }
if (args.FirstOrDefault() == "--live") { await LiveComparison.Run(args[1]); return; }
var output=Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/lab");Directory.CreateDirectory(output);
var revision=Environment.GetEnvironmentVariable("THADDEUS_REVISION") ?? "working-tree-unversioned";
var arms=new[]{("minimal",true,true),("evidence",true,true),("evidence-no-validation",false,true),("evidence-no-detail",true,false)};
var fixtures=new[]{"normal","draft-failure","denied","no-model-budget"};
var results=new List<object>();
foreach(var (arm,validate,journal) in arms)foreach(var fixture in fixtures)
{
    var root=Path.Combine(output,"workspaces",arm+"-"+fixture+"-"+Guid.NewGuid().ToString("N"));
    using var store=new Store(root);
    const string note="# Fictional planning note\nOutline due Thursday 16:00. Family visit Tuesday afternoon. Unresolved: two Thursday 10:00 appointments; ask which to keep.";
    store.Write("notes/source.md",note,"absent");
    var budget=new Budget(ModelCalls:fixture=="no-model-budget"?0:3);
    var goal=new Goal("Create a weekly plan; preserve the unresolved conflict.",["notes/source.md"],"plans/",[new("Exact write","deterministic")],budget,new());
    var rt=new Runtime(store,_=>new ScriptedProvider(),new PlanValidator(),new EvidencePolicy());
    var run=rt.Create(goal,fixture=="draft-failure",arm=="minimal"?"minimal":"evidence",validate,journal);
    var sw=Stopwatch.StartNew();await rt.Execute(run.Id);run=store.Get(run.Id)!;
    if(run.State==RunState.AwaitingApproval){var a=run.Approval!;await rt.Decide(run.Id,a.Id,a.Digest,fixture!="denied");}
    sw.Stop();run=store.Get(run.Id)!;var page=store.Page("plans/weekly-plan.md");
    // Scoring is outside the model-visible workspace. No answer keys in the butler's pockets.
    var contentCheck=page!=null && page.Content.StartsWith("# ",StringComparison.Ordinal)&&page.Content.Contains("notes/source.md",StringComparison.Ordinal)&&page.Content.Contains("unresolved",StringComparison.OrdinalIgnoreCase);
    var expectedStop=fixture is "denied" or "no-model-budget" || (fixture=="draft-failure"&&arm=="minimal");
    var passed=expectedStop?page==null:contentCheck;
    results.Add(new{schemaVersion=1,arm,fixture,fixtureHash=Wire.Hash(note),sourceRevision=revision,provider=goal.Provider,budget,validation=validate,journalDetail=journal,state=run.State,outcomePassed=passed,falseSuccess=run.State==RunState.Succeeded&&!contentCheck,run.ModelCalls,run.ToolCalls,run.Repairs,run.InputTokens,run.OutputTokens,run.Cost,latencyMs=sw.ElapsedMilliseconds,processWorkingSetBytes=Process.GetCurrentProcess().WorkingSet64,resourceMeasurement="shared process snapshot; not isolated peak or GPU usage",events=store.Events(0,run.Id)});
}
var report=new{schemaVersion=1,kind="scripted contract comparison, not live model efficacy",decision="INCONCLUSIVE",reason="Synthetic cases establish instrumentation only; no model-capability or generalization claim.",plannedCases=16,results};
await File.WriteAllTextAsync(Path.Combine(output,"report.json"),Wire.Pack(report));
Console.WriteLine($"Recorded 16 scripted cases in {output}. The measuring tape is working; it is not a trophy.");
