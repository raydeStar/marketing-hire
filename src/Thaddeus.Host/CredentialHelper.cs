using System.Text;
using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public sealed record VaultRequest(string Operation, string Scope, string Id, string? Value = null);
public sealed record VaultReply(bool Ok, string? Value = null);

public static class CredentialHelper
{
    public static async Task<int> Run()
    {
        try
        {
            using var input = Console.OpenStandardInput(); using var bytes = new MemoryStream(); var buffer = new byte[1024];
            while (await input.ReadAsync(buffer) is var count && count != 0)
            {
                if (bytes.Length + count > 12_000) throw new ArgumentException("Credential request is too large.");
                bytes.Write(buffer, 0, count);
            }
            var request = JsonSerializer.Deserialize<VaultRequest>(bytes.ToArray(), Wire.Json) ?? throw new ArgumentException("Missing operation.");
            var value = NativeCredentialVault.Execute(request.Operation, request.Scope, request.Id, request.Value);
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.Write(Wire.Pack(new VaultReply(true, value))); return 0;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException or JsonException or IOException)
        {
            // This pipe carries a capability response. Neither native error text nor the submitted secret belongs in logs.
            Console.Write(Wire.Pack(new VaultReply(false))); return 1;
        }
    }
}
