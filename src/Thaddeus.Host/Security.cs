using System.Security.Cryptography;
using Thaddeus.Core;
using Thaddeus.Infrastructure;
namespace Thaddeus.Host;
public record DeviceSession(string Id, string TokenHash, string Csrf, string Name, bool Owner, DateTimeOffset Expires, bool Revoked = false);
public record Pairing(string Id, string CodeHash, DateTimeOffset Expires, string? ClaimHash = null, string? Name = null, bool Confirmed = false, bool Used = false);
public sealed class Security(Store store)
{
    private readonly object gate = new();
    public static string Random() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
    private List<DeviceSession> Sessions() => Wire.Unpack<List<DeviceSession>>(store.Setting("sessions") ?? "[]");
    private List<Pairing> Pairs() => Wire.Unpack<List<Pairing>>(store.Setting("pairings") ?? "[]");
    public DeviceSession? Authenticate(HttpContext c)
    {
        var token = c.Request.Cookies["thaddeus-session"];
        if (token == null) return null;
        lock (gate) return Sessions().FirstOrDefault(s => !s.Revoked && s.Expires > DateTimeOffset.UtcNow && s.TokenHash == Wire.Hash(token));
    }
    public DeviceSession Issue(HttpContext c, string name, bool owner)
    {
        lock (gate)
        {
            var token = Random(); var s = new DeviceSession(Guid.NewGuid().ToString("N"), Wire.Hash(token), Random(), name[..Math.Min(name.Length, 60)], owner, DateTimeOffset.UtcNow.AddDays(7));
            var sessions = Sessions(); sessions.Add(s); store.Setting("sessions", Wire.Pack(sessions));
            c.Response.Cookies.Append("thaddeus-session", token, new() { HttpOnly = true, Secure = c.Request.IsHttps, SameSite = SameSiteMode.Strict, Path = "/", Expires = s.Expires });
            return s;
        }
    }
    public object[] Devices() { lock (gate) return Sessions().Where(s => !s.Revoked).Select(s => (object)new { s.Id, s.Name, s.Owner, s.Expires }).ToArray(); }
    public DeviceSession? ActiveDevice(string id) { lock (gate) return Sessions().FirstOrDefault(s => s.Id == id && !s.Revoked && s.Expires > DateTimeOffset.UtcNow); }
    public void Revoke(string id) { lock (gate) store.Setting("sessions", Wire.Pack(Sessions().Select(s => s.Id == id ? s with { Revoked = true } : s))); }
    public object StartPair()
    {
        lock (gate)
        {
            var code = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(5));
            var pair = new Pairing(Guid.NewGuid().ToString("N"), Wire.Hash(code), DateTimeOffset.UtcNow.AddMinutes(5));
            var pairs = Pairs().Where(p => p.Expires > DateTimeOffset.UtcNow && !p.Used).ToList(); pairs.Add(pair);
            store.Setting("pairings", Wire.Pack(pairs)); return new { pair.Id, code, pair.Expires };
        }
    }
    public object Claim(HttpContext c, string code, string name)
    {
        lock (gate)
        {
            var pairs = Pairs(); var p = pairs.FirstOrDefault(p => p.CodeHash == Wire.Hash(code) && p.Expires > DateTimeOffset.UtcNow && !p.Used && p.ClaimHash == null) ?? throw new ArgumentException("Pairing code invalid or expired.");
            var claim = Random(); pairs[pairs.IndexOf(p)] = p with { ClaimHash = Wire.Hash(claim), Name = name[..Math.Min(name.Length, 60)] };
            store.Setting("pairings", Wire.Pack(pairs));
            c.Response.Cookies.Append("thaddeus-pair", claim, new() { HttpOnly = true, Secure = c.Request.IsHttps, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = TimeSpan.FromMinutes(5) });
            return new { p.Id, status = "Awaiting host confirmation" };
        }
    }
    public object[] Pending() { lock (gate) return Pairs().Where(p => p.ClaimHash != null && !p.Used && p.Expires > DateTimeOffset.UtcNow).Select(p => (object)new { p.Id, p.Name, p.Confirmed, p.Expires }).ToArray(); }
    public void Confirm(string id)
    {
        lock (gate)
        {
            var pairs = Pairs(); var p = pairs.FirstOrDefault(p => p.Id == id && p.ClaimHash != null && !p.Used && p.Expires > DateTimeOffset.UtcNow) ?? throw new ArgumentException("No pending device claim.");
            pairs[pairs.IndexOf(p)] = p with { Confirmed = true }; store.Setting("pairings", Wire.Pack(pairs));
        }
    }
    public DeviceSession? Exchange(HttpContext c)
    {
        lock (gate)
        {
            var token = c.Request.Cookies["thaddeus-pair"];
            if (token == null) throw new ArgumentException("Claim the pairing code first.");
            var pairs = Pairs(); var p = pairs.FirstOrDefault(p => p.ClaimHash == Wire.Hash(token) && !p.Used && p.Expires > DateTimeOffset.UtcNow) ?? throw new ArgumentException("Pairing expired or already used.");
            if (!p.Confirmed) return null;
            pairs[pairs.IndexOf(p)] = p with { Used = true }; store.Setting("pairings", Wire.Pack(pairs));
            return Issue(c, p.Name ?? "Paired device", false);
        }
    }
}
