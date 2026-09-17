using Microsoft.Win32;
using Thaddeus.Notifications;

namespace Thaddeus.Tests;

public sealed class NotificationRegistrationTests
{
    [Fact]
    public void NotificationClickOpensTheSendingStudyResultsWithoutLaunchingAnotherHost()
    {
        Assert.True(NotificationActivation.IsInvocation(["----AppNotificationActivated:"]));
        Assert.False(NotificationActivation.IsInvocation(["--desktop"]));
        string? launched = null;
        var arguments = new Dictionary<string, string> { ["open"] = "activity", ["origin"] = "http://127.0.0.1:57391" };
        Assert.Equal(0, NotificationActivation.OpenStudy(arguments, url => { launched = url; return true; }));
        Assert.Equal("http://127.0.0.1:57391/?view=upcoming", launched);
        arguments.Remove("origin");
        Assert.Equal(0, NotificationActivation.OpenStudy(arguments, url => { launched = url; return true; }));
        Assert.Equal("http://localhost:5179/?view=upcoming", launched);
        arguments["open"] = "send-email";
        Assert.Equal(2, NotificationActivation.OpenStudy(arguments, _ => throw new Exception("Must not launch")));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://localhost:5179/execute")]
    [InlineData("http://localhost:5179/?secret=unsafe")]
    [InlineData("http://localhost:5179/#launch=unsafe")]
    [InlineData("http://user@localhost:5179")]
    [InlineData("http://localhost:80")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("http://localhost.example.com:5179")]
    public void NotificationClickRefusesAnExternalOrActiveDestination(string origin)
    {
        var arguments = new Dictionary<string, string> { ["open"] = "activity", ["origin"] = origin };
        Assert.Equal(2, NotificationActivation.OpenStudy(arguments, _ => throw new Exception("Must not launch")));
    }

    [Fact]
    public async Task ActivationWaitsForTheActualComCallbackAndExpiresWithoutLaunching()
    {
        var callback = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = NotificationActivation.WaitForInvocation(callback.Task, TimeSpan.FromSeconds(2));
        Assert.False(pending.IsCompleted);
        callback.SetResult(0);
        Assert.Equal(0, await pending);
        Assert.Equal(2, await NotificationActivation.WaitForInvocation(new TaskCompletionSource<int>().Task, TimeSpan.FromMilliseconds(10)));
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
