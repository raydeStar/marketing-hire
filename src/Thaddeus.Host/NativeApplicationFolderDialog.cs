using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

/// <summary>Fixed desktop adapters only. Browser input never becomes a command or an initial shell location.</summary>
public sealed class NativeApplicationFolderDialog(string package) : IApplicationFolderDialog
{
    private string? Backend => OperatingSystem.IsWindowsVersionAtLeast(10) && Environment.UserInteractive &&
        string.Equals(Environment.ProcessPath, Path.Combine(package, "Thaddeus.Host.exe"), StringComparison.OrdinalIgnoreCase) ? "windows"
        : OperatingSystem.IsMacOS() && Environment.UserInteractive && File.Exists("/usr/bin/osascript") ? "mac"
        : OperatingSystem.IsLinux() && (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            ? File.Exists("/usr/bin/zenity") ? "zenity" : File.Exists("/usr/bin/kdialog") ? "kdialog" : null : null;
    public bool Available => Backend != null;

    public async Task<string?> Choose(CancellationToken cancellation)
    {
        var backend = Backend;
        if (backend == "windows" && OperatingSystem.IsWindowsVersionAtLeast(10)) return await ChooseWindows(cancellation);
        var (executable, arguments) = backend switch
        {
            "mac" => ("/usr/bin/osascript", new[] { "-e", "try\nreturn POSIX path of (choose folder with prompt \"Choose the extracted Thaddeus application folder\")\non error number -128\nreturn \"\"\nend try" }),
            "zenity" => ("/usr/bin/zenity", new[] { "--file-selection", "--directory", "--title=Choose the extracted Thaddeus application folder" }),
            "kdialog" => ("/usr/bin/kdialog", new[] { "--getexistingdirectory", package, "--title", "Choose the extracted Thaddeus application folder" }),
            _ => throw new InvalidOperationException("A desktop folder chooser is unavailable.")
        };
        var result = await new HostProcessRunner().Run(new(executable, arguments, package, TimeSpan.FromMinutes(5), OutputLimit: 8192), cancellation);
        if (result.Failure == null && result.ExitCode == 1 && backend is "zenity" or "kdialog") return null;
        if (!result.Succeeded) throw new InvalidOperationException("The desktop folder chooser did not complete.");
        // Remove only the line ending written by the adapter. Spaces can belong to a real folder name.
        var path = result.Output.TrimEnd('\r', '\n');
        return path.Length == 0 ? null : path;
    }

    [SupportedOSPlatform("windows10.0")]
    private async Task<string?> ChooseWindows(CancellationToken cancellation)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "SystemRoot", "SystemDrive", "WINDIR", "USERPROFILE", "LOCALAPPDATA", "APPDATA", "TEMP", "TMP",
            "ALLUSERSPROFILE", "ProgramData", "PUBLIC", "HOMEDRIVE", "HOMEPATH", "ProgramFiles", "ProgramFiles(x86)",
            "ProgramW6432", "CommonProgramFiles", "CommonProgramFiles(x86)", "CommonProgramW6432" })
            if (Environment.GetEnvironmentVariable(name) is { } value) environment[name] = value;
        await using var child = WindowsJobProcess.Start(new(Path.Combine(package, "Thaddeus.Host.exe"),
            ["--choose-application-folder"], package, environment, TimeSpan.FromMinutes(5), 8192), cancellation);
        child.Input.Close();
        using var output = new StreamReader(child.Output, Encoding.UTF8);
        using var error = new StreamReader(child.Error, Encoding.UTF8);
        var text = output.ReadToEndAsync(); var errors = error.ReadToEndAsync();
        await Task.WhenAll(text, errors, child.Completion);
        cancellation.ThrowIfCancellationRequested();
        if (!(await child.Completion).Succeeded) throw new InvalidOperationException("The desktop folder chooser did not complete.");
        return JsonSerializer.Deserialize<string?>(await text);
    }

    public static int RunHelper()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) return 1;
        string? selected = null; Exception? failure = null;
        var thread = new Thread(() => { try { if (OperatingSystem.IsWindowsVersionAtLeast(10)) selected = ShowWindows(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) { Console.Error.WriteLine("The folder chooser could not open. The path may still be supplied by hand."); return 1; }
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.WriteLine(JsonSerializer.Serialize(selected)); return 0;
    }

    [SupportedOSPlatform("windows10.0")]
    private static string? ShowWindows()
    {
        var type = Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"), throwOnError: true)!;
        var dialog = (IFileDialog)Activator.CreateInstance(type)!;
        try
        {
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | 0x20 | 0x40 | 0x800 | 0x02000000); // Folders, real paths, existing paths, no recent-items entry.
            dialog.SetTitle("Choose the extracted Thaddeus application folder");
            dialog.SetOkButtonLabel("Choose application folder");
            var result = dialog.Show(0);
            if (result == unchecked((int)0x800704C7)) return null;
            Marshal.ThrowExceptionForHR(result);
            dialog.GetResult(out var item);
            try
            {
                item.GetDisplayName(0x80058000, out var path); // SIGDN_FILESYSPATH
                try { return Marshal.PtrToStringUni(path); } finally { Marshal.FreeCoTaskMem(path); }
            }
            finally { Marshal.ReleaseComObject(item); }
        }
        finally { Marshal.ReleaseComObject(dialog); }
    }

    // COM slots through GetResult must retain the native IFileDialog order, including unused methods.
    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(nint owner);
        void SetFileTypes(uint count, nint filters);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(nint events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem folder);
        void SetFolder(IShellItem folder);
        void GetFolder(out IShellItem folder);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName(out nint name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
    }
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint context, ref Guid handler, ref Guid iid, out nint result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint kind, out nint name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem item, uint hint, out int order);
    }
}
