using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

var archive = args.Length == 5 && args[0] == "archive";
if (!(archive || args.Length == 3) || args[^1] is not ("win-x64" or "linux-x64"))
    throw new ArgumentException("Usage: WorkerBundle PINNED_INSTALLATION_JSON NEW_WORKER_DIRECTORY RUNTIME, or WorkerBundle archive PINNED_INSTALLATION_JSON HOST_PACKAGE NEW_ZIP RUNTIME (win-x64|linux-x64).");
using var stop = new CancellationTokenSource();
stop.CancelAfter(TimeSpan.FromMinutes(10));
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };
try
{
    var source = Path.GetFullPath(args[archive ? 1 : 0]); Store.AssertNoLinks(source);
    await using var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
    if (file.Length is < 1 or > 64000) throw new ArgumentException("Use a bounded pinned installation file.");
    using var json = await JsonDocument.ParseAsync(file, new() { MaxDepth = 8 }, stop.Token);
    if (json.RootElement.GetProperty("kind").GetString() != "qemu") throw new ArgumentException("Use the explicit operator QEMU installation format.");
    var installation = json.RootElement.GetProperty("installation").Deserialize<QemuInstallation>(Wire.Json)!;
    if (archive)
    {
        var archivePath = Path.GetFullPath(args[3]);
        foreach (var suffix in new[] { ".manifest.json", ".receipt.json" })
        {
            Store.AssertNoLinks(archivePath + suffix);
            if (Path.Exists(archivePath + suffix)) throw new IOException("An archive sidecar already exists; choose a fresh output name.");
        }
        Console.WriteLine("Preparing one combined archive from pinned files. The raven will not stage another guest disk.");
        var previous = ""; var next = DateTimeOffset.MinValue;
        var receipt = await WorkerBundleArchive.Create(installation, Path.GetFullPath(args[2]), archivePath, args[4], stop.Token, progress =>
        {
            if (progress.Phase == previous && DateTimeOffset.UtcNow < next && progress.Files != progress.TotalFiles) return;
            previous = progress.Phase; next = DateTimeOffset.UtcNow.AddSeconds(5);
            Console.WriteLine(Wire.Pack(progress));
        });
        using (var zip = ZipFile.OpenRead(receipt.Archive))
        {
            var entry = zip.GetEntry("thaddeus-" + args[4] + "/package-manifest.json") ?? throw new IOException("The verified package manifest is missing.");
            if (entry.Length > 4_000_000) throw new IOException("The manifest exceeds its bound.");
            using var contents = entry.Open(); using var buffer = new MemoryStream(); await contents.CopyToAsync(buffer, stop.Token);
            var bytes = buffer.ToArray();
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != receipt.CombinedManifestSha256) throw new IOException("The verified package manifest changed.");
            await using var sidecar = new FileStream(archivePath + ".manifest.json", FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await sidecar.WriteAsync(bytes, stop.Token); sidecar.Flush(true);
        }
        await using (var sidecar = new FileStream(archivePath + ".receipt.json", FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { await JsonSerializer.SerializeAsync(sidecar, receipt, Wire.Json, stop.Token); sidecar.Flush(true); }
        Console.WriteLine(Wire.Pack(receipt));
    }
    else
    {
        Console.WriteLine("Preparing an independent worker bundle. The raven carries copies, never your credentials.");
        var receipt = await WorkerBundlePreparation.Create(installation, Path.GetFullPath(args[1]), args[2], stop.Token);
        Console.WriteLine(Wire.Pack(receipt));
    }
}
catch (Exception error) when (error is not OutOfMemoryException)
{
    Console.Error.WriteLine((archive ? "The archive could not be confirmed. Owned incomplete archive output is cleaned up; existing archives and inputs stay intact. " :
        "The bundle was not completed. Preserve its partial directory; the original estate is untouched. ") + error.Message);
    Environment.ExitCode = 1;
}
