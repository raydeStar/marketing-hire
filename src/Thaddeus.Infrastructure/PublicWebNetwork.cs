using System.Net;
using System.Net.Sockets;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public static class PublicWebNetwork
{
    // Conservative exclusions from the IANA special-purpose registries, plus multicast.
    // Reviewed 2026-09-12. Public reachability is necessary, never proof that a page is trustworthy.
    private static readonly IPNetwork[] ExcludedV4 = new[]
    {
        "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12",
        "192.0.0.0/24", "192.0.2.0/24", "192.88.99.0/24", "192.168.0.0/16", "198.18.0.0/15",
        "198.51.100.0/24", "203.0.113.0/24", "224.0.0.0/4", "240.0.0.0/4"
    }.Select(IPNetwork.Parse).ToArray();
    private static readonly IPNetwork GlobalV6 = IPNetwork.Parse("2000::/3");
    private static readonly IPNetwork[] ExcludedV6 = new[] { "2001::/23", "2001:db8::/32", "2002::/16", "3fff::/20" }.Select(IPNetwork.Parse).ToArray();

    public static bool IsPublic(IPAddress address) => address.AddressFamily switch
    {
        AddressFamily.InterNetwork => !ExcludedV4.Any(range => range.Contains(address)),
        AddressFamily.InterNetworkV6 => address.ScopeId == 0 && !address.IsIPv4MappedToIPv6 &&
            GlobalV6.Contains(address) && !ExcludedV6.Any(range => range.Contains(address)),
        _ => false
    };

    public static void ValidateScope(PublicWebScope scope)
    {
        if (scope.Hosts is not { Length: > 0 and <= 8 } || scope.MaxFetches is < 1 or > 8 ||
            scope.Hosts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != scope.Hosts.Length || scope.Hosts.Any(host =>
                string.IsNullOrWhiteSpace(host) || host.Length > 253 || !host.Contains('.') || host.EndsWith('.') ||
                host.Any(c => c > 127) || Uri.CheckHostName(host) != UriHostNameType.Dns ||
                host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Public research requires one to eight exact public DNS hostnames and a bounded fetch allowance.");
    }

    public static Uri Destination(string text, PublicWebScope scope)
    {
        ValidateScope(scope);
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2048 || text.Any(char.IsWhiteSpace) || text.Any(char.IsControl) ||
            !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.HostNameType != UriHostNameType.Dns ||
            !scope.Hosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Use an HTTPS URL on an exact host granted to this task, without credentials or a custom port.");
        return new UriBuilder(uri) { Fragment = "" }.Uri;
    }

    public static async ValueTask<Stream> Connect(DnsEndPoint endpoint,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPEndPoint, CancellationToken, ValueTask<Stream>> dial, CancellationToken cancellation)
    {
        if (endpoint.Port != 443) throw new HttpRequestException("Public retrieval uses HTTPS port 443 only.");
        var addresses = await resolve(endpoint.Host, cancellation);
        if (addresses.Length is 0 or > 32 || addresses.Any(address => !IsPublic(address)))
            throw new HttpRequestException("The public hostname resolved to a denied address.");
        // Resolve once, validate every answer, then dial the exact IP. No second DNS lookup can rebind the request.
        foreach (var address in addresses.Distinct())
        {
            cancellation.ThrowIfCancellationRequested();
            try { return await dial(new(address, endpoint.Port), cancellation); }
            catch (SocketException) { }
        }
        throw new HttpRequestException("No validated public address accepted the connection.");
    }

    public static SocketsHttpHandler Handler() => new()
    {
        AllowAutoRedirect = false, UseProxy = false, UseCookies = false, Credentials = null, PreAuthenticate = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        MaxResponseHeadersLength = 16, MaxConnectionsPerServer = 1, PooledConnectionLifetime = TimeSpan.Zero,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        ConnectCallback = (context, cancellation) => Connect(context.DnsEndPoint,
            (host, token) => Dns.GetHostAddressesAsync(host, token), async (endpoint, token) =>
            {
                var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try { await socket.ConnectAsync(endpoint, token); return new NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); throw; }
            }, cancellation)
    };
}
