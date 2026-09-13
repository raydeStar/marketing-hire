using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public sealed record MaintenanceRequest(string Version, string Mode);
public sealed record MaintenancePlan(string Id, string Mode, string Source, string BackupRoot, string Destination,
    string Origin, string Package, DeviceSession Owner);
public sealed record MaintenanceView(string Phase, string Version, string Source, string BackupRoot, string? Destination,
    string Message, bool CanStart, StudyBackupReceipt? Receipt = null);

/// <summary>Closes admission before asking the host to put down its tray. No task is silently cancelled.</summary>
public sealed class MaintenanceControl : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource closing = new();
    private readonly string version = Guid.NewGuid().ToString("N");
    private int admitted;
    private MaintenancePlan? plan;
    public CancellationToken Closing => closing.Token;
    public MaintenancePlan? Plan { get { lock (gate) return plan; } }

    public IDisposable? Admit(bool observation = false)
    {
        lock (gate)
        {
            if (plan != null && !observation) return null;
            if (!observation) admitted++;
            return new Admission(() => { if (!observation) lock (gate) admitted--; });
        }
    }
    private sealed class Admission(Action release) : IDisposable
    {
        private Action? end = release;
        public void Dispose() => Interlocked.Exchange(ref end, null)?.Invoke();
    }
    public static string? Blocked(IEnumerable<Run> runs) => runs.Any(run => run.ReservedTokens != 0 ||
        run.State is RunState.Running or RunState.Queued || run.Research is { Phase: not "finished" })
        ? "Finish or cancel active work before maintenance. Saved approvals and your existing data will remain intact." : null;
    public MaintenanceView View(Store store)
    {
        lock (gate)
        {
            if (plan != null) return ClosingView(plan);
            var reason = Blocked(store.List());
            return new("ready", version, store.Root, BackupRoot(store.Root), null,
                reason ?? "Close the study safely, with an optional verified backup. No models will run during maintenance.", reason == null);
        }
    }
    public MaintenancePlan Prepare(Store store, DeviceSession owner, MaintenanceRequest request, string origin, string package)
    {
        lock (gate)
        {
            if (!owner.Owner || owner.Revoked || owner.Expires <= DateTimeOffset.UtcNow) throw new InvalidOperationException("A current local owner session is required.");
            if (plan != null || request.Version != version) throw new InvalidOperationException("This host changed. Refresh the maintenance review before proceeding.");
            if (request.Mode is not ("backup" or "stop")) throw new ArgumentException("Choose backup or stop.");
            // The requesting POST owns one admission. In-flight edits, provider checks and worker requests must finish first.
            if (admitted != 1) throw new InvalidOperationException("Another request is still finishing. Wait a moment and retry maintenance.");
            if (Blocked(store.List()) is { } reason) throw new InvalidOperationException(reason);
            var backupRoot = BackupRoot(store.Root); Store.AssertNoLinks(backupRoot);
            var id = Guid.NewGuid().ToString("N");
            plan = new(id, request.Mode, store.Root, backupRoot,
                Path.Combine(backupRoot, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + id), origin, package,
                owner with { Expires = owner.Expires < DateTimeOffset.UtcNow.AddHours(1) ? owner.Expires : DateTimeOffset.UtcNow.AddHours(1) });
            return plan;
        }
    }
    private static string BackupRoot(string source) => Path.TrimEndingDirectorySeparator(source) + "-backups";
    public static MaintenanceView ClosingView(MaintenancePlan value) => new("closing", value.Id, value.Source, value.BackupRoot,
        value.Mode == "backup" ? value.Destination : null, "Closing the study before maintenance. Keep this page open.", false);
    public void CloseStreams() => closing.Cancel();
    public void Dispose() => closing.Dispose();
}
