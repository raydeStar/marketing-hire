using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Thaddeus.Host;

/// <summary>Renders a page that builds itself with JavaScript (pricing tables, most often) using a Chromium-family
/// browser already on the machine, headless, with a throwaway profile. Every connection the page makes goes through
/// <see cref="PublicOnlyProxy"/>, so a page can reach public HTTPS sites only: never this host, the LAN or loopback.
/// No browser installed means no rendering; the static reader's answer stands.</summary>
public static class PageRenderer
{
    static readonly SemaphoreSlim One = new(1, 1);
    static string? configured;

    /// <summary>An explicit browser path from configuration ("off" disables rendering), else the first one found.</summary>
    public static void Configure(string? path) => configured = path;

    public static string? Browser()
    {
        if (configured is "off") return null;
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;
        string[] candidates = OperatingSystem.IsWindows()
            ? [@"C:\Program Files\Google\Chrome\Application\chrome.exe", @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
               @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"]
            : OperatingSystem.IsMacOS()
            ? ["/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge", "/Applications/Chromium.app/Contents/MacOS/Chromium"]
            : ["/usr/bin/chromium", "/usr/bin/chromium-browser", "/usr/bin/google-chrome", "/usr/bin/microsoft-edge"];
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>A PNG screenshot of an https page at a desktop size, through the same public-only proxy, or null when no browser is available.</summary>
    public static async Task<byte[]?> Screenshot(Uri page, CancellationToken cancellation, int width = 1280, int height = 800)
    {
        if (page.Scheme != Uri.UriSchemeHttps || Browser() is null) return null;
        var file = Path.Combine(Path.GetTempPath(), "fe-shot-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            await Run(page, ["--hide-scrollbars", $"--window-size={width},{height}", "--screenshot=" + file], cancellation);
            return File.Exists(file) && new FileInfo(file).Length is > 100 and < 8_000_000 ? await File.ReadAllBytesAsync(file, cancellation) : null;
        }
        finally { try { File.Delete(file); } catch (IOException) { } }
    }

    /// <summary>The rendered DOM of an https page, or null when no browser is available. One render at a time, 25 seconds at most.</summary>
    public static async Task<string?> Render(Uri page, CancellationToken cancellation)
    {
        if (page.Scheme != Uri.UriSchemeHttps || Browser() is null) return null;
        return await Run(page, ["--dump-dom"], cancellation);
    }

    static async Task<string> Run(Uri page, string[] mode, CancellationToken cancellation)
    {
        var browser = Browser()!;
        await One.WaitAsync(cancellation);
        var profile = Path.Combine(Path.GetTempPath(), "fe-render-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var proxy = PublicOnlyProxy.Start();
            var start = new ProcessStartInfo(browser) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
            foreach (var argument in new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--disable-sync",
                "--disable-background-networking", "--disable-component-update", "--disable-quic", "--mute-audio", "--force-webrtc-ip-handling-policy=disable_non_proxied_udp",
                $"--proxy-server=http://127.0.0.1:{proxy.Port}", "--proxy-bypass-list=<-loopback>", "--user-data-dir=" + profile,
                "--virtual-time-budget=8000" }.Concat(mode).Append(page.AbsoluteUri))
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("The browser did not start.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            _ = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } throw new IOException("The page took too long to render."); }
            var html = await output;
            return html.Length > 4_000_000 ? html[..4_000_000] : html;
        }
        finally
        {
            One.Release();
            for (var attempt = 0; attempt < 5 && Directory.Exists(profile); attempt++)
            {
                try { Directory.Delete(profile, recursive: true); }
                catch (IOException) { await Task.Delay(400, CancellationToken.None); }
                catch (UnauthorizedAccessException) { await Task.Delay(400, CancellationToken.None); }
            }
        }
    }
}

/// <summary>A loopback CONNECT proxy for the rendering browser: port 443 to public IPv4 addresses only, a bounded number
/// of tunnels and bytes. Plain-HTTP requests, private or loopback targets and anything else are refused.</summary>
public sealed class PublicOnlyProxy : IDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource stop = new();
    int tunnels; long bytes;
    public const int MaxTunnels = 120;
    public const long MaxBytes = 40_000_000;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public static PublicOnlyProxy Start() { var proxy = new PublicOnlyProxy(); proxy.listener.Start(); _ = proxy.Accept(); return proxy; }

    async Task Accept()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop.Token); }
            catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
            _ = Serve(client);
        }
    }

    async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var head = await ReadHead(stream);
                var parts = head?.Split(' ', 3);
                if (parts is not ["CONNECT", var authority, _] || Interlocked.Increment(ref tunnels) > MaxTunnels || await Target(authority, stop.Token) is not { } address)
                { await Refuse(stream); return; }
                using var upstream = new TcpClient(AddressFamily.InterNetwork);
                using var connect = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                connect.CancelAfter(TimeSpan.FromSeconds(10));
                await upstream.ConnectAsync(new IPEndPoint(address, 443), connect.Token);
                await stream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), stop.Token);
                var remote = upstream.GetStream();
                await Task.WhenAny(Pump(stream, remote), Pump(remote, stream));
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    /// <summary>"host:443" to a public IPv4 address, or null. Literal IP targets are resolved the same way and must be public too.</summary>
    public static async Task<IPAddress?> Target(string authority, CancellationToken cancellation)
    {
        var colon = authority.LastIndexOf(':');
        if (colon <= 0 || authority[(colon + 1)..] != "443") return null;
        var host = authority[..colon].Trim('[', ']');
        if (host.Length is 0 or > 253 || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, cancellation);
            return addresses.FirstOrDefault(MeetingSourceReader.PublicIPv4);
        }
        catch (SocketException) { return null; }
    }

    static async Task<string?> ReadHead(NetworkStream stream)
    {
        var buffer = new byte[4096]; var count = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count, 1), timeout.Token);
            if (read == 0) return null;
            count++;
            if (count >= 4 && buffer[count - 4] == '\r' && buffer[count - 3] == '\n' && buffer[count - 2] == '\r' && buffer[count - 1] == '\n')
                return Encoding.ASCII.GetString(buffer, 0, count).Split("\r\n")[0];
        }
        return null;
    }

    static async Task Refuse(NetworkStream stream)
    {
        try { await stream.WriteAsync("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray()); } catch (IOException) { }
    }

    async Task Pump(NetworkStream from, NetworkStream to)
    {
        var buffer = new byte[16384];
        while (true)
        {
            var read = await from.ReadAsync(buffer, stop.Token);
            if (read == 0 || Interlocked.Add(ref bytes, read) > MaxBytes) return;
            await to.WriteAsync(buffer.AsMemory(0, read), stop.Token);
        }
    }

    public void Dispose() { stop.Cancel(); listener.Stop(); stop.Dispose(); }
}
