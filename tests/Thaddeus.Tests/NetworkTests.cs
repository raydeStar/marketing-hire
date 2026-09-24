using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Thaddeus.Host;

namespace Thaddeus.Tests;

public sealed class NetworkTests
{
    [Theory]
    [InlineData("203.0.113.8", false)]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    public async Task ProxyHeaders_AreAcceptedOnlyFromExplicitLoopbackProxies(string source, bool trusted)
    {
        var c = new DefaultHttpContext(); c.Connection.RemoteIpAddress = IPAddress.Parse(source);
        c.Request.Scheme = "http"; c.Request.Host = new("localhost:5179");
        c.Request.Headers["X-Forwarded-For"] = "100.64.0.20";
        c.Request.Headers["X-Forwarded-Host"] = "study.example.ts.net";
        c.Request.Headers["X-Forwarded-Proto"] = "https";
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(NetworkBoundary.TailscaleProxy("https://study.example.ts.net")));
        await middleware.Invoke(c);
        Assert.Equal(trusted ? "https" : "http", c.Request.Scheme);
        Assert.Equal(trusted ? "study.example.ts.net" : "localhost:5179", c.Request.Host.Value);
        Assert.Equal(trusted ? "100.64.0.20" : source, c.Connection.RemoteIpAddress?.ToString());
        Assert.False(NetworkBoundary.IsLocalOwnerOrigin(c, "http://localhost:5179"));
    }
    [Fact] public async Task WrongForwardedHost_CannotAcquirePhoneOrigin()
    {
        var c = new DefaultHttpContext(); c.Connection.RemoteIpAddress = IPAddress.Loopback;
        c.Request.Scheme = "http"; c.Request.Host = new("localhost:5179");
        c.Request.Headers["X-Forwarded-For"] = "100.64.0.20"; c.Request.Headers["X-Forwarded-Host"] = "evil.example"; c.Request.Headers["X-Forwarded-Proto"] = "https";
        await new ForwardedHeadersMiddleware(_=>Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(NetworkBoundary.TailscaleProxy("https://study.example.ts.net"))).Invoke(c);
        Assert.NotEqual("evil.example", c.Request.Host.Value);
    }
    [Theory][InlineData("https://user:secret@host")][InlineData("http://phone.example")][InlineData("https://phone.example/path")][InlineData("https://phone.example?key=x")]
    public void PhoneOrigin_RejectsAmbiguousConfiguration(string origin) => Assert.Throws<ArgumentException>(()=>NetworkBoundary.Origin(origin,false));

    [Fact] public async Task RealTls_PairConfirmReconnectAndRevoke_RequireSeparateHostAuthority()
    {
        var root = Path.Combine(Path.GetTempPath(), "thaddeus-tls-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo != null && !File.Exists(Path.Combine(repo.FullName,"Thaddeus.slnx"))) repo = repo.Parent;
        Assert.NotNull(repo);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); request.CertificateExtensions.Add(san.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var password = Guid.NewGuid().ToString("N"); var certPath = Path.Combine(root,"test.pfx");
        await File.WriteAllBytesAsync(certPath,certificate.Export(X509ContentType.Pfx,password));
        static int Port() { using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();return ((IPEndPoint)listener.LocalEndpoint).Port; }
        var local = "http://localhost:" + Port(); var phone = "https://localhost:" + Port();
        var start = new ProcessStartInfo("dotnet") { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true };
        // Use the host built with this test, including an isolated --artifacts-path. No haunted Debug checkout.
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--contentRoot"); start.ArgumentList.Add(Path.Combine(repo.FullName,"src","Thaddeus.Host"));
        start.Environment["Thaddeus__Data"]=root;start.Environment["Thaddeus__LocalOrigin"]=local;start.Environment["Thaddeus__PhoneOrigin"]=phone;start.Environment["Thaddeus__PhoneMode"]="direct";
        start.Environment["Kestrel__Certificates__Default__Path"]=certPath;start.Environment["Kestrel__Certificates__Default__Password"]=password;
        using var process = Process.Start(start)!; process.BeginOutputReadLine();process.BeginErrorReadLine();
        try
        {
            using var host = new HttpClient(new HttpClientHandler { CookieContainer=new() }) { BaseAddress=new(local), Timeout=TimeSpan.FromSeconds(5) };
            // Trust only this ephemeral test certificate. Nothing is installed in the OS trust store.
            using var device = new HttpClient(new HttpClientHandler { CookieContainer=new(), ServerCertificateCustomValidationCallback=(_,cert,_,errors)=>cert?.Thumbprint==certificate.Thumbprint && (errors & ~SslPolicyErrors.RemoteCertificateChainErrors)==SslPolicyErrors.None }) { BaseAddress=new(phone), Timeout=TimeSpan.FromSeconds(5) };
            var ready = false;
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                while (!startup.IsCancellationRequested)
                {
                    if (process.HasExited) throw new InvalidOperationException("TLS fixture host exited before readiness, code " + process.ExitCode + ".");
                    try { using var response = await host.GetAsync("/", startup.Token); if (response.IsSuccessStatusCode) { ready = true; break; } }
                    catch (HttpRequestException) { }
                    await Task.Delay(50, startup.Token);
                }
            }
            catch (OperationCanceledException) when (startup.IsCancellationRequested) { }
            Assert.True(ready, "TLS fixture host did not become ready within 30 seconds.");
            async Task<HttpResponseMessage> Post(HttpClient client,string path,object body,string? csrf=null)
            {
                var msg=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};
                msg.Headers.Add("Origin",client.BaseAddress!.GetLeftPart(UriPartial.Authority));if(csrf!=null)msg.Headers.Add("X-CSRF",csrf);
                return await client.SendAsync(msg);
            }
            var key=await File.ReadAllTextAsync(Path.Combine(root,"host-key.txt"));
            Assert.Equal(HttpStatusCode.Unauthorized,(await Post(device,"/api/auth/login",new{key})).StatusCode);
            using var login=await Post(host,"/api/auth/login",new{key});login.EnsureSuccessStatusCode();
            var owner=await login.Content.ReadFromJsonAsync<JsonElement>();var csrf=owner.GetProperty("csrf").GetString()!;
            using var codeResponse=await Post(host,"/api/pair/start",new{},csrf);codeResponse.EnsureSuccessStatusCode();
            var code=await codeResponse.Content.ReadFromJsonAsync<JsonElement>();
            using var claim=await Post(device,"/api/pair/claim",new{code=code.GetProperty("code").GetString(),name="TLS test phone"});claim.EnsureSuccessStatusCode();
            Assert.Contains(claim.Headers.GetValues("Set-Cookie"),c=>c.Contains("secure",StringComparison.OrdinalIgnoreCase)&&c.Contains("httponly",StringComparison.OrdinalIgnoreCase));
            Assert.Equal(HttpStatusCode.Accepted,(await Post(device,"/api/pair/exchange",new{})).StatusCode);
            (await Post(host,"/api/pair/"+code.GetProperty("id").GetString()+"/confirm",new{},csrf)).EnsureSuccessStatusCode();
            using var exchange=await Post(device,"/api/pair/exchange",new{});exchange.EnsureSuccessStatusCode();
            var paired=await exchange.Content.ReadFromJsonAsync<JsonElement>();Assert.False(paired.GetProperty("owner").GetBoolean());
            Assert.Equal(HttpStatusCode.OK,(await device.GetAsync("/api/state")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await device.GetAsync("/api/state")).StatusCode); // Reconnect using the same secure cookie.
            Assert.Equal(HttpStatusCode.Forbidden,(await device.GetAsync("/api/settings/diagnostics")).StatusCode);
            var session=await device.GetFromJsonAsync<JsonElement>("/api/session");
            (await Post(host,"/api/devices/"+session.GetProperty("id").GetString()+"/revoke",new{},csrf)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Unauthorized,(await device.GetAsync("/api/state")).StatusCode);
        }
        finally { if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();} Directory.Delete(root,true); }
    }
}
