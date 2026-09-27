using System.Net;
using Microsoft.AspNetCore.Http;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class PlowIngressTests : IDisposable
{
    private const string Origin = "https://claw-fixture.exe.xyz:3000";
    private readonly string root = Path.Combine(Path.GetTempPath(), "claw-plow-ingress-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    private readonly Security security;
    private readonly PlowIngress ingress = new(Origin);

    public PlowIngressTests() { store = new(root); security = new(store); }

    private static DefaultHttpContext Request(string? user = "usr_fixture_owner")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Host = new("claw-fixture.exe.xyz:3000");
        context.Request.Scheme = "http";
        context.Request.Method = "GET"; context.Request.Path = "/api/session";
        if (user != null) context.Request.Headers["X-Plow-User"] = user;
        return context;
    }

    [Fact]
    public void TrustedOwner_UsesNormalSecureCookieCsrfAndStableAccountAcrossRestarts()
    {
        var context = Request(); Assert.True(ingress.Apply(context));
        var session = ingress.Session(context, security, null)!;
        Assert.True(session.Owner); Assert.NotEmpty(session.Csrf);
        var cookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("secure", cookie); Assert.Contains("httponly", cookie);
        var read = Request(); read.Request.Headers.Cookie = cookie.Split(';')[0];
        Assert.True(ingress.Apply(read));
        Assert.Equal(session.Id, ingress.Session(read, security, security.Authenticate(read))!.Id);
        var secondBrowser = Request(); Assert.True(ingress.Apply(secondBrowser));
        var reopenedSecurity = new Security(store);
        Assert.Equal(session.AccountId, ingress.Session(secondBrowser, reopenedSecurity, null)!.AccountId);
    }

    [Theory]
    [InlineData(null)] [InlineData("usr_owner,usr_other")] [InlineData(" usr_owner")] [InlineData("usr/owner")]
    public void MissingOrAmbiguousIdentity_DoesNotEnter(string? user) => Assert.False(ingress.Apply(Request(user)));

    [Fact]
    public void PublicPeer_WrongHost_AndBrowserForwardingCannotClaimOwner()
    {
        var external = Request(); external.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.8");
        Assert.False(ingress.Apply(external));
        var wrongHost = Request(); wrongHost.Request.Host = new("evil.exe.xyz:3000");
        Assert.False(ingress.Apply(wrongHost));
        var forwarded = Request(null); forwarded.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
        forwarded.Request.Headers["X-Forwarded-Host"] = "claw-fixture.exe.xyz:3000";
        Assert.False(ingress.Apply(forwarded));
    }

    [Fact]
    public void MutationDoesNotBootstrap_AndCookieCannotCrossProxyIdentities()
    {
        var context = Request(); Assert.True(ingress.Apply(context));
        var owner = ingress.Session(context, security, null)!;
        var mutation = Request(); mutation.Request.Method = "POST"; mutation.Request.Path = "/api/shifts";
        Assert.True(ingress.Apply(mutation)); Assert.Null(ingress.Session(mutation, security, null));
        var other = Request("usr_other_owner"); other.Request.Method = "POST"; other.Request.Path = "/api/shifts";
        Assert.True(ingress.Apply(other)); Assert.Null(ingress.Session(other, security, owner));
        security.Revoke(owner.Id);
        var revoked = Request(); revoked.Request.Headers.Cookie = context.Response.Headers.SetCookie.ToString().Split(';')[0];
        Assert.Null(security.Authenticate(revoked));
    }

    [Fact]
    public void LocalProxy_IsExplicitAndLoopbackOnly()
    {
        var local = new PlowIngress("http://localhost:5183", localDevelopment: true);
        var context = Request(); context.Request.Host = new("localhost:5183");
        Assert.True(local.Apply(context));
        Assert.True(local.Session(context, security, null)!.Owner);
        Assert.DoesNotContain("secure", context.Response.Headers.SetCookie.ToString());
        Assert.Throws<ArgumentException>(() => new PlowIngress("http://localhost:5183"));
        Assert.Throws<ArgumentException>(() => new PlowIngress("http://public.example", localDevelopment: true));
    }

    public void Dispose() { store.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
}
