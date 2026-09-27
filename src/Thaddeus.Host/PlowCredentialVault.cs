using System.Security.Cryptography;
using System.Text;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

/// <summary>A headless Plow VM has no desktop Secret Service. Keep encrypted keys outside study exports.</summary>
internal sealed class PlowCredentialVault : ICredentialVault
{
    private readonly string root;
    private readonly object gate = new();
    public string Name => "Encrypted Plow volume";

    internal PlowCredentialVault(string directory)
    {
        root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked credential directories are refused.");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void RefuseLink(string file)
    {
        if (File.Exists(file) && File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked credential files are refused.");
    }

    private static FileStream PrivateFile(string file)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(file, options);
    }

    private byte[] Key()
    {
        var file = Path.Combine(root, "key"); RefuseLink(file);
        if (!File.Exists(file))
        {
            // Never replace a key on corruption: that would strand every saved connection.
            var key = RandomNumberGenerator.GetBytes(32);
            try { using var stream = PrivateFile(file); stream.Write(key); stream.Flush(flushToDisk: true); }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        var stored = File.ReadAllBytes(file);
        if (stored.Length != 32) { CryptographicOperations.ZeroMemory(stored); throw new InvalidOperationException("The Plow credential key is unreadable. Restore the retained volume key."); }
        return stored;
    }

    public Task<string?> Execute(string operation, string scope, string id, string? value, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!NativeCredentialVault.Identifier(scope) || !NativeCredentialVault.Identifier(id) || operation is not ("read" or "write" or "forget"))
            throw new ArgumentException("Invalid credential operation.");
        if (operation == "write" && (value == null || Encoding.UTF8.GetByteCount(value) > 2500 || value.Contains('\0')))
            throw new ArgumentException("Invalid credential value.");
        lock (gate)
        {
            var file = Path.Combine(root, scope + "-" + id); RefuseLink(file);
            if (operation == "forget") { File.Delete(file); return Task.FromResult<string?>(null); }
            if (operation == "read" && !File.Exists(file)) return Task.FromResult<string?>(null);
            var key = Key();
            byte[]? plain = null;
            try
            {
                using var aes = new AesGcm(key, 16);
                var binding = Encoding.UTF8.GetBytes(scope + "/" + id);
                if (operation == "read")
                {
                    var envelope = File.ReadAllBytes(file);
                    if (envelope.Length is < 29 or > 2529 || envelope[0] != 1) throw new CryptographicException();
                    plain = new byte[envelope.Length - 29];
                    aes.Decrypt(envelope.AsSpan(1, 12), envelope.AsSpan(29), envelope.AsSpan(13, 16), plain, binding);
                    return Task.FromResult<string?>(Encoding.UTF8.GetString(plain));
                }
                plain = Encoding.UTF8.GetBytes(value!);
                var saved = new byte[29 + plain.Length]; saved[0] = 1;
                RandomNumberGenerator.Fill(saved.AsSpan(1, 12));
                aes.Encrypt(saved.AsSpan(1, 12), plain, saved.AsSpan(29), saved.AsSpan(13, 16), binding);
                var temporary = Path.Combine(root, "new-" + Guid.NewGuid().ToString("N"));
                try
                {
                    using (var stream = PrivateFile(temporary)) { stream.Write(saved); stream.Flush(flushToDisk: true); }
                    File.Move(temporary, file, overwrite: true);
                }
                finally { File.Delete(temporary); }
                return Task.FromResult<string?>(null);
            }
            catch (CryptographicException) { throw new InvalidOperationException("The saved Plow credential cannot be authenticated. No credential was sent."); }
            finally { CryptographicOperations.ZeroMemory(key); if (plain != null) CryptographicOperations.ZeroMemory(plain); }
        }
    }
}
