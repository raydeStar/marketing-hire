using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Thaddeus.Notifications;

/// <summary>Keep an unpackaged notification identity attached to the current installation.</summary>
[SupportedOSPlatform("windows")]
internal static class NotificationRegistration
{
    internal const string AppId = "raydeStar.Thaddeus";

    internal static bool RefreshActivationTarget(RegistryKey classes, string executable)
    {
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable) || !Path.GetFileName(executable).Equals("Thaddeus.Notifications.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The current notification helper is missing.");

        using var app = classes.OpenSubKey(@"AppUserModelId\" + AppId);
        if (app?.GetValue("CustomActivator") is not string registered || !Guid.TryParseExact(registered, "B", out var activator))
            throw new InvalidOperationException("Windows did not register Thaddeus' notification activator.");

        using var server = classes.OpenSubKey(@"CLSID\" + activator.ToString("B") + @"\LocalServer32", writable: true);
        if (server == null) throw new InvalidOperationException("The registered notification activation target is missing.");

        // The SDK reuses the first CLSID without refreshing its executable after an upgrade.
        // Forward the raven's mail; keep the identity, notification history and owner settings intact.
        var command = "\"" + executable + "\" ----AppNotificationActivated:";
        if (string.Equals(server.GetValue("") as string, command, StringComparison.OrdinalIgnoreCase)) return false;
        server.SetValue("", command, RegistryValueKind.String);
        return true;
    }
}
