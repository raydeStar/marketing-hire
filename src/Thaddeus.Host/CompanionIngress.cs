using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Thaddeus.Host;

/// <summary>An individual, short-lived assertion from the colocated companion connector.</summary>
internal sealed class CompanionIngress
{
    private const string IdentityKey = "hirezero-companion";
    private readonly string workspace, owner, secret;
    private readonly Uri origin;
    private readonly Dictionary<string, long> consumed = [];
    private static readonly Regex Subject = new("^[A-Za-z0-9_-]{3,128}$");
    private static bool Hex(string? value, int length) => value?.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal sealed record Identity(int Version, string Workspace, string Request, string Subject, string Name,
        string Session, long Issued, long Expires, string Method, string Path, string BodyHash);

    internal CompanionIngress(string workspace, string origin, string owner, string secret)
    {
        if (!Hex(workspace, 32) || !Subject.IsMatch(owner) || !Regex.IsMatch(secret, "^[A-Za-z0-9_-]{43,100}$"))
            throw new ArgumentException("The companion connection is incomplete.");
        this.origin = NetworkBoundary.Origin(origin, false);
        this.workspace = workspace; this.owner = owner; this.secret = secret;
    }

    internal static CompanionIngress? Configure(IConfiguration configuration)
    {
        var origin = configuration["Thaddeus:CompanionOrigin"];
        return string.IsNullOrEmpty(origin) ? null : new(configuration["Thaddeus:CompanionWorkspace"] ?? "", origin,
            configuration["Thaddeus:CompanionOwner"] ?? "", configuration["Thaddeus:CompanionSecret"] ?? "");
    }

    internal static bool IsCompanion(HttpContext context) => context.Items.ContainsKey(IdentityKey);
    internal static bool HasHeaders(HttpContext context) => context.Request.Headers.ContainsKey("X-HireZero-Identity") || context.Request.Headers.ContainsKey("X-HireZero-Signature");

    internal async Task<bool> Apply(HttpContext context)
    {
        var request = context.Request;
        if (!string.Equals(request.Host.Value, origin.Authority, StringComparison.OrdinalIgnoreCase)) return !HasHeaders(context);
        if (context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address) || request.Headers.ContainsKey("X-Plow-User")) return false;
        var encoded = request.Headers["X-HireZero-Identity"];
        var signature = request.Headers["X-HireZero-Signature"];
        if (encoded.Count != 1 || signature.Count != 1 || encoded[0] is not { Length: > 0 and < 8192 } text ||
            !Hex(signature[0], 64) || !Regex.IsMatch(text, "^[A-Za-z0-9_-]+$")) return false;
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.ASCII.GetBytes(text));
        if (!CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(signature[0]!))) return false;
        Identity? identity;
        try
        {
            var json = Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + new string('=', (4 - text.Length % 4) % 4));
            identity = JsonSerializer.Deserialize<Identity>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception error) when (error is JsonException or FormatException) { return false; }
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (identity is null || identity.Version != 1 || identity.Workspace != workspace || !Hex(identity.Request, 48) ||
            identity.Subject is null || !Subject.IsMatch(identity.Subject) || identity.Name is not { Length: <= 60 } ||
            !Hex(identity.Session, 64) || !Hex(identity.BodyHash, 64) || identity.Issued > now + 5 || identity.Issued < now - 30 ||
            identity.Expires <= now || identity.Expires > identity.Issued + 30 || identity.Method != request.Method ||
            identity.Path != request.PathBase + request.Path + request.QueryString || request.ContentLength > 150000) return false;
        request.EnableBuffering(bufferThreshold: 160000, bufferLimit: 150000);
        try
        {
            var hash = await SHA256.HashDataAsync(request.Body, context.RequestAborted);
            request.Body.Position = 0;
            if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(identity.BodyHash))) return false;
        }
        catch (IOException) { return false; }
        lock (consumed)
        {
            foreach (var key in consumed.Where(item => item.Value <= now).Select(item => item.Key).ToArray()) consumed.Remove(key);
            if (consumed.Count >= 20000 || !consumed.TryAdd(identity.Request, identity.Expires)) return false;
        }
        context.Items[IdentityKey] = identity;
        request.Scheme = origin.Scheme;
        return true;
    }

    internal DeviceSession? Session(HttpContext context, Security security, DeviceSession? current)
    {
        if (context.Items[IdentityKey] is not Identity identity) return current;
        var binding = workspace + ":" + identity.Session;
        if (current?.AccountId is { } id && security.Account(id) is { } account &&
            account.Issuer == PlowIngress.Issuer && account.Subject == identity.Subject &&
            current.CompanionBinding == binding && current.Owner == (identity.Subject == owner)) return current;
        if (!HttpMethods.IsGet(context.Request.Method) || context.Request.Path != "/api/session") return null;
        // A revoked grant stays revoked. A fresh portal sign-in supplies a new binding.
        try { return security.IssueCompanion(context, identity.Subject, identity.Name, identity.Subject == owner, binding); }
        catch (InvalidOperationException) { return null; }
    }
}
