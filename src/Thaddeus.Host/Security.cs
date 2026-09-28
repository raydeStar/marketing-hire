using System.Security.Cryptography;
using Thaddeus.Core;
using Thaddeus.Infrastructure;
namespace Thaddeus.Host;
public record DeviceSession(string Id, string TokenHash, string Csrf, string Name, bool Owner, DateTimeOffset Expires, bool Revoked = false, bool CampaignOnly = false, string? AccountId = null, string? CompanionBinding = null)
{
    public string PrincipalId => AccountId ?? Id;
}
public record CustomerAccount(string Id, string Issuer, string Subject, string Name, string? Email, bool EmailVerified, bool Owner, bool Revoked = false);
public record Pairing(string Id, string CodeHash, DateTimeOffset Expires, string? ClaimHash = null, string? Name = null, bool Confirmed = false, bool Used = false);
public sealed class Security(Store store)
{
    private readonly object gate = new();
    public static string Random() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
    private List<DeviceSession> Sessions() => Wire.Unpack<List<DeviceSession>>(store.Setting("sessions") ?? "[]");
    private List<Pairing> Pairs() => Wire.Unpack<List<Pairing>>(store.Setting("pairings") ?? "[]");
    private List<CustomerAccount> Accounts() => Wire.Unpack<List<CustomerAccount>>(store.Setting("customer-accounts") ?? "[]");
    public DeviceSession? Authenticate(HttpContext c)
    {
        var token = c.Request.Cookies["thaddeus-session"];
        if (token == null) return null;
        lock (gate)
        {
            var session = Sessions().FirstOrDefault(s => !s.Revoked && s.Expires > DateTimeOffset.UtcNow && s.TokenHash == Wire.Hash(token));
            if (session?.AccountId == null) return session;
            var account = Accounts().FirstOrDefault(a => a.Id == session.AccountId && !a.Revoked);
            return account == null ? null : session with { Owner = account.Owner, Name = account.Name, CampaignOnly = !account.Owner };
        }
    }
    public DeviceSession Issue(HttpContext c, string name, bool owner, bool campaignOnly = false)
        => IssueSession(c, name, owner, campaignOnly, null);

    private DeviceSession IssueSession(HttpContext c, string name, bool owner, bool campaignOnly, string? accountId, string? companionBinding = null)
    {
        lock (gate)
        {
            var token = Random(); var s = new DeviceSession(Guid.NewGuid().ToString("N"), Wire.Hash(token), Random(), name[..Math.Min(name.Length, 60)], owner, DateTimeOffset.UtcNow.AddDays(7), CampaignOnly: campaignOnly, AccountId: accountId, CompanionBinding: companionBinding);
            var sessions = Sessions(); sessions.Add(s); store.Setting("sessions", Wire.Pack(sessions));
            c.Response.Cookies.Append("thaddeus-session", token, new() { HttpOnly = true, Secure = c.Request.IsHttps, SameSite = SameSiteMode.Strict, Path = "/", Expires = s.Expires });
            return s;
        }
    }
    internal DeviceSession IssueCustomer(HttpContext context, string issuer, string subject, string name,
        string? email, bool emailVerified, bool owner)
    {
        if (!context.Request.IsHttps) throw new InvalidOperationException("Customer sign-in requires HTTPS.");
        return IssueAccount(context, issuer, subject, name, email, emailVerified, owner);
    }

    internal DeviceSession IssuePlowOwner(HttpContext context, string subject, bool localDevelopment)
    {
        if (!context.Request.IsHttps && !(localDevelopment && context.Connection.RemoteIpAddress is { } address &&
            System.Net.IPAddress.IsLoopback(address) && new Uri($"{context.Request.Scheme}://{context.Request.Host}").IsLoopback))
            throw new InvalidOperationException("Plow owner sign-in requires HTTPS or an explicit loopback development proxy.");
        return IssueAccount(context, PlowIngress.Issuer, subject, "Plow owner", null, false, owner: true);
    }

    internal DeviceSession IssueCompanion(HttpContext context, string subject, string name, bool owner, string binding)
    {
        if (!context.Request.IsHttps) throw new InvalidOperationException("Companion sign-in requires HTTPS.");
        lock (gate)
        {
            if (Sessions().Any(s => s.CompanionBinding == binding && s.Revoked))
                throw new InvalidOperationException("Sign in again to open this workspace.");
            return IssueAccount(context, PlowIngress.Issuer, subject, name, null, false, owner, binding);
        }
    }

    private DeviceSession IssueAccount(HttpContext context, string issuer, string subject, string name,
        string? email, bool emailVerified, bool owner, string? companionBinding = null)
    {
        if (string.IsNullOrWhiteSpace(issuer) || issuer.Length > 500 || string.IsNullOrWhiteSpace(subject) || subject.Length > 500)
            throw new ArgumentException("A validated issuer and subject are required.");
        lock (gate)
        {
            var accounts = Accounts();
            var old = accounts.FirstOrDefault(a => a.Issuer == issuer && a.Subject == subject);
            if (old?.Revoked == true) throw new InvalidOperationException("This workspace account has been revoked.");
            // Names and email addresses are labels; the signed issuer/subject pair is the identity.
            var account = new CustomerAccount(old?.Id ?? Guid.NewGuid().ToString("N"), issuer, subject,
                name[..Math.Min(name.Length, 60)], email?[..Math.Min(email.Length, 320)], emailVerified, owner);
            if (old == null) accounts.Add(account); else accounts[accounts.IndexOf(old)] = account;
            store.Setting("customer-accounts", Wire.Pack(accounts));
            return IssueSession(context, account.Name, owner, campaignOnly: !owner, account.Id, companionBinding);
        }
    }
    public CustomerAccount? Account(string id) { lock (gate) return Accounts().FirstOrDefault(a => a.Id == id && !a.Revoked); }
    internal CustomerAccount[] KnownReviewerAccounts() { lock (gate) return Accounts().Where(a => !a.Revoked && !a.Owner).ToArray(); }
    public void SignOut(HttpContext context, DeviceSession session)
    {
        Revoke(session.Id);
        context.Response.Cookies.Delete("thaddeus-session", new() { HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Strict, Path = "/" });
    }
    public object[] Devices() { lock (gate) return Sessions().Where(s => !s.Revoked).Select(s => (object)new { s.Id, s.Name, s.Owner, s.Expires, s.AccountId }).ToArray(); }
    public DeviceSession? ActiveDevice(string id)
    {
        lock (gate)
        {
            var session = Sessions().FirstOrDefault(s => (s.Id == id || s.AccountId == id) && !s.Revoked && s.Expires > DateTimeOffset.UtcNow);
            if (session?.AccountId == null) return session;
            var account = Accounts().FirstOrDefault(a => a.Id == session.AccountId && !a.Revoked);
            return account == null ? null : session with { Owner = account.Owner, Name = account.Name, CampaignOnly = !account.Owner };
        }
    }
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
