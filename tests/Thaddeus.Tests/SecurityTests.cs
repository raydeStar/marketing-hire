using Microsoft.AspNetCore.Http;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;
namespace Thaddeus.Tests;
public sealed class SecurityTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"thaddeus-security-"+Guid.NewGuid().ToString("N"));
    private readonly Store store;
    private readonly Security security;
    public SecurityTests(){store=new(root);security=new(store);}
    private static string Cookie(DefaultHttpContext c,string name)=>c.Response.Headers.SetCookie.First(s=>s!.StartsWith(name+"=",StringComparison.Ordinal))!.Split(';')[0];
    [Fact]public void Sessions_UseHttpOnlyCookiesAndRevokeImmediately()
    {
        var c=new DefaultHttpContext();c.Request.Scheme="https";var s=security.Issue(c,"test",true);var cookie=Cookie(c,"thaddeus-session");
        Assert.Contains("httponly",c.Response.Headers.SetCookie.ToString().ToLowerInvariant());Assert.Contains("secure",c.Response.Headers.SetCookie.ToString());
        var read=new DefaultHttpContext();read.Request.Headers.Cookie=cookie;Assert.NotNull(security.Authenticate(read));security.Revoke(s.Id);Assert.Null(security.Authenticate(read));
        Assert.DoesNotContain(cookie.Split('=')[1],store.Setting("sessions")!);
    }
    [Fact]public void Pairing_RequiresHostConfirmationAndIsSingleUse()
    {
        var start=System.Text.Json.JsonSerializer.SerializeToElement(security.StartPair(),Wire.Json);var id=start.GetProperty("id").GetString()!;var code=start.GetProperty("code").GetString()!;
        var claim=new DefaultHttpContext();claim.Request.Scheme="https";security.Claim(claim,code,"phone");
        Assert.Throws<ArgumentException>(()=>security.Claim(new DefaultHttpContext(),code,"second"));
        var exchange=new DefaultHttpContext();exchange.Request.Headers.Cookie=Cookie(claim,"thaddeus-pair");Assert.Null(security.Exchange(exchange));security.Confirm(id);
        var s=security.Exchange(exchange);Assert.NotNull(s);Assert.False(s.Owner);Assert.Throws<ArgumentException>(()=>security.Exchange(exchange));
    }
    [Fact]public void InvalidPairingCode_DoesNotCreateSession()=>Assert.Throws<ArgumentException>(()=>security.Claim(new DefaultHttpContext(),"bad","phone"));
    public void Dispose(){store.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
}
