using System.Diagnostics;
using System.Text;

namespace Thaddeus.Host;

/// <summary>Runs the same employee commands in a local Docker worker or beside the host on Plow.</summary>
internal sealed class MarketingProcessTransport
{
    private readonly bool direct;
    private readonly string primaryContainer;
    private readonly string openClawScript;
    private readonly string? gatewayPasswordFile;

    internal MarketingProcessTransport(IConfiguration config, string primaryContainer)
    {
        var mode = config["Marketing:Transport"] ?? "docker";
        if (mode is not ("docker" or "direct"))
            throw new ArgumentException("Marketing:Transport must be docker or direct.");
        direct = mode == "direct";
        this.primaryContainer = primaryContainer;
        openClawScript = config["Marketing:OpenClawScript"] ?? "/app/openclaw.mjs";
        gatewayPasswordFile = direct ? config["Marketing:GatewayPasswordFile"] ?? "/var/lib/plow/gateway-password" : null;
    }

    internal ProcessStartInfo Command(string container, params string[] arguments)
    {
        if (arguments.Length == 0) throw new ArgumentException("An employee command is required.");
        if (direct && container != primaryContainer)
            throw new IOException("This operation needs a separate shared Gateway. It is not configured in this Plow package.");
        var start = new ProcessStartInfo(direct ? arguments[0] == "openclaw" ? "node" : arguments[0] : "docker")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        if (!direct)
        {
            start.ArgumentList.Add("exec"); start.ArgumentList.Add("-i"); start.ArgumentList.Add(container);
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
        }
        else
        {
            if (arguments[0] == "openclaw")
            {
                start.ArgumentList.Add(openClawScript);
                // Plow rotates this per boot. Read it at dispatch, never bake it into a command or a log.
                if (gatewayPasswordFile != null)
                    start.Environment["OPENCLAW_GATEWAY_PASSWORD"] = File.ReadAllText(gatewayPasswordFile).Trim();
            }
            foreach (var argument in arguments.Skip(1)) start.ArgumentList.Add(argument);
        }
        return start;
    }

    internal async Task<(int Exit, string Output, string Error)> Run(string container, string? input,
        TimeSpan timeout, CancellationToken cancellation, params string[] arguments)
    {
        using var process = new Process { StartInfo = Command(container, arguments) };
        if (!process.Start()) throw new IOException("The employee command did not start.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(timeout);
        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        var error = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            if (input != null) await process.StandardInput.WriteAsync(input.AsMemory(), limit.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(limit.Token);
            return (process.ExitCode, await output, await error);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            // Observe canceled reads as well as the process: no ghost servants after a timeout.
            try { await Task.WhenAll(output, error); } catch (OperationCanceledException) { }
            throw;
        }
    }
}
