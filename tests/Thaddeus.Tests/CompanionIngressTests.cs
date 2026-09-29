using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class CompanionIngressTests : IAsyncLifetime
{
    const string Workspace = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", Origin = "https://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.work.example.com";
    const string Secret = "fictional-companion-signing-key-for-isolated-tests-only", Owner = "owner-fixture";
    readonly string root = Path.Combine(Path.GetTempPath(), "hirezero-companion-host-" + Guid.NewGuid().ToString("N"));
    readonly WebApplicationFactory<Program> factory;
    sealed class Loopback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        { app.Use((c, proceed) => { c.Connection.RemoteIpAddress = IPAddress.Loopback; return proceed(); }); next(app); };
    }
    public CompanionIngressTests() => factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Thaddeus:Data", root);
        builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5183");
        builder.UseSetting("Thaddeus:PhoneMode", "plow");
        builder.UseSetting("Thaddeus:PhoneOrigin", "https://plow-fixture.example.com");
        builder.UseSetting("Thaddeus:CompanionOrigin", Origin);
        builder.UseSetting("Thaddeus:CompanionWorkspace", Workspace);
        builder.UseSetting("Thaddeus:CompanionOwner", Owner);
        builder.UseSetting("Thaddeus:CompanionSecret", Secret);
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo != null && !File.Exists(Path.Combine(repo.FullName, "business/agent/hire/bin/runway.py"))) repo = repo.Parent;
        builder.UseSetting("Marketing:FixtureLedger", Path.Combine(root, "fixture-ledger"));
        builder.UseSetting("Marketing:FixtureRunwayScript", Path.Combine(repo!.FullName, "business/agent/hire/bin/runway.py"));
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, Loopback>());
    });
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        var store = factory.Services.GetService<Store>();
        await factory.DisposeAsync(); store?.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
    static (string Identity, string Signature) Sign(string path, string subject = Owner, string method = "GET", string body = "", string binding = "b", long age = 0)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - age;
        var json = JsonSerializer.Serialize(new { version = 1, workspace = Workspace, request = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24)),
            subject, name = subject, session = new string(binding[0], 64), issued = now, expires = now + 30, method, path,
            bodyHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body))) });
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (encoded, Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.ASCII.GetBytes(encoded))));
    }
    static DefaultHttpContext Context(string path = "/api/session", string subject = Owner, string method = "GET", string body = "", string binding = "b", long age = 0)
    {
        var c = new DefaultHttpContext(); c.Connection.RemoteIpAddress = IPAddress.Loopback;
        c.Request.Host = new(new Uri(Origin).Host); c.Request.Method = method; c.Request.Path = path;
        c.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        var signed = Sign(path, subject, method, body, binding, age);
        c.Request.Headers["X-HireZero-Identity"] = signed.Identity; c.Request.Headers["X-HireZero-Signature"] = signed.Signature;
        return c;
    }
    [Fact] public async Task RejectsReplayTamperingWrongEntranceAndExpiredAssertions()
    {
        var ingress = new CompanionIngress(Workspace, Origin, Owner, Secret);
        var good = Context(); Assert.True(await ingress.Apply(good)); Assert.False(await ingress.Apply(good));
        var body = Context(method: "POST", body: "{}"); body.Request.Body = new MemoryStream("changed"u8.ToArray()); Assert.False(await ingress.Apply(body));
        var path = Context(); path.Request.Path = "/api/export"; Assert.False(await ingress.Apply(path));
        var host = Context(); host.Request.Host = new("different.example.com"); Assert.False(await ingress.Apply(host));
        var remote = Context(); remote.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1"); Assert.False(await ingress.Apply(remote));
        var plow = Context(); plow.Request.Headers["X-Plow-User"] = Owner; Assert.False(await ingress.Apply(plow));
        Assert.False(await ingress.Apply(Context(age: 40)));
        var forged = Context(); forged.Request.Headers["X-HireZero-Signature"] = new string('0', 64); Assert.False(await ingress.Apply(forged));
        var duplicate = Context(); duplicate.Request.Headers.Append("X-HireZero-Identity", "other"); Assert.False(await ingress.Apply(duplicate));
    }
    [Fact] public async Task InvitationsUseNativeRolesAndRecheckTheInviterWithoutOwnerImpersonation()
    {
        using var client = factory.CreateClient(new() { HandleCookies = false, AllowAutoRedirect = false });
        async Task<HttpResponseMessage> Send(string path, string actor, object? value = null)
        {
            var body = value == null ? "" : JsonSerializer.Serialize(value);
            var method = value == null ? "GET" : "POST";
            var signed = Sign(path, actor, method, body);
            using var request = new HttpRequestMessage(new HttpMethod(method), Origin + path);
            request.Headers.Add("X-HireZero-Identity", signed.Identity); request.Headers.Add("X-HireZero-Signature", signed.Signature);
            if (value != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return await client.SendAsync(request);
        }
        object Grant(string id, string who, string role) => new { invitation = id.PadLeft(32, '0'), subject = who, name = who, role };
        const string Team = "/api/companion/team", GrantPath = Team + "/grant";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Origin + Team)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(Team, Owner)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(GrantPath, Owner, Grant("1", "bad-owner", "owner"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(GrantPath, Owner, Grant("2", "reviewer-inviter", "reviewer"))).StatusCode);
        var choices = await Send(Team, "reviewer-inviter");
        var json = JsonDocument.Parse(await choices.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(new[] { "viewer", "reviewer" }, json.GetProperty("roles").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(GrantPath, "reviewer-inviter", Grant("3", "overpowered", "manager"))).StatusCode);
        var viewerGrant = Grant("4", "viewer-invitee", "viewer");
        Assert.Equal(HttpStatusCode.OK, (await Send(GrantPath, "reviewer-inviter", viewerGrant)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(GrantPath, "reviewer-inviter", viewerGrant)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(Team, "viewer-invitee")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(GrantPath, "viewer-invitee", Grant("5", "another-viewer", "viewer"))).StatusCode);
        var security = factory.Services.GetRequiredService<Security>();
        var roles = factory.Services.GetRequiredService<MemberRoles>();
        var viewer = security.CompanionAccount("viewer-invitee")!;
        Assert.Equal(MemberRole.Viewer, roles.Explicit(viewer.Id));
        Assert.False(viewer.Owner);
        Assert.Equal(HttpStatusCode.OK, (await Send(GrantPath, "reviewer-inviter", Grant("7", "viewer-invitee", "viewer"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(GrantPath, Owner, Grant("8", "viewer-invitee", "manager"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(GrantPath, "reviewer-inviter", Grant("4", "different-person", "viewer"))).StatusCode);
        roles.Set(security.CompanionAccount("reviewer-inviter")!.Id, "viewer", "Owner");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(GrantPath, "reviewer-inviter", Grant("6", "stale-invite", "reviewer"))).StatusCode);
        roles.Set(viewer.Id, "manager", "Owner");
        Assert.Equal(HttpStatusCode.Conflict, (await Send(GrantPath, Owner, viewerGrant)).StatusCode);
        Assert.Equal(MemberRole.Manager, roles.Explicit(viewer.Id));
    }
    [Fact] public async Task CookiesCannotCrossPeopleOrPortalGrantsAndRevocationSurvivesBootstrap()
    {
        var security = factory.Services.GetRequiredService<Security>();
        var ingress = new CompanionIngress(Workspace, Origin, Owner, Secret);
        var c = Context(); Assert.True(await ingress.Apply(c)); var owner = ingress.Session(c, security, null)!;
        Assert.True(owner.Owner); Assert.Contains("secure", c.Response.Headers.SetCookie.ToString());
        var member = Context(subject: "member-fixture", binding: "c"); Assert.True(await ingress.Apply(member));
        var reviewer = ingress.Session(member, security, owner)!; Assert.False(reviewer.Owner); Assert.True(reviewer.CampaignOnly);
        var write = Context("/api/objectives", "member-fixture", "POST", "{}", "c"); Assert.True(await ingress.Apply(write));
        Assert.Null(ingress.Session(write, security, owner));
        var otherGrant = Context("/api/objectives", "member-fixture", "POST", "{}", "d"); Assert.True(await ingress.Apply(otherGrant));
        Assert.Null(ingress.Session(otherGrant, security, reviewer));
        security.Revoke(reviewer.Id);
        var retry = Context(subject: "member-fixture", binding: "c"); Assert.True(await ingress.Apply(retry)); Assert.Null(ingress.Session(retry, security, null));
        var fresh = Context(subject: "member-fixture", binding: "d"); Assert.True(await ingress.Apply(fresh));
        Assert.Equal(reviewer.AccountId, ingress.Session(fresh, security, null)!.AccountId);
    }
    [Fact] public async Task ActualHostEnforcesCsrfRolesAndOwnerOnlyDecisions()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new(Origin), HandleCookies = false });
        async Task<HttpResponseMessage> Send(string path, string subject = Owner, string method = "GET", string body = "", string? cookie = null, string? csrf = null)
        {
            var signed = Sign(path, subject, method, body, subject == Owner ? "b" : "c");
            var request = new HttpRequestMessage(new HttpMethod(method), path);
            request.Headers.Add("X-HireZero-Identity", signed.Identity); request.Headers.Add("X-HireZero-Signature", signed.Signature);
            request.Headers.Add("Origin", Origin);
            if (cookie != null) request.Headers.Add("Cookie", cookie);
            if (csrf != null) request.Headers.Add("X-CSRF", csrf);
            if (method != "GET") request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return await client.SendAsync(request);
        }
        var ready = await Send("/api/companion/ready"); ready.EnsureSuccessStatusCode(); Assert.False(ready.Headers.Contains("Set-Cookie"));
        var opened = await Send("/api/session"); opened.EnsureSuccessStatusCode();
        var owner = JsonDocument.Parse(await opened.Content.ReadAsStringAsync()).RootElement;
        Assert.False(owner.GetProperty("canPair").GetBoolean());
        var ownerCookie = opened.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        var joined = await Send("/api/session", "member-fixture"); joined.EnsureSuccessStatusCode();
        var member = JsonDocument.Parse(await joined.Content.ReadAsStringAsync()).RootElement;
        var memberCookie = joined.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        var csrf = member.GetProperty("csrf").GetString(); var ownerCsrf = owner.GetProperty("csrf").GetString();
        var pairing = await Send("/api/pair/start", method: "POST", body: "{}", cookie: ownerCookie, csrf: ownerCsrf);
        Assert.Equal(HttpStatusCode.Forbidden, pairing.StatusCode);
        Assert.Contains("teammate invitations", await pairing.Content.ReadAsStringAsync());
        Assert.False(member.GetProperty("owner").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await Send("/api/export", "member-fixture", cookie: memberCookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send("/api/marketing/drafts/1/decision", "member-fixture", "POST", "{}", memberCookie, csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send("/api/company-wiki", "member-fixture", cookie: ownerCookie)).StatusCode);
        var rolePath = "/api/team/roles/" + member.GetProperty("principalId").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(rolePath, method: "PUT", body: "{\"role\":\"contributor\"}", cookie: ownerCookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(rolePath, method: "PUT", body: "{\"role\":\"contributor\"}", cookie: ownerCookie, csrf: ownerCsrf)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send("/api/company-wiki", "member-fixture", cookie: memberCookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(rolePath, "member-fixture", "PUT", "{\"role\":\"manager\"}", memberCookie, csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(rolePath, method: "PUT", body: "{\"role\":\"viewer\"}", cookie: ownerCookie, csrf: ownerCsrf)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send("/api/company-wiki", "member-fixture", cookie: memberCookie)).StatusCode);
        // The relay grant alone shares no campaign. Exercise the actual ledger,
        // then the owner's explicit sharing and the teammate's review routes.
        async Task<JsonElement> OwnerPost(string path, object body)
        {
            var response = await Send(path, method: "POST", body: JsonSerializer.Serialize(body), cookie: ownerCookie, csrf: ownerCsrf);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }
        var seeded = await OwnerPost("/api/marketing/runway/fixture/seed", new { requestId = "companion-seed" });
        var id = seeded.GetProperty("project").GetProperty("id").GetString();
        var source = seeded.GetProperty("artifacts")[0]; var asset = seeded.GetProperty("artifacts")[1];
        var saved = await OwnerPost($"/api/marketing/runway/{id}/campaign-brief", new {
            requestId = "companion-brief", projectVersion = seeded.GetProperty("project").GetProperty("version").GetInt32(), version = 0,
            sourceArtifactId = source.GetProperty("id").GetString(), sourceArtifactDigest = source.GetProperty("digest").GetString(),
            brief = new { audience = "Founders", problem = "Limited time", hypothesis = "Clear drafts help", metric_definition = "Relevant replies",
                priority_rationale = "Fictional attention bottleneck", proposition = "Marketing help", desired_behavior = "Request a demo",
                channel = "Owner reviewed draft", primary_metric = "Qualified replies",
                guardrail = "No guarantees", review_timing = "Owner review", non_goals = "No other channels" },
            experiment = new { intervention = "Fictional draft", target_population = "Founders", observation_window = "Seven days", metric_source = "Fixture", decision_rule = "minimum_sample", minimum_sample = 3 } });
        Assert.Equal(HttpStatusCode.Forbidden, (await Send($"/api/marketing/campaigns/{id}/review", "member-fixture", cookie: memberCookie)).StatusCode);
        var principal = member.GetProperty("principalId").GetString();
        await OwnerPost($"/api/marketing/campaigns/{id}/access", new { deviceId = principal, action = "grant" });
        Assert.Equal(HttpStatusCode.OK, (await Send($"/api/marketing/campaigns/{id}/review", "member-fixture", cookie: memberCookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(rolePath, method: "PUT", body: "{\"role\":\"reviewer\"}", cookie: ownerCookie, csrf: ownerCsrf)).StatusCode);
        foreach (var kind in new[] { "comment", "revision_request" })
        {
            var latest = await Send($"/api/marketing/runways/{id}", cookie: ownerCookie);
            latest.EnsureSuccessStatusCode();
            var version = JsonDocument.Parse(await latest.Content.ReadAsStringAsync()).RootElement.GetProperty("project").GetProperty("version").GetInt32();
            var response = await Send($"/api/marketing/campaigns/{id}/inputs", "member-fixture", "POST", JsonSerializer.Serialize(new {
                requestId = Guid.NewGuid().ToString(), kind, artifactId = asset.GetProperty("id").GetString(),
                artifactDigest = asset.GetProperty("digest").GetString(), projectVersion = version, content = "Please remove the unsupported claim." }), memberCookie, csrf);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        await OwnerPost($"/api/marketing/campaigns/{id}/access", new { deviceId = principal, action = "revoke" });
        Assert.Equal(HttpStatusCode.Forbidden, (await Send($"/api/marketing/campaigns/{id}/review", "member-fixture", cookie: memberCookie)).StatusCode);
    }
}
