using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

internal interface IWindowsNotificationSink : IDisposable
{
    string Show(string title, string message);
}

public sealed class WindowsDelegationDispatcher : IDelegationDispatcher, IDisposable
{
    private readonly Func<IWindowsNotificationSink> createNotifications;
    private readonly Func<bool> supportsNotifications;
    private IWindowsNotificationSink? notifications;

    public WindowsDelegationDispatcher() : this(() => new WindowsBalloonNotifier(), OperatingSystem.IsWindows) { }

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
            var nativeId = notifications.Show(job.Title, message);
            return Task.FromResult(new DelegationDispatchResult("accepted",
                "Windows accepted the notification and the reminder remains unread in Thaddeus.", true, nativeId,
                JsonSerializer.SerializeToElement(new { mechanism = "Shell_NotifyIcon", accepted = true }, Wire.Json), "accepted"));
        }
        catch (Win32Exception error)
        {
            return Task.FromResult(NotificationFailure("failed",
                "Windows refused the app notification. Open Windows Settings → System → Notifications, allow notifications for Thaddeus, and check Do not disturb. Use Review in Chat to schedule a new reminder only if it is still useful; this occurrence will not fire again automatically.",
                error.NativeErrorCode));
        }
    }

    private static DelegationDispatchResult NotificationFailure(string status, string error, int? nativeError = null) =>
        new("accepted", "The reminder was saved as an unread result in Thaddeus, but its Windows notification was not displayed.", true,
            ProviderEvidence: JsonSerializer.SerializeToElement(new { mechanism = "Shell_NotifyIcon", accepted = false, error = nativeError }, Wire.Json),
            NotificationStatus: status, NotificationError: error);

    public void Dispose() => notifications?.Dispose();

    private sealed class WindowsBalloonNotifier : IWindowsNotificationSink
    {
        private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
        private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 16;
        private const uint NIIF_INFO = 1, NOTIFYICON_VERSION_4 = 4, WM_APP = 0x8000;
        private static readonly IntPtr HWND_MESSAGE = new(-3), IDI_INFORMATION = new(32516);
        private readonly WindowProcedure procedure;
        private readonly string className = "Thaddeus.Notification." + Guid.NewGuid().ToString("N");
        private readonly IntPtr window;
        private readonly IntPtr icon;
        private bool registered;
        private uint sequence;

        public WindowsBalloonNotifier()
        {
            procedure = WindowProc;
            var module = GetModuleHandleW(null);
            var windowClass = new WindowClass { Instance = module, Procedure = Marshal.GetFunctionPointerForDelegate(procedure), ClassName = className };
            if (RegisterClassW(ref windowClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            window = CreateWindowExW(0, className, "Thaddeus notifications", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, module, IntPtr.Zero);
            if (window == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            icon = LoadIconW(IntPtr.Zero, IDI_INFORMATION);
            if (icon == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            var data = Data(); data.Flags = NIF_MESSAGE | NIF_ICON | NIF_TIP; data.CallbackMessage = WM_APP + 42;
            data.Icon = icon; data.Tip = "Thaddeus reminders";
            if (!Shell_NotifyIconW(NIM_ADD, ref data)) throw new Win32Exception(Marshal.GetLastWin32Error());
            registered = true; data.TimeoutOrVersion = NOTIFYICON_VERSION_4; _ = Shell_NotifyIconW(NIM_SETVERSION, ref data);
        }

        public string Show(string title, string message)
        {
            var data = Data(); data.Flags = NIF_INFO; data.InfoTitle = Bound(title, 63); data.Info = Bound(message, 255);
            data.InfoFlags = NIIF_INFO; data.TimeoutOrVersion = 10_000;
            if (!Shell_NotifyIconW(NIM_MODIFY, ref data)) throw new Win32Exception(Marshal.GetLastWin32Error());
            sequence++; return $"windows-shell:{Environment.ProcessId}:{sequence}";
        }

        private NotifyIconData Data() => new() { Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = window, Id = 1 };
        private static string Bound(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";
        private static IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam) => DefWindowProcW(window, message, wParam, lParam);

        public void Dispose()
        {
            if (registered) { var data = Data(); _ = Shell_NotifyIconW(NIM_DELETE, ref data); registered = false; }
            if (window != IntPtr.Zero) _ = DestroyWindow(window);
            _ = UnregisterClassW(className, GetModuleHandleW(null));
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClass
        {
            public uint Style;
            public IntPtr Procedure;
            public int ClassExtra;
            public int WindowExtra;
            public IntPtr Instance;
            public IntPtr Icon;
            public IntPtr Cursor;
            public IntPtr Background;
            public string? MenuName;
            public string ClassName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            public uint Size;
            public IntPtr Window;
            public uint Id;
            public uint Flags;
            public uint CallbackMessage;
            public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? Tip;
            public uint State;
            public uint StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string? Info;
            public uint TimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string? InfoTitle;
            public uint InfoFlags;
            public Guid GuidItem;
            public IntPtr BalloonIcon;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassW(ref WindowClass windowClass);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool UnregisterClassW(string className, IntPtr instance);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowExW(uint exStyle, string className, string title,
            uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadIconW(IntPtr instance, IntPtr name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? moduleName);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);
    }
}
