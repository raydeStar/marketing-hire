namespace Thaddeus.Notifications;

/// <summary>A notification opens the study that sent it, never a guessed data directory.</summary>
internal static class NotificationActivation
{
    private const string Prefix = "----AppNotificationActivated:";

    internal static bool IsInvocation(string[] args) =>
        args.Any(argument => argument.StartsWith(Prefix, StringComparison.Ordinal));

    internal static string StudyUrl(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
            uri.Host.ToLowerInvariant() is not ("localhost" or "127.0.0.1" or "[::1]") ||
            uri.Port is < 1024 or > 65535 || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("The notification must open an exact local study origin.");
        return uri.GetLeftPart(UriPartial.Authority) + "/?view=upcoming";
    }

    internal static int OpenStudy(IDictionary<string, string> arguments, Func<string, bool> launch)
    {
        if (!arguments.TryGetValue("open", out var action) || action != "activity") return 2;
        // Older retained notifications had no origin; they belonged to the default local study.
        var origin = arguments.TryGetValue("origin", out var target) ? target : "http://localhost:5179";
        try { return launch(StudyUrl(origin)) ? 0 : 2; }
        catch (ArgumentException) { return 2; }
    }

    internal static async Task<int> WaitForInvocation(Task<int> invoked, TimeSpan timeout)
    {
        try { return await invoked.WaitAsync(timeout); }
        catch (TimeoutException) { return 2; }
    }
}
