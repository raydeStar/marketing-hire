using System.Net;
using System.Text.RegularExpressions;

namespace Thaddeus.Host;

/// <summary>Plow's owner proxy terminates TLS and supplies identity on a private loopback entrance.</summary>
internal sealed class PlowIngress(string origin, bool localDevelopment = false)
{
    internal const string Issuer = "https://api.plow.co";
    private const string IdentityKey = "plow-owner-identity";
    private static readonly Regex Uid = new("^[A-Za-z0-9_-]{3,128}$", RegexOptions.Compiled);
    private readonly Uri expected = NetworkBoundary.Origin(origin, localDevelopment);

    internal bool Apply(HttpContext context)
    {
        // Do not consume arbitrary forwarded headers. The platform preserves the external Host
        // and supplies X-Plow-User only after stripping the browser's identity headers.
        if (!string.Equals(context.Request.Host.Value, expected.Authority, StringComparison.OrdinalIgnoreCase))
            return !context.Request.Headers.ContainsKey("X-Plow-User");
        var user = context.Request.Headers["X-Plow-User"];
        if (context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address) ||
            user.Count != 1 || user[0] is not { } identity || !Uid.IsMatch(identity)) return false;
        context.Items[IdentityKey] = identity;
        context.Request.Scheme = expected.Scheme;
        return true;
    }

    internal DeviceSession? Session(HttpContext context, Security security, DeviceSession? current)
    {
        if (context.Items[IdentityKey] is not string identity) return current;
        if (current?.AccountId is { } id && security.Account(id) is { } account &&
            account.Issuer == Issuer && account.Subject == identity) return current;
        // A cookie is never enough on this entrance: every request must still pass Plow's proxy.
        // Bootstrap on the existing session read so the web app keeps its normal cookie/CSRF flow.
        if (!HttpMethods.IsGet(context.Request.Method) || context.Request.Path != "/api/session") return null;
        return security.IssuePlowOwner(context, identity, localDevelopment);
    }
}
