using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Client;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

// One private stdio client owns one dedicated Chrome profile. Nothing listens on a browser-control port.
public sealed class ManagedBrowserSession(Store store, IConfiguration configuration) : IBrowserSession
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private McpClient? client;
    private StdioClientTransport? transport;
    private string? activeId;
    private BrowserTaskScope? scope;
    private readonly string? fixtureInitPage;
    private readonly string? fixtureProfileRoot;
    internal Action<string>? FixtureTrace { get; set; }
    internal ManagedBrowserSession(Store store, IConfiguration configuration, string fixtureInitPage, string fixtureProfileRoot)
        : this(store, configuration) { this.fixtureInitPage = fixtureInitPage; this.fixtureProfileRoot = fixtureProfileRoot; }
    private string RuntimeRoot => Path.GetFullPath(configuration["Thaddeus:BrowserRuntime"] ?? Path.Combine(AppContext.BaseDirectory, "browser-runtime"));
    private string ProfileRoot => fixtureProfileRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thaddeus", "BrowserProfiles",
        Wire.Hash(Path.GetFullPath(store.Root).ToUpperInvariant())[..32]);
    public bool Available => OperatingSystem.IsWindows() && File.Exists(Path.Combine(RuntimeRoot, "node.exe")) &&
        File.Exists(Path.Combine(RuntimeRoot, "node_modules", "@playwright", "mcp", "cli.js")) && ChromePath() != null;
    public bool IsOpen(string sessionId) => activeId == sessionId && client != null;

    private static string? ChromePath() => new[] {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
    }.FirstOrDefault(File.Exists);

    public async Task<BrowserPage> Start(string sessionId, BrowserTaskScope requested, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            if (activeId != null) throw new InvalidOperationException("Close the current browser task before starting another.");
            if (!Regex.IsMatch(sessionId, @"\A[a-f0-9]{32}\z")) throw new ArgumentException("Invalid browser session.");
            if (!Available) throw new InvalidOperationException("Install Chrome and use a Thaddeus package with the browser runtime included.");
            scope = BrowserTaskPolicy.ValidateScope(requested);
            var work = Path.Combine(ProfileRoot, "sessions", sessionId); Directory.CreateDirectory(work);
            var config = Path.Combine(work, "browser-config.json");
            await File.WriteAllTextAsync(config, Wire.Pack(new {
                browser = new { browserName = "chromium", userDataDir = Path.Combine(ProfileRoot, "chrome"),
                    launchOptions = new { channel = "chrome", executablePath = ChromePath(), headless = fixtureInitPage != null },
                    initPage = fixtureInitPage == null ? Array.Empty<string>() : [fixtureInitPage],
                    contextOptions = new { acceptDownloads = false, serviceWorkers = "block" } },
                webmcp = false, saveSession = false, allowUnrestrictedFileAccess = false,
                outputDir = Path.Combine(work, "output"), outputMaxSize = 1_000_000,
                console = new { level = "error" }, imageResponses = "omit", codegen = "none",
                timeouts = new { action = 5000, navigation = 20000, idle = 0 }
            }), cancellation);
            var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
            environment["PATH"] = RuntimeRoot + ";" + Environment.GetFolderPath(Environment.SpecialFolder.System);
            transport = new(new() {
                // SDK 2.2 wraps Windows commands in cmd /c, which drops the first quote for paths with spaces.
                // Use a fixed executable name on a host-controlled PATH and disable cmd autorun/expansion.
                Command = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                Arguments = ["/d", "/v:off", "/c", "node.exe", Path.Combine(RuntimeRoot, "node_modules", "@playwright", "mcp", "cli.js"), "--config", config],
                Name = "Thaddeus dedicated Chrome", WorkingDirectory = work,
                InheritEnvironmentVariables = false, EnvironmentVariables = environment,
                ShutdownTimeout = TimeSpan.FromSeconds(10)
            });
            activeId = sessionId;
            try
            {
                client = await McpClient.CreateAsync(transport, cancellationToken: cancellation);
                await Call("browser_navigate", new() { ["url"] = scope.StartUrl }, cancellation);
                return await Snapshot(cancellation);
            }
            catch { await CloseCore(); throw; }
        }
        finally { gate.Release(); }
    }

    public async Task<BrowserPage> Observe(string sessionId, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try { RequireSession(sessionId); return await Snapshot(cancellation); }
        catch (OperationCanceledException) { await CloseCore(); throw; }
        finally { gate.Release(); }
    }

    public async Task<BrowserPage> Act(string sessionId, BrowserAction action, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            RequireSession(sessionId);
            var current = await Snapshot(cancellation);
            BrowserTaskPolicy.ValidateAction(scope!, current, action);
            if (action.Kind == "snapshot") return current;
            if (action.Kind == "navigate") await Call("browser_navigate", new() { ["url"] = action.Url }, cancellation);
            else
            {
                // This fixed host-authored function is never supplied by the model. It refuses credential fields,
                // files, editable secrets and browser-wide keys, and acts only on the reviewed snapshot reference.
                var code = "async page => { const a = " + Wire.Pack(action) + "; const origins = " + Wire.Pack(scope!.Hosts.Select(host => "https://" + host)) + "; " + """
                    const element = page.locator('aria-ref=' + a.target);
                    const handle = await element.elementHandle();
                    if (!handle) throw new Error('The reviewed element is gone.');
                    const frame = await handle.ownerFrame();
                    const url = frame ? frame.url() : '';
                    if (!origins.some(origin => url === origin || url.startsWith(origin + '/') || url.startsWith(origin + '?')))
                      throw new Error('The target frame is outside the reviewed websites.');
                    const secret = await element.evaluate(el => {
                      const type = (el.getAttribute('type') || '').toLowerCase();
                      const hint = ['autocomplete','name','id','aria-label'].map(key => el.getAttribute(key) || '').join(' ');
                      return type === 'password' || type === 'file' || /password|secret|token|one-time-code|\botp\b|cc-|card.?number|\bcvv\b|\bcvc\b/i.test(hint);
                    });
                    if (secret) throw new Error('Take over to enter credentials or select files.');
                    if (a.kind === 'click') await element.click({timeout:5000});
                    else if (a.kind === 'type') await element.fill(a.text,{timeout:5000});
                    else if (a.kind === 'select') await element.selectOption(a.values,{timeout:5000});
                    else if (a.kind === 'key') await element.press(a.key,{timeout:5000});
                    else throw new Error('Unavailable action');
                    return {dispatched:true}; }
                    """;
                await Call("browser_run_code_unsafe", new() { ["code"] = code }, cancellation);
            }
            return await Snapshot(cancellation);
        }
        catch (OperationCanceledException) { await CloseCore(); throw; }
        finally { gate.Release(); }
    }

    private void RequireSession(string sessionId)
    {
        if (client == null || activeId != sessionId || scope == null)
            throw new InvalidOperationException("This browser session ended. Start a newly reviewed task; old actions cannot resume.");
    }

    private async Task<string> Call(string name, Dictionary<string, object?> arguments, CancellationToken cancellation)
    {
        var result = await client!.CallToolAsync(name, arguments, cancellationToken: cancellation);
        if (fixtureInitPage != null) FixtureTrace?.Invoke(name + ": " + Wire.Pack(result));
        // Never forward MCP-provided instructions, attachments, resource paths or console logs as host instructions.
        if (result.IsError == true) throw new InvalidOperationException("The browser action failed. Inspect the page before another attempt; the outcome is not retried automatically.");
        var json = JsonSerializer.SerializeToElement(result, Wire.Json);
        var text = string.Join("\n", json.GetProperty("content").EnumerateArray()
            .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "text")
            .Select(item => item.GetProperty("text").GetString()));
        if (text.Length > 100_000) throw new InvalidOperationException("Browser output exceeded the bounded reading limit.");
        return text;
    }

    private async Task<BrowserPage> Snapshot(CancellationToken cancellation)
    {
        // Do not ask for an accessibility snapshot of a sign-in/payment form. Upstream snapshots include
        // password input values. A closed curtain is preferable to a politely redacted password leak.
        var guard = await Call("browser_run_code_unsafe", new() { ["code"] = "async page => { const origins = " +
            Wire.Pack(scope!.Hosts.Select(host => "https://" + host)) + "; " + """
            let restricted = false;
            for (const frame of page.frames()) {
              const url = frame.url();
              if (url !== 'about:blank' && !origins.some(origin => url === origin || url.startsWith(origin + '/') || url.startsWith(origin + '?'))) { restricted = true; break; }
              if (await frame.locator('input').evaluateAll(elements => elements.some(el => {
                const type = (el.getAttribute('type') || '').toLowerCase();
                const hint = ['autocomplete','name','id','aria-label'].map(key => el.getAttribute(key) || '').join(' ');
                return type === 'password' || /password|secret|token|one-time-code|\botp\b|cc-|card.?number|\bcvv\b|\bcvc\b/i.test(hint);
              }))) { restricted = true; break; }
            }
            return {url:page.url(), restricted};
            }
            """ }, cancellation);
        var result = Regex.Match(guard, @"### Result\s*([\s\S]*?)(?=\r?\n###|\z)").Groups[1].Value.Trim();
        using var parsed = JsonDocument.Parse(result);
        if (parsed.RootElement.GetProperty("restricted").GetBoolean())
            return BrowserTaskPolicy.Page(scope, parsed.RootElement.GetProperty("url").GetString()!, "Manual attention needed",
                "Sensitive form or an embedded website outside the approved scope. Page content is withheld from the AI. Ask the owner to Take over in Chrome, then resume from an approved page after sign-in.");
        return ParseSnapshot(scope, await Call("browser_snapshot", new(), cancellation));
    }

    internal static BrowserPage ParseSnapshot(BrowserTaskScope scope, string text)
    {
        var url = Regex.Match(text, @"(?m)^- Page URL: (.+)$").Groups[1].Value.Trim();
        var title = Regex.Match(text, @"(?m)^- Page Title: (.*)$").Groups[1].Value.Trim();
        var snapshot = Regex.Match(text, @"### Snapshot\s*```yaml\r?\n([\s\S]*?)\r?\n```");
        if (url == "" || !snapshot.Success) throw new InvalidOperationException("The browser did not return a usable page snapshot. Take over to inspect it.");
        return BrowserTaskPolicy.Page(scope, url, title, snapshot.Groups[1].Value);
    }

    public async Task Close(string sessionId)
    {
        await gate.WaitAsync();
        try { if (activeId == sessionId) await CloseCore(); }
        finally { gate.Release(); }
    }

    private async Task CloseCore()
    {
        try
        {
            if (client != null)
            {
                // Explicitly close Chrome before the stdio process. Otherwise a clean Node exit can orphan it.
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await client.CallToolAsync("browser_close", cancellationToken: timeout.Token); }
                finally { await client.DisposeAsync(); }
            }
        }
        finally { client = null; transport = null; activeId = null; scope = null; }
    }
    public async ValueTask DisposeAsync() { await gate.WaitAsync(); try { await CloseCore(); } finally { gate.Release(); } }
}
