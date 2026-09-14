using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed record BundledWorkerInputs(QemuPinnedFile Executable, QemuPinnedFile ImageTool,
    QemuPinnedFile Kernel, QemuPinnedFile Initrd, QemuPinnedFile BaseDisk, QemuRuntimePackage RuntimePackage);
public sealed record BundledWorkerDocument(int SchemaVersion, string Kind, string Runtime, BundledWorkerInputs Installation);

/// <summary>Resolves the installed bundle's own files. The browser never chooses the butler's executable.</summary>
public static class BundledWorkerInstallation
{
    public const string Kind = "thaddeus-worker-bundle";
    public static readonly string RelativeDescriptor = Path.Combine("worker", "installation.json");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };

    public static QemuInstallation Resolve(JsonElement document, string directory, string runtime)
    {
        if (runtime is not ("win-x64" or "linux-x64")) throw new ArgumentException("This host has no supported bundled worker.");
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Use an installed bundle directory.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)); Store.AssertNoLinks(root);
        Unique(document);
        var bundle = document.Deserialize<BundledWorkerDocument>(Json);
        if (bundle is not { SchemaVersion: 1, Kind: Kind, Installation: not null } || bundle.Runtime != runtime)
            throw new ArgumentException("The included worker targets a different host or bundle format.");
        var inputs = bundle.Installation;
        if (inputs.RuntimePackage == null) throw new ArgumentException("The included worker requires its complete runtime package.");
        var package = new QemuRuntimePackage(ResolvePath(root, inputs.RuntimePackage.Root), Pin(root, inputs.RuntimePackage.Manifest));
        var result = new QemuInstallation(Pin(root, inputs.Executable), Pin(root, inputs.ImageTool),
            Pin(root, inputs.Kernel), Pin(root, inputs.Initrd), Pin(root, inputs.BaseDisk), package);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (new[] { result.Executable, result.ImageTool }.Any(file => !file.Path.StartsWith(package.Root + Path.DirectorySeparatorChar, comparison)))
            throw new ArgumentException("Bundled executables must belong to the included runtime.");
        return result;
    }

    private static QemuPinnedFile Pin(string root, QemuPinnedFile? pin)
    {
        if (pin == null || pin.Sha256 == null || !Regex.IsMatch(pin.Sha256, "\\A[a-f0-9]{64}\\z"))
            throw new ArgumentException("Each included worker file needs its exact SHA-256 pin.");
        return new(ResolvePath(root, pin.Path), pin.Sha256);
    }

    private static string ResolvePath(string root, string? relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Length > 240 || relative.Any(character => char.IsControl(character) || "\\:*?\"<>|".Contains(character)) ||
            relative.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
                Regex.IsMatch(part, "\\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\\.|$)", RegexOptions.IgnoreCase)))
            throw new ArgumentException("Included worker paths must stay relative to their bundle.");
        var result = Path.GetFullPath(Path.Combine(root, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!result.StartsWith(root + Path.DirectorySeparatorChar, comparison)) throw new ArgumentException("Included worker path escaped its bundle.");
        Store.AssertNoLinks(result); return result;
    }

    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ArgumentException("The worker bundle has ambiguous duplicate fields.");
                Unique(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) Unique(child);
    }
}
