using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public static class PackageMaintenance
{
    public static async Task<int> Run(string[] args)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            if (args.Length != 3 || args[0] != "--verify-package") throw new ArgumentException("Use --verify-package APPLICATION_FOLDER EXPECTED_MANIFEST_SHA256.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(args[2], "\\A[a-f0-9]{64}\\z")) throw new ArgumentException("A reviewed package hash is required.");
            var receipt = await ApplicationPackage.Verify(args[1], deadline.Token, args[2]);
            Console.WriteLine(Wire.Pack(receipt));
            Console.Error.WriteLine("Application files verified. The raven has checked the packing list; publisher trust remains yours.");
            return 0;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException or OperationCanceledException)
        {
            Console.Error.WriteLine("Application verification stopped. The study stays untouched. " + error.Message);
            return 1;
        }
    }
}
