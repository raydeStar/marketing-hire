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

public sealed class WorkspaceLibraryTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "workspace-library-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> factory;
    public WorkspaceLibraryTests() => factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
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

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static Task<HttpResponseMessage> File(HttpClient client, string key, int version, string? folder, params string[] tags) =>
        client.PutAsJsonAsync("/api/workspace-library/entries/" + Uri.EscapeDataString(key), new { expectedVersion = version, folder, tags });
    private static Task<HttpResponseMessage> Folders(HttpClient client, int version, string[] folders, object[]? moves = null) =>
        client.PutAsJsonAsync("/api/workspace-library/folders", new { expectedVersion = version, folders, moves });
    private static string[] Strings(JsonElement value) => value.EnumerateArray().Select(item => item.GetString()!).ToArray();
    private static JsonElement Entry(JsonElement view, string key) =>
        view.GetProperty("entries").EnumerateArray().Single(entry => entry.GetProperty("key").GetString() == key);

    [Fact] public async Task FilingIntoANestedFolderAddsItsAncestorsAndNormalizesTags()
    {
        var (owner, _) = Client(true);
        using (owner)
        {
            var empty = await Json(await owner.GetAsync("/api/workspace-library"));
            Assert.Equal(0, empty.GetProperty("version").GetInt32());
            Assert.Empty(empty.GetProperty("folders").EnumerateArray());
            Assert.Empty(empty.GetProperty("pins").EnumerateArray());

            var view = await Json(await File(owner, "wiki:brand-voice", 0, "  Launch / Q3 /Assets ", " Brand ", "brand", "Social-Media", "go_live"));
            Assert.Equal(1, view.GetProperty("version").GetInt32());
            Assert.Equal(["Launch", "Launch/Q3", "Launch/Q3/Assets"], Strings(view.GetProperty("folders")));
            var entry = Entry(view, "wiki:brand-voice");
            Assert.Equal("Launch/Q3/Assets", entry.GetProperty("folder").GetString());
            Assert.Equal(["brand", "social-media", "go_live"], Strings(entry.GetProperty("tags")));
            Assert.StartsWith("Owner ", entry.GetProperty("updatedBy").GetString());

            // A case-insensitive match keeps the existing spelling; folders sort ignoring case.
            view = await Json(await File(owner, "page:abc.123", 1, "launch/q3/Copy"));
            Assert.Equal(["archive", "Launch", "Launch/Q3", "Launch/Q3/Assets", "Launch/Q3/Copy"],
                Strings((await Json(await File(owner, "media:m1", 2, "archive"))).GetProperty("folders")));
            Assert.Equal("Launch/Q3/Copy", Entry(view, "page:abc.123").GetProperty("folder").GetString());

            // No folder and no tags removes the entry.
            view = await Json(await File(owner, "media:m1", 3, null));
            Assert.DoesNotContain(view.GetProperty("entries").EnumerateArray(), item => item.GetProperty("key").GetString() == "media:m1");
            Assert.Contains("archive", Strings(view.GetProperty("folders")));

            var export = await owner.GetFromJsonAsync<JsonElement>("/api/export");
            Assert.Equal(4, export.GetProperty("workspaceLibrary").GetProperty("version").GetInt32());
        }
    }

    [Fact] public async Task StaleVersionsConflictAndBadInputIsRejected()
    {
        var (owner, _) = Client(true);
        using (owner)
        {
            await Json(await File(owner, "draft:d1", 0, "Drafts"));
            var stale = await File(owner, "draft:d2", 0, "Drafts");
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("The library changed. Refresh and try again.", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
            Assert.Equal(HttpStatusCode.Conflict, (await Folders(owner, 0, ["Drafts"])).StatusCode);

            Assert.Equal(HttpStatusCode.BadRequest, (await File(owner, "unknown:x", 1, "Drafts")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await File(owner, "wiki:has space", 1, "Drafts")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await File(owner, "wiki:ok", 1, "/Drafts")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await File(owner, "wiki:ok", 1, "a//b")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await File(owner, "wiki:ok", 1, "a/b/c/d/e/f")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await File(owner, "wiki:ok", 1, null, "no#hash")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await File(owner, "wiki:ok", 1, null, Enumerable.Range(0, 13).Select(i => "t" + i).ToArray())).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/workspace-library/pins", new { keys = new[] { "bogus" } })).StatusCode);
            Assert.Equal(1, (await Json(await owner.GetAsync("/api/workspace-library"))).GetProperty("version").GetInt32());
        }
    }

    [Fact] public async Task RenamingAFolderRefilesEntriesAndSubFoldersAndDeletingUnfilesThem()
    {
        var (owner, _) = Client(true);
        using (owner)
        {
            await Json(await File(owner, "wiki:a", 0, "Launch/Q3"));
            await Json(await File(owner, "source:s", 1, "Launch", "competitor"));
            var view = await Json(await File(owner, "brief:b", 2, "Other"));

            // Rename Launch -> Campaigns/Fall; the client sends the renamed top folder, the host re-files the rest.
            view = await Json(await Folders(owner, 3, ["Campaigns/Fall", "Other"], [new { from = "Launch", to = "Campaigns/Fall" }]));
            Assert.Equal(["Campaigns", "Campaigns/Fall", "Campaigns/Fall/Q3", "Other"], Strings(view.GetProperty("folders")));
            Assert.Equal("Campaigns/Fall/Q3", Entry(view, "wiki:a").GetProperty("folder").GetString());
            Assert.Equal("Campaigns/Fall", Entry(view, "source:s").GetProperty("folder").GetString());

            // Deleting a folder without a move leaves its entries unfiled; a tagless entry disappears.
            view = await Json(await Folders(owner, 4, ["Campaigns/Fall/Q3"]));
            Assert.Equal(["Campaigns", "Campaigns/Fall", "Campaigns/Fall/Q3"], Strings(view.GetProperty("folders")));
            Assert.DoesNotContain(view.GetProperty("entries").EnumerateArray(), item => item.GetProperty("key").GetString() == "brief:b");

            // Deleting with an explicit move to unfiled clears the sub-tree too.
            view = await Json(await Folders(owner, 5, [], [new { from = "Campaigns", to = (string?)null }]));
            Assert.Empty(view.GetProperty("folders").EnumerateArray());
            var source = Entry(view, "source:s");
            Assert.Equal(JsonValueKind.Null, source.GetProperty("folder").ValueKind);
            Assert.Equal(["competitor"], Strings(source.GetProperty("tags")));
            Assert.Single(view.GetProperty("entries").EnumerateArray());
        }
    }

    [Fact] public async Task PinsArePersonalAndRolesGateEditing()
    {
        var (owner, ownerSession) = Client(true);
        var (contributor, member) = Client(false);
        var (reviewer, _) = Client(false, campaignOnly: true);
        using (owner) using (contributor) using (reviewer)
        {
            Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/team/roles/" + member.PrincipalId, new MemberRoleChange("contributor"))).StatusCode);

            var ownerPins = await Json(await owner.PutAsJsonAsync("/api/workspace-library/pins", new { keys = new[] { "wiki:a", "wiki:a", "page:p" } }));
            Assert.Equal(["wiki:a", "page:p"], Strings(ownerPins.GetProperty("pins")));
            var memberPins = await Json(await contributor.PutAsJsonAsync("/api/workspace-library/pins", new { keys = new[] { "media:m" } }));
            Assert.Equal(["media:m"], Strings(memberPins.GetProperty("pins")));
            Assert.Equal(2, memberPins.GetProperty("version").GetInt32());
            Assert.Equal(["wiki:a", "page:p"], Strings((await Json(await owner.GetAsync("/api/workspace-library"))).GetProperty("pins")));

            // A contributor edits shared entries under their own name.
            var edited = await Json(await File(contributor, "deliverable:x", 2, "Shared", "Final"));
            Assert.StartsWith("Member " + member.PrincipalId, Entry(edited, "deliverable:x").GetProperty("updatedBy").GetString());
            Assert.Equal(["media:m"], Strings(edited.GetProperty("pins")));
            Assert.Equal(HttpStatusCode.OK, (await Folders(contributor, 3, ["Shared", "More"])).StatusCode);

            // A campaign-only reviewer with no assigned role cannot see or change the library.
            Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.GetAsync("/api/workspace-library")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await File(reviewer, "wiki:a", 4, "X")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.PutAsJsonAsync("/api/workspace-library/pins", new { keys = new[] { "wiki:a" } })).StatusCode);

            // An assigned viewer does not reach the library at all.
            Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/team/roles/" + member.PrincipalId, new MemberRoleChange("viewer"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await contributor.GetAsync("/api/workspace-library")).StatusCode);
            Assert.NotEqual(ownerSession.PrincipalId, member.PrincipalId);
        }
    }
}
