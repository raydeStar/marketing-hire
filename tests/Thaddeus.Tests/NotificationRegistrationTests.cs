using Microsoft.Win32;
using Thaddeus.Notifications;

namespace Thaddeus.Tests;

public sealed class NotificationRegistrationTests
{
    [Fact]
    public void NotificationActivationReopensOnlyThePackagedHost()
    {
        var folder = Path.Combine(Path.GetTempPath(), "thaddeus-notification-activation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var host = Path.Combine(folder, "Thaddeus.Host.exe");
        File.WriteAllText(host, "Activation fixture only.");
        try
        {
            Assert.True(NotificationActivation.IsInvocation(["----AppNotificationActivated:open=activity"]));
            Assert.False(NotificationActivation.IsInvocation(["--desktop"]));
            string? launched = null;
            Assert.Equal(0, NotificationActivation.OpenApp(folder, path => { launched = path; return true; }));
            Assert.Equal(Path.GetFullPath(host), launched);
            File.Delete(host);
            Assert.Equal(2, NotificationActivation.OpenApp(folder, _ => true));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void MovingTheHelperRefreshesOnlyItsExistingActivationTarget()
    {
        if (!OperatingSystem.IsWindows()) return;
        var key = @"Software\ThaddeusTests\NotificationRegistration-" + Guid.NewGuid().ToString("N");
        var folder = Path.Combine(Path.GetTempPath(), "thaddeus-notification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var executable = Path.Combine(folder, "Thaddeus.Notifications.exe");
        File.WriteAllText(executable, "Not executable: registration fixture only.");
        try
        {
            using var classes = Registry.CurrentUser.CreateSubKey(key);
            var activator = Guid.NewGuid().ToString("B");
            using var app = classes.CreateSubKey(@"AppUserModelId\" + NotificationRegistration.AppId);
            app.SetValue("CustomActivator", activator);
            app.SetValue("DisplayName", "Thaddeus");
            using var server = classes.CreateSubKey(@"CLSID\" + activator + @"\LocalServer32");
            server.SetValue("", "\"C:\\removed-probe\\Thaddeus.Notifications.exe\" ----AppNotificationActivated:");
            server.SetValue("Unrelated", "keep");
            Assert.True(NotificationRegistration.RefreshActivationTarget(classes, executable));
            Assert.Equal("\"" + executable + "\" ----AppNotificationActivated:", server.GetValue(""));
            Assert.Equal(activator, app.GetValue("CustomActivator"));
            Assert.Equal("Thaddeus", app.GetValue("DisplayName"));
            Assert.Equal("keep", server.GetValue("Unrelated"));
            Assert.False(NotificationRegistration.RefreshActivationTarget(classes, executable));

            // A damaged registration must fail closed, not create an arbitrary CLSID path.
            app.SetValue("CustomActivator", @"..\Unrelated");
            Assert.Throws<InvalidOperationException>(() =>
            {
                if (OperatingSystem.IsWindows()) NotificationRegistration.RefreshActivationTarget(classes, executable);
            });
            Assert.Equal("\"" + executable + "\" ----AppNotificationActivated:", server.GetValue(""));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
            Directory.Delete(folder, recursive: true);
        }
    }
}
