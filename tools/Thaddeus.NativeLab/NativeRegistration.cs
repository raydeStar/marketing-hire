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
    FrozenNativeInput[] Inputs, string DecisionRule, string Mode = "scripted")
{
    internal bool ArtifactPilot => Mode == "luna-artifact-pilot";
    internal bool Live => Mode is "luna-pilot" or "luna-artifact-pilot";
    internal static ProviderSnapshot LiveProvider => new("compatible", "gpt-5.6-luna", "high", "http://127.0.0.1:5181/v1");
    internal static Budget LiveBudget => new(ModelCalls: 6, ToolCalls: 16, Seconds: 300, MaxTotalTokens: 96000);
    internal static NativeLabCase[] LiveCases => [new("workshop", "development", "A fictional workshop lasts 45 minutes. Its audience is undecided.", 45, "Developers", "none")];
    internal static NativeLabItem[] LivePlan => [new("live-unchanged-0", "workshop", "unchanged", 0), new("live-candidate-0", "workshop", "candidate", 0)];
    internal static NativeLabItem[] ArtifactPlan => [new("live-artifact-0", "workshop", "artifact", 0)];
    internal const string Objective = "Read notes/source.md. Ask which workshop audience to use and wait for the answer. Write report.md with '# Workshop brief', a blank line, and one JSON object containing durationMinutes (number), audience, sourcePath and sourceQuote (strings). Preserve the source facts and exact user answer. Then propose the exact content for plans/report.md with captured citations.";
    internal const string ArtifactObjective = "Read notes/source.md. Ask which workshop audience to use and wait for the answer. Write report.md with '# Workshop brief', a blank line, and one JSON object containing durationMinutes (number), audience, sourcePath and sourceQuote (strings). Preserve the source facts and exact user answer. Then request import of report.md to plans/report.md with captured citations.";
    internal string TaskObjective => ArtifactPilot ? ArtifactObjective : Objective;
    internal static PolicyProfile Profile(string arm) => arm switch
    {
        "unchanged" => PolicyProfile.NativeUnchecked, "candidate" => PolicyProfile.NativeEvidence,
        "artifact" => PolicyProfile.ArtifactEvidence,
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
    internal static async Task Register(string root, string installationFile, bool live = false, bool artifact = false)
    {
        live |= artifact;
        if (Directory.Exists(root)) throw new InvalidOperationException("Choose a fresh campaign directory.");
        var repository = Directory.GetCurrentDirectory();
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(installationFile));
        var installation = config.RootElement.GetProperty("installation").Deserialize<QemuInstallation>(Wire.Json)!;
        // The worker adapter validates these same pins again and keeps its input files read-locked while in use.
        foreach (var file in installation.Files) if (FileHash(file.Path) != file.Sha256) throw new IOException("A pinned VM input differs.");
        using (await QemuRuntimeLease.Open(installation.RuntimePackage ?? throw new IOException("New native registrations require a full runtime package."),
            installation.Executable, installation.ImageTool, default)) { }
        PrivateWorkerDirectory.Create(root);
        var provider = live ? LiveProvider : new ProviderSnapshot("compatible", "scripted-native-lab-v1", "high", "https://model.fixture.invalid/v1");
        var budget = live ? LiveBudget : new Budget(ModelCalls: 8, ToolCalls: 16, Seconds: 180, MaxTotalTokens: 96000);
        NativeLabCase[] cases = [
            new("repair", "development", "A fictional workshop lasts 45 minutes. Its audience is undecided.", 45, "Developers", "quotation-and-duration"),
            new("false-success", "negative", "A fictional workshop lasts 30 minutes. Its audience is undecided.", 30, "Beginners", "duration-only")
        ];
        NativeLabItem[] plan = [new("repair-unchanged-0", "repair", "unchanged", 0), new("repair-candidate-0", "repair", "candidate", 0),
            new("repair-candidate-1", "repair", "candidate", 1), new("repair-unchanged-1", "repair", "unchanged", 1),
            new("negative-unchanged-0", "false-success", "unchanged", 0), new("negative-candidate-0", "false-success", "candidate", 0)];
        if (live) { cases = LiveCases; plan = LivePlan; await ObserveBridge(root, "bridge-at-registration.json"); }
        if (artifact) plan = ArtifactPlan;
        var inputs = new List<FrozenNativeInput>();
        foreach (var fixture in cases) foreach (var arm in plan.Where(item => item.CaseId == fixture.Id).Select(item => item.Arm).Distinct())
        {
            var data = System.IO.Path.Combine(root, "preflight", fixture.Id + "-" + arm); PrivateWorkerDirectory.Create(data);
            using var store = new Store(data); store.Write("notes/source.md", fixture.Note, "absent");
            var profile = Profile(arm);
            var run = new Run { Goal = new(artifact ? ArtifactObjective : Objective, ["notes/source.md"], "plans/", [], budget, provider, "research"), Profile = profile,
                Execution = new("openclaw", "thaddeus-" + Guid.NewGuid().ToString("N"), "agent:thaddeus:preflight", OpenClawBackend.PinnedVersion) };
            store.Save(run, "lab.preflight", new { workerStarted = false });
            var runtime = new Runtime(store, _ => throw new InvalidOperationException("Preflight cannot infer."), new PlanValidator(), new EvidencePolicy());
            var context = await runtime.PrepareExecutionContext(run.Id, default);
            inputs.Add(new(fixture.Id, arm, context.ContentHash, Wire.Hash(Wire.Pack(runtime.ToolsFor(run.Id))), profile.Digest));
        }
        var manifest = new NativeRegistration(1, artifact ? "native-luna-artifact-pilot-v1" : live ? "native-luna-paired-pilot-v1" : "native-lab-protocol-v1", DateTimeOffset.UtcNow, Git(repository, "rev-parse", "HEAD"), repository,
            SourceFiles(repository), Binaries(), System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription, installation, provider, budget, 600, cases, plan, inputs.ToArray(),
            artifact ? "One contract-2 workflow task, no injected defect or automatic repeat. Accept the same typed Workshop brief report, plain or one json fence, LF or CRLF. Protocol and complete usage must be verified; document predicates are scored independently and must pass for a successful smoke check. Allowance 96000 tokens; no certified remote ceiling, billing total, controlled weights/sampling or CLI internal call count. No comparative efficacy, capacity, repeat, holdout or production-qualification claim."
            : live ? "One matched task in each arm, no injected defect, no automatic repeat. Accept a typed JSON object below the Workshop brief heading, plain or in one json fence. Preserve failures and unknown charges. Combined allowance 192000 tokens; provider bounds, CLI internal request count, model weights and sampling are uncertified. No promotion; model capacity NOT_EVALUATED and efficacy INCONCLUSIVE. Review before any repeat or holdout."
            : "All six native captures, unchanged repeats, delivered repair feedback and valid-quotation false-success negatives must be observed. Model capacity and general product efficacy remain INCONCLUSIVE. No selective replay or tuning against these public synthetic predicates.", artifact ? "luna-artifact-pilot" : live ? "luna-pilot" : "scripted");
        await WriteNew(System.IO.Path.Combine(root, "registration.json"), Wire.Pack(manifest));
        NativeUsage.Publish(root, manifest);
        Console.WriteLine(artifact ? "Frozen one Luna High file-import task; 96,000-token allowance, no automatic repeat. usage.md keeps the purse in view."
            : live ? "Frozen two Luna High tasks; combined allowance 192,000 tokens, no automatic repeat. The purse has a ledger, though the provider ceiling is uncertified."
            : "Frozen six native protocol runs. Scripted responses only; the ruler precedes the result.");
    }
    internal static async Task ObserveBridge(string root, string name)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(5) };
        var text = await client.GetStringAsync(LiveProvider.Endpoint + "/models");
        using var document = JsonDocument.Parse(text);
        if (!document.RootElement.GetProperty("data").EnumerateArray().Any(model => model.GetProperty("id").GetString() == LiveProvider.Model))
            throw new InvalidOperationException("The fixed Luna development bridge is not reporting its registered model.");
        await WriteNew(System.IO.Path.Combine(root, name), Wire.Pack(new { observed = DateTimeOffset.UtcNow, models = document.RootElement.Clone(), inferenceDispatched = false,
            limitations = "Advertised model only; remote weights, sampling, loaded CLI identity and CLI internal calls are not independently certified." }));
    }
    internal static async Task WriteNew(string path, string content)
    {
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(file); await writer.WriteAsync(content);
    }
    internal void ValidatePlan()
    {
        if (Mode is not ("scripted" or "luna-pilot" or "luna-artifact-pilot")) throw new InvalidOperationException("Unsupported native model mode.");
        if (ArtifactPilot)
        {
            if (SchemaVersion != 1 || Id != "native-luna-artifact-pilot-v1" || Wire.Pack(Plan) != Wire.Pack(ArtifactPlan) || Wire.Pack(Cases) != Wire.Pack(LiveCases))
                throw new InvalidOperationException("The registration is not the single artifact-reference pilot.");
            ValidateInputs(); return;
        }
        if (Live)
        {
            if (SchemaVersion != 1 || Id != "native-luna-paired-pilot-v1" || Wire.Pack(Plan) != Wire.Pack(LivePlan) || Wire.Pack(Cases) != Wire.Pack(LiveCases))
                throw new InvalidOperationException("The registration is not the complete Luna pilot schedule.");
            ValidateInputs(); return;
        }
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
    private void ValidateInputs()
    {
        var expected = Plan.Select(item => (item.CaseId, item.Arm)).Distinct().ToArray();
        if (Inputs.Length != expected.Length || Inputs.Select(input => (input.CaseId, input.Arm)).Distinct().Count() != expected.Length ||
            Inputs.Any(input => !expected.Contains((input.CaseId, input.Arm)) ||
                input.PolicyDigest != Profile(input.Arm).Digest || !System.Text.RegularExpressions.Regex.IsMatch(input.ContextHash, "\\A[a-f0-9]{64}\\z") ||
                !System.Text.RegularExpressions.Regex.IsMatch(input.BrokerToolsHash, "\\A[a-f0-9]{64}\\z")))
            throw new InvalidOperationException("The frozen native context or tool inputs are missing or invalid.");
    }
    internal void Verify()
    {
        ValidatePlan();
        if (System.IO.Path.GetFullPath(Repository) != Directory.GetCurrentDirectory()) throw new InvalidOperationException("Run the campaign from its registered repository.");
        if (SchemaVersion != 1 || Provider != (Live ? LiveProvider : new ProviderSnapshot("compatible", "scripted-native-lab-v1", "high", "https://model.fixture.invalid/v1")) ||
            CampaignSeconds != 600 || Budget != (Live ? LiveBudget : new Budget(ModelCalls: 8, ToolCalls: 16, Seconds: 180, MaxTotalTokens: 96000)))
            throw new InvalidOperationException("Unsupported native protocol registration.");
        if (Git(Repository, "rev-parse", "HEAD") != SourceRevision || Wire.Pack(SourceFiles(Repository)) != Wire.Pack(Sources) ||
            Wire.Pack(Binaries()) != Wire.Pack(Assemblies) || HostRuntime != System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription ||
            Platform != System.Runtime.InteropServices.RuntimeInformation.OSDescription)
            throw new InvalidOperationException("Sources, binaries or host runtime changed after registration; preserve this campaign and register a new one.");
    }
}
