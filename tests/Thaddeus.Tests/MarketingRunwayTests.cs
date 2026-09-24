using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
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
    public void DirectLedgerCampaignCannotImpersonateAnAuthenticatedOwnerReceipt()
    {
        var backend = factory.Services.GetRequiredService<MarketingBackend>();
        const string id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string source = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string digest = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        const string brief = "{\"audience\":\"Founders\"}";
        const string experiment = "{\"decision_rule\":\"learning_only\"}";
        var raw = JsonSerializer.SerializeToElement(new { project = new { id }, campaign_revisions = new[] { new { version = 1 } }, campaign = new {
            runway_id = id, version = 1, source_artifact_id = source,
            source_artifact_digest = digest, brief_json = brief, experiment_json = experiment } });
        Assert.False(backend.WithCampaignAuthority(raw).GetProperty("campaign").GetProperty("owner_verified").GetBoolean());
        using (var db = new SqliteConnection($"Data Source={Path.Combine(root, "marketing-chat.sqlite")}"))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO owner_campaign_briefs VALUES($request,$id,1,$source,$digest,$owner,$brief,$experiment,$time)";
            command.Parameters.AddWithValue("$request", "fixture-owner-receipt");
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$source", source);
            command.Parameters.AddWithValue("$digest", digest);
            command.Parameters.AddWithValue("$owner", "host-owner-session");
            command.Parameters.AddWithValue("$brief", brief);
            command.Parameters.AddWithValue("$experiment", experiment);
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
        Assert.True(backend.WithCampaignAuthority(raw).GetProperty("campaign").GetProperty("owner_verified").GetBoolean());
        var changed = JsonSerializer.SerializeToElement(new { project = new { id }, campaign_revisions = new[] { new { version = 1 } }, campaign = new {
            runway_id = id, version = 1, source_artifact_id = source,
            source_artifact_digest = digest, brief_json = "{\"audience\":\"Other\"}", experiment_json = experiment } });
        Assert.False(backend.WithCampaignAuthority(changed).GetProperty("campaign").GetProperty("owner_verified").GetBoolean());
    }

    [Fact]
    public async Task IsolatedFixtureCampaignTraversesAuthenticatedHttpWithoutPublishing()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var script = Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py");
        var fixtureLedger = Path.Combine(root, "isolated-campaign-ledger");
        using var isolated = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "fixture-host"));
            builder.UseSetting("Marketing:FixtureLedger", fixtureLedger);
            builder.UseSetting("Marketing:FixtureRunwayScript", script);
        });
        using var client = isolated.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        var context = new DefaultHttpContext();
        var owner = isolated.Services.GetRequiredService<Security>().Issue(context, "Owner fixture", true);
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        async Task<JsonElement> Post(string path, object body)
        {
            using var response = await client.PostAsJsonAsync(path, body);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }
        var seeded = await Post("/api/marketing/runway/fixture/seed", new { requestId = "isolated-campaign-seed" });
        var id = seeded.GetProperty("project").GetProperty("id").GetString()!;
        Assert.Equal("needs_review", seeded.GetProperty("project").GetProperty("status").GetString());
        Assert.Equal(0, seeded.GetProperty("project").GetProperty("token_used").GetInt32());
        var source = seeded.GetProperty("artifacts")[0];
        var asset = seeded.GetProperty("artifacts")[1];
        var brief = new { audience = "Founders", problem = "Marketing time", hypothesis = "A bounded draft is clearer",
            proposition = "Configurable marketing employee", desired_behavior = "Ask for a demo",
            channel = "Owner reviewed draft", primary_metric = "Qualified replies",
            metric_definition = "Count relevant replies", guardrail = "No outcome guarantee" };
        var experiment = new { intervention = "One fixture draft", target_population = "Founders",
            observation_window = "Seven days", metric_source = "Fixture observation",
            decision_rule = "minimum_sample", minimum_sample = 3 };
        var saved = await Post($"/api/marketing/runway/{id}/campaign-brief", new {
            requestId = "fixture-brief-http", projectVersion = seeded.GetProperty("project").GetProperty("version").GetInt32(),
            version = 0, sourceArtifactId = source.GetProperty("id").GetString(),
            sourceArtifactDigest = source.GetProperty("digest").GetString(), brief, experiment });
        Assert.True(saved.GetProperty("campaign").GetProperty("owner_verified").GetBoolean());
        var repeated = await Post($"/api/marketing/runway/{id}/campaign-brief", new {
            requestId = "fixture-brief-http", projectVersion = seeded.GetProperty("project").GetProperty("version").GetInt32(),
            version = 0, sourceArtifactId = source.GetProperty("id").GetString(),
            sourceArtifactDigest = source.GetProperty("digest").GetString(), brief, experiment });
        Assert.Single(repeated.GetProperty("campaign_revisions").EnumerateArray());
        var reviewed = await Post($"/api/marketing/runway/{id}/review", new {
            requestId = "fixture-approval-http", version = saved.GetProperty("project").GetProperty("version").GetInt32(),
            artifactId = asset.GetProperty("id").GetString(), digest = asset.GetProperty("digest").GetString(),
            decision = "approved" });
        var reviewId = reviewed.GetProperty("reviews").EnumerateArray().Last().GetProperty("id").GetString();
        var current = reviewed;
        async Task<JsonElement> Act(string action, object payload, string requestId)
        {
            current = await Post($"/api/marketing/runway/{id}/campaign-action", new {
                requestId, projectVersion = current.GetProperty("project").GetProperty("version").GetInt32(),
                version = current.GetProperty("campaign").GetProperty("version").GetInt32(), action, payload });
            return current;
        }
        await Act("align", new { review_id = reviewId, asset_id = asset.GetProperty("id").GetString(),
            asset_digest = asset.GetProperty("digest").GetString() }, "align-http");
        var checklist = new { asset = "checked", link = "not_applicable", tracking = "fixture_only",
            destination = "fixture_only", rollback = "fixture_reset" };
        await Act("launch", new { destination = "fixture://publisher", checklist }, "launch-http");
        var launch = current.GetProperty("campaign_actions").EnumerateArray().Last();
        Assert.Equal("simulated", launch.GetProperty("status").GetString());
        Assert.Contains("SIMULATED_ONLY", launch.GetProperty("payload_json").GetString());
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        object Observation(string observationId, int numerator, int denominator) => new {
            observation_id = observationId, source = "Fixture observation", captured_at = now,
            period_start = now - 3600, period_end = now - 30, timezone = "America/Denver",
            metric_definition = "Count relevant replies", attribution_limitations = "Synthetic, no causal inference",
            numerator, denominator, value_type = "actual" };
        await Act("measure", Observation("http-observation-1", 1, 2), "measure-http-1");
        await Act("decide", new { decision = "collect_evidence", rationale = "Two actual observations are insufficient" }, "wait-http");
        Assert.Equal("measure", current.GetProperty("campaign").GetProperty("stage").GetString());
        await Act("measure", Observation("http-observation-2", 0, 1), "measure-http-2");
        await Act("decide", new { decision = "pause", rationale = "Small synthetic result; pause for real evidence" }, "decision-http");
        await Act("learn", new { lesson = "Specific controls may help clarity", context = "Founders, fixture draft",
            uncertainty = "No real audience response", revisit_condition = "Real evidence arrives",
            next_action = "Remain paused" }, "lesson-http");
        Assert.Equal("complete", current.GetProperty("campaign").GetProperty("stage").GetString());
        Assert.True(current.GetProperty("campaign").GetProperty("owner_verified").GetBoolean());
        var reopened = await client.GetFromJsonAsync<JsonElement>($"/api/marketing/runways/{id}");
        Assert.Equal("complete", reopened.GetProperty("campaign").GetProperty("stage").GetString());
        Assert.Equal(0, reopened.GetProperty("project").GetProperty("token_used").GetInt32());
        var workState = await client.GetFromJsonAsync<JsonElement>("/api/marketing/state");
        Assert.True(workState.GetProperty("fixtureCampaignEnabled").GetBoolean());
        Assert.Equal("connected", workState.GetProperty("connection").GetProperty("status").GetString());
        Assert.True(workState.GetProperty("runway").GetProperty("campaign").GetProperty("owner_verified").GetBoolean());
        var lessons = await Post("/api/marketing/runway/fixture/lessons", new { audience = "founders" });
        Assert.Single(lessons.GetProperty("lessons").EnumerateArray());
    }

    [Fact]
    public void WorkerMeterPreflightRequiresTheExactReadyRoute()
    {
        using var ready = JsonDocument.Parse("""{"ready":true,"guardInstalled":true,"nativeGuarded":true,"version":"marketing-meter-v5","route":"openai/gpt-5.6-luna","transport":"sse"}""");
        Assert.True(MarketingBackend.RunwayMeterReady(ready.RootElement));
        using var nativeBypass = JsonDocument.Parse("""{"ready":true,"guardInstalled":true,"nativeGuarded":false,"version":"marketing-meter-v5","route":"openai/gpt-5.6-luna","transport":"sse"}""");
        Assert.False(MarketingBackend.RunwayMeterReady(nativeBypass.RootElement));
        using var unloaded = JsonDocument.Parse("""{"ready":false,"guardInstalled":false,"nativeGuarded":false,"version":"marketing-meter-v5","route":"openai/gpt-5.6-luna","transport":"sse"}""");
        Assert.False(MarketingBackend.RunwayMeterReady(unloaded.RootElement));
        using var changed = JsonDocument.Parse("""{"ready":true,"guardInstalled":true,"nativeGuarded":true,"version":"marketing-meter-v5","route":"openai/gpt-6-luna","transport":"sse"}""");
        Assert.False(MarketingBackend.RunwayMeterReady(changed.RootElement));
        using var oldMeter = JsonDocument.Parse("""{"ready":true,"guardInstalled":true,"nativeGuarded":true,"version":"marketing-meter-v2","route":"openai/gpt-5.6-luna","transport":"sse"}""");
        Assert.False(MarketingBackend.RunwayMeterReady(oldMeter.RootElement));
        using var layeredMeter = JsonDocument.Parse("""{"ready":true,"guardInstalled":true,"nativeGuarded":true,"version":"marketing-meter-v3","route":"openai/gpt-5.6-luna","transport":"sse"}""");
        Assert.False(MarketingBackend.RunwayMeterReady(layeredMeter.RootElement));
        using var uncappedMeter = JsonDocument.Parse("""{"ready":true,"guardInstalled":true,"nativeGuarded":true,"version":"marketing-meter-v4","route":"openai/gpt-5.6-luna","transport":"sse"}""");
        Assert.False(MarketingBackend.RunwayMeterReady(uncappedMeter.RootElement));
        using var socket = JsonDocument.Parse("""{"ready":true,"version":"marketing-meter-v2","route":"openai/gpt-5.6-luna","transport":"websocket"}""");
        Assert.False(MarketingBackend.RunwayMeterReady(socket.RootElement));
        using var invalid = JsonDocument.Parse("""{"ready":true,"version":7,"route":"openai/gpt-5.6-luna","transport":"sse"}""");
        Assert.False(MarketingBackend.RunwayMeterReady(invalid.RootElement));
    }

    [Fact]
    public async Task ExplicitShortPilotModeStillRefusesAssignmentWithoutReadyMeter()
    {
        Assert.False(factory.Services.GetRequiredService<MarketingBackend>().RunwayLiveInferenceEnabled);
        using var pilot = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "pilot"));
            builder.UseSetting("Marketing:RunwayPilotMode", "v5-short-pilot");
        });
        var backend = pilot.Services.GetRequiredService<MarketingBackend>();
        Assert.True(backend.RunwayLiveInferenceEnabled);
        using var client = pilot.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        var context = new DefaultHttpContext();
        var owner = pilot.Services.GetRequiredService<Security>().Issue(context, "Owner fixture", true);
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        using var response = await client.PostAsJsonAsync("/api/marketing/runway",
            new { requestId = "pilot-meter-unavailable", goal = "Prepare a bounded draft packet" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("request meter is unavailable", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.PostAsJsonAsync("/api/marketing/runway/resume",
                new { id = new string('a', 32), version = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.PostAsJsonAsync("/api/marketing/revision-grants/" + new string('a', 32) + "/release",
                new { })).StatusCode);
    }

    [Fact]
    public void NativeGatewayReplyPreservesTextAndReportedRequestUsage()
    {
        using var confirmed = JsonDocument.Parse("""{"runId":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","status":"ok","result":{"payloads":[{"text":"Offline Gateway receipt only"}],"meta":{"agentMeta":{"credentialSource":{"kind":"profile"},"usage":{"input":5,"output":3,"total":8}}}}}""");
        var (reply, usage, total) = MarketingBackend.ReadRunwayReply(confirmed.RootElement);
        Assert.Equal("Offline Gateway receipt only", reply);
        Assert.NotNull(usage);
        Assert.Equal(8, total);
        using var uncertain = JsonDocument.Parse("""{"status":"ok","result":{"payloads":[{"text":"Unmetered output"}],"meta":{"agentMeta":{}}}}""");
        Assert.Null(MarketingBackend.ReadRunwayReply(uncertain.RootElement).TotalTokens);
    }

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
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/marketing/runways")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/marketing/runways/" + new string('f', 32))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/marketing/runway/pause", new { id = "fixture", version = 1, owner = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/marketing/runway/fixture/review", new { owner = true, decision = "approved" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/marketing/runway/fixture/campaign-brief", new { owner = true, stage = "launch" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/marketing/runway/fixture/seed", new { owner = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/marketing/runway/fixture/campaign-action", new { owner = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/marketing/runway/fixture/lessons", new { owner = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/marketing/runway/fixture/revision-grants", new { owner = true })).StatusCode);
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

    [Fact]
    public async Task SharedGatewayIngressKeepsOwnerControlAndDoesNotTrustBodyIdentity()
    {
        const string project = "91c4b1df1e6942e2a986936127b37742";
        using var collaboratorClient = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        collaboratorClient.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        var collaboratorContext = new DefaultHttpContext();
        var collaborator = factory.Services.GetRequiredService<Security>().Issue(collaboratorContext, "Collaborator fixture", false);
        collaboratorClient.DefaultRequestHeaders.Add("Cookie", collaboratorContext.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        collaboratorClient.DefaultRequestHeaders.Add("X-CSRF", collaborator.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await collaboratorClient.PostAsJsonAsync($"/api/marketing/runway/{project}/shared", new { owner = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await collaboratorClient.PostAsJsonAsync($"/api/marketing/runway/{project}/shared/collaborator",
                new { owner = true, deviceId = collaborator.Id })).StatusCode);
        using var empty = await collaboratorClient.GetAsync($"/api/marketing/runway/{project}/shared");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        using var emptyJson = JsonDocument.Parse(await empty.Content.ReadAsStringAsync());
        Assert.False(emptyJson.RootElement.GetProperty("available").GetBoolean());

        using var ownerClient = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        ownerClient.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        var ownerContext = new DefaultHttpContext();
        var owner = factory.Services.GetRequiredService<Security>().Issue(ownerContext, "Owner fixture", true);
        ownerClient.DefaultRequestHeaders.Add("Cookie", ownerContext.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        ownerClient.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        Assert.Equal(HttpStatusCode.Conflict,
            (await ownerClient.PostAsJsonAsync("/api/marketing/runway/resume", new { id = project, version = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await ownerClient.GetAsync("/api/marketing/runways/not-a-project")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await ownerClient.PostAsJsonAsync($"/api/marketing/runway/{project}/revision-grants",
                new { requestId = "incomplete-grant", owner = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await ownerClient.PostAsJsonAsync($"/api/marketing/runway/{project}/shared", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await ownerClient.PostAsJsonAsync($"/api/marketing/runway/{project}/shared/collaborator",
                new { deviceId = collaborator.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await collaboratorClient.PostAsJsonAsync($"/api/marketing/runway/{project}/shared/suggestions",
                new { owner = true, requestId = "fixture", version = 1, content = "Do not spend" })).StatusCode);
    }

    [Fact]
    public async Task NativeProjectViewNeedsOwnerApprovedActiveDevice()
    {
        const string project = "91c4b1df1e6942e2a986936127b37742";
        var security = factory.Services.GetRequiredService<Security>();
        _ = factory.Services.GetRequiredService<MarketingBackend>();
        using (var db = new SqliteConnection($"Data Source={Path.Combine(root, "marketing-chat.sqlite")}"))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO shared_marketing_sessions " +
                "(project_id,session_key,session_id,creator_profile,created_at) " +
                "VALUES($project,$key,$session,$creator,$time)";
            command.Parameters.AddWithValue("$project", project);
            command.Parameters.AddWithValue("$key", "agent:shared-marketing:fixture-room");
            command.Parameters.AddWithValue("$session", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("$creator", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
        using var collaboratorClient = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        collaboratorClient.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        var collaboratorContext = new DefaultHttpContext();
        var collaborator = security.Issue(collaboratorContext, "Collaborator fixture", false);
        collaboratorClient.DefaultRequestHeaders.Add("Cookie", collaboratorContext.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        collaboratorClient.DefaultRequestHeaders.Add("X-CSRF", collaborator.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, (await collaboratorClient.GetAsync($"/api/marketing/runway/{project}/shared")).StatusCode);

        using var ownerClient = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        ownerClient.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        var ownerContext = new DefaultHttpContext();
        var owner = security.Issue(ownerContext, "Owner fixture", true);
        ownerClient.DefaultRequestHeaders.Add("Cookie", ownerContext.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        ownerClient.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        Assert.Equal(HttpStatusCode.OK,
            (await ownerClient.PostAsJsonAsync($"/api/marketing/runway/{project}/shared/collaborator",
                new { deviceId = collaborator.Id })).StatusCode);
        using var shared = await collaboratorClient.GetAsync($"/api/marketing/runway/{project}/shared");
        Assert.Equal(HttpStatusCode.OK, shared.StatusCode);
        using var receipt = JsonDocument.Parse(await shared.Content.ReadAsStringAsync());
        Assert.True(receipt.RootElement.GetProperty("available").GetBoolean());
        const string uncertainRequest = "fixture-unknown-native-input";
        using (var db = new SqliteConnection($"Data Source={Path.Combine(root, "marketing-chat.sqlite")}"))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO shared_marketing_inputs " +
                "(request_id,project_id,actor_id,actor_name,content,project_version,status,created_at,updated_at) " +
                "VALUES($request,$project,$actor,$name,$content,1,'unknown',$time,$time)";
            command.Parameters.AddWithValue("$request", uncertainRequest);
            command.Parameters.AddWithValue("$project", project);
            command.Parameters.AddWithValue("$actor", collaborator.Id);
            command.Parameters.AddWithValue("$name", collaborator.Name);
            command.Parameters.AddWithValue("$content", "Unconfirmed native suggestion");
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await collaboratorClient.PostAsJsonAsync($"/api/marketing/runway/{project}/shared/reconcile",
                new { requestId = uncertainRequest })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await ownerClient.PostAsJsonAsync($"/api/marketing/runway/{project}/shared/reconcile",
                new { requestId = uncertainRequest })).StatusCode);
        security.Revoke(collaborator.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await collaboratorClient.GetAsync($"/api/marketing/runway/{project}/shared")).StatusCode);
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
