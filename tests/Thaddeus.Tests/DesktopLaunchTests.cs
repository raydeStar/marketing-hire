using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Thaddeus.Host;

namespace Thaddeus.Tests;

public sealed class DesktopLaunchTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-desktop-" + Guid.NewGuid().ToString("N"));
    private string Package => Path.Combine(root, "package");
    private string Data => Path.Combine(root, "private data-é");
    public DesktopLaunchTests()
    {
        Directory.CreateDirectory(Path.Combine(Package, "wwwroot"));
        File.WriteAllText(Path.Combine(Package, "wwwroot", "index.html"), "A fictional packaged study.");
    }
    private string Profile(object settings)
    {
        var file = Path.Combine(root, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, JsonSerializer.Serialize(settings)); return file;
    }
    [Fact] public void OrdinaryHostArgumentsRemainTheOrdinaryEntryPoint() => Assert.Null(DesktopLaunch.Parse(["--Thaddeus:Data=example"], Package));
    [Fact] public void ProfileKeepsUnicodeDataOutsidePackage()
    {
        var file = Profile(new { schemaVersion = 1, dataDirectory = Data, localOrigin = "http://[::1]:5189", workerPort = 5190 });
        var launch = DesktopLaunch.Parse(["--desktop", "--no-browser", "--launch-profile", file], Package)!;
        Assert.Equal(Data, launch.Data); Assert.True(launch.NoBrowser); Assert.Equal("http://[::1]:5189", launch.Origin);
        Assert.False(Directory.Exists(Data));
    }
    [Theory]
    [InlineData("https://localhost:5189")]
    [InlineData("http://example.com:5189")]
    [InlineData("http://localhost:80")]
    [InlineData("http://localhost:5189/path")]
    [InlineData("http://localhost:5189/")]
    [InlineData("http://localhost:5189?q=x")]
    [InlineData("http://name@localhost:5189")]
    public void RefusesAmbiguousOrNonlocalOrigin(string origin)
    {
        var file = Profile(new { schemaVersion = 1, dataDirectory = Data, localOrigin = origin });
        Assert.Throws<ArgumentException>(() => DesktopLaunch.Parse(["--desktop", "--launch-profile", file], Package));
        Assert.False(Directory.Exists(Data));
    }
    [Theory]
    [InlineData("--unknown")]
    [InlineData("--launch-profile")]
    public void RefusesUnknownOrIncompleteCommand(string option) => Assert.Throws<ArgumentException>(() => DesktopLaunch.Parse(["--desktop", option], Package));
    [Fact] public void RefusesPackageDataAndPortCollision()
    {
        foreach (var directory in new[] { Package, Path.Combine(Package, "private"), "relative-data" })
        {
            var file = Profile(new { schemaVersion = 1, dataDirectory = directory });
            Assert.Throws<ArgumentException>(() => DesktopLaunch.Parse(["--desktop", "--launch-profile", file], Package));
        }
        var collision = Profile(new { schemaVersion = 1, dataDirectory = Data, workerPort = 5179 });
        Assert.Throws<ArgumentException>(() => DesktopLaunch.Parse(["--desktop", "--launch-profile", collision], Package));
    }
    [Theory]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"SchemaVersion\":1}")]
    [InlineData("[]")]
    public void RefusesUnsupportedOrAmbiguousProfile(string json)
    {
        var file = Path.Combine(root, "invalid.json"); File.WriteAllText(file, json);
        Assert.Throws<ArgumentException>(() => DesktopLaunch.Parse(["--desktop", "--launch-profile", file], Package));
    }
    [Fact] public void RefusesUnknownSettingsInsteadOfSilentlyExposingPhone()
    {
        var file = Profile(new { schemaVersion = 1, dataDirectory = Data, phoneOrigin = "https://192.0.2.99:7443" });
        Assert.Throws<JsonException>(() => DesktopLaunch.Parse(["--desktop", "--launch-profile", file], Package));
    }
    [Fact] public void OccupiedPortRefusalDoesNotCreateDataOrStopListener()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var launch = new DesktopLaunch(Package, Data, $"http://127.0.0.1:{port}", port == 5183 ? 5184 : 5183, null, true);
        Assert.Throws<InvalidOperationException>(() => launch.Acquire());
        Assert.True(listener.Server.IsBound); Assert.False(Directory.Exists(Data));
    }
    [Fact] public void UnixLinkedProfileIsRefused()
    {
        if (OperatingSystem.IsWindows()) return;
        var file = Profile(new { schemaVersion = 1, dataDirectory = Data });
        var link = Path.Combine(root, "linked.json"); File.CreateSymbolicLink(link, file);
        Assert.Throws<ArgumentException>(() => DesktopLaunch.Parse(["--desktop", "--launch-profile", link], Package));
    }
    public void Dispose() => Directory.Delete(root, true);
}
