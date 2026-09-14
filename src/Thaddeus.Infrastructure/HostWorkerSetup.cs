using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed record HostWorkerCandidate(string Backend, string Name, string InstallationDigest, bool DevelopmentOnly);
public sealed record HostWorkerCheck(string InstallationDigest, DateTimeOffset CheckedAt, bool Passed, string Summary, BoundaryCheck[] Checks);
public sealed record HostWorkerEnrollment(string InstallationDigest, bool Enabled, DateTimeOffset ChangedAt);
public sealed record HostWorkerSetupView(HostWorkerCandidate? Worker, bool Enabled, bool CanEnable, string Status, string Summary, HostWorkerCheck? LastCheck, HostWorkerCheckProgress? Progress = null);

/// <summary>The owner can enable only the installation supplied by host configuration, never an arbitrary browser path.</summary>
public sealed class HostWorkerSetup(Store store, HostWorkerCandidate? candidate = null, IResearchWorkerFactory? backend = null,
    Func<CancellationToken, Action<WorkerVerificationStep>, Task<SandboxInspection>>? inspect = null, string? unavailable = null) : IResearchWorkerFactory
{
    private readonly object checkGate = new();
    private CancellationTokenSource? checkCancellation;
    private HostWorkerCheckProgress? progress;
    private HostWorkerCheckProgress? Progress { get { lock (checkGate) return progress; } }
    private HostWorkerEnrollment? Enrollment => store.Setting("host-worker-enrollment") is { } raw ? Wire.Unpack<HostWorkerEnrollment>(raw) : null;
    private HostWorkerCheck? LastCheck => store.Setting("host-worker-check") is { } raw ? Wire.Unpack<HostWorkerCheck>(raw) : null;
    private bool Enabled => candidate != null && backend != null && Enrollment is { Enabled: true } saved && saved.InstallationDigest == candidate.InstallationDigest;
    private bool Fresh(HostWorkerCheck? check) => candidate != null && check is { Passed: true } && check.InstallationDigest == candidate.InstallationDigest &&
        check.CheckedAt <= DateTimeOffset.UtcNow && DateTimeOffset.UtcNow - check.CheckedAt < TimeSpan.FromMinutes(10);
    public HostWorkerSetupView View
    {
        get
        {
            var enabled = Enabled; var check = LastCheck; var activity = Progress;
            return new(candidate, enabled, activity == null && !enabled && backend != null && Fresh(check),
                candidate == null ? "installation-required" : activity != null ? "checking" : enabled ? "enabled" : Fresh(check) ? "ready-to-enable" : "check-required",
                candidate == null ? unavailable ?? "No worker package is configured on this host. Ordinary chat remains available."
                    : enabled ? "Preview research is enabled on this host. Each task keeps its own limits and exact import approvals."
                    : "Check the installed worker, then enable research on this host.", check?.InstallationDigest == candidate?.InstallationDigest ? check : null, activity);
        }
    }
    public ResearchAvailability Availability => new(Enabled, candidate?.Backend ?? "unconfigured",
        Enabled ? "development-preview" : View.Status, View.Summary, candidate?.DevelopmentOnly ?? false);

    public async Task<HostWorkerSetupView> Check(CancellationToken cancellation)
    {
        if (candidate == null || inspect == null) throw new InvalidOperationException(View.Summary);
        CancellationTokenSource stop;
        lock (checkGate)
        {
            if (checkCancellation != null) throw new InvalidOperationException("A worker check is already running.");
            stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            stop.CancelAfter(TimeSpan.FromMinutes(10));
            checkCancellation = stop;
            progress = new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, new("starting"));
        }
        void Report(WorkerVerificationStep step) { lock (checkGate) { if (progress != null && ReferenceEquals(checkCancellation, stop)) progress = progress with { Step = step }; } }
        HostWorkerCheck check = new(candidate.InstallationDigest, DateTimeOffset.UtcNow, false,
            "Verification has not completed. Check again before enabling research.", []);
        try
        {
            // An interrupted recheck must not revive the previous positive receipt. The raven waits for proof.
            store.Setting("host-worker-check", Wire.Pack(check));
            store.Setting("host-worker-enrollment", Wire.Pack(new HostWorkerEnrollment(candidate.InstallationDigest, false, DateTimeOffset.UtcNow)));
            var report = await inspect(stop.Token, Report);
            stop.Token.ThrowIfCancellationRequested();
            var passed = report.Backend == candidate.Backend && report.ObservedVersion == report.RequiredVersion &&
                new[] { "pinned-inputs", "runtime-package" }.All(id => report.Checks.Any(item => item.Id == id && item.State == CheckState.Passed));
            check = new(candidate.InstallationDigest, DateTimeOffset.UtcNow, passed,
                passed ? "Installed files verified. This worker is available for the development preview. No VM or model was started."
                       : "The installed package did not pass its checks. Research remains unavailable.", report.Checks.ToArray());
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            check = new(candidate.InstallationDigest, DateTimeOffset.UtcNow, false,
                "Verification stopped before it finished. Research remains disabled. You can check again when ready.",
                [new("incomplete", CheckState.Unverified, "The check was cancelled, disconnected or reached its ten-minute limit. No worker or model was started.")]);
        }
        catch (WindowsQemuPathException error)
        {
            check = new(candidate.InstallationDigest, DateTimeOffset.UtcNow, false, error.Message,
                [new("windows-paths", CheckState.Failed, error.Message)]);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            check = new(candidate.InstallationDigest, DateTimeOffset.UtcNow, false,
                "The installed worker could not be verified. Repair its host installation before trying again. No model was started.", []);
        }
        finally
        {
            try { store.Setting("host-worker-check", Wire.Pack(check)); }
            finally { lock (checkGate) { checkCancellation = null; progress = null; stop.Dispose(); } }
        }
        return View;
    }
    public HostWorkerSetupView CancelCheck(string checkId)
    {
        lock (checkGate)
        {
            if (progress?.Id != checkId || checkCancellation == null)
                throw new InvalidOperationException("That worker check is no longer running. Refresh its progress.");
            checkCancellation.Cancel();
        }
        return View;
    }
    public HostWorkerSetupView SetEnabled(string installationDigest, bool enabled)
    {
        if (candidate == null || installationDigest != candidate.InstallationDigest)
            throw new InvalidOperationException("The installed worker changed. Refresh setup and check it again.");
        if (enabled && (Progress != null || backend == null || !Fresh(LastCheck))) throw new InvalidOperationException("Check this installed worker before enabling it.");
        store.Setting("host-worker-enrollment", Wire.Pack(new HostWorkerEnrollment(installationDigest, enabled, DateTimeOffset.UtcNow)));
        return View;
    }
    public IResearchWorker Open(Run run)
    {
        // Cleanup of an existing recorded task remains possible after admission is disabled.
        if (candidate == null || backend == null || run.Research == null || store.Get(run.Id)?.Execution != run.Execution)
            throw new InvalidOperationException("The installed worker cannot open this task.");
        return backend.Open(run);
    }
}
