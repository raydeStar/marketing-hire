using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;
using Thaddeus.Lab;

internal record FrozenFile(string Path, string Sha256);
internal record FrozenNativeInput(string CaseId, string Arm, string ContextHash, string BrokerToolsHash, string PolicyDigest);
internal record NativeRegistration(int SchemaVersion, string Id, DateTimeOffset Created, string SourceRevision, string Repository,
    FrozenFile[] Sources, FrozenFile[] Assemblies, string HostRuntime, string Platform, QemuInstallation Installation,
    ProviderSnapshot Provider, Budget Budget, int CampaignSeconds, NativeLabCase[] Cases, NativeLabItem[] Plan,
    FrozenNativeInput[] Inputs, string DecisionRule)
{
    internal const string Objective = "Read notes/source.md. Ask which workshop audience to use and wait for the answer. Write report.md with '# Workshop brief', a blank line, and one JSON object containing durationMinutes (number), audience, sourcePath and sourceQuote (strings). Preserve the source facts and exact user answer. Then propose the exact content for plans/report.md with captured citations.";
    internal static PolicyProfile Profile(string arm) => arm switch
    {
        "unchanged" => PolicyProfile.NativeUnchecked, "candidate" => PolicyProfile.NativeEvidence,
        _ => throw new ArgumentException("Unknown registered arm.")
    };
    internal static string FileHash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    internal static string Git(string repository, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = repository, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Cannot inspect source revision.");
        var output = process.StandardOutput.ReadToEnd(); var error = process.StandardError.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Git provenance inspection failed: " + error);
        return output.TrimEnd('\r', '\n');
    }
    internal static FrozenFile[] SourceFiles(string repository) => Git(repository, "ls-files", "--cached", "--others", "--exclude-standard", "-z")
        .Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
        .Select(path => new FrozenFile(path, FileHash(System.IO.Path.Combine(repository, path)))).ToArray();
    internal static FrozenFile[] Binaries() => new[] { typeof(NativeRegistration).Assembly, typeof(NativeLabScore).Assembly,
        typeof(Runtime).Assembly, typeof(Run).Assembly, Assembly.Load("Thaddeus.Host") }.Select(assembly => assembly.Location).Distinct()
        .Order(StringComparer.Ordinal).Select(path => new FrozenFile(path, FileHash(path))).ToArray();
    internal static async Task Register(string root, string installationFile)
    {
        if (Directory.Exists(root)) throw new InvalidOperationException("Choose a fresh campaign directory.");
        var repository = Directory.GetCurrentDirectory();
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(installationFile));
        var installation = config.RootElement.GetProperty("installation").Deserialize<QemuInstallation>(Wire.Json)!;
        // The worker adapter validates these same pins again and keeps its input files read-locked while in use.
        foreach (var file in installation.Files) if (FileHash(file.Path) != file.Sha256) throw new IOException("A pinned VM input differs.");
        PrivateWorkerDirectory.Create(root);
        var provider = new ProviderSnapshot("compatible", "scripted-native-lab-v1", "high", "https://model.fixture.invalid/v1");
        var budget = new Budget(ModelCalls: 8, ToolCalls: 16, Seconds: 180, MaxTotalTokens: 96000);
        NativeLabCase[] cases = [
            new("repair", "development", "A fictional workshop lasts 45 minutes. Its audience is undecided.", 45, "Developers", "quotation-and-duration"),
            new("false-success", "negative", "A fictional workshop lasts 30 minutes. Its audience is undecided.", 30, "Beginners", "duration-only")
        ];
        NativeLabItem[] plan = [new("repair-unchanged-0", "repair", "unchanged", 0), new("repair-candidate-0", "repair", "candidate", 0),
            new("repair-candidate-1", "repair", "candidate", 1), new("repair-unchanged-1", "repair", "unchanged", 1),
            new("negative-unchanged-0", "false-success", "unchanged", 0), new("negative-candidate-0", "false-success", "candidate", 0)];
        var inputs = new List<FrozenNativeInput>();
        foreach (var fixture in cases) foreach (var arm in new[] { "unchanged", "candidate" })
        {
            var data = System.IO.Path.Combine(root, "preflight", fixture.Id + "-" + arm); PrivateWorkerDirectory.Create(data);
            using var store = new Store(data); store.Write("notes/source.md", fixture.Note, "absent");
            var profile = Profile(arm);
            var run = new Run { Goal = new(Objective, ["notes/source.md"], "plans/", [], budget, provider, "research"), Profile = profile,
                Execution = new("openclaw", "thaddeus-" + Guid.NewGuid().ToString("N"), "agent:thaddeus:preflight", OpenClawBackend.PinnedVersion) };
            store.Save(run, "lab.preflight", new { workerStarted = false });
            var runtime = new Runtime(store, _ => throw new InvalidOperationException("Preflight cannot infer."), new PlanValidator(), new EvidencePolicy());
            var context = await runtime.PrepareExecutionContext(run.Id, default);
            inputs.Add(new(fixture.Id, arm, context.ContentHash, Wire.Hash(Wire.Pack(runtime.ToolsFor(run.Id))), profile.Digest));
        }
        var manifest = new NativeRegistration(1, "native-lab-protocol-v1", DateTimeOffset.UtcNow, Git(repository, "rev-parse", "HEAD"), repository,
            SourceFiles(repository), Binaries(), System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription, installation, provider, budget, 600, cases, plan, inputs.ToArray(),
            "All six native captures, unchanged repeats, delivered repair feedback and valid-quotation false-success negatives must be observed. Model capacity and general product efficacy remain INCONCLUSIVE. No selective replay or tuning against these public synthetic predicates.");
        await WriteNew(System.IO.Path.Combine(root, "registration.json"), Wire.Pack(manifest));
        Console.WriteLine("Frozen six native protocol runs. Scripted responses only; the ruler precedes the result.");
    }
    internal static async Task WriteNew(string path, string content)
    {
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(file); await writer.WriteAsync(content);
    }
    internal void ValidatePlan()
    {
        NativeLabItem[] expected = [new("repair-unchanged-0", "repair", "unchanged", 0), new("repair-candidate-0", "repair", "candidate", 0),
            new("repair-candidate-1", "repair", "candidate", 1), new("repair-unchanged-1", "repair", "unchanged", 1),
            new("negative-unchanged-0", "false-success", "unchanged", 0), new("negative-candidate-0", "false-success", "candidate", 0)];
        if (SchemaVersion != 1 || Id != "native-lab-protocol-v1" || Wire.Pack(Plan) != Wire.Pack(expected) || Cases.Length != 2 ||
            Cases[0] != new NativeLabCase("repair", "development", "A fictional workshop lasts 45 minutes. Its audience is undecided.", 45, "Developers", "quotation-and-duration") ||
            Cases[1] != new NativeLabCase("false-success", "negative", "A fictional workshop lasts 30 minutes. Its audience is undecided.", 30, "Beginners", "duration-only") ||
            Inputs.Length != 4 || Inputs.Select(input => (input.CaseId, input.Arm)).Distinct().Count() != 4 ||
            Inputs.Any(input => !Cases.Any(fixture => fixture.Id == input.CaseId) || input.Arm is not ("unchanged" or "candidate") ||
                input.PolicyDigest != Profile(input.Arm).Digest || !System.Text.RegularExpressions.Regex.IsMatch(input.ContextHash, "\\A[a-f0-9]{64}\\z") ||
                !System.Text.RegularExpressions.Regex.IsMatch(input.BrokerToolsHash, "\\A[a-f0-9]{64}\\z")))
            throw new InvalidOperationException("The registration is not the supported complete native protocol schedule.");
    }
    internal void Verify()
    {
        ValidatePlan();
        if (System.IO.Path.GetFullPath(Repository) != Directory.GetCurrentDirectory()) throw new InvalidOperationException("Run the campaign from its registered repository.");
        if (SchemaVersion != 1 || Provider != new ProviderSnapshot("compatible", "scripted-native-lab-v1", "high", "https://model.fixture.invalid/v1") ||
            CampaignSeconds != 600 || Budget != new Budget(ModelCalls: 8, ToolCalls: 16, Seconds: 180, MaxTotalTokens: 96000))
            throw new InvalidOperationException("Unsupported native protocol registration.");
        if (Git(Repository, "rev-parse", "HEAD") != SourceRevision || Wire.Pack(SourceFiles(Repository)) != Wire.Pack(Sources) ||
            Wire.Pack(Binaries()) != Wire.Pack(Assemblies) || HostRuntime != System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription ||
            Platform != System.Runtime.InteropServices.RuntimeInformation.OSDescription)
            throw new InvalidOperationException("Sources, binaries or host runtime changed after registration; preserve this campaign and register a new one.");
    }
}
