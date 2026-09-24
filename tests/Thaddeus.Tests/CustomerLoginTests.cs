using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

// Real middleware, fake issuer: the doorman is tested without inviting the internet in.
public sealed class CustomerLoginTests : IAsyncLifetime
{
    const string Origin = "https://workspace.example.test";
    const string Issuer = "https://identity.example.test/";
    const string ClientId = "fixture-client";
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-customer-login-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> factory;
    private readonly IdentityProvider provider = new();
    private Store? ownedStore;

    private sealed class IdentityProvider : HttpMessageHandler
    {
        public readonly RSA Rsa = RSA.Create(2048);
        public string Nonce = "", Challenge = "", Subject = "google-oauth2|person-one", TokenIssuer = Issuer, Audience = ClientId;
        public bool WrongNonce, WrongKey;
        public int Requests;
        public RsaSecurityKey Key => new(Rsa) { KeyId = "fixture-key" };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(Issuer + "oauth/token", request.RequestUri!.ToString());
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal(ClientId, form["client_id"].ToString());
            Assert.Equal("fixture-secret", form["client_secret"].ToString());
            Assert.Equal("authorization_code", form["grant_type"].ToString());
            Assert.Equal(Origin + CustomerLogin.CallbackPath, form["redirect_uri"].ToString());
            Assert.Equal(Challenge, Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"].ToString()))));
            Requests++;
            using var wrongRsa = RSA.Create(2048);
            var token = new JwtSecurityToken(TokenIssuer, Audience,
                [new Claim("sub", Subject), new Claim("nonce", WrongNonce ? "wrong-nonce" : Nonce),
                 new Claim("name", "Fixture Person"), new Claim("email", "fixture@example.test"),
                 new Claim("email_verified", "true", ClaimValueTypes.Boolean),
                 new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)],
                DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow.AddMinutes(5),
                new SigningCredentials(WrongKey ? new RsaSecurityKey(wrongRsa) { KeyId = "fixture-key" } : Key, SecurityAlgorithms.RsaSha256));
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = "fixture-access", token_type = "Bearer", expires_in = 300,
                id_token = new JwtSecurityTokenHandler().WriteToken(token) }) };
        }
    }

    public CustomerLoginTests()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", root);
            builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Thaddeus:PhoneOrigin", Origin);
            builder.UseSetting("Marketing:Container", "nonexistent-fixture-container");
            builder.UseSetting("CustomerLogin:Enabled", "true");
            builder.UseSetting("CustomerLogin:Authority", Issuer);
            builder.UseSetting("CustomerLogin:Origin", Origin);
            builder.UseSetting("CustomerLogin:ClientId", ClientId);
            builder.UseSetting("CustomerLogin:ClientSecret", "fixture-secret");
            builder.UseSetting("CustomerLogin:Providers", "google,microsoft");
            builder.UseSetting("CustomerLogin:MicrosoftConnection", "windowslive");
            builder.UseSetting("CustomerLogin:MicrosoftSubjectPrefix", "windowslive|");
            builder.UseSetting("CustomerLogin:OwnerSubject", "google-oauth2|explicit-owner");
            builder.ConfigureServices(services =>
            {
                foreach (var service in services.Where(s => s.ImplementationType == typeof(MarketingRunwayPump)).ToArray()) services.Remove(service);
                services.PostConfigure<OpenIdConnectOptions>(CustomerLogin.Scheme, options =>
                {
                options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer,
                    AuthorizationEndpoint = Issuer + "authorize", TokenEndpoint = Issuer + "oauth/token" };
                options.Configuration.SigningKeys.Add(provider.Key);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(options.Configuration);
                options.Backchannel = new HttpClient(provider);
                });
            });
        });
    }
    private HttpClient Client()
    {
        var client = factory.CreateClient(new() { BaseAddress = new(Origin), AllowAutoRedirect = false, HandleCookies = false });
        ownedStore ??= factory.Services.GetRequiredService<Store>();
        return client;
    }
    private async Task<(string State, string Cookies)> Challenge(HttpClient client, string name = "google")
    {
        using var response = await client.GetAsync("/api/auth/customer/login?provider=" + name);
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
        var location = response.Headers.Location!;
        Assert.Equal(Issuer + "authorize", location.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal(name == "google" ? "google-oauth2" : "windowslive", query["connection"].ToString());
        Assert.Equal("code", query["response_type"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.Equal("openid profile email", query["scope"].ToString());
        provider.Nonce = query["nonce"].ToString(); provider.Challenge = query["code_challenge"].ToString();
        return (query["state"].ToString(), string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(x => x.Split(';')[0])));
    }
    private static async Task<HttpResponseMessage> Callback(HttpClient client, string state, string cookies)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CustomerLogin.CallbackPath + "?code=fixture-code&state=" + Uri.EscapeDataString(state));
        if (cookies.Length > 0) request.Headers.Add("Cookie", cookies);
        return await client.SendAsync(request);
    }
    private async Task<JsonElement> SignIn(HttpClient client, string name = "google")
    {
        var challenge = await Challenge(client, name);
        using var response = await Callback(client, challenge.State, challenge.Cookies);
        Assert.Equal("/", response.Headers.Location?.ToString());
        var cookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("thaddeus-session=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie); Assert.Contains("secure", cookie); Assert.Contains("samesite=strict", cookie);
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        return await client.GetFromJsonAsync<JsonElement>("/api/session");
    }
    [Fact] public async Task SameIdentityAcrossBrowsersHasStablePrincipalAndIndependentSessions()
    {
        using var first = Client(); using var second = Client();
        var a = await SignIn(first); var b = await SignIn(second);
        Assert.NotEqual(a.GetProperty("id").GetString(), b.GetProperty("id").GetString());
        Assert.Equal(a.GetProperty("principalId").GetString(), b.GetProperty("principalId").GetString());
        Assert.False(a.GetProperty("owner").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await first.GetAsync("/api/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await first.GetAsync("/api/meetings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await first.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        first.DefaultRequestHeaders.Add("Origin", Origin); first.DefaultRequestHeaders.Add("X-CSRF", a.GetProperty("csrf").GetString());
        Assert.Equal(HttpStatusCode.OK, (await first.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/session")).StatusCode);
        Assert.DoesNotContain("fixture-access", ownedStore!.Setting("sessions")!);
    }
    [Fact] public async Task SameEmailAndNameDoNotMergeDifferentProviderSubjects()
    {
        using var first = Client(); using var second = Client();
        var a = await SignIn(first); provider.Subject = "windowslive|different-person";
        var b = await SignIn(second, "microsoft");
        Assert.NotEqual(a.GetProperty("principalId").GetString(), b.GetProperty("principalId").GetString());
        Assert.False(b.GetProperty("owner").GetBoolean());
    }
    [Fact] public async Task OnlyExplicitOwnerSubjectGetsOwnerRole()
    {
        using var client = Client(); provider.Subject = "google-oauth2|explicit-owner";
        var session = await SignIn(client); Assert.True(session.GetProperty("owner").GetBoolean());
    }
    [Fact] public async Task CampaignMembershipFollowsPersonAndRevocationReachesBothBrowsers()
    {
        using var first = Client(); using var second = Client(); using var outsider = Client();
        var a = await SignIn(first); var b = await SignIn(second);
        provider.Subject = "google-oauth2|outsider"; var other = await SignIn(outsider);
        var security = factory.Services.GetRequiredService<Security>();
        var backend = factory.Services.GetRequiredService<MarketingBackend>();
        var firstSession = security.ActiveDevice(a.GetProperty("id").GetString()!)!;
        var secondSession = security.ActiveDevice(b.GetProperty("id").GetString()!)!;
        var otherSession = security.ActiveDevice(other.GetProperty("id").GetString()!)!;
        const string campaign = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(root, "marketing-chat.sqlite")}"); db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "INSERT INTO campaign_memberships(project_id,device_id,granted_by,granted_at) VALUES($project,$person,'fixture-owner',$now)";
        command.Parameters.AddWithValue("$project", campaign); command.Parameters.AddWithValue("$person", firstSession.PrincipalId);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O")); command.ExecuteNonQuery();
        Assert.True(backend.HasCampaignAccess(campaign, firstSession, security));
        Assert.True(backend.HasCampaignAccess(campaign, secondSession, security));
        Assert.False(backend.HasCampaignAccess("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", secondSession, security));
        Assert.False(backend.HasCampaignAccess(campaign, otherSession, security));
        security.Revoke(firstSession.Id);
        Assert.False(backend.HasCampaignAccess(campaign, firstSession, security));
        Assert.True(backend.HasCampaignAccess(campaign, secondSession, security));
        command.CommandText = "UPDATE campaign_memberships SET revoked_at=$now WHERE project_id=$project AND device_id=$person"; command.ExecuteNonQuery();
        Assert.False(backend.HasCampaignAccess(campaign, secondSession, security));
        Assert.True(backend.HasEverCampaignMembership(secondSession.PrincipalId));
    }
    [Theory]
    [InlineData("state")]
    [InlineData("correlation")]
    [InlineData("nonce")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("provider")]
    public async Task InvalidCallbackNeverCreatesWorkspaceSession(string fault)
    {
        using var client = Client(); var challenge = await Challenge(client);
        if (fault == "nonce") provider.WrongNonce = true;
        if (fault == "issuer") provider.TokenIssuer = "https://impostor.example.test/";
        if (fault == "audience") provider.Audience = "another-app";
        if (fault == "signature") provider.WrongKey = true;
        if (fault == "provider") provider.Subject = "auth0|unexpected-password-user";
        using var response = await Callback(client, fault == "state" ? "tampered" : challenge.State, fault == "correlation" ? "" : challenge.Cookies);
        Assert.Equal("/#sign-in-error=not-completed", response.Headers.Location?.ToString());
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(x => x.StartsWith("thaddeus-session=")));
        Assert.Null(ownedStore!.Setting("customer-accounts"));
        if (fault is "state" or "correlation") Assert.Equal(0, provider.Requests);
    }
    [Fact] public async Task LoginRejectsUntrustedOriginAndUnknownProvider()
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/auth/customer/login?provider=attacker")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("http://localhost:5179/api/auth/customer/login?provider=google")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("https://attacker.example.test/api/auth/customer/login?provider=google")).StatusCode);
        Assert.Equal(0, provider.Requests);
    }
    [Fact] public void PrivateLoginFileCannotChangeNetworkSettingsOrOverrideExplicitConfiguration()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,"customer-login.json"), JsonSerializer.Serialize(new {
            Thaddeus = new { PhoneOrigin = "https://attacker.example.test" },
            CustomerLogin = new { Enabled="true", Authority=Issuer, ClientId, ClientSecret="file-secret", Origin,
                Providers="google", OwnerSubject="file-owner" } }));
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string,string?> { ["CustomerLogin:OwnerSubject"]="explicit-owner" });
        CustomerLogin.LoadPrivateSettings(config, root);
        var settings = CustomerLoginSettings.Read(config, Origin)!;
        Assert.Equal("explicit-owner", settings.OwnerSubject); Assert.Equal(["google"], settings.Providers);
        Assert.Null(config["Thaddeus:PhoneOrigin"]);
        Assert.Throws<ArgumentException>(() => CustomerLoginSettings.Read(config,"https://other.example.test"));
    }
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        await factory.DisposeAsync(); ownedStore?.Dispose(); provider.Rsa.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
