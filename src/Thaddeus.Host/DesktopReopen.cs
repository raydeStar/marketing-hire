using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using Thaddeus.Core;

namespace Thaddeus.Host;

/// <summary>Reopen the same desktop study without discovering or trusting an HTTP port.</summary>
internal static class DesktopReopen
{
    internal static string PipeName(DesktopLaunch launch)
    {
        string Canonical(string value) => OperatingSystem.IsWindows() ? value.ToUpperInvariant() : value;
        var identity = new { package = Canonical(launch.Package), data = Canonical(launch.Data), launch.Origin,
            launch.WorkerPort, installation = launch.Installation == null ? null : Canonical(launch.Installation), user = Environment.UserName };
        return "thaddeus-open-" + Wire.Hash(Wire.Pack(identity));
    }

    internal static async Task<string?> Request(DesktopLaunch launch, int waitMilliseconds, CancellationToken cancellation = default)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName(launch), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, TokenImpersonationLevel.Anonymous);
        try { await pipe.ConnectAsync(waitMilliseconds, cancellation); }
        catch (TimeoutException) { return null; }
        catch (IOException) { return null; }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await pipe.WriteAsync(new byte[] { launch.NoBrowser ? (byte)2 : (byte)1 }, deadline.Token);
            var status = new byte[1]; await pipe.ReadExactlyAsync(status, deadline.Token);
            if (status[0] != 1) throw new InvalidOperationException("The running study cannot open another login link yet. Return to its browser or try again shortly.");
            if (launch.NoBrowser) return launch.Origin;
            var bytes = new byte[48]; await pipe.ReadExactlyAsync(bytes, deadline.Token);
            var ticket = Encoding.ASCII.GetString(bytes);
            if (ticket.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
                throw new InvalidOperationException("The running study returned an invalid launch link. No browser was opened.");
            return launch.Origin + "/#launch=" + ticket;
        }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        { throw new InvalidOperationException("The running study did not finish reopening. Return to its browser; no replacement was started."); }
    }

    internal static async Task Serve(DesktopLaunch launch, BrowserLaunchTickets tickets, CancellationToken stopping)
    {
        using var pipe = new NamedPipeServerStream(PipeName(launch), PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        while (!stopping.IsCancellationRequested)
        {
            await pipe.WaitForConnectionAsync(stopping);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                var request = new byte[1]; await pipe.ReadExactlyAsync(request, deadline.Token);
                // A fixed command only: the second launcher cannot supply a URL, path or model request.
                byte[] response = [0];
                if (request[0] == 2) response = [1];
                else if (request[0] == 1)
                {
                    try { response = [1, ..Encoding.ASCII.GetBytes(tickets.Issue().Ticket)]; }
                    catch (InvalidOperationException) { /* The raven's tray of unclaimed login links is full. */ }
                }
                await pipe.WriteAsync(response, deadline.Token);
                await pipe.FlushAsync(deadline.Token);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException) { }
            finally { pipe.Disconnect(); }
        }
    }
}

public sealed class DesktopReopenService(DesktopLaunch launch, BrowserLaunchTickets tickets,
    IHostApplicationLifetime lifetime, ILogger<DesktopReopenService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        try
        {
            await started.Task.WaitAsync(stoppingToken);
            await DesktopReopen.Serve(launch, tickets, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        { logger.LogWarning("Reopening from the desktop is unavailable. Use the current browser window; the study remains open and the raven is still at his desk."); }
    }
}
