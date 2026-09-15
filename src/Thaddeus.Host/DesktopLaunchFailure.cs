using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Thaddeus.Host;

/// <summary>Keep an installed shortcut's startup failure visible after its console closes.</summary>
internal static class DesktopLaunchFailure
{
    public static void Report(string[] arguments, string message)
    {
        Console.Error.WriteLine("Could not open the study: " + message);
        if (arguments.FirstOrDefault() != "--desktop" || arguments.Contains("--no-browser", StringComparer.Ordinal)
            || !OperatingSystem.IsWindows() || !Environment.UserInteractive) return;

        // Unattended callers keep the console contract; the raven only rings a visible doorbell for desktop launches.
        ShowWindows(message + "\n\nThe raven is waiting at the door. Close this message when you have read the details.");
    }

    [SupportedOSPlatform("windows")]
    private static void ShowWindows(string message)
    {
        const uint okWithErrorIcon = 0x00000010;
        // A task-modal dialog remains visible even when Start launched a minimized console.
        const uint taskModal = 0x00002000;
        MessageBox(0, message, "Thaddeus could not open", okWithErrorIcon | taskModal);
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBox(nint owner, string text, string caption, uint type);
}
