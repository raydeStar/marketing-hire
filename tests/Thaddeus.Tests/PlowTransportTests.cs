using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

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
    public async Task MeetingPrompt_ReachesTheCliThroughARegularFile()
    {
        var script = Path.Combine(root, "prompt-reader.mjs");
        await File.WriteAllTextAsync(script, """
            import {readFile} from 'node:fs/promises';
            const args = process.argv.slice(2), file = args[args.indexOf('--message-file') + 1];
            const text = await readFile(file, 'utf8');
            console.log(JSON.stringify({status:'ok',result:{payloads:[{text}]}}));
            """);
        var password = Path.Combine(root, "gateway-password");
        await File.WriteAllTextAsync(password, "fictional-password");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Marketing:Transport"] = "direct", ["Marketing:OpenClawScript"] = script,
            ["Marketing:GatewayPasswordFile"] = password
        }).Build();
        using var store = new Store(Path.Combine(root, "store"));
        var backend = new MarketingBackend(store, config);
        var prompt = "Owner’s café 🦉\n" + new string('x', 40000) + "\n$(not-a-shell-command)";
        Assert.Equal(prompt, await backend.MeetingReply("marketing", "stdin-fixture", prompt, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task DirectPromptFile_IsPrivateAndRemovedOnSuccessOrFailure(int exit)
    {
        var script = Path.Combine(root, "private-prompt.mjs");
        await File.WriteAllTextAsync(script, """
            import {readFileSync, statSync} from 'node:fs';
            const args = process.argv.slice(2), file = args[args.indexOf('--message-file') + 1];
            console.log(JSON.stringify({file, text:readFileSync(file,'utf8'), mode:statSync(file).mode & 0o777}));
            process.exitCode = Number(args.at(-1));
            """);
        var result = await Direct(script).Run("employee", "Private owner brief 🦉", TimeSpan.FromSeconds(5), CancellationToken.None,
            "openclaw", "agent", "--message-file", "/dev/stdin", exit.ToString());
        Assert.Equal(exit, result.Exit);
        using var response = JsonDocument.Parse(result.Output);
        Assert.Equal("Private owner brief 🦉", response.RootElement.GetProperty("text").GetString());
        Assert.False(File.Exists(response.RootElement.GetProperty("file").GetString()));
        if (!OperatingSystem.IsWindows()) Assert.Equal(384, response.RootElement.GetProperty("mode").GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledDirectCommand_ActuallyStopsItsOwnedProcess(bool filePrompt)
    {
        var pidFile = Path.Combine(root, "pid");
        var script = Path.Combine(root, "waiting.mjs");
        await File.WriteAllTextAsync(script, """
            import {writeFileSync} from 'node:fs';
            const args = process.argv.slice(2);
            writeFileSync(args.at(-1), JSON.stringify({pid:process.pid, file:args.includes('--message-file') ? args[args.indexOf('--message-file') + 1] : null}));
            setInterval(() => {}, 1000);
            """);
        var transport = Direct(script);
        using var cancel = new CancellationTokenSource();
        var running = filePrompt
            ? transport.Run("employee", "Private canceled brief", TimeSpan.FromSeconds(20), cancel.Token, "openclaw", "agent", "--message-file", "/dev/stdin", pidFile)
            : transport.Run("employee", null, TimeSpan.FromSeconds(20), cancel.Token, "openclaw", pidFile);
        for (var i = 0; i < 100 && !File.Exists(pidFile); i++) await Task.Delay(20);
        Assert.True(File.Exists(pidFile), "The fictional worker must actually start before cancellation.");
        using var started = JsonDocument.Parse(await File.ReadAllTextAsync(pidFile));
        using var process = Process.GetProcessById(started.RootElement.GetProperty("pid").GetInt32());
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(process.HasExited, "Canceled worker remained alive.");
        if (filePrompt) Assert.False(File.Exists(started.RootElement.GetProperty("file").GetString()));
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
