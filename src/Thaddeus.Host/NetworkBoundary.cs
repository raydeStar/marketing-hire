using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Thaddeus.Host;

public static class NetworkBoundary
{
    public static Uri Origin(string value, bool local)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/" || value.EndsWith('/') || (local ? !uri.IsLoopback || uri.Scheme is not ("http" or "https") : uri.Scheme != "https"))
            throw new ArgumentException("Configure an exact origin without a path: loopback for the host, HTTPS for the phone.");
        return uri;
    }
    public static ForwardedHeadersOptions TailscaleProxy(string phoneOrigin)
    {
        var origin = Origin(phoneOrigin, false);
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1,
            RequireHeaderSymmetry = true
        };
        options.KnownIPNetworks.Clear(); options.KnownProxies.Clear();
        options.KnownProxies.Add(IPAddress.Loopback); options.KnownProxies.Add(IPAddress.IPv6Loopback);
        options.AllowedHosts.Add(origin.Host);
        return options;
    }
    public static bool IsLocalOwnerOrigin(HttpContext context, string localOrigin) =>
        context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address) &&
        string.Equals($"{context.Request.Scheme}://{context.Request.Host}", localOrigin, StringComparison.OrdinalIgnoreCase);
}
