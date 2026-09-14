using System.Diagnostics;
using System.Net;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public sealed record OpenStudyRequest(string ReviewId, string Target, bool OpenBrowser = true);
public sealed record PreparedStudyOpen(string Id, RestoredLauncher Launcher, DesktopLaunch Launch,
    VerifiedApplicationPackage Application, bool OpenBrowser);
public sealed record StudyOpenResult(bool Started, bool CanReopenOriginal, string Message, int? ProcessId = null, string? ProcessStartTicks = null);

/// <summary>A deliberate transfer between closed studies. There is no task replay, service installation or automatic retry.</summary>
public static class ApplicationHandoff
{
    public static async Task<PreparedStudyOpen> Prepare(MaintenancePlan plan, GuidedRestoreView view, OpenStudyRequest request, CancellationToken cancellation)
    {
        if (view.Phase != "restored" || view.Review?.Id != request.ReviewId || view.Receipt == null || plan.Launch == null)
            throw new InvalidOperationException("Choose a completed, verified restored study before opening it.");
        var launcher = request.Target switch
        {
            "restored" => view.Launcher,
            "original" => view.ReturnLauncher,
            _ => throw new ArgumentException("Choose the restored study or the original study.")
        } ?? throw new InvalidOperationException("This study does not have a prepared launcher.");
        var data = request.Target == "restored" ? view.Receipt.Directory : plan.Source;
        var expectedPackage = request.Target == "restored" ? view.Review.Application?.Directory ?? plan.Package : plan.Package;
        if (launcher.Package != expectedPackage || launcher.Profile != Path.Combine(launcher.Directory, "launch.json") ||
            Path.GetDirectoryName(launcher.Directory) != Path.GetDirectoryName(data) || !launcher.FileHashes.ContainsKey("launch.json"))
            throw new InvalidOperationException("The prepared launcher does not belong to this reviewed study.");
        await ApplicationPackage.VerifyLauncherFiles(launcher.Directory, launcher.FileHashes, cancellation);
        var application = await ApplicationPackage.Verify(expectedPackage, cancellation,
            request.Target == "restored" ? view.Review.Application?.ManifestSha256 : null);
        ApplicationPackage.RequireStudyCompatibility(application, request.Target == "restored" ? view.Receipt.DatabaseSchemaVersion : Store.CurrentSchemaVersion);
        var launch = DesktopLaunch.Parse(["--desktop", "--no-browser", "--launch-profile", launcher.Profile], expectedPackage)!;
        if (launch.Data != data || launch.Origin != plan.Origin || launch.WorkerPort != plan.Launch.WorkerPort)
            throw new InvalidOperationException("The prepared launcher points to a different study or address.");
        return new(Guid.NewGuid().ToString("N"), launcher, launch, application, request.OpenBrowser);
    }

