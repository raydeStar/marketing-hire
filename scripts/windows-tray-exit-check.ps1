param([Parameter(Mandatory)][int]$HostProcessId)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class ThaddeusTrayCheck {
    delegate bool WindowCallback(IntPtr handle, IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr state);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr handle, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    public static void Exit(int expectedProcessId) {
        IntPtr selected = IntPtr.Zero;
        int matches = 0;
        EnumWindows((handle, state) => {
            uint actual; GetWindowThreadProcessId(handle, out actual);
            if (actual != (uint)expectedProcessId) return true;
            var title = new StringBuilder(64);
            GetWindowTextW(handle, title, title.Capacity);
            if (title.ToString() == "Thaddeus tray") { selected = handle; matches++; }
            return true;
        }, IntPtr.Zero);
        if (matches != 1) throw new InvalidOperationException("Expected exactly one tray window owned by the verified fixture host.");
        SendMessageW(selected, 0x0111, new IntPtr(2), IntPtr.Zero);
    }
}
'@
[ThaddeusTrayCheck]::Exit($HostProcessId)
