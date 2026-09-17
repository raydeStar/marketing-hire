namespace Thaddeus.Notifications;

/// <summary>Turns a notification click into the same safe desktop reopen path as the launcher.</summary>
internal static class NotificationActivation
{
    private const string Prefix = "----AppNotificationActivated:";

    internal static bool IsInvocation(string[] args) =>
        args.Any(argument => argument.StartsWith(Prefix, StringComparison.Ordinal));

    internal static int OpenApp(string baseDirectory, Func<string, bool> launch)
    {
        var helper = Path.GetFullPath(baseDirectory);
        var host = Path.GetFullPath(Path.Combine(helper, "Thaddeus.Host.exe"));
        if (!host.StartsWith(helper.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) || !File.Exists(host))
            return 2;
        return launch(host) ? 0 : 2;
    }
}
