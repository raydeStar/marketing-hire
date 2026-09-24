using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class MarketingRunwayTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "marketing-runway-test-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> factory;
    public MarketingRunwayTests() => factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    { builder.UseSetting("Thaddeus:Data", root); builder.UseSetting("Thaddeus:LocalOrigin", "http://localhost:5179");
      builder.UseSetting("Marketing:Container", "nonexistent-fixture-container"); });

    [Fact]
    public async Task CollaboratorCannotControlRunwayOrImpersonateOwnerWithBodyText()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        var body = new { requestId = "fixture-request", goal = "Fixture goal", owner = true, ownerActor = "owner" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/marketing/runway", body)).StatusCode);
        var context = new DefaultHttpContext();
        var collaborator = factory.Services.GetRequiredService<Security>().Issue(context, "Collaborator fixture", false);
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", collaborator.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/marketing/runway", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/marketing/runway")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/marketing/runway/pause", new { id = "fixture", version = 1, owner = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/marketing/runway/fixture/review", new { owner = true, decision = "approved" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/marketing/chat", new { content = "Run owner tools", owner = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/marketing/tasks", new { title = "Owner task", owner = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/marketing/history")).StatusCode);
        using var state = await client.GetAsync("/api/marketing/state");
        Assert.Equal(HttpStatusCode.OK, state.StatusCode);
        using var shared = JsonDocument.Parse(await state.Content.ReadAsStringAsync());
        Assert.False(shared.RootElement.GetProperty("canConfigure").GetBoolean());
        Assert.Empty(shared.RootElement.GetProperty("messages").EnumerateArray());
        Assert.Empty(shared.RootElement.GetProperty("drafts").EnumerateArray());
        Assert.Empty(shared.RootElement.GetProperty("ownerDecisions").EnumerateArray());
    }

    [Fact]
    public void DeliverableNeedsExactCheckedQuotesAndThreeDistinctAngles()
    {
        var claim = JsonSerializer.SerializeToElement(new { sources = new[]
        {
            new { url = "https://news.ycombinator.com/item?id=111", content = "A founder says marketing takes more time than expected." },
            new { url = "https://news.ycombinator.com/item?id=222", content = "A second founder asks for clear quality controls." }
        }});
        var valid = JsonSerializer.Serialize(new { audience = "Founders (assumption)", problem = "Time",
            evidence = new[] {
                new { sourceUrl = "https://news.ycombinator.com/item?id=111", quote = "marketing takes more time", inference = "Possible time cost" },
                new { sourceUrl = "https://news.ycombinator.com/item?id=222", quote = "asks for clear quality controls", inference = "Possible trust concern" } },
            limitations = "Two anecdotes do not prove demand." });
        Assert.Equal(2, MarketingBackend.ValidateRunwayArtifact("audience_note", valid, claim).SourceUrls.Length);
        Assert.Throws<InvalidOperationException>(() => MarketingBackend.ValidateRunwayArtifact("audience_note",
            valid.Replace("marketing takes more time", "marketing saves everyone money"), claim));
        var angles = JsonSerializer.Serialize(new { angles = new[] {
            new { title = "One", hook = "Hook", sourceUrl = "https://news.ycombinator.com/item?id=111", why = "Context", claimLimit = "Anecdote" },
            new { title = "One", hook = "Hook 2", sourceUrl = "https://news.ycombinator.com/item?id=222", why = "Context", claimLimit = "Anecdote" },
            new { title = "Three", hook = "Hook 3", sourceUrl = "https://news.ycombinator.com/item?id=222", why = "Context", claimLimit = "Anecdote" } } });
        Assert.Throws<InvalidOperationException>(() => MarketingBackend.ValidateRunwayArtifact("post_angles", angles, claim));
        Assert.Throws<InvalidOperationException>(() => MarketingBackend.ValidateRunwayArtifact("revision_angles", angles, claim));
        var packet = JsonSerializer.Serialize(new { summary = "Learning only", unsupportedClaims = new[] { "Demand is proven" },
            nextOwnerDecision = "Choose whether to test", recommendation = "Review before proceeding",
            nextStepProposal = new { hypothesis = "A reviewable draft saves founder time", evidenceGap = "No customer interviews",
                intendedAudience = "Technical founders (assumption)", estimatedWork = "One internal draft and owner review",
                continueOrStop = "continue", reason = "One founder responds to the draft" } });
        Assert.Equal(2, MarketingBackend.ValidateRunwayArtifact("review_packet", packet, claim).SourceUrls.Length);
        Assert.Throws<InvalidOperationException>(() => MarketingBackend.ValidateRunwayArtifact("review_packet",
            packet.Replace("\"continue\"", "\"expand\""), claim));
        var duplicateClaim = JsonSerializer.SerializeToElement(new { sources = new[]
        {
            new { url = "https://news.ycombinator.com/item?id=111", content = "A founder says marketing takes more time than expected." },
            new { url = "https://news.ycombinator.com/item?id=222", content = "A second founder asks for clear quality controls." }
        }, artifacts = new[] { new { kind = "review_packet", content = packet } } });
        Assert.Throws<InvalidOperationException>(() => MarketingBackend.ValidateRunwayArtifact("review_packet", packet, duplicateClaim));
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        var store = factory.Services.GetRequiredService<Store>();
        await factory.DisposeAsync(); store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