    public static async Task<StudyOpenResult> Start(PreparedStudyOpen prepared)
    {
        Process? child = null;
        string? startTicks = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            // The maintenance listener has already closed. Recheck the exact reviewed bytes before executing any target code.
            await ApplicationPackage.VerifyLauncherFiles(prepared.Launcher.Directory, prepared.Launcher.FileHashes, deadline.Token);
            await ApplicationPackage.Verify(prepared.Application.Directory, deadline.Token, prepared.Application.ManifestSha256);
            var expected = await File.ReadAllBytesAsync(Path.Combine(prepared.Launch.Package, "wwwroot", "index.html"), deadline.Token);
            var start = new ProcessStartInfo(Path.Combine(prepared.Launch.Package, OperatingSystem.IsWindows() ? "Thaddeus.Host.exe" : "Thaddeus.Host"))
            {
                WorkingDirectory = prepared.Launch.Package, UseShellExecute = false, CreateNoWindow = true
            };
            foreach (var argument in new[] { "--desktop", "--no-browser", "--launch-profile", prepared.Launcher.Profile }) start.ArgumentList.Add(argument);
            child = Process.Start(start) ?? throw new IOException("The selected application did not start.");
            startTicks = child.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
            var ready = false;
            while (!deadline.IsCancellationRequested)
            {
                if (child.HasExited) throw new IOException("The selected application exited before it became ready.");
                try
                {
                    using var response = await client.GetAsync(prepared.Launch.Origin + "/", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        await response.Content.LoadIntoBufferAsync(expected.Length + 1, deadline.Token);
                        ready = (await response.Content.ReadAsByteArrayAsync(deadline.Token)).AsSpan().SequenceEqual(expected);
                    }
                }
                catch (Exception error) when (error is HttpRequestException or TaskCanceledException) { }
                if (ready) break;
                await Task.Delay(150, deadline.Token);
            }
            if (!ready || child.HasExited) throw new IOException("The selected application did not become ready within its launch deadline.");
            var message = "The selected study is open. Its saved tasks were not resumed automatically.";
            if (prepared.OpenBrowser)
            {
                // Reuse the existing one-use launcher API. Only this exact loopback origin receives the host key.
                var keyPath = Path.Combine(prepared.Launch.Data, "host-key.txt"); Store.AssertNoLinks(keyPath);
                if (new FileInfo(keyPath).Length > 256) throw new IOException("The selected study has an invalid access key.");
                var key = (await File.ReadAllTextAsync(keyPath, deadline.Token)).Trim();
                using var request = new HttpRequestMessage(HttpMethod.Post, prepared.Launch.Origin + "/api/auth/launch")
                { Content = JsonContent.Create(new { key }) };
                request.Headers.Add("Origin", prepared.Launch.Origin);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                response.EnsureSuccessStatusCode(); await response.Content.LoadIntoBufferAsync(4096, deadline.Token);
                var ticket = await response.Content.ReadFromJsonAsync<BrowserLaunchTicket>(deadline.Token);
                if (ticket == null || ticket.Ticket.Length != 48 || ticket.Ticket.Any(c => !char.IsAsciiHexDigit(c)) || ticket.Expires <= DateTimeOffset.UtcNow)
                    throw new IOException("The selected study did not issue a valid launch link.");
                try { Process.Start(new ProcessStartInfo(prepared.Launch.Origin + "/?study=" + prepared.Id + "#launch=" + ticket.Ticket) { UseShellExecute = true })?.Dispose(); }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
                { message = "The selected study is open, but the browser could not be opened. Visit its local address to unlock it."; }
            }
            var result = new StudyOpenResult(true, false, message, child.Id, startTicks);
            Record(prepared, result);
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or OperationCanceledException or System.Text.Json.JsonException or HttpRequestException or System.ComponentModel.Win32Exception)
        {
            var stopped = child == null || child.HasExited;
            if (!stopped)
            {
                try { child!.Kill(entireProcessTree: true); stopped = child.WaitForExit(10_000); }
                catch (Exception stop) when (stop is InvalidOperationException or System.ComponentModel.Win32Exception) { stopped = child!.HasExited; }
            }
            var result = new StudyOpenResult(false, stopped, stopped
                ? "The selected application could not be opened. Its restored copy is preserved; the original study can be reopened or you can try its prepared launcher."
                : "The selected application's exit could not be confirmed. Keep both studies and inspect the prepared launcher before opening another host.", child?.Id, startTicks);
            try { Record(prepared, result); } catch (Exception recording) when (recording is IOException or UnauthorizedAccessException) { }
            return result;
        }
        finally { child?.Dispose(); }
    }

    private static void Record(PreparedStudyOpen prepared, StudyOpenResult result)
    {
        Store.AssertNoLinks(prepared.Launcher.Directory);
        var path = Path.Combine(prepared.Launcher.Directory, "open-" + prepared.Id + ".json");
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var bytes = System.Text.Encoding.UTF8.GetBytes(Wire.Pack(new { prepared.Id, prepared.Application.ManifestSha256, prepared.Launch.Data, result }));
        output.Write(bytes); output.Flush(flushToDisk: true);
    }
}
