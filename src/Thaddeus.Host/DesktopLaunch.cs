using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

/// <summary>The portable foreground entry point uses the bundled runtime and the ordinary host.</summary>
public sealed record DesktopLaunch(string Package, string Data, string Origin, int WorkerPort, string? Installation, bool NoBrowser)
{
    private sealed record Profile(int SchemaVersion, string? DataDirectory, string? LocalOrigin, int? WorkerPort, string? DevelopmentWorkerInstallation);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };

    public static DesktopLaunch? Parse(string[] arguments, string package)
    {
        if (arguments.Length == 0 || arguments[0] != "--desktop") return null;
        string? profilePath = null; var noBrowser = false;
        for (var i = 1; i < arguments.Length; i++)
        {
            if (arguments[i] == "--no-browser" && !noBrowser) noBrowser = true;
            else if (arguments[i] == "--launch-profile" && profilePath == null && i + 1 < arguments.Length) profilePath = arguments[++i];
            else throw new ArgumentException("Use --desktop [--no-browser] [--launch-profile /absolute/path.json].");
        }
        package = Path.TrimEndingDirectorySeparator(Path.GetFullPath(package));
        Store.AssertNoLinks(package);
        if (!File.Exists(Path.Combine(package, "wwwroot", "index.html"))) throw new ArgumentException("Open Thaddeus from a complete published package.");
        if (profilePath != null && !Path.IsPathFullyQualified(profilePath)) throw new ArgumentException("Use an absolute launch profile path.");
        var selected = profilePath ?? Path.Combine(package, "launch.json");
        PlainFile(selected);
        Profile? profile = null;
        if (File.Exists(selected))
        {
            if (new FileInfo(selected).Length > 16_000) throw new ArgumentException("The launch profile is too large.");
            var text = File.ReadAllText(selected);
            using var document = JsonDocument.Parse(text, new() { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1))
                throw new ArgumentException("Use one unambiguous object for the launch profile.");
            profile = JsonSerializer.Deserialize<Profile>(text, Json);
            if (profile?.SchemaVersion != 1) throw new ArgumentException("Unsupported launch profile.");
        }
        else if (profilePath != null) throw new ArgumentException("The selected launch profile does not exist.");
        var data = profile?.DataDirectory ?? DefaultData();
        if (!Path.IsPathFullyQualified(data)) throw new ArgumentException("The data folder must be an absolute path.");
        data = Path.TrimEndingDirectorySeparator(Path.GetFullPath(data));
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (data.Equals(package, comparison) || data.StartsWith(package + Path.DirectorySeparatorChar, comparison) || Path.GetPathRoot(data) == data)
            throw new ArgumentException("Keep private data outside the replaceable application package and filesystem root.");
        Store.AssertNoLinks(data);
        var origin = profile?.LocalOrigin ?? "http://localhost:5179";
        var address = NetworkBoundary.Origin(origin, true);
        if (address.Scheme != "http" || address.Port < 1024 || address.Host is not ("localhost" or "127.0.0.1" or "[::1]"))
            throw new ArgumentException("Use an exact HTTP localhost, 127.0.0.1 or [::1] origin with an unprivileged port.");
        var workerPort = profile?.WorkerPort ?? 5183;
        if (workerPort is < 1024 or > 65535 || workerPort == address.Port) throw new ArgumentException("Choose a separate unprivileged worker port.");
        var installation = profile?.DevelopmentWorkerInstallation;
        if (installation != null)
        {
            if (NativeWorkerPlatform.Backend == null)
                throw new ArgumentException("The development worker installation requires native Windows x64 or Linux x64. No fallback worker was started.");
            if (!Path.IsPathFullyQualified(installation) || !File.Exists(installation)) throw new ArgumentException("The configured development worker installation is missing.");
            PlainFile(installation);
        }
        return new(package, data, origin, workerPort, installation, noBrowser);
    }

    private static void PlainFile(string file)
    {
        Store.AssertNoLinks(file);
        if (new FileInfo(file).LinkTarget != null) throw new ArgumentException("Linked launch files are not permitted.");
    }

    private static string DefaultData()
    {
        if (OperatingSystem.IsMacOS()) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Thaddeus2");
        if (OperatingSystem.IsWindows()) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thaddeus2");
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (xdg != null && !Path.IsPathFullyQualified(xdg)) throw new ArgumentException("XDG_DATA_HOME must be an absolute path.");
        return Path.Combine(xdg ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "Thaddeus2");
    }

    public FileStream Acquire()
    {
        // Check the door before moving furniture. Kestrel still owns the final bind and refuses a later race.
        var listeners = new List<TcpListener>();
        try
        {
            var address = new Uri(Origin);
            var bindings = new List<(IPAddress Address, int Port)> { (IPAddress.Loopback, WorkerPort) };
            if (address.Host != "[::1]") bindings.Add((IPAddress.Loopback, address.Port));
            if (address.Host != "127.0.0.1" && Socket.OSSupportsIPv6) bindings.Add((IPAddress.IPv6Loopback, address.Port));
            foreach (var binding in bindings)
            {
                var listener = new TcpListener(binding.Address, binding.Port) { ExclusiveAddressUse = true };
                listeners.Add(listener); listener.Start();
            }
        }
        catch (SocketException) { throw new InvalidOperationException("A configured port is in use. Return to the running study or close its terminal before launching again. No process was stopped."); }
        finally { foreach (var listener in listeners) listener.Stop(); }

        Store.AssertNoLinks(Data);
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(Data);
        else
        {
            Directory.CreateDirectory(Data, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var mode = File.GetUnixFileMode(Data);
            if ((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                throw new InvalidOperationException("The private data folder must be accessible only to its owner (mode 700). Existing permissions were not changed.");
        }
        var lockPath = Path.Combine(Data, "launcher.lock"); PlainFile(lockPath);
        try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new InvalidOperationException("This data folder already has a launcher. Return to that study; no replacement was started."); }
    }

    public void Configure(WebApplicationBuilder builder)
    {
        // An inherited development shell must not silently expose the packaged study to the network.
        foreach (var item in builder.Configuration.AsEnumerable().Where(item => item.Key.StartsWith("Thaddeus:", StringComparison.OrdinalIgnoreCase) && !item.Key.Equals("Thaddeus:ApiKey", StringComparison.OrdinalIgnoreCase) && !item.Key.Equals("Thaddeus:ApiKeyEndpoint", StringComparison.OrdinalIgnoreCase)).ToArray())
            builder.Configuration[item.Key] = null;
        builder.Configuration["Thaddeus:Data"] = Data;
        builder.Configuration["Thaddeus:LocalOrigin"] = Origin;
        builder.Configuration["Thaddeus:WorkerPort"] = WorkerPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        builder.Configuration["Thaddeus:DevelopmentWorkerInstallation"] = Installation;
    }

    public void OpenBrowser(BrowserLaunchTickets tickets, ILogger logger)
    {
        logger.LogInformation("The study is open at {Origin}. Keep this terminal open; Ctrl+C stops the host. No model has been disturbed.", Origin);
        if (NoBrowser) return;
        try
        {
            var url = Origin + "/#launch=" + tickets.Issue().Ticket;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning("The browser could not be opened. Visit {Origin} and use host-key.txt in the private data folder. The host remains available.", Origin);
        }
    }
}
