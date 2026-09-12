using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Thaddeus.Infrastructure;

public record HostProcessRequest(string Executable, IReadOnlyList<string> Arguments, string WorkingDirectory,
    TimeSpan Timeout, string? Input = null, int OutputLimit = 524288);
public record HostProcessResult(int? ExitCode, string Output, string Error, string? Failure = null)
{
    public bool Succeeded => ExitCode == 0 && Failure == null;
}
public interface IHostProcessRunner
{
    Task<HostProcessResult> Run(HostProcessRequest request, CancellationToken cancellation);
}

/// <summary>Runs a trusted host adapter executable. Agent commands belong inside ISandboxBackend.</summary>
public sealed class HostProcessRunner : IHostProcessRunner
{
    public async Task<HostProcessResult> Run(HostProcessRequest request, CancellationToken cancellation)
    {
        if (request.Timeout <= TimeSpan.Zero || request.Timeout > TimeSpan.FromMinutes(15) || request.OutputLimit is < 1 or > 2_000_000)
            throw new ArgumentException("Process limits are outside the supported range.");
        cancellation.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(request.Executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        foreach (var argument in request.Arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or DirectoryNotFoundException)
        { return new(null, "", "", "start-failed"); }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(request.Timeout);
        var output = new StringBuilder(); var error = new StringBuilder(); var overflow = 0;
        async Task Drain(StreamReader reader, StringBuilder destination)
        {
            var buffer = new char[4096];
            while (true)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(), limit.Token);
                if (count == 0) break;
                var remaining = request.OutputLimit - destination.Length;
                destination.Append(buffer, 0, Math.Min(count, remaining));
                if (count > remaining) { Interlocked.Exchange(ref overflow, 1); limit.Cancel(); break; }
            }
        }
        async Task Feed()
        {
            if (request.Input != null) await process.StandardInput.WriteAsync(request.Input.AsMemory(), limit.Token);
            process.StandardInput.Close();
        }
        var streams = Task.WhenAll(Drain(process.StandardOutput, output), Drain(process.StandardError, error), Feed());
        string? failure = null;
        try { await Task.WhenAll(process.WaitForExitAsync(limit.Token), streams); }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            failure = overflow != 0 ? "output-limit" : cancellation.IsCancellationRequested ? "cancelled" : limit.IsCancellationRequested ? "timeout" : "io-failed";
            limit.Cancel();
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            try { await streams; } catch (Exception stopped) when (stopped is OperationCanceledException or IOException) { }
        }
        cancellation.ThrowIfCancellationRequested();
        return new(process.ExitCode, output.ToString(), error.ToString(), failure);
    }
}
