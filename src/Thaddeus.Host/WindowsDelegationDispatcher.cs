using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

internal interface IWindowsNotificationSink : IDisposable
{
    WindowsNotificationReceipt Show(string title, string message);
}

internal sealed record WindowsNotificationReceipt(string ProviderId, string Mechanism, string Setting, uint NotificationId,
    int ActiveCount, bool RetainedInNotificationCenter);

public sealed class WindowsDelegationDispatcher : IDelegationDispatcher, IDisposable
{
    private readonly Func<IWindowsNotificationSink> createNotifications;
    private readonly Func<bool> supportsNotifications;
    private IWindowsNotificationSink? notifications;

    public WindowsDelegationDispatcher() : this(
        () => new WindowsAppNotificationProcess(Path.Combine(AppContext.BaseDirectory, "Thaddeus.Notifications.exe")),
        OperatingSystem.IsWindows) { }

    internal WindowsDelegationDispatcher(Func<IWindowsNotificationSink> createNotifications, Func<bool> supportsNotifications)
    {
        this.createNotifications = createNotifications;
        this.supportsNotifications = supportsNotifications;
    }

    public Task<DelegationDispatchResult> Dispatch(DelegationJob job, DelegationOccurrence occurrence, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (job.Kind != "reminder") throw new InvalidOperationException("This delegation provider is not configured for that action.");
        if (!job.Action.Payload.TryGetProperty("message", out var property) || property.ValueKind != JsonValueKind.String ||
            property.GetString() is not { Length: > 0 and <= 2000 } message)
            throw new InvalidOperationException("The reviewed reminder payload is invalid.");
        if (!supportsNotifications())
            return Task.FromResult(NotificationFailure("unsupported",
                "Windows app notifications are unavailable on this host."));
        try
        {
            notifications ??= createNotifications();
            var native = notifications.Show(job.Title, message);
            return Task.FromResult(new DelegationDispatchResult("accepted",
                "Windows accepted the notification and the reminder remains unread in Thaddeus.", true, native.ProviderId,
                JsonSerializer.SerializeToElement(new { mechanism = native.Mechanism, accepted = true, setting = native.Setting,
                    notificationId = native.NotificationId, activeCount = native.ActiveCount,
                    retainedInNotificationCenter = native.RetainedInNotificationCenter }, Wire.Json), "accepted"));
        }
        catch (Win32Exception error)
        {
            return Task.FromResult(NotificationFailure("failed",
                "Windows refused the app notification. Open Windows Settings → System → Notifications, allow notifications for Thaddeus, and check Do not disturb. Use Review in Chat to schedule a new reminder only if it is still useful; this occurrence will not fire again automatically.",
                error.NativeErrorCode));
        }
    }

    internal DelegationDispatchResult NotifyResult(DelegationJob job, DelegationDispatchResult result, string message)
    {
        if (!result.ActionSucceeded || result.Quiet) return result;
        if (message.Length is < 1 or > 2000) throw new InvalidOperationException("The notification message is invalid.");
        if (!supportsNotifications())
            return result with
            {
                NotificationStatus = "unsupported",
                NotificationError = "Windows app notifications are unavailable on this host. The unread result remains saved in Thaddeus."
            };
        try
        {
            notifications ??= createNotifications();
            var native = notifications.Show(job.Title, message);
            return result with
            {
                ProviderEvidence = AddNotificationEvidence(result.ProviderEvidence, native),
                NotificationStatus = "accepted",
                NotificationError = null
            };
        }
        catch (Win32Exception error)
        {
            return result with
            {
                ProviderEvidence = AddNotificationEvidence(result.ProviderEvidence, error.NativeErrorCode),
                NotificationStatus = "failed",
                NotificationError = "Windows refused the app notification. Open Windows Settings → System → Notifications, allow notifications for Thaddeus, and check Do not disturb. The unread result remains saved in Thaddeus."
            };
        }
    }

    private static JsonElement AddNotificationEvidence(JsonElement? evidence, WindowsNotificationReceipt receipt)
    {
        var root = EvidenceObject(evidence);
        root["nativeNotification"] = JsonSerializer.SerializeToNode(new
        {
            receipt.Mechanism,
            accepted = true,
            receipt.Setting,
            receipt.NotificationId,
            receipt.ActiveCount,
            receipt.RetainedInNotificationCenter
        }, Wire.Json);
        return JsonSerializer.SerializeToElement(root, Wire.Json);
    }

    private static JsonElement AddNotificationEvidence(JsonElement? evidence, int nativeError)
    {
        var root = EvidenceObject(evidence);
        root["nativeNotification"] = JsonSerializer.SerializeToNode(new
        {
            mechanism = "AppNotificationManager",
            accepted = false,
            error = nativeError
        }, Wire.Json);
        return JsonSerializer.SerializeToElement(root, Wire.Json);
    }

    private static JsonObject EvidenceObject(JsonElement? evidence)
    {
        if (evidence is { ValueKind: JsonValueKind.Object })
            return JsonNode.Parse(evidence.Value.GetRawText())?.AsObject() ?? new JsonObject();
        var root = new JsonObject();
        if (evidence is { } value) root["providerResult"] = JsonNode.Parse(value.GetRawText());
        return root;
    }

    private static DelegationDispatchResult NotificationFailure(string status, string error, int? nativeError = null) =>
        new("accepted", "The reminder was saved as an unread result in Thaddeus, but its Windows notification was not displayed.", true,
            ProviderEvidence: JsonSerializer.SerializeToElement(new { mechanism = "AppNotificationManager", accepted = false, error = nativeError }, Wire.Json),
            NotificationStatus: status, NotificationError: error);

    public void Dispose() => notifications?.Dispose();

    private sealed class WindowsAppNotificationProcess(string executable) : IWindowsNotificationSink
    {
        public WindowsNotificationReceipt Show(string title, string message)
        {
            if (!File.Exists(executable)) throw new Win32Exception(2, "The packaged Windows notification helper is missing.");
            using var process = new Process
            {
                StartInfo = new()
                {
                    FileName = executable,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            if (!process.Start()) throw new Win32Exception("Windows did not start the notification helper.");
            process.StandardInput.Write(JsonSerializer.Serialize(new { title, message }, Wire.Json));
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new Win32Exception(1460, "The Windows notification helper timed out.");
            }
            Task.WaitAll(output, error);
            if (process.ExitCode != 0) throw new Win32Exception(process.ExitCode, Bound(error.Result, 300));
            var receipt = JsonSerializer.Deserialize<NotificationHelperReceipt>(output.Result, Wire.Json);
            if (receipt is null || !receipt.Accepted || receipt.NotificationId == 0 || receipt.ActiveCount < 1 ||
                !receipt.RetainedInNotificationCenter ||
                receipt.Mechanism != "AppNotificationManager" || string.IsNullOrWhiteSpace(receipt.Setting))
                throw new Win32Exception("The Windows notification helper returned an invalid receipt.");
            return new($"windows-app:{receipt.NotificationId}", receipt.Mechanism, receipt.Setting, receipt.NotificationId,
                receipt.ActiveCount, receipt.RetainedInNotificationCenter);
        }

        private static string Bound(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";
        public void Dispose() { }
        private sealed record NotificationHelperReceipt(bool Accepted, string Mechanism, string Setting, uint NotificationId,
            int ActiveCount, bool RetainedInNotificationCenter);
    }
}
