using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Thaddeus.Host;

/// <summary>The Windows notification-area control for one running desktop host.</summary>
internal sealed class WindowsTray(Action open, Action exit, ILogger logger) : IDisposable
{
    private const uint TrayMessage = 0x8001;
    private const uint WindowClose = 0x0010;
    private const uint WindowDestroy = 0x0002;
    private const uint WindowCommand = 0x0111;
    private const uint WindowContextMenu = 0x007B;
    private const uint LeftDoubleClick = 0x0203;
    private const uint RightButtonUp = 0x0205;
    private const uint MenuOpen = 1;
    private const uint MenuExit = 2;
    private const uint IconAdd = 0;
    private const uint IconDelete = 2;
    private const uint IconSetVersion = 4;
    private const uint IconMessage = 1;
    private const uint IconImage = 2;
    private const uint IconTip = 4;
    private const uint IconShowTip = 0x80;
    private const uint Version4 = 4;
    private const uint MenuSeparator = 0x0800;
    private const uint MenuReturnCommand = 0x0100;
    private const uint MenuRightButton = 0x0002;

    private readonly string className = "ThaddeusTray-" + Guid.NewGuid().ToString("N");
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? thread;
    private IntPtr window;
    private WindowProcedure? procedure;
    private uint taskbarCreated;
    private NotifyIconData icon;
    private bool iconAdded;
    private bool ownsIcon;
    private bool disposed;
    internal IntPtr WindowHandle => window;

    public void Start()
    {
        if (!OperatingSystem.IsWindows()) return;
        thread = new Thread(Run) { IsBackground = true, Name = "Thaddeus tray" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { ready.Task.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException error) { logger.LogWarning(error.InnerException, "The tray could not start; Thaddeus remains available in the browser."); }
        if (!ready.Task.IsCompleted)
            logger.LogWarning("The tray did not appear promptly; Thaddeus remains available in the browser.");
    }

    private void Run()
    {
        try
        {
            procedure = HandleMessage;
            var registration = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                Procedure = Marshal.GetFunctionPointerForDelegate(procedure),
                Instance = GetModuleHandleW(null),
                ClassName = className
            };
            if (RegisterClassExW(ref registration) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                window = CreateWindowExW(0, className, "Thaddeus tray", 0, 0, 0, 0, 0,
                    IntPtr.Zero, IntPtr.Zero, registration.Instance, IntPtr.Zero);
                if (window == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
                icon = new NotifyIconData
                {
                    Size = (uint)Marshal.SizeOf<NotifyIconData>(),
                    Window = window,
                    Id = 1,
                    Flags = IconMessage | IconImage | IconTip | IconShowTip,
                    CallbackMessage = TrayMessage,
                    Image = LoadTrayIcon(),
                    Tip = "Thaddeus is running",
                    Info = "",
                    InfoTitle = ""
                };
                if (icon.Image == IntPtr.Zero || !AddIcon()) throw new Win32Exception(Marshal.GetLastWin32Error());
                ready.TrySetResult(true);
                while (GetMessageW(out var message, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref message);
                    DispatchMessageW(ref message);
                }
            }
            finally
            {
                if (iconAdded) Shell_NotifyIconW(IconDelete, ref icon);
                if (ownsIcon) DestroyIcon(icon.Image);
                if (window != IntPtr.Zero) DestroyWindow(window);
                UnregisterClassW(className, registration.Instance);
            }
        }
        catch (Exception error)
        {
            ready.TrySetException(error);
            logger.LogWarning(error, "The tray stopped unexpectedly; the study remains available in the browser.");
        }
    }

    private bool AddIcon()
    {
        iconAdded = Shell_NotifyIconW(IconAdd, ref icon);
        if (!iconAdded) return false;
        icon.Version = Version4;
        Shell_NotifyIconW(IconSetVersion, ref icon);
        return true;
    }

    private IntPtr LoadTrayIcon()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Assets", "thaddeus-tray.ico");
        var image = LoadImageW(IntPtr.Zero, file, 1, 32, 32, 0x0010);
        if (image != IntPtr.Zero) { ownsIcon = true; return image; }
        logger.LogWarning("The Thaddeus tray image is unavailable; using the Windows application icon.");
        return LoadIconW(IntPtr.Zero, new IntPtr(32512));
    }

    private IntPtr HandleMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == taskbarCreated && taskbarCreated != 0)
        {
            iconAdded = false; // Explorer discarded the prior icon.
            AddIcon();
            return IntPtr.Zero;
        }
        if (message == TrayMessage)
        {
            var action = (uint)(lParam.ToInt64() & 0xffff);
            if (action == LeftDoubleClick) Queue(open);
            else if (action is WindowContextMenu or RightButtonUp) ShowMenu(handle);
            return IntPtr.Zero;
        }
        if (message == WindowCommand)
        {
            Select((uint)(wParam.ToInt64() & 0xffff));
            return IntPtr.Zero;
        }
        if (message == WindowClose) { DestroyWindow(handle); return IntPtr.Zero; }
        if (message == WindowDestroy) { window = IntPtr.Zero; PostQuitMessage(0); return IntPtr.Zero; }
        return DefWindowProcW(handle, message, wParam, lParam);
    }

    private void ShowMenu(IntPtr handle)
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenuW(menu, 0, MenuOpen, "Open Thaddeus");
            AppendMenuW(menu, MenuSeparator, 0, null);
            AppendMenuW(menu, 0, MenuExit, "Exit Thaddeus");
            GetCursorPos(out var position);
            SetForegroundWindow(handle);
            Select(TrackPopupMenu(menu, MenuReturnCommand | MenuRightButton, position.X, position.Y, 0, handle, IntPtr.Zero));
            PostMessageW(handle, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { DestroyMenu(menu); }
    }

    private void Select(uint command)
    {
        if (command == MenuOpen) Queue(open);
        else if (command == MenuExit) Queue(exit);
    }

    private void Queue(Action action) => ThreadPool.QueueUserWorkItem(_ =>
    {
        try { action(); }
        catch (Exception error) { logger.LogWarning(error, "The tray action failed; the raven remains on duty."); }
    });

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var handle = window;
        if (handle != IntPtr.Zero) PostMessageW(handle, WindowClose, IntPtr.Zero, IntPtr.Zero);
        if (thread != null && Thread.CurrentThread != thread) thread.Join(TimeSpan.FromSeconds(3));
    }

    private delegate IntPtr WindowProcedure(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size; public uint Style; public IntPtr Procedure; public int ClassExtra; public int WindowExtra;
        public IntPtr Instance; public IntPtr Icon; public IntPtr Cursor; public IntPtr Background;
        [MarshalAs(UnmanagedType.LPWStr)] public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size; public IntPtr Window; public uint Id; public uint Flags; public uint CallbackMessage;
        public IntPtr Image;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State; public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window; public uint Id; public IntPtr WParam; public IntPtr LParam;
        public uint Time; public Point Position; public uint Private;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? module);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassExW(ref WindowClass registration);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClassW(string className, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMessageW(out Message message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessageW(ref Message message);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int exitCode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")] private static extern IntPtr LoadIconW(IntPtr instance, IntPtr name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImageW(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(IntPtr menu, uint flags, uint id, string? text);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr window, IntPtr rectangle);
}
