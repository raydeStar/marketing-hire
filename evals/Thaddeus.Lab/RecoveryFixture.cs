using Thaddeus.Core;
using Thaddeus.Infrastructure;

internal static class RecoveryFixture
{
    internal static async Task Create(string directory)
    {
        if (Directory.Exists(directory)) throw new ArgumentException("Use a new disposable directory for the recovery fixture.");
        var armed = false;
        using var store = new Store(directory, stage => { if(armed && stage == "after-projection")throw new IOException("Lab-injected crash after file projection"); });
        store.Write("notes/source.md","# Fictional recovery note\nUnresolved Thursday appointment conflict. Ask which to keep.","absent");
        var runtime = new Runtime(store,_=>new ScriptedProvider(),new PlanValidator(),new EvidencePolicy());
        var run = runtime.Create(new("Reconcile the fictional interrupted write",["notes/source.md"],"plans/",[],new(),new()));
        await runtime.Execute(run.Id);run=store.Get(run.Id)!;
        armed = true;await runtime.Decide(run.Id,run.Approval!.Id,run.Approval.Digest,true);
        await File.WriteAllTextAsync(Path.Combine(directory,"fixture.json"),Wire.Pack(new { run.Id, expectedState="needsAttention", expectedRevisions=1, kind="real Store fault injection after file projection" }));
        Console.WriteLine("Prepared the interrupted-write fixture. The spilled tea is deliberate and confined to this tray.");
    }
}
