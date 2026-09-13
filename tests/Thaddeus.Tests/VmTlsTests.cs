using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class VmTlsTests
{
    private static string Fresh() => Path.Combine(Path.GetTempPath(), "thaddeus-tls-" + Guid.NewGuid().ToString("N"));
    private static X509Certificate2 Certificate(VmTlsChannel channel)
    {
        using var ephemeral = X509Certificate2.CreateFromPemFile(Path.Combine(channel.CredentialsDirectory, "client-cert.pem"), Path.Combine(channel.CredentialsDirectory, "client-key.pem"));
        // Schannel needs a user key container. The loader removes its temporary container when disposed.
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.UserKeySet);
    }

    private static async Task<SslStream> Connect(VmTlsChannel target, X509Certificate2? certificate, CancellationToken cancellation)
    {
        var socket = new TcpClient(); await socket.ConnectAsync(IPAddress.Loopback, target.Port, cancellation);
        var stream = new SslStream(socket.GetStream(), false);
        using var authority = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(target.CredentialsDirectory, "ca-cert.pem"));
        var policy = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck, DisableCertificateDownloads = true };
        policy.CustomTrustStore.Add(authority);
        try
        {
            await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "127.0.0.1", ClientCertificates = certificate == null ? [] : new X509CertificateCollection { certificate },
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, AllowTlsResume = false, CertificateChainPolicy = policy
            }, cancellation);
            return stream;
        }
        catch { stream.Dispose(); socket.Dispose(); throw; }
    }

    [Fact] public async Task MutuallyAuthenticatedConnectionTransfersExactBytesAndDeletesPrivateKeys()
    {
        var root = Fresh(); string? keyName = null;
        using (var channel = new VmTlsChannel(root))
        using (var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        using (var certificate = Certificate(channel))
        {
            var expected = new List<string> { "ca-cert.pem", "client-cert.pem", "client-key.pem" };
            if (OperatingSystem.IsWindows()) expected.Add("server-key-name.txt");
            Assert.Equal(expected, Directory.GetFiles(root).Select(Path.GetFileName).Order().ToArray());
            if (OperatingSystem.IsWindows())
            {
                keyName = File.ReadAllText(Path.Combine(root, "server-key-name.txt"));
                Assert.True(CngKey.Exists(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider));
                using var key = CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider);
                Assert.Equal(CngExportPolicies.None, key.ExportPolicy);
                var security = new DirectoryInfo(root).GetAccessControl();
                Assert.True(security.AreAccessRulesProtected);
            }
            var accept = channel.Accept(limit.Token);
            using var client = await Connect(channel, certificate, limit.Token);
            var server = await accept;
            Assert.True(server.IsMutuallyAuthenticated); Assert.True(server.IsEncrypted);
            var payload = System.Security.Cryptography.RandomNumberGenerator.GetBytes(65536);
            var received = new byte[payload.Length]; var read = server.ReadExactlyAsync(received, limit.Token);
            await client.WriteAsync(payload, limit.Token); await read;
            Assert.Equal(payload, received); Assert.Equal(0, channel.RejectedConnections);
            await Assert.ThrowsAsync<InvalidOperationException>(() => channel.Accept(limit.Token));
        }
        Assert.False(Directory.Exists(root));
        if (OperatingSystem.IsWindows()) Assert.False(CngKey.Exists(keyName!, CngProvider.MicrosoftSoftwareKeyStorageProvider));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task MissingOrOtherBootCertificateCannotClaimConnection(bool otherBoot)
    {
        using var channel = new VmTlsChannel(Fresh()); using var other = new VmTlsChannel(Fresh());
        using var wrong = otherBoot ? Certificate(other) : null;
        using var correct = Certificate(channel);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var accept = channel.Accept(limit.Token);
        try
        {
            using var client = await Connect(channel, wrong, limit.Token);
            var buffer = new byte[1]; Assert.Equal(0, await client.ReadAsync(buffer, limit.Token));
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException) { }
        Assert.False(accept.IsCompletedSuccessfully);
        using var approved = await Connect(channel, correct, limit.Token);
        var server = await accept;
        Assert.True(server.IsMutuallyAuthenticated); Assert.Equal(1, channel.RejectedConnections);
    }
}
