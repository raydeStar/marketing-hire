using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using Thaddeus.Host;

namespace Thaddeus.Tests;

public sealed class WindowsTrayTests
{
    [Fact]
    public void NativeMenuCommandsOpenAndExitOnlyThisHost()
    {
        if (!OperatingSystem.IsWindows()) return;
        var opened = 0; var exited = 0;
        using var tray = new WindowsTray(() => Interlocked.Increment(ref opened),
            () => Interlocked.Increment(ref exited), NullLogger.Instance);
        tray.Start();
        Assert.NotEqual(IntPtr.Zero, tray.WindowHandle);

        SendMessageW(tray.WindowHandle, 0x0111, new IntPtr(1), IntPtr.Zero);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref opened) == 1, TimeSpan.FromSeconds(3)));
        Assert.Equal(0, Volatile.Read(ref exited));

        SendMessageW(tray.WindowHandle, 0x0111, new IntPtr(2), IntPtr.Zero);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref exited) == 1, TimeSpan.FromSeconds(3)));
        Assert.Equal(1, Volatile.Read(ref opened));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
