using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed class DockerSandboxBackend(IHostProcessRunner runner, string executable, Store store) : ISandboxBackend
{
    public const string PinnedVersion = "0.42.1";
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    public static string FindExecutable(string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathFullyQualified(configured)) throw new ArgumentException("Sandbox executable must be an absolute host configuration path.");
            return configured;
        }
        if (OperatingSystem.IsWindows())
        {
            var userInstall = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DockerSandboxes", "bin", "sbx.exe");
            if (File.Exists(userInstall)) return userInstall;
            var machineInstall = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DockerSandboxes", "bin", "sbx.exe");
            if (File.Exists(machineInstall)) return machineInstall;
        }
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (!Path.IsPathFullyQualified(directory)) continue;
            var candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? "sbx.exe" : "sbx");
            if (File.Exists(candidate)) return candidate;
        }
        // An absolute missing path prevents executable lookup in the application's current directory.
        return Path.Combine(AppContext.BaseDirectory, "unconfigured-sandbox-backend", OperatingSystem.IsWindows() ? "sbx.exe" : "sbx");
    }
    private Task<HostProcessResult> Invoke(IReadOnlyList<string> arguments, CancellationToken cancellation, string? input = null, int seconds = 30) =>
        runner.Run(new(executable, arguments, store.Root, TimeSpan.FromSeconds(seconds), input), cancellation);

    public async Task<SandboxInspection> Inspect(CancellationToken cancellation)
    {
        var checks = new List<BoundaryCheck>(); string? observed = null;
        SandboxInspection Report(string status, string summary) => new("docker-sandboxes", PinnedVersion, observed, DateTimeOffset.UtcNow, status, summary, checks);
        var version = await Invoke(["version"], cancellation);
        if (!version.Succeeded)
        {
            checks.Add(new("installation", CheckState.Failed, "The sandbox command could not be run. Install the supported package on the host."));
            return Report("installation-required", "Install Docker Sandboxes on the computer that hosts Thaddeus.");
        }
        var match = Regex.Match(version.Output.Trim(), @"\Asbx version: v(?<version>[0-9]+\.[0-9]+\.[0-9]+)(?:\s|$)");
        observed = match.Success ? match.Groups["version"].Value : null;
        checks.Add(new("installation", CheckState.Passed, "The sandbox executable responded."));
        checks.Add(new("version", observed == PinnedVersion ? CheckState.Passed : CheckState.Failed,
            observed == PinnedVersion ? "Pinned adapter version matches." : "The installed CLI does not match the version this adapter supports."));
        if (observed != PinnedVersion) return Report("version-mismatch", $"This build requires Docker Sandboxes {PinnedVersion}.");
        var inventory = await Invoke(["ls", "--json"], cancellation);
        if (!inventory.Succeeded)
        {
            var authentication = (inventory.Output + inventory.Error).Contains("Not authenticated to Docker", StringComparison.OrdinalIgnoreCase);
            checks.Add(new("authentication", authentication ? CheckState.Failed : CheckState.Unverified,
                authentication ? "Complete Docker sign-in on the host, then check again." : "Docker sign-in could not be verified."));
            checks.Add(new("service", CheckState.Unverified, "The sandbox service did not return an inventory."));
            return Report(authentication ? "sign-in-required" : "service-unavailable",
                authentication ? "Sign in to Docker on the host." : "The sandbox service is unavailable. Check host setup.");
        }
        try
        {
            using var json = JsonDocument.Parse(inventory.Output);
            if (json.RootElement.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object)) throw new JsonException();
        }
        catch (JsonException)
        {
            checks.Add(new("service", CheckState.Failed, "The CLI returned an unrecognized inventory response."));
            return Report("protocol-mismatch", "Sandbox inventory could not be interpreted safely.");
        }
        checks.Add(new("authentication", CheckState.Passed, "Docker accepted the authenticated inventory request."));
        checks.Add(new("service", CheckState.Passed, "The sandbox service returned an inventory."));
        var images = await Invoke(["template", "ls", "--json"], cancellation);
        if (!images.Succeeded)
        {
            checks.Add(new("image-service", CheckState.Failed, "Sandbox management responds, but its image service is unavailable. This is a host runtime issue, not an account-tier requirement."));
            return Report("image-service-unavailable", "Docker's sandbox image service needs attention before a worker can start.");
        }
        checks.Add(new("image-service", CheckState.Passed, "The sandbox image service returned its inventory."));
        foreach (var id in new[] { "vm-boundary", "private-workspace", "network-confinement", "no-shared-skills", "resource-limits", "artifact-transfer", "restart-recovery" })
            checks.Add(new(id, CheckState.Unverified, "A disposable worker must demonstrate this boundary before agent execution is enabled."));
        return Report("qualification-required", "Sandbox service connected. Worker isolation still needs qualification.");
    }

    public static void ValidateId(string id)
    {
        if (!Regex.IsMatch(id, @"\Athaddeus-[a-f0-9]{32}\z")) throw new ArgumentException("Sandbox ID must be issued by Thaddeus.");
    }
    public static void ValidateSpec(SandboxSpec spec)
    {
        ValidateId(spec.Id);
        if (!Regex.IsMatch(spec.Image, @"\A[a-z0-9][a-z0-9./:_-]*@sha256:[a-f0-9]{64}\z")) throw new ArgumentException("Sandbox images must be pinned by SHA-256 digest.");
        if (spec.Cpus is < 1 or > 4 || spec.MemoryMiB is < 1024 or > 8192) throw new ArgumentException("Worker resources exceed the initial single-worker limits.");
    }
    public async Task Create(SandboxSpec spec, CancellationToken cancellation)
    {
        ValidateSpec(spec);
        await lifecycle.WaitAsync(cancellation);
        try
        {
            if (store.Setting("sandbox:" + spec.Id) != null) throw new InvalidOperationException("Worker ID is already registered. Reconcile it before creating another worker.");
            if (store.Setting("active-sandbox") is { } active && Registration(active).Status != "removed")
                throw new InvalidOperationException("This host permits one worker. Stop and remove the previous worker before provisioning another.");
            var report = await Inspect(cancellation);
            if (report.Status != "qualification-required") throw new InvalidOperationException(report.Summary);
            store.Setting("sandbox:" + spec.Id, Wire.Pack(new SandboxRegistration(spec, "creation-unknown", DateTimeOffset.UtcNow)));
            store.Setting("active-sandbox", spec.Id);
            // Never use `sbx run` here: its implicit current-directory mount would hand over the silverware.
            var result = await Invoke(["create", "shell", "--name", spec.Id, "--template", spec.Image,
                "--cpus", spec.Cpus.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--memory", spec.MemoryMiB.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m",
                "--deny-network", "**", "--no-share-skills"], cancellation, seconds: 600);
            RequireSuccess(result, "Sandbox creation was not confirmed. Inspect the recorded worker ID before retrying.");
            SetStatus(spec.Id, "created-unqualified");
        }
        finally { lifecycle.Release(); }
    }
    public async Task Stop(string id, CancellationToken cancellation)
    { Registration(id); RequireSuccess(await Invoke(["stop", id], cancellation, seconds: 90), "Sandbox stop was not confirmed."); SetStatus(id, "stopped"); }
    public async Task ReconcileAbsentCreation(string id, CancellationToken cancellation)
    {
        await lifecycle.WaitAsync(cancellation);
        try
        {
            if (Registration(id).Status != "creation-unknown") throw new InvalidOperationException("Only an unconfirmed creation can be reconciled as absent.");
            var result = await Invoke(["ls", "--json"], cancellation);
            RequireSuccess(result, "Cannot reconcile without a current sandbox inventory.");
            using var parsed = JsonDocument.Parse(result.Output);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object || !parsed.RootElement.TryGetProperty("sandboxes", out var rows) ||
                rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != 0)
                throw new InvalidOperationException("This conservative reconciliation requires an empty sandbox inventory. Inspect any existing workers explicitly.");
            SetStatus(id, "removed");
            store.Setting("sandbox-absence:" + id, Wire.Pack(new { observedAt = DateTimeOffset.UtcNow, inventoryHash = Wire.Hash(result.Output), reason = "Current empty inventory confirms no creation remains" }));
        }
        finally { lifecycle.Release(); }
    }
    public async Task Remove(string id, CancellationToken cancellation)
    { Registration(id); RequireSuccess(await Invoke(["rm", id], cancellation, seconds: 90), "Sandbox removal was not confirmed."); SetStatus(id, "removed"); }
    public async Task<SandboxCommandResult> Execute(string id, IReadOnlyList<string> command, string? input, CancellationToken cancellation)
    {
        var registration = Registration(id);
        if (registration.Status == "removed") throw new InvalidOperationException("This worker was removed.");
        if (command.Count == 0 || command.Count > 128 || command.Any(s => s.Length > 100_000 || s.Contains('\0')) || input?.Length > 200_000)
            throw new ArgumentException("Invalid worker command envelope.");
        var args = new List<string> { "exec", "-i", "--workdir", "/home/agent", id };
        args.AddRange(command);
        var result = await Invoke(args, cancellation, input, seconds: 600);
        if (result.Failure != null) throw new InvalidOperationException("Worker command interrupted. Its effects require inspection before retrying.");
        return new(result.ExitCode ?? -1, result.Output, result.Error);
    }
    public static void ValidateArtifactPath(string path)
    {
        if (!Regex.IsMatch(path, @"\A[a-z0-9][a-z0-9-]{0,90}\.(?:md|txt|json)\z"))
            throw new ArgumentException("Artifact must be a lowercase filename ending in .md, .txt or .json, for example draft.md. Use letters, digits and hyphens; no folders, spaces or titles.");
    }
    public async Task PutText(string id, string path, string content, CancellationToken cancellation)
    {
        ValidateArtifactPath(path);
        if (Encoding.UTF8.GetByteCount(content) > 100_000) throw new ArgumentException("Text artifact exceeds 100 KB.");
        var payload = Wire.Pack(new { path, content });
        var result = await Execute(id, ["python3", "-c", PutTextProgram], payload, cancellation);
        if (result.ExitCode != 0 || result.Output.Trim() != Wire.Hash(content)) throw new InvalidOperationException("Worker text copy was not verified.");
    }
    public async Task<SandboxText> GetText(string id, string path, CancellationToken cancellation)
    {
        ValidateArtifactPath(path);
        var result = await Execute(id, ["python3", "-c", GetTextProgram], Wire.Pack(new { path }), cancellation);
        if (result.ExitCode != 0) throw new InvalidOperationException("Worker text could not be read safely.");
        SandboxText text;
        try { text = Wire.Unpack<SandboxText>(result.Output); }
        catch (JsonException) { throw new InvalidOperationException("Worker returned an invalid text artifact."); }
        if (text == null || text.Path != path || text.Content == null || Encoding.UTF8.GetByteCount(text.Content) > 100_000 || Wire.Hash(text.Content) != text.Sha256)
            throw new InvalidOperationException("Worker text hash or path does not match.");
        return text;
    }
    private static void RequireSuccess(HostProcessResult result, string message)
    { if (!result.Succeeded) throw new InvalidOperationException(message); }
    private SandboxRegistration Registration(string id)
    {
        ValidateId(id);
        var saved = store.Setting("sandbox:" + id) ?? throw new InvalidOperationException("This host has no registered ownership of that worker.");
        var registration = Wire.Unpack<SandboxRegistration>(saved);
        if (registration.Spec.Id != id) throw new InvalidOperationException("Worker registration does not match its ID.");
        return registration;
    }
    private void SetStatus(string id, string status) => store.Setting("sandbox:" + id, Wire.Pack(Registration(id) with { Status = status, Updated = DateTimeOffset.UtcNow }));

    // No tar extraction or host-path arguments: the untrusted machine returns bounded text only.
    private const string ArtifactPrelude = """
        import os, sys, json, hashlib, stat
        request = json.load(sys.stdin)
        root = '/home/agent/thaddeus-artifacts'
        os.makedirs(root, mode=0o700, exist_ok=True)
        directory = os.open(root, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        """;
    private const string PutTextProgram = ArtifactPrelude + "\n" + """
        body = request['content'].encode('utf-8')
        fd = os.open(request['path'], os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=directory)
        with os.fdopen(fd, 'wb') as target:
            target.write(body)
        print(hashlib.sha256(body).hexdigest())
        """;
    private const string GetTextProgram = ArtifactPrelude + "\n" + """
        fd = os.open(request['path'], os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=directory)
        assert stat.S_ISREG(os.fstat(fd).st_mode), 'Expected a regular text file'
        with os.fdopen(fd, 'rb') as source:
            body = source.read(100001)
        assert len(body) <= 100000, 'Artifact too large'
        print(json.dumps({'path': request['path'], 'content': body.decode('utf-8'), 'sha256': hashlib.sha256(body).hexdigest()}))
        """;
}
public record SandboxRegistration(SandboxSpec Spec, string Status, DateTimeOffset Updated);
