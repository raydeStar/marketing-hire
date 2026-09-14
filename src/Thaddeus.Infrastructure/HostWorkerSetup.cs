using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed record HostWorkerCandidate(string Backend, string Name, string InstallationDigest, bool DevelopmentOnly);
public sealed record HostWorkerCheck(string InstallationDigest, DateTimeOffset CheckedAt, bool Passed, string Summary, BoundaryCheck[] Checks);
public sealed record HostWorkerEnrollment(string InstallationDigest, bool Enabled, DateTimeOffset ChangedAt);
public sealed record HostWorkerSetupView(HostWorkerCandidate? Worker, bool Enabled, bool CanEnable, string Status, string Summary, HostWorkerCheck? LastCheck);

/// <summary>The owner can enable only the installation supplied by host configuration, never an arbitrary browser path.</summary>
public sealed class HostWorkerSetup(Store store, HostWorkerCandidate? candidate = null, IResearchWorkerFactory? backend = null,
    Func<CancellationToken, Task<SandboxInspection>>? inspect = null, string? unavailable = null) : IResearchWorkerFactory
{
    private HostWorkerEnrollment? Enrollment => store.Setting("host-worker-enrollment") is { } raw ? Wire.Unpack<HostWorkerEnrollment>(raw) : null;
    private HostWorkerCheck? LastCheck => store.Setting("host-worker-check") is { } raw ? Wire.Unpack<HostWorkerCheck>(raw) : null;
    private bool Enabled => candidate != null && backend != null && Enrollment is { Enabled: true } saved && saved.InstallationDigest == candidate.InstallationDigest;
    private bool Fresh(HostWorkerCheck? check) => candidate != null && check is { Passed: true } && check.InstallationDigest == candidate.InstallationDigest &&
        check.CheckedAt <= DateTimeOffset.UtcNow && DateTimeOffset.UtcNow - check.CheckedAt < TimeSpan.FromMinutes(10);
    public HostWorkerSetupView View
    {
        get
        {
            var enabled = Enabled; var check = LastCheck;
            return new(candidate, enabled, !enabled && backend != null && Fresh(check),
                candidate == null ? "installation-required" : enabled ? "enabled" : Fresh(check) ? "ready-to-enable" : "check-required",
                candidate == null ? unavailable ?? "No worker package is configured on this host. Ordinary chat remains available."
                    : enabled ? "Preview research is enabled on this host. Each task keeps its own limits and exact import approvals."
                    : "Check the installed worker, then enable research on this host.", check?.InstallationDigest == candidate?.InstallationDigest ? check : null);
        }
    }
    public ResearchAvailability Availability => new(Enabled, candidate?.Backend ?? "unconfigured",
        Enabled ? "development-preview" : View.Status, View.Summary, candidate?.DevelopmentOnly ?? false);

    public async Task<HostWorkerSetupView> Check(CancellationToken cancellation)
    {
        if (candidate == null || inspect == null) throw new InvalidOperationException(View.Summary);
        HostWorkerCheck check;
        try
        {
            var report = await inspect(cancellation);
            var passed = report.Backend == candidate.Backend && report.ObservedVersion == report.RequiredVersion &&
                new[] { "pinned-inputs", "runtime-package" }.All(id => report.Checks.Any(item => item.Id == id && item.State == CheckState.Passed));
            check = new(candidate.InstallationDigest, DateTimeOffset.UtcNow, passed,
                passed ? "Installed files verified. This worker is available for the development preview. No VM or model was started."
                       : "The installed package did not pass its checks. Research remains unavailable.", report.Checks.ToArray());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            check = new(candidate.InstallationDigest, DateTimeOffset.UtcNow, false,
                "The installed worker could not be verified. Repair its host installation before trying again. No model was started.", []);
        }
        store.Setting("host-worker-check", Wire.Pack(check));
        if (!check.Passed) store.Setting("host-worker-enrollment", Wire.Pack(new HostWorkerEnrollment(candidate.InstallationDigest, false, DateTimeOffset.UtcNow)));
        return View;
    }
    public HostWorkerSetupView SetEnabled(string installationDigest, bool enabled)
    {
        if (candidate == null || installationDigest != candidate.InstallationDigest)
            throw new InvalidOperationException("The installed worker changed. Refresh setup and check it again.");
        if (enabled && (backend == null || !Fresh(LastCheck))) throw new InvalidOperationException("Check this installed worker before enabling it.");
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
