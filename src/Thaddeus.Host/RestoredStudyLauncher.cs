using System.Text;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public sealed record RestoredLauncher(string Directory, string EntryPoint, string Profile, string Package, Dictionary<string, string> FileHashes);

public static class RestoredStudyLauncher
{
    public static async Task<RestoredLauncher> Create(DesktopLaunch launch, string restored, CancellationToken cancellation)
    {
        Store.AssertNoLinks(restored); Store.AssertNoLinks(launch.Package);
        if (!File.Exists(Path.Combine(restored, "ledger.sqlite"))) throw new ArgumentException("The restored study is missing.");
        var executable = Path.Combine(launch.Package, OperatingSystem.IsWindows() ? "Thaddeus.Host.exe" : "Thaddeus.Host");
        if (!File.Exists(executable) || !File.Exists(Path.Combine(launch.Package, "wwwroot", "index.html")))
            throw new InvalidOperationException("Keep the complete application package available before creating a restored-study launcher.");
        // Keep the shortcut's own path short: Windows PowerShell still rejects long script paths.
        // The restored study can retain its full descriptive name; the profile binds that exact location.
        var folder = Path.Combine(Path.GetDirectoryName(restored)!, "thaddeus-launcher-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows() && (Path.Combine(folder, "Open restored study.ps1").Length >= 260 ||
            Path.Combine(launch.Package, "launch-host.ps1").Length >= 260))
            throw new IOException("The restored study is preserved, but Windows needs a shorter parent folder for its launcher or application package.");
        if (Directory.Exists(folder) || File.Exists(folder)) throw new IOException("The launcher destination already exists.");
        PrivateWorkerDirectory.Create(folder);
        var profile = Path.Combine(folder, "launch.json");
        var files = new Dictionary<string, string>
        {
            ["launch.json"] = Wire.Pack(new { schemaVersion = 1, dataDirectory = restored, localOrigin = launch.Origin,
                workerPort = launch.WorkerPort, developmentWorkerInstallation = launch.Installation }),
            ["README.txt"] = "This launcher opens the separate restored study at:\n" + restored +
                "\n\nClose the currently running Thaddeus host first. Keep the complete application package at:\n" + launch.Package +
                "\n\nThe original study was not replaced. This launcher does not install a worker, move credentials or start an agent task.\n"
        };
        string entryPoint;
        if (OperatingSystem.IsWindows())
        {
            if (!File.Exists(Path.Combine(launch.Package, "launch-host.ps1"))) throw new InvalidOperationException("The packaged Windows launcher is missing.");
            entryPoint = "Start restored study.cmd";
            files[entryPoint] = "@echo off\r\npowershell.exe -NoProfile -File \"%~dp0Open restored study.ps1\" %*\r\nif errorlevel 1 pause\r\n";
            files["launch-target.json"] = Wire.Pack(new { package = launch.Package });
            files["Open restored study.ps1"] = """
                param([switch]$NoBrowser)
                $ErrorActionPreference = 'Stop'
                $target = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'launch-target.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                $script = Join-Path $target.package 'launch-host.ps1'
                if (!(Test-Path -LiteralPath $script -PathType Leaf)) { throw 'The original application package is missing. Keep the restored study; its launcher needs that package.' }
                & $script -LaunchProfile (Join-Path $PSScriptRoot 'launch.json') -NoBrowser:$NoBrowser
                """ + "\n";
        }
        else
        {
            entryPoint = OperatingSystem.IsMacOS() ? "Start restored study.command" : "start-restored-study.sh";
            files[entryPoint] = "#!/bin/sh\nset -eu\nlaunch_dir=$(CDPATH= cd -- \"$(dirname -- \"$0\")\" && pwd -P)\nexec " + ShellQuote(executable) +
                " --desktop --launch-profile \"$launch_dir/launch.json\" \"$@\"\n";
        }
        var hashes = new Dictionary<string, string>();
        foreach (var file in files)
        {
            cancellation.ThrowIfCancellationRequested();
            var bytes = new UTF8Encoding(false).GetBytes(file.Value);
            using var output = new FileStream(Path.Combine(folder, file.Key), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await output.WriteAsync(bytes, cancellation); output.Flush(flushToDisk: true);
            hashes[file.Key] = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        }
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(folder, entryPoint), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var checkedLaunch = DesktopLaunch.Parse(["--desktop", "--no-browser", "--launch-profile", profile], launch.Package);
        if (checkedLaunch?.Data != restored || checkedLaunch.Origin != launch.Origin || checkedLaunch.WorkerPort != launch.WorkerPort || checkedLaunch.Installation != launch.Installation)
            throw new InvalidOperationException("The restored-study launch profile did not verify.");
        return new(folder, Path.Combine(folder, entryPoint), profile, launch.Package, hashes);
    }
    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
}
