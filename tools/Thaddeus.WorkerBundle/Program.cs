using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

if (args.Length != 3 || args[2] is not ("win-x64" or "linux-x64"))
    throw new ArgumentException("Usage: WorkerBundle PINNED_INSTALLATION_JSON NEW_WORKER_DIRECTORY win-x64|linux-x64");
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };
try
{
    var source = Path.GetFullPath(args[0]); Store.AssertNoLinks(source);
    await using var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
    if (file.Length is < 1 or > 64000) throw new ArgumentException("Use a bounded pinned installation file.");
    using var json = await JsonDocument.ParseAsync(file, new() { MaxDepth = 8 }, stop.Token);
    if (json.RootElement.GetProperty("kind").GetString() != "qemu") throw new ArgumentException("Use the explicit operator QEMU installation format.");
    var installation = json.RootElement.GetProperty("installation").Deserialize<QemuInstallation>(Wire.Json)!;
    Console.WriteLine("Preparing an independent worker bundle. The raven carries copies, never your credentials.");
    var receipt = await WorkerBundlePreparation.Create(installation, Path.GetFullPath(args[1]), args[2], stop.Token);
    Console.WriteLine(Wire.Pack(receipt));
}
catch (Exception error) when (error is not OutOfMemoryException)
{
    Console.Error.WriteLine("The bundle was not completed. Preserve its partial directory; the original estate is untouched. " + error.Message);
    Environment.ExitCode = 1;
}
