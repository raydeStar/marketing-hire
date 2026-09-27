using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Thaddeus.Host;

namespace Thaddeus.Tests;

public sealed class PlowTransportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "claw-plow-transport-" + Guid.NewGuid().ToString("N"));

    public PlowTransportTests() => Directory.CreateDirectory(root);

    private MarketingProcessTransport Direct(string script)
    {
        var password = Path.Combine(root, "gateway-password");
        File.WriteAllText(password, "fictional-per-boot-password\n");
        return new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Marketing:Transport"] = "direct", ["Marketing:OpenClawScript"] = script,
            ["Marketing:GatewayPasswordFile"] = password
        }).Build(), "employee");
    }

    [Fact]
    public async Task DirectCommand_PreservesUnicodeAndLiteralArguments_AndReloadsBootPassword()
    {
        var script = Path.Combine(root, "employee.mjs");
        await File.WriteAllTextAsync(script, """
            let input = ''; for await (const chunk of process.stdin) input += chunk;
            console.log(JSON.stringify({ input, args: process.argv.slice(2), passwordLength: process.env.OPENCLAW_GATEWAY_PASSWORD.length }));
            console.error('fictional diagnostic');
            """);
        var transport = Direct(script);
        const string text = "Owner’s direction → café 🦉";
        const string literal = "spaces; $(do-not-execute) `quotes`";
        var result = await transport.Run("employee", text, TimeSpan.FromSeconds(5), CancellationToken.None, "openclaw", literal);
        Assert.Equal(0, result.Exit);
        using var reply = JsonDocument.Parse(result.Output);
        Assert.Equal(text, reply.RootElement.GetProperty("input").GetString());
        Assert.Equal(literal, reply.RootElement.GetProperty("args")[0].GetString());
        Assert.Contains("fictional diagnostic", result.Error);
        File.WriteAllText(Path.Combine(root, "gateway-password"), "rotated-fixture-password\n");
        Assert.Equal("rotated-fixture-password", transport.Command("employee", "openclaw", "gateway", "health").Environment["OPENCLAW_GATEWAY_PASSWORD"]);
        Assert.Throws<IOException>(() => transport.Command("separate-shared-employee", "openclaw", "agent"));
    }

    [Fact]
    public async Task CanceledDirectCommand_ActuallyStopsItsOwnedProcess()
    {
        var pidFile = Path.Combine(root, "pid");
        var script = Path.Combine(root, "waiting.mjs");
        await File.WriteAllTextAsync(script, "import {writeFileSync} from 'node:fs'; writeFileSync(process.argv[2], String(process.pid)); setInterval(() => {}, 1000);");
        var transport = Direct(script);
        using var cancel = new CancellationTokenSource();
        var running = transport.Run("employee", null, TimeSpan.FromSeconds(20), cancel.Token, "openclaw", pidFile);
        for (var i = 0; i < 100 && !File.Exists(pidFile); i++) await Task.Delay(20);
        Assert.True(File.Exists(pidFile), "The fictional worker must actually start before cancellation.");
        using var process = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(pidFile)));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(process.HasExited, "Canceled worker remained alive.");
    }

    [Fact]
    public void DefaultDockerCommand_KeepsTheExistingContainerBoundary()
    {
        var transport = new MarketingProcessTransport(new ConfigurationBuilder().Build(), "employee");
        var command = transport.Command("shared-employee", "hire", "draft", "get", "--id", "1");
        Assert.Equal("docker", command.FileName);
        Assert.Equal(new[] { "exec", "-i", "shared-employee", "hire", "draft", "get", "--id", "1" }, command.ArgumentList);
        Assert.False(command.UseShellExecute);
    }

    public void Dispose() => Directory.Delete(root, true);
}
