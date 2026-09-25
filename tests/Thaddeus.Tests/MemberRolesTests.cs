using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class MemberRolesTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "member-roles-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> factory;
    public MemberRolesTests() => factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
        builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
    });
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        var store = factory.Services.GetService<Store>();
        await factory.DisposeAsync(); store?.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(root); attempt++)
        {
            try { Directory.Delete(root, true); }
            catch (IOException) when (attempt < 10) { await Task.Delay(200); }
        }
    }

    private (HttpClient Client, DeviceSession Session) Client(bool owner, bool campaignOnly = false)
    {
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var context = new DefaultHttpContext();
        var session = factory.Services.GetRequiredService<Security>().Issue(context, owner ? "Owner" : "Teammate", owner, campaignOnly);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", session.Csrf);
        return (client, session);
    }
    private static WikiChange Page(string title) => new(Guid.NewGuid().ToString("N"), null, 0, "company", "company", title, "Body", "fact", "draft");
    private static EmployeeFileChange File(string name) => new(Guid.NewGuid().ToString("N"), name, 0, "# Notes");
    private static async Task<HttpStatusCode> SetRole(HttpClient owner, string principal, string role) =>
        (await owner.PutAsJsonAsync("/api/team/roles/" + principal, new MemberRoleChange(role))).StatusCode;

    [Fact] public void RolesFormALadderAndOnlyTheOwnerKeepsApprovals()
    {
        Assert.False(MemberRoles.Grants(MemberRole.Viewer, Capability.CommentOnCampaigns));
        Assert.True(MemberRoles.Grants(MemberRole.Reviewer, Capability.CommentOnCampaigns));
        Assert.False(MemberRoles.Grants(MemberRole.Reviewer, Capability.ReadWorkspace));
        Assert.True(MemberRoles.Grants(MemberRole.Contributor, Capability.EditWiki));
        Assert.False(MemberRoles.Grants(MemberRole.Contributor, Capability.ChatWithEmployee));
        Assert.True(MemberRoles.Grants(MemberRole.Manager, Capability.PublishPages));
        foreach (var path in new[] { "/api/marketing/drafts/1/decision", "/api/marketing/runway/x/review", "/api/marketing/runway", "/api/export", "/api/devices", "/api/team/roles/x", "/api/settings/connection", "/api/chat", "/api/meetings" })
            Assert.False(MemberRoles.Reaches(MemberRole.Manager, "POST", path), path);
        Assert.False(MemberRoles.Reaches(MemberRole.Viewer, "POST", "/api/marketing/campaigns/abc/inputs"));
        Assert.True(MemberRoles.Reaches(MemberRole.Reviewer, "POST", "/api/marketing/campaigns/abc/inputs"));
        Assert.Null(MemberRoles.Parse("owner")); Assert.Null(MemberRoles.Parse("3")); Assert.Equal(MemberRole.Manager, MemberRoles.Parse("Manager"));
    }

    [Fact] public async Task TheOwnerAssignsRolesAndEachRoleReachesOnlyWhatItGrants()
    {
        var (owner, ownerSession) = Client(true);
        var (teammate, member) = Client(false, campaignOnly: true);
        using (owner) using (teammate)
        {
            // A collaborator without a role stays inside shared campaigns.
            Assert.Equal(HttpStatusCode.Forbidden, (await teammate.GetAsync("/api/company-wiki")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await teammate.GetAsync("/api/team/roles")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await teammate.PutAsJsonAsync("/api/team/roles/" + member.PrincipalId, new MemberRoleChange("manager"))).StatusCode);

            Assert.Equal(HttpStatusCode.NotFound, await SetRole(owner, ownerSession.PrincipalId, "manager"));
            Assert.Equal(HttpStatusCode.BadRequest, await SetRole(owner, member.PrincipalId, "owner"));
            Assert.Equal(HttpStatusCode.OK, await SetRole(owner, member.PrincipalId, "contributor"));
            var wiki = await teammate.PutAsJsonAsync("/api/company-wiki", Page("From a contributor"));
            wiki.EnsureSuccessStatusCode();
            Assert.StartsWith("Member ", (await wiki.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("author").GetString());
            Assert.Equal(HttpStatusCode.OK, (await teammate.GetAsync("/api/organization/agents/marketing-main/files")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await teammate.PutAsJsonAsync("/api/organization/agents/marketing-main/files", File("NOTES.md"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await teammate.PostAsJsonAsync("/api/marketing/chat", new { requestId = "r", content = "Hi" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await teammate.PostAsJsonAsync("/api/marketing/drafts/1/decision", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await teammate.GetAsync("/api/export")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await teammate.PutAsJsonAsync("/api/organization", new { })).StatusCode);
            var state = await teammate.GetFromJsonAsync<JsonElement>("/api/state");
            Assert.Equal(JsonValueKind.Array, state.GetProperty("artifacts").ValueKind);
            Assert.Empty(state.GetProperty("chats").EnumerateArray());

            // Promotion and demotion take effect on the next request.
            Assert.Equal(HttpStatusCode.OK, await SetRole(owner, member.PrincipalId, "manager"));
            Assert.Equal(HttpStatusCode.OK, (await teammate.PutAsJsonAsync("/api/organization/agents/marketing-main/files", File("NOTES.md"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await teammate.PostAsJsonAsync("/api/marketing/runway/x/review", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, await SetRole(owner, member.PrincipalId, "viewer"));
            Assert.Equal(HttpStatusCode.Forbidden, (await teammate.GetAsync("/api/company-wiki")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await teammate.PostAsJsonAsync("/api/marketing/campaigns/abc/inputs", new { })).StatusCode);

            var roles = await owner.GetFromJsonAsync<JsonElement>("/api/team/roles");
            Assert.Equal("viewer", Assert.Single(roles.EnumerateArray()).GetProperty("role").GetString());
        }
    }
}
