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
        public string Email = "fixture@example.test";
        public bool EmailVerified = true;
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
                 new Claim("name", "Fixture Person"), new Claim("email", Email),
                 new Claim("email_verified", EmailVerified ? "true" : "false", ClaimValueTypes.Boolean),
                 new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)],
                DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow.AddMinutes(5),
                new SigningCredentials(WrongKey ? new RsaSecurityKey(wrongRsa) { KeyId = "fixture-key" } : Key, SecurityAlgorithms.RsaSha256));
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = "fixture-access", token_type = "Bearer", expires_in = 300,
                id_token = new JwtSecurityTokenHandler().WriteToken(token) }) };
        }
    }

    public CustomerLoginTests()
    {
        Directory.CreateDirectory(root);
        // Only the campaign ledger read is synthetic; routes, OIDC and membership writes are real.
        File.WriteAllText(Path.Combine(root, "invite-ledger.py"), """
            import json, sys
            request = json.load(sys.stdin)
            print(json.dumps({'project': {'id': request['id'], 'goal': 'Invitation fixture campaign'},
                              'campaign': {} if request['id'] == 'a' * 32 else None}))
            """);
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", root);
            builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
            builder.UseSetting("Thaddeus:PhoneOrigin", Origin);
            builder.UseSetting("Marketing:Container", "nonexistent-fixture-container");
            builder.UseSetting("Marketing:FixtureLedger", Path.Combine(root, "invitation-ledger"));
            builder.UseSetting("Marketing:FixtureRunwayScript", Path.Combine(root, "invite-ledger.py"));
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
    private static void AuthorizeWrites(HttpClient client, JsonElement session)
    {
        client.DefaultRequestHeaders.Add("Origin", Origin);
        client.DefaultRequestHeaders.Add("X-CSRF", session.GetProperty("csrf").GetString());
    }
    private async Task<(HttpClient Owner, JsonElement Invite, string Token)> Invitation()
    {
        var owner = Client(); provider.Subject = "google-oauth2|explicit-owner";
        AuthorizeWrites(owner, await SignIn(owner));
        using var response = await owner.PostAsJsonAsync("/api/marketing/campaigns/" + new string('a', 32) + "/invitations",
            new { email = "fixture@example.test", provider = "google", expiresInHours = 72 });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var invite = await response.Content.ReadFromJsonAsync<JsonElement>();
        provider.Subject = "google-oauth2|person-one";
        return (owner, invite, invite.GetProperty("url").GetString()!.Split("#invite=")[1]);
    }
    private void ChangeInvitation(string id, string column, string value)
    {
        Assert.Contains(column, new[] { "expires_at", "issuer" });
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(root, "marketing-chat.sqlite")}"); db.Open();
        using var command = db.CreateCommand();
        command.CommandText = $"UPDATE campaign_invitations SET {column}=$value WHERE id=$id";
        command.Parameters.AddWithValue("$value", value); command.Parameters.AddWithValue("$id", id); command.ExecuteNonQuery();
    }
    [Fact] public async Task InvitationNeedsOwnerAndSavedCampaignAndEnforcesExpiryLimits()
    {
        using var member = Client(); AuthorizeWrites(member, await SignIn(member));
        var path = "/api/marketing/campaigns/" + new string('a', 32) + "/invitations";
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync(path,
            new { email = "fixture@example.test", provider = "google", expiresInHours = 72 })).StatusCode);
        using var owner = Client(); provider.Subject = "google-oauth2|explicit-owner"; AuthorizeWrites(owner, await SignIn(owner));
        foreach (var hours in new[] { 0, 169 }) Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path,
            new { email = "fixture@example.test", provider = "google", expiresInHours = hours })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path,
            new { email = "fixture@example.test", provider = "unconfigured", expiresInHours = 72 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(path.Replace(new string('a',32), new string('b',32)),
            new { email = "fixture@example.test", provider = "google", expiresInHours = 72 })).StatusCode);
    }
    [Fact] public async Task ExactAccountInvitationSupportsUnverifiedMicrosoftWithoutTrustingSameEmail()
    {
        using var member = Client(); provider.Subject = "windowslive|exact-member"; provider.EmailVerified = false;
        var original = await SignIn(member, "microsoft"); AuthorizeWrites(member, original);
        var accountId = original.GetProperty("principalId").GetString()!;
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/marketing/invitation-accounts")).StatusCode);
        using var owner = Client(); provider.Subject = "google-oauth2|explicit-owner"; provider.EmailVerified = true;
        var ownerSession = await SignIn(owner); AuthorizeWrites(owner, ownerSession);
        var accounts = (await owner.GetFromJsonAsync<JsonElement>("/api/marketing/invitation-accounts")).GetProperty("accounts");
        Assert.Single(accounts.EnumerateArray()); Assert.Equal(accountId, accounts[0].GetProperty("id").GetString());
        Assert.False(accounts[0].GetProperty("emailVerified").GetBoolean());
        var path = "/api/marketing/campaigns/" + new string('a', 32) + "/invitations";
        foreach (var invalid in new[] { new string('f', 32), ownerSession.GetProperty("principalId").GetString()!, "not-an-account" })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, new { accountId = invalid, expiresInHours = 72 })).StatusCode);
        using var created = await owner.PostAsJsonAsync(path, new { accountId, expiresInHours = 72 });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var invite = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("account", invite.GetProperty("targetKind").GetString());
        var token = invite.GetProperty("url").GetString()!.Split("#invite=")[1];
        using var outsider = Client(); provider.Subject = "windowslive|same-email-outsider"; provider.EmailVerified = true;
        AuthorizeWrites(outsider, await SignIn(outsider, "microsoft"));
        foreach (var action in new[] { "preview", "accept" })
            Assert.Equal(HttpStatusCode.Conflict, (await outsider.PostAsJsonAsync("/api/marketing/campaigns/invitations/" + action, new { token })).StatusCode);
        using var second = Client(); provider.Subject = "windowslive|exact-member"; provider.Email = "changed-label@example.test"; provider.EmailVerified = false;
        var secondSession = await SignIn(second, "microsoft"); AuthorizeWrites(second, secondSession);
        Assert.Equal(accountId, secondSession.GetProperty("principalId").GetString());
        Assert.Equal(HttpStatusCode.OK, (await second.PostAsJsonAsync("/api/marketing/campaigns/invitations/preview", new { token })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.PostAsJsonAsync("/api/marketing/campaigns/invitations/accept", new { token })).StatusCode);
        var backend = factory.Services.GetRequiredService<MarketingBackend>(); var security = factory.Services.GetRequiredService<Security>();
        var actor = security.ActiveDevice(original.GetProperty("id").GetString()!)!;
        Assert.True(backend.HasCampaignAccess(new string('a',32), actor, security));
        Assert.False(backend.HasCampaignAccess(new string('b',32), actor, security));
        Assert.Equal(HttpStatusCode.Conflict, (await member.PostAsJsonAsync("/api/marketing/campaigns/invitations/accept", new { token })).StatusCode);
        Assert.DoesNotContain(token, await owner.GetStringAsync(path));
    }

    [Fact] public async Task RevocationClosesPendingExactAccountInvitesAfterEmailChanges()
    {
        using var member = Client(); provider.Subject = "windowslive|revoked-member"; provider.EmailVerified = false;
        var signedIn = await SignIn(member, "microsoft"); AuthorizeWrites(member, signedIn);
        var accountId = signedIn.GetProperty("principalId").GetString()!;
        using var owner = Client(); provider.Subject = "google-oauth2|explicit-owner"; provider.EmailVerified = true;
        AuthorizeWrites(owner, await SignIn(owner));
        var path = "/api/marketing/campaigns/" + new string('a',32);
        using var invitation = await owner.PostAsJsonAsync(path + "/invitations", new { accountId, expiresInHours = 72 });
        var receipt = await invitation.Content.ReadFromJsonAsync<JsonElement>();
        var token = receipt.GetProperty("url").GetString()!.Split("#invite=")[1];
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(path + "/access", new { deviceId = accountId, action = "grant" })).StatusCode);
        provider.Subject = "windowslive|revoked-member"; provider.Email = "new-email@example.test"; provider.EmailVerified = false;
        using var changed = Client(); AuthorizeWrites(changed, await SignIn(changed, "microsoft"));
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(path + "/access", new { deviceId = accountId, action = "revoke" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await changed.PostAsJsonAsync("/api/marketing/campaigns/invitations/accept", new { token })).StatusCode);
        Assert.Contains("revoked", await owner.GetStringAsync(path + "/invitations"));
    }
    [Fact] public async Task InvitationBindsOnePersonOneCampaignAndConcurrentAcceptanceCannotReplay()
    {
        var (owner, invite, token) = await Invitation(); using var ownedClient = owner;
        using var member = Client(); using var second = Client();
        var firstSession = await SignIn(member); var secondSession = await SignIn(second);
        const string accept = "/api/marketing/campaigns/invitations/accept";
        var body = new { token };
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync(accept, body)).StatusCode);
        AuthorizeWrites(member, firstSession); AuthorizeWrites(second, secondSession);
        using var preview = await member.PostAsJsonAsync("/api/marketing/campaigns/invitations/preview", body);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var backend = factory.Services.GetRequiredService<MarketingBackend>(); var security = factory.Services.GetRequiredService<Security>();
        var actor = security.ActiveDevice(firstSession.GetProperty("id").GetString()!)!;
        Assert.False(backend.HasCampaignAccess(new string('a',32), actor, security));
        var attempts = await Task.WhenAll(member.PostAsJsonAsync(accept, body), second.PostAsJsonAsync(accept, body));
        Assert.Single(attempts, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(attempts, r => r.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in attempts) response.Dispose();
        Assert.True(backend.HasCampaignAccess(new string('a',32), actor, security));
        Assert.True(backend.HasCampaignAccess(new string('a',32), security.ActiveDevice(secondSession.GetProperty("id").GetString()!)!, security));
        Assert.False(backend.HasCampaignAccess(new string('b',32), actor, security));
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/meetings")).StatusCode);
        var listing = await owner.GetStringAsync("/api/marketing/campaigns/" + new string('a',32) + "/invitations");
        Assert.Contains(actor.PrincipalId, listing); Assert.Contains("accepted", listing); Assert.DoesNotContain(token, listing);
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(root, "marketing-chat.sqlite")}"); db.Open();
        using var read = db.CreateCommand(); read.CommandText = "SELECT token_hash FROM campaign_invitations";
        Assert.NotEqual(token, read.ExecuteScalar());
        using var another = await owner.PostAsJsonAsync("/api/marketing/campaigns/" + new string('a',32) + "/invitations",
            new { email = "fixture@example.test", provider = "google", expiresInHours = 72 });
        var pending = await another.Content.ReadFromJsonAsync<JsonElement>();
        var pendingToken = pending.GetProperty("url").GetString()!.Split("#invite=")[1];
        using var revoke = await owner.PostAsJsonAsync("/api/marketing/campaigns/" + new string('a',32) + "/access",
            new { deviceId = actor.PrincipalId, action = "revoke" });
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.False(backend.HasCampaignAccess(new string('a',32), actor, security));
        Assert.False(backend.HasCampaignAccess(new string('a',32), security.ActiveDevice(secondSession.GetProperty("id").GetString()!)!, security));
        Assert.Equal(HttpStatusCode.Conflict, (await member.PostAsJsonAsync(accept, new { token = pendingToken })).StatusCode);
    }
    [Fact] public async Task OwnerCanSeeAndRevokeAccountAccessWithNoActiveBrowserOrLedger()
    {
        var (owner, invite, token) = await Invitation(); using var ownedClient = owner;
        using var member = Client(); var signedIn = await SignIn(member); AuthorizeWrites(member, signedIn);
        using var accepted = await member.PostAsJsonAsync("/api/marketing/campaigns/invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var security = factory.Services.GetRequiredService<Security>(); security.Revoke(signedIn.GetProperty("id").GetString()!);
        var path = "/api/marketing/campaigns/" + new string('a',32) + "/access";
        var listed = await owner.GetFromJsonAsync<JsonElement>(path);
        Assert.True(listed.GetProperty("members")[0].GetProperty("active").GetBoolean());
        File.Delete(Path.Combine(root, "invite-ledger.py"));
        using var revoked = await owner.PostAsJsonAsync(path, new { deviceId = signedIn.GetProperty("principalId").GetString(), action = "revoke" });
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        var after = await owner.GetFromJsonAsync<JsonElement>(path);
        Assert.False(after.GetProperty("members")[0].GetProperty("active").GetBoolean());
    }
    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("email")]
    [InlineData("unverified")]
    [InlineData("provider")]
    [InlineData("issuer")]
    public async Task InvitationRefusesOtherIdentitiesAndInvalidLinksWithoutRevealingCampaign(string fault)
    {
        var (owner, invite, token) = await Invitation(); using var ownedClient = owner;
        if (fault == "expired") ChangeInvitation(invite.GetProperty("id").GetString()!, "expires_at", DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
        if (fault == "issuer") ChangeInvitation(invite.GetProperty("id").GetString()!, "issuer", "https://different-issuer.example.test/");
        if (fault == "revoked")
        {
            using var revoked = await owner.PostAsJsonAsync("/api/marketing/campaigns/" + new string('a',32) + "/invitations/" + invite.GetProperty("id").GetString() + "/revoke", new {});
            Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        }
        if (fault == "email") provider.Email = "someone-else@example.test";
        if (fault == "unverified") provider.EmailVerified = false;
        if (fault == "provider") provider.Subject = "windowslive|same-email";
        using var member = Client(); AuthorizeWrites(member, await SignIn(member, fault == "provider" ? "microsoft" : "google"));
        foreach (var action in new[] { "preview", "accept" })
        {
            using var refused = await member.PostAsJsonAsync("/api/marketing/campaigns/invitations/" + action, new { token });
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.DoesNotContain("Invitation fixture campaign", await refused.Content.ReadAsStringAsync());
        }
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
