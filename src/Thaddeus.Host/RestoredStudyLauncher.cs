using System.Text;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public sealed record RestoredLauncher(string Directory, string EntryPoint, string Profile, string Package, Dictionary<string, string> FileHashes);

public static class RestoredStudyLauncher
{
    public static Task<RestoredLauncher> Create(DesktopLaunch launch, string restored, CancellationToken cancellation, VerifiedApplicationPackage? selected = null)
        => CreateCore(launch, restored, cancellation, selected, original: false);
    public static Task<RestoredLauncher> CreateOriginal(DesktopLaunch launch, CancellationToken cancellation)
        => CreateCore(launch, launch.Data, cancellation, selected: null, original: true);
    private static async Task<RestoredLauncher> CreateCore(DesktopLaunch launch, string restored, CancellationToken cancellation, VerifiedApplicationPackage? selected, bool original)
    {
        var verifier = Path.Combine(launch.Package, OperatingSystem.IsWindows() ? "Thaddeus.Host.exe" : "Thaddeus.Host");
        if (selected != null)
        {
            await ApplicationPackage.Verify(selected.Directory, cancellation, selected.ManifestSha256);
            // An included worker belongs to its package. An explicit external installation remains an operator choice.
            var installation = launch.Installation;
            if (installation == Path.Combine(launch.Package, BundledWorkerInstallation.RelativeDescriptor)) installation = null;
            launch = launch with { Package = selected.Directory, Installation = installation };
        }
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
            ["README.txt"] = (original ? "This return launcher opens your original study at:\n" : "This launcher opens the separate restored study at:\n") + restored +
                "\n\nClose the currently running Thaddeus host first. Keep the complete application package at:\n" + launch.Package +
                "\n\nThe original study was not replaced. This launcher does not install a worker, move credentials or start an agent task.\n"
        };
        if (selected != null) files["README.txt"] += "\nKeep the original application folder for the check performed before each launch:\n" + Path.GetDirectoryName(verifier) + "\nUse the separately prepared original-study launcher to return without depending on earlier versions.\n";
        var label = original ? "original" : "restored";
        string entryPoint;
        if (OperatingSystem.IsWindows())
        {
            if (!File.Exists(Path.Combine(launch.Package, "launch-host.ps1"))) throw new InvalidOperationException("The packaged Windows launcher is missing.");
            entryPoint = "Start " + label + " study.cmd";
            files[entryPoint] = "@echo off\r\npowershell.exe -NoProfile -File \"%~dp0Open " + label + " study.ps1\" %*\r\nif errorlevel 1 pause\r\n";
            files["launch-target.json"] = Wire.Pack(new { package = launch.Package, verifier, packageManifestSha256 = selected?.ManifestSha256 });
            files["Open " + label + " study.ps1"] = """
                param([switch]$NoBrowser)
                $ErrorActionPreference = 'Stop'
                $target = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'launch-target.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                if ($target.packageManifestSha256) {
                    & $target.verifier --verify-package $target.package $target.packageManifestSha256 | Out-Null
                    if ($LASTEXITCODE -ne 0) { throw 'The reviewed application no longer verifies. Keep the study and original package; the raven has left the door closed.' }
                }
                $script = Join-Path $target.package 'launch-host.ps1'
                if (!(Test-Path -LiteralPath $script -PathType Leaf)) { throw 'The original application package is missing. Keep the restored study; its launcher needs that package.' }
                & $script -LaunchProfile (Join-Path $PSScriptRoot 'launch.json') -NoBrowser:$NoBrowser
                """ + "\n";
        }
        else
        {
            entryPoint = OperatingSystem.IsMacOS() ? "Start " + label + " study.command" : "start-" + label + "-study.sh";
            var verify = selected == null ? "" : ShellQuote(verifier) + " --verify-package " + ShellQuote(launch.Package) + " " + ShellQuote(selected.ManifestSha256) + " >/dev/null\n";
            files[entryPoint] = "#!/bin/sh\nset -eu\nlaunch_dir=$(CDPATH= cd -- \"$(dirname -- \"$0\")\" && pwd -P)\n" + verify + "exec " + ShellQuote(executable) +
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
        var expectedInstallation = launch.Installation ?? (File.Exists(Path.Combine(launch.Package, BundledWorkerInstallation.RelativeDescriptor)) ? Path.Combine(launch.Package, BundledWorkerInstallation.RelativeDescriptor) : null);
        if (checkedLaunch?.Data != restored || checkedLaunch.Origin != launch.Origin || checkedLaunch.WorkerPort != launch.WorkerPort || checkedLaunch.Installation != expectedInstallation)
            throw new InvalidOperationException("The restored-study launch profile did not verify.");
        return new(folder, Path.Combine(folder, entryPoint), profile, launch.Package, hashes);
    }
    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
}
