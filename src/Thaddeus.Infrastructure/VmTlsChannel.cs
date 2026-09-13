using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

namespace Thaddeus.Infrastructure;

public sealed record VmCredentialRetirement(string Directory, string? ServerKey, bool ServerKeyExisted);

/// <summary>A single QEMU character-device connection, authenticated independently for each boot/channel.</summary>
public sealed class VmTlsChannel : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly X509Certificate2? authority, server, client;
    private CngKey? windowsServerKey;
    private readonly CancellationTokenSource closing = new();
    private readonly string directory;
    private int accepting, disposed, rejected;
    private SslStream? connection;
    public int Port { get; }
    public int RejectedConnections => Volatile.Read(ref rejected);
    public string CredentialsDirectory => directory;
    public string? NegotiatedProtocol => connection?.SslProtocol.ToString();
    public string ClientCertificateSha256 => Convert.ToHexStringLower(SHA256.HashData(client!.RawData));
    public string ServerCertificateSha256 => Convert.ToHexStringLower(SHA256.HashData(server!.RawData));

    public VmTlsChannel(string freshCredentialsDirectory)
    {
        directory = PrivateWorkerDirectory.Create(freshCredentialsDirectory);
        try
        {
            using var caKey = RSA.Create(2048);
            var caRequest = new CertificateRequest("CN=Thaddeus private channel " + Guid.NewGuid().ToString("N"), caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            var now = DateTimeOffset.UtcNow;
            authority = caRequest.CreateSelfSigned(now.AddMinutes(-1), now.AddMinutes(30));
            using var serverKey = CreateServerKey();
            using var clientKey = RSA.Create(2048);
            server = Issue("server", serverKey, authority, now); client = Issue("client", clientKey, authority, now);
            File.WriteAllText(Path.Combine(directory, "ca-cert.pem"), authority.ExportCertificatePem());
            File.WriteAllText(Path.Combine(directory, "client-cert.pem"), client.ExportCertificatePem());
            File.WriteAllText(Path.Combine(directory, "client-key.pem"), clientKey.ExportPkcs8PrivateKeyPem());
            // Windows Schannel requires a named user key; no server/CA key is exported to the QEMU directory.
            listener.Start(4); Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        catch { Dispose(); throw; }
    }

    private RSA CreateServerKey()
    {
        if (!OperatingSystem.IsWindows()) return RSA.Create(2048);
        var name = "thaddeus-channel-" + Guid.NewGuid().ToString("N");
        // Record ownership before creating the key so a later host can clean up after abrupt termination.
        File.WriteAllText(Path.Combine(directory, "server-key-name.txt"), name);
        var parameters = new CngKeyCreationParameters { Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
            KeyUsage = CngKeyUsages.Signing | CngKeyUsages.Decryption, ExportPolicy = CngExportPolicies.None };
        parameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
        windowsServerKey = CngKey.Create(CngAlgorithm.Rsa, name, parameters);
        return new RSACng(windowsServerKey);
    }

    private static X509Certificate2 Issue(string role, RSA key, X509Certificate2 authority, DateTimeOffset now)
    {
        var request = new CertificateRequest("CN=Thaddeus " + role, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(role == "server" ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, true));
        if (role == "server") { var names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback); request.CertificateExtensions.Add(names.Build()); }
        using var publicCertificate = request.Create(authority, now.AddSeconds(-30), now.AddMinutes(25), RandomNumberGenerator.GetBytes(16));
        return publicCertificate.CopyWithPrivateKey(key);
    }

    public async Task<SslStream> Accept(CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        if (Interlocked.Exchange(ref accepting, 1) != 0) throw new InvalidOperationException("A VM channel accepts one authenticated connection.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation, closing.Token);
        limit.CancelAfter(TimeSpan.FromSeconds(40));
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var socket = await listener.AcceptTcpClientAsync(limit.Token); socket.NoDelay = true;
            var stream = new SslStream(socket.GetStream(), leaveInnerStreamOpen: false);
            try
            {
                using var handshake = CancellationTokenSource.CreateLinkedTokenSource(limit.Token); handshake.CancelAfter(TimeSpan.FromSeconds(3));
                await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = server, ClientCertificateRequired = true,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    AllowRenegotiation = false, AllowTlsResume = false,
                    RemoteCertificateValidationCallback = (_, certificate, _, _) => ValidatePeer(certificate)
                }, handshake.Token);
                if (!stream.IsMutuallyAuthenticated || !stream.IsEncrypted) throw new AuthenticationException("VM channel authentication was not established.");
                connection = stream; listener.Stop(); return stream;
            }
            catch (Exception ex) when (ex is AuthenticationException or IOException or OperationCanceledException)
            {
                stream.Dispose(); socket.Dispose(); Interlocked.Increment(ref rejected);
                limit.Token.ThrowIfCancellationRequested();
            }
        }
        throw new AuthenticationException("VM channel authentication attempt limit reached.");
    }

    private bool ValidatePeer(X509Certificate? certificate)
    {
        if (certificate == null || !CryptographicOperations.FixedTimeEquals(certificate.GetRawCertData(), client!.RawData)) return false;
        using var peer = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(authority!);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.2"));
        return chain.Build(peer);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        closing.Cancel(); listener.Stop(); connection?.Dispose();
        authority?.Dispose(); server?.Dispose(); client?.Dispose();
        if (OperatingSystem.IsWindows() && windowsServerKey != null) { windowsServerKey.Delete(); windowsServerKey = null; }
        RetireAbandonedCredentials(directory);
        closing.Dispose();
    }

    /// <summary>Call only after the owning VM/host is known stopped, under the worker registry lock.</summary>
    public static VmCredentialRetirement RetireAbandonedCredentials(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Owned credential directory must be absolute.");
        // Known files only, without recursive deletion or following a replacement directory link.
        Store.AssertNoLinks(directory);
        var known = new[] { "ca-cert.pem", "client-cert.pem", "client-key.pem", "server-key-name.txt" };
        var entries = Directory.EnumerateFileSystemEntries(directory).Take(5).ToArray();
        foreach (var entry in entries)
        {
            Store.AssertNoLinks(entry);
            if (!known.Contains(Path.GetFileName(entry), StringComparer.Ordinal) ||
                (File.GetAttributes(entry) & FileAttributes.Directory) != 0 || new FileInfo(entry).Length > 16384)
                throw new IOException("Unexpected owned credential entry; cleanup requires inspection.");
        }
        var marker = Path.Combine(directory, "server-key-name.txt");
        string? name = null; var existed = false;
        if (OperatingSystem.IsWindows() && File.Exists(marker))
        {
            name = File.ReadAllText(marker);
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"\Athaddeus-channel-[a-f0-9]{32}\z")) throw new IOException("Invalid owned channel key identity.");
            existed = CngKey.Exists(name, CngProvider.MicrosoftSoftwareKeyStorageProvider);
            if (existed)
            { using var key = CngKey.Open(name, CngProvider.MicrosoftSoftwareKeyStorageProvider); key.Delete(); }
            if (CngKey.Exists(name, CngProvider.MicrosoftSoftwareKeyStorageProvider)) throw new IOException("Abandoned channel key retirement was not confirmed.");
        }
        foreach (var file in known) File.Delete(Path.Combine(directory, file));
        Directory.Delete(directory, recursive: false);
        return new(directory, name, existed);
    }
}

public static class PrivateWorkerDirectory
{
    public static string Create(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Worker directory must be absolute.");
        var full = Path.GetFullPath(path); Store.AssertNoLinks(full);
        if (Directory.Exists(full) || File.Exists(full)) throw new IOException("Worker directory must be new.");
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var owner = identity.User ?? throw new InvalidOperationException("Missing Windows user identity.");
            var security = new DirectorySecurity(); security.SetOwner(owner); security.SetAccessRuleProtection(true, false);
            foreach (var principal in new[] { owner, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                security.AddAccessRule(new FileSystemAccessRule(principal, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(full).Create(security);
        }
        else Directory.CreateDirectory(full, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return full;
    }
}
