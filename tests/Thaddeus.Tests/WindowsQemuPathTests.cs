using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class WindowsQemuPathTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-path-" + Guid.NewGuid().ToString("N"));
    public WindowsQemuPathTests() => Directory.CreateDirectory(root);

    [Fact] public void WindowsAliasesAddressTheSameExistingAndNewFiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(root, "Worker – 移動"); Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "kernel"); File.WriteAllText(input, "The same immutable input.");
        string alias;
        try { alias = WindowsQemuPath.Existing(input); }
        catch (WindowsQemuPathException) { Assert.Equal("The same immutable input.", File.ReadAllText(input)); return; }
        Assert.All(alias, character => Assert.InRange((int)character, 0, 127));
        Assert.Equal(File.ReadAllText(input), File.ReadAllText(alias));
        var next = Path.Combine(directory, "worker.qcow2"); var output = WindowsQemuPath.NewFile(next);
        File.WriteAllText(output, "Created through the same directory.");
        Assert.Equal("Created through the same directory.", File.ReadAllText(next));
        Assert.Equal("Created through the same directory.", File.ReadAllText(WindowsQemuPath.Existing(next)));
    }

    [Fact] public void MissingOrStillUnicodeAliasesRefuseWithoutChangingFiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        var file = Path.Combine(root, "移動"); File.WriteAllText(file, "Keep this file.");
        Assert.Throws<WindowsQemuPathException>(() => WindowsQemuPath.Existing(file, _ => null));
        Assert.Throws<WindowsQemuPathException>(() => WindowsQemuPath.Existing(file, _ => file));
        Assert.Throws<WindowsQemuPathException>(() => WindowsQemuPath.Existing(file, _ => "relative"));
        Assert.Equal("Keep this file.", File.ReadAllText(file));
        Assert.Throws<FileNotFoundException>(() => WindowsQemuPath.Existing(Path.Combine(root, "missing")));
    }

    [Fact] public async Task SetupExplainsAnUnavailableAliasAndKeepsAdmissionDisabled()
    {
        using var store = new Store(Path.Combine(root, "study"));
        var setup = new HostWorkerSetup(store, new("qemu-whpx", "Windows worker", new string('a', 64), true),
            inspect: (_, _) => throw new WindowsQemuPathException());
        var result = await setup.Check(default);
        Assert.False(result.Enabled); Assert.False(result.CanEnable); Assert.False(result.LastCheck!.Passed);
        Assert.Contains("short path", result.LastCheck.Summary);
        Assert.Equal("windows-paths", Assert.Single(result.LastCheck.Checks).Id);
    }

    public void Dispose() => Directory.Delete(root, true);
}
