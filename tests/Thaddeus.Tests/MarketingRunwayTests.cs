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
    [Fact]
    public void WorkerReplySurvivesMissingUsageWithoutBecomingAnArtifactOrAcceptingReplacement()
    {
        var backend = factory.Services.GetRequiredService<MarketingBackend>();
        var projectId = new string('a', 32); var executionId = new string('b', 32);
        var claim = JsonSerializer.SerializeToElement(new { execution_id = executionId,
            project = new { id = projectId }, step = new { id = new string('c', 32), kind = "audience_note" } });
        var response = JsonSerializer.SerializeToElement(new { payloads = new[] { new { text = "Returned fixture draft" } } });
        Assert.Equal("Returned fixture draft", backend.RecordWorkerResponse(claim, response));
        backend.RecordWorkerResponse(claim, response);
        Assert.Throws<IOException>(() => MarketingBackend.ConfirmedProviderTokens(null, "receipt missing"));
        var raw = JsonSerializer.SerializeToElement(new { project = new { id = projectId },
            artifacts = Array.Empty<object>(), worker_responses = new[] { new { content = "Forged ledger response" } } });
        var saved = backend.WithCampaignAuthority(raw);
        Assert.Empty(saved.GetProperty("artifacts").EnumerateArray());
        var replies = saved.GetProperty("worker_responses");
        Assert.Single(replies.EnumerateArray());
        Assert.Equal(executionId, replies[0].GetProperty("execution_id").GetString());
        Assert.Equal("Returned fixture draft", replies[0].GetProperty("content").GetString());
        Assert.False(replies[0].GetProperty("truncated").GetBoolean());
        Assert.Throws<InvalidOperationException>(() => backend.RecordWorkerResponse(claim,
            JsonSerializer.SerializeToElement(new { payloads = new[] { new { text = "Replacement" } } })));
        var other = JsonSerializer.SerializeToElement(new { project = new { id = new string('d', 32) }, worker_responses = replies });
        Assert.Empty(backend.WithCampaignAuthority(other).GetProperty("worker_responses").EnumerateArray());
        Assert.Equal(replies.GetRawText(), backend.WithCampaignAuthority(raw).GetProperty("worker_responses").GetRawText());
    }

    [Fact]
    public void OversizedWorkerReplyKeepsBoundedPreviewAndFullOriginalDigest()
    {
        var backend = factory.Services.GetRequiredService<MarketingBackend>();
        var projectId = new string('a', 32);
        var claim = JsonSerializer.SerializeToElement(new { execution_id = new string('b', 32),
            project = new { id = projectId }, step = new { id = new string('c', 32), kind = "post_angles" } });
        var text = new string('x', 13000);
        var response = JsonSerializer.SerializeToElement(new { payloads = new[] { new { text } } });
        Assert.Equal(text, backend.RecordWorkerResponse(claim, response));
        var raw = JsonSerializer.SerializeToElement(new { project = new { id = projectId } });
        var saved = backend.WithCampaignAuthority(raw).GetProperty("worker_responses")[0];
        Assert.Equal(12000, saved.GetProperty("content").GetString()!.Length);
        Assert.Equal(13000, saved.GetProperty("original_characters").GetInt32());
        Assert.True(saved.GetProperty("truncated").GetBoolean());
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))),
            saved.GetProperty("digest").GetString());
    }

    [Fact]
    public void SourceDiscoveryPinsItsHostAndRejectsUnusableOrStaleCandidates()
    {
        var now = DateTimeOffset.UtcNow;
        var uri = MarketingSourceSearch.SearchUri("marketing & tags=other", now);
        Assert.Equal("hn.algolia.com", uri.Host);
        Assert.Contains("marketing%20%26%20tags%3Dother", uri.Query);
        foreach (var query in new[] { "", "a", new string('x', 121), "private\nquery" })
            Assert.Throws<ArgumentException>(() => MarketingSourceSearch.SearchUri(query, now));
        var data = JsonSerializer.SerializeToElement(new { hits = new object[] {
            new { objectID = "111", title = "Current discussion", created_at_i = now.AddDays(-1).ToUnixTimeSeconds(), num_comments = 8, url = "http://127.0.0.1/ignored" },
            new { objectID = "111", title = "Duplicate", created_at_i = now.ToUnixTimeSeconds() },
            new { objectID = "222", title = "Old discussion", created_at_i = now.AddDays(-91).ToUnixTimeSeconds() },
            new { objectID = "333", title = "Malformed date", created_at_i = "yesterday" },
            new { objectID = "../private", title = "Bad ID", created_at_i = now.ToUnixTimeSeconds() },
            new { objectID = "444", title = "Future post", created_at_i = now.AddDays(1).ToUnixTimeSeconds() }
        } });
        var items = MarketingSourceSearch.Parse(data, now);
        Assert.Single(items); Assert.Equal("https://news.ycombinator.com/item?id=111", items[0].Url);
        Assert.Equal(8, items[0].Comments);
    }

    [Fact]
    public void RevisionAuthorityRequiresExactSavedOwnerInstructionEvenWithoutCampaignBrief()
    {
        var backend = factory.Services.GetRequiredService<MarketingBackend>();
        var review = new { id = "review-fixture", artifact_id = "draft-fixture", artifact_digest = "digest-fixture",
            decision = "revision_requested", actor_id = "owner-fixture", instruction = "Remove the unsupported claim." };
        var raw = JsonSerializer.SerializeToElement(new { project = new { id = "project-fixture" }, reviews = new[] { review } });
        bool Verified(JsonElement value) => backend.WithCampaignAuthority(value).GetProperty("reviews")[0].GetProperty("owner_verified").GetBoolean();
        Assert.False(Verified(raw));
        using var db = new SqliteConnection($"Data Source={Path.Combine(root, "marketing-chat.sqlite")}");
        db.Open(); using var command = db.CreateCommand();
        command.CommandText = "INSERT INTO owner_runway_reviews VALUES('request-fixture','review-fixture','project-fixture','owner-fixture','draft-fixture','digest-fixture','revision_requested','2026-09-24')";
        command.ExecuteNonQuery();
        Assert.False(Verified(raw));
        command.CommandText = "INSERT INTO owner_revision_instructions VALUES('review-fixture','Remove the unsupported claim.')";
        command.ExecuteNonQuery();
        Assert.True(Verified(raw));
        Assert.False(Verified(JsonSerializer.SerializeToElement(new { project = new { id = "project-fixture" }, reviews = new[] {
            new { review.id, review.artifact_id, review.artifact_digest, review.decision, review.actor_id, instruction = "Different feedback" } } })));
    }

    [Fact]
    public void ChatUsageIsDurableIdempotentAndMissingCountsRemainUnknown()
    {
        var backend = factory.Services.GetRequiredService<MarketingBackend>();
        backend.RecordChatDispatch("reported-chat");
        backend.RecordChatUsage("reported-chat", """{"result":{"meta":{"agentMeta":{"usage":{"input":12,"output":8,"total":20}}}}}""");
        backend.RecordChatDispatch("reported-chat");
        backend.RecordChatUsage("reported-chat", """{"result":{"meta":{"agentMeta":{"usage":{"total":999}}}}}""");
        backend.RecordChatDispatch("unknown-chat");
        backend.RecordChatUsage("unknown-chat", "malformed reply");
        var events = JsonSerializer.SerializeToElement(backend.ChatUsageHistory());
        Assert.Equal(2, events.GetArrayLength());
        Assert.Equal(20, events[0].GetProperty("totalTokens").GetInt32());
        Assert.Equal(JsonValueKind.Null, events[1].GetProperty("totalTokens").ValueKind);
        Assert.Equal("unknown", events[1].GetProperty("status").GetString());
        using var db = new SqliteConnection($"Data Source={Path.Combine(root, "marketing-chat.sqlite")}");
        db.Open(); using var command = db.CreateCommand();
        command.CommandText = "SELECT total_tokens FROM marketing_chat_usage WHERE request_id='reported-chat'";
        Assert.Equal(20L, command.ExecuteScalar());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"result\":[]}")]
    [InlineData("{\"result\":{\"meta\":{\"agentMeta\":{\"usage\":{\"input\":12,\"output\":8}}}}}")]
    [InlineData("{\"result\":{\"meta\":{\"agentMeta\":{\"usage\":{\"total\":-1}}}}}")]
    [InlineData("{\"result\":{\"meta\":{\"agentMeta\":{\"usage\":{\"input\":12,\"output\":8,\"total\":10}}}}}")]
    public void ChatTrackerNeverInventsUsage(string json) => Assert.Null(MarketingBackend.ReadChatTokenCounts(json));

    [Fact]
    public async Task CollaboratorCannotReadOwnerTokenHistory()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        var security = factory.Services.GetRequiredService<Security>();
        var context = new DefaultHttpContext();
        security.Issue(context, "Usage collaborator fixture", false);
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/marketing/usage")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/marketing/sources/search?query=marketing")).StatusCode);
    }

    [Fact]
    public void ChatRespectsExecutionOwnershipEvenWhenLiveAdmissionIsDisabled()
    {
        Assert.NotNull(MarketingBackend.RunwayChatBlocker(null, "ledger unavailable"));
        Assert.Null(MarketingBackend.RunwayChatBlocker(null, null));
        foreach (var status in new[] { "running", "unknown", "paused" })
            Assert.NotNull(MarketingBackend.RunwayChatBlocker(JsonSerializer.SerializeToElement(
                new { project = new { status, active_execution = "held-request" } }), null));
        Assert.Null(MarketingBackend.RunwayChatBlocker(JsonSerializer.SerializeToElement(
            new { project = new { status = "needs_review", active_execution = (string?)null } }), null));
    }

    [Fact]
    public void AssignmentSourcesMustBeExplicitDistinctAndWithinReadScope()
    {
        Assert.Throws<ArgumentException>(() => MarketingBackend.RunwaySourceUrls(JsonSerializer.SerializeToElement(new { })));
        var first = "https://news.ycombinator.com/item?id=111";
        var second = "https://news.ycombinator.com/item?id=222";
        Assert.Equal(new[] { first, second }, MarketingBackend.RunwaySourceUrls(
            JsonSerializer.SerializeToElement(new { sourceUrls = new[] { first, second } })));
        foreach (var other in new[] { first, "http://news.ycombinator.com/item?id=222", "https://127.0.0.1/private", "https://news.ycombinator.com/item?id=222#fragment" })
            Assert.Throws<ArgumentException>(() => MarketingBackend.RunwaySourceUrls(
                JsonSerializer.SerializeToElement(new { sourceUrls = new[] { first, other } })));
    }

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
        const string actionId = "dddddddddddddddddddddddddddddddd";
        const string payload = "{\"observation_id\":\"owner-supplied\"}";
        var withObservation = JsonSerializer.SerializeToElement(new { project = new { id },
            campaign_revisions = new[] { new { version = 1 } }, campaign = new {
                runway_id = id, version = 2, source_artifact_id = source,
                source_artifact_digest = digest, brief_json = brief, experiment_json = experiment },
            campaign_actions = new[] { new { id = actionId, request_id = "owner-observation-fixture",
                actor_id = "host-owner-session", action = "manual_observation", payload_json = payload } } });
        Assert.False(backend.WithCampaignAuthority(withObservation).GetProperty("campaign_actions")[0]
            .GetProperty("owner_verified").GetBoolean());
        using (var db = new SqliteConnection($"Data Source={Path.Combine(root, "marketing-chat.sqlite")}"))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO owner_campaign_observations VALUES($request,$action,$campaign,$owner,$digest,$time)";
            command.Parameters.AddWithValue("$request", "owner-observation-fixture");
            command.Parameters.AddWithValue("$action", actionId);
            command.Parameters.AddWithValue("$campaign", id);
            command.Parameters.AddWithValue("$owner", "host-owner-session");
            command.Parameters.AddWithValue("$digest", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant());
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
        Assert.True(backend.WithCampaignAuthority(withObservation).GetProperty("campaign_actions")[0]
            .GetProperty("owner_verified").GetBoolean());
        var forgedActor = JsonSerializer.SerializeToElement(new { project = new { id },
            campaign_revisions = new[] { new { version = 1 } }, campaign = new {
                runway_id = id, version = 2, source_artifact_id = source,
                source_artifact_digest = digest, brief_json = brief, experiment_json = experiment },
            campaign_actions = new[] { new { id = actionId, request_id = "owner-observation-fixture",
                actor_id = "forged-actor", action = "manual_observation", payload_json = payload } } });
        Assert.False(backend.WithCampaignAuthority(forgedActor).GetProperty("campaign_actions")[0]
            .GetProperty("owner_verified").GetBoolean());
        const string adoptionId = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        const string adoptionPayload = "{\"revision_artifact_id\":\"linked-draft\",\"external_effect\":false}";
        var withAdoption = JsonSerializer.SerializeToElement(new { project = new { id },
            campaign_revisions = new[] { new { version = 1 } }, campaign = new {
                runway_id = id, version = 3, source_artifact_id = source,
                source_artifact_digest = digest, brief_json = brief, experiment_json = experiment },
            campaign_actions = new[] { new { id = adoptionId, request_id = "owner-adoption-fixture",
                actor_id = "host-owner-session", action = "adopt_revision", payload_json = adoptionPayload } } });
        Assert.False(backend.WithCampaignAuthority(withAdoption).GetProperty("campaign_actions")[0]
            .GetProperty("owner_verified").GetBoolean());
        using (var db = new SqliteConnection($"Data Source={Path.Combine(root, "marketing-chat.sqlite")}"))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO owner_campaign_adoptions VALUES($request,$action,$campaign,$owner,$digest,$time)";
            command.Parameters.AddWithValue("$request", "owner-adoption-fixture");
            command.Parameters.AddWithValue("$action", adoptionId);
            command.Parameters.AddWithValue("$campaign", id);
            command.Parameters.AddWithValue("$owner", "host-owner-session");
            command.Parameters.AddWithValue("$digest", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(adoptionPayload))).ToLowerInvariant());
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
        Assert.True(backend.WithCampaignAuthority(withAdoption).GetProperty("campaign_actions")[0]
            .GetProperty("owner_verified").GetBoolean());
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
            priority_rationale = "Founder attention is the current bottleneck in the checked source notes",
            proposition = "Configurable marketing employee", desired_behavior = "Ask for a demo",
            channel = "Owner reviewed draft", primary_metric = "Qualified replies",
            metric_definition = "Count relevant replies", guardrail = "No outcome guarantee",
            review_timing = "At owner review; no calendar date set",
            non_goals = "No new channels or unverified product claims" };
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
        var nowForRejectedObservation = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using (var rejectedObservation = await client.PostAsJsonAsync($"/api/marketing/runway/{id}/campaign-observation", new {
            requestId = "fixture-must-not-become-real", projectVersion = saved.GetProperty("project").GetProperty("version").GetInt32(),
            version = saved.GetProperty("campaign").GetProperty("version").GetInt32(),
            observation = new { observation_id = "fixture-observation", source = "Fixture observation",
                source_reference = "Synthetic sheet", interpretation = "No real outcome", captured_at = nowForRejectedObservation,
                period_start = nowForRejectedObservation - 3600, period_end = nowForRejectedObservation - 30,
                timezone = "America/Denver", metric_definition = "Count relevant replies",
                attribution_limitations = "Synthetic only", numerator = 0, denominator = 1, value_type = "actual" } }))
            Assert.Equal(HttpStatusCode.Conflict, rejectedObservation.StatusCode);
        var reviewed = await Post($"/api/marketing/runway/{id}/review", new {
            requestId = "fixture-revision-http", version = saved.GetProperty("project").GetProperty("version").GetInt32(),
            artifactId = asset.GetProperty("id").GetString(), digest = asset.GetProperty("digest").GetString(),
            decision = "revision_requested", instruction = "Make the first hook more specific" });
        var revisionReviewId = reviewed.GetProperty("reviews").EnumerateArray().Last().GetProperty("id").GetString();
        var current = reviewed;
        async Task<JsonElement> Act(string action, object payload, string requestId)
        {
            current = await Post($"/api/marketing/runway/{id}/campaign-action", new {
                requestId, projectVersion = current.GetProperty("project").GetProperty("version").GetInt32(),
                version = current.GetProperty("campaign").GetProperty("version").GetInt32(), action, payload });
            return current;
        }
        await Act("revise_asset", new { review_id = revisionReviewId,
            predecessor_id = asset.GetProperty("id").GetString(),
            predecessor_digest = asset.GetProperty("digest").GetString(),
            revision_note = "Make the first hook more specific" }, "revision-asset-http");
        var revisedAsset = current.GetProperty("artifacts").EnumerateArray().Last();
        Assert.Equal("revision_angles", revisedAsset.GetProperty("kind").GetString());
        Assert.Equal(asset.GetProperty("id").GetString(),
            JsonDocument.Parse(revisedAsset.GetProperty("content").GetString()!).RootElement.GetProperty("revisionOf").GetString());
        current = await Post($"/api/marketing/runway/{id}/review", new {
            requestId = "fixture-approval-http", version = current.GetProperty("project").GetProperty("version").GetInt32(),
            artifactId = revisedAsset.GetProperty("id").GetString(), digest = revisedAsset.GetProperty("digest").GetString(),
            decision = "approved" });
        var reviewId = current.GetProperty("reviews").EnumerateArray().Last().GetProperty("id").GetString();
        using (var db = new SqliteConnection($"Data Source={Path.Combine(root, "fixture-host", "marketing-chat.sqlite")}"))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT review_id,artifact_digest,decision FROM owner_runway_reviews WHERE request_id='fixture-approval-http'";
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(reviewId, reader.GetString(0));
            Assert.Equal(revisedAsset.GetProperty("digest").GetString(), reader.GetString(1));
            Assert.Equal("approved", reader.GetString(2));
        }
        await Act("align", new { review_id = reviewId, asset_id = revisedAsset.GetProperty("id").GetString(),
            asset_digest = revisedAsset.GetProperty("digest").GetString() }, "align-http");
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
        var prior = Assert.Single(lessons.GetProperty("lessons").EnumerateArray());
        Assert.Equal("pause", prior.GetProperty("decision").GetProperty("decision").GetString());
        Assert.Equal(2, prior.GetProperty("observations").GetArrayLength());
        Assert.Equal("No real audience response", prior.GetProperty("lesson").GetProperty("uncertainty").GetString());
        var excluded = await Post("/api/marketing/runway/fixture/lessons", new {
            audience = "founders", excludeCampaignId = id });
        Assert.Empty(excluded.GetProperty("lessons").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/api/marketing/runway/fixture/lessons", new { audience = "founders", excludeCampaignId = "bad" })).StatusCode);
    }

    [Fact]
    public async Task OwnerObservationRoutePersistsAVerifiedInternalReceiptWithoutLaunching()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var fixtureLedger = Path.Combine(root, "observation-ledger");
        using var isolated = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "observation-host"));
            builder.UseSetting("Marketing:FixtureLedger", fixtureLedger);
            builder.UseSetting("Marketing:FixtureRunwayScript", Path.Combine(directory.FullName, "business", "agent", "hire", "bin", "runway.py"));
        });
        using var client = isolated.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        var context = new DefaultHttpContext();
        var owner = isolated.Services.GetRequiredService<Security>().Issue(context, "Owner fixture", true);
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        client.DefaultRequestHeaders.Add("X-CSRF", owner.Csrf);
        using var seededResponse = await client.PostAsJsonAsync("/api/marketing/runway/fixture/seed", new { requestId = "observation-seed" });
        Assert.Equal(HttpStatusCode.OK, seededResponse.StatusCode);
        using var seededDocument = JsonDocument.Parse(await seededResponse.Content.ReadAsStringAsync());
        var seeded = seededDocument.RootElement;
        var id = seeded.GetProperty("project").GetProperty("id").GetString()!;
        var source = seeded.GetProperty("artifacts")[0];
        var asset = seeded.GetProperty("artifacts")[1];
        var brief = new { audience = "Founders", metric_definition = "Count relevant replies" };
        var experiment = new { metric_source = "Owner notebook", decision_rule = "learning_only", minimum_sample = 0 };
        var briefJson = JsonSerializer.Serialize(brief);
        var experimentJson = JsonSerializer.Serialize(experiment);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using (var db = new SqliteConnection($"Data Source={Path.Combine(fixtureLedger, "hire.sqlite")}"))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO runway_campaigns " +
                "(runway_id,version,stage,mode,owner_actor,source_artifact_id,source_artifact_digest,asset_artifact_id,asset_artifact_digest,brief_json,experiment_json,created_at,updated_at) " +
                "VALUES($id,1,'align','internal',$owner,$source,$sourceDigest,$asset,$assetDigest,$brief,$experiment,$now,$now); " +
                "INSERT INTO runway_campaign_revisions " +
                "(id,runway_id,version,request_id,payload_digest,actor_id,source_artifact_id,source_artifact_digest,brief_json,experiment_json,created_at) " +
                "VALUES($revision,$id,1,'internal-test-brief',$payload,$owner,$source,$sourceDigest,$brief,$experiment,$now)";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$owner", owner.Id);
            command.Parameters.AddWithValue("$source", source.GetProperty("id").GetString()!);
            command.Parameters.AddWithValue("$sourceDigest", source.GetProperty("digest").GetString()!);
            command.Parameters.AddWithValue("$asset", asset.GetProperty("id").GetString()!);
            command.Parameters.AddWithValue("$assetDigest", asset.GetProperty("digest").GetString()!);
            command.Parameters.AddWithValue("$brief", briefJson);
            command.Parameters.AddWithValue("$experiment", experimentJson);
            command.Parameters.AddWithValue("$now", now);
            command.Parameters.AddWithValue("$revision", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$payload", new string('a', 64));
            command.ExecuteNonQuery();
        }
        using (var db = new SqliteConnection($"Data Source={Path.Combine(root, "observation-host", "marketing-chat.sqlite")}"))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO owner_campaign_briefs VALUES($request,$id,1,$source,$digest,$owner,$brief,$experiment,$time)";
            command.Parameters.AddWithValue("$request", "internal-test-brief");
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$source", source.GetProperty("id").GetString()!);
            command.Parameters.AddWithValue("$digest", source.GetProperty("digest").GetString()!);
            command.Parameters.AddWithValue("$owner", owner.Id);
            command.Parameters.AddWithValue("$brief", briefJson);
            command.Parameters.AddWithValue("$experiment", experimentJson);
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
        var observation = new { observation_id = "owner-record-17", source = "Owner notebook",
            source_reference = "Notebook entry 17", interpretation = "Context only; no campaign launch",
            captured_at = now, period_start = now - 7200, period_end = now - 3600,
            timezone = "America/Denver", metric_definition = "Count relevant replies",
            attribution_limitations = "No launch or control group", numerator = 1, denominator = 2,
            value_type = "actual" };
        var body = new { requestId = "owner-observation-http",
            projectVersion = seeded.GetProperty("project").GetProperty("version").GetInt32(), version = 1, observation };
        using var savedResponse = await client.PostAsJsonAsync($"/api/marketing/runway/{id}/campaign-observation", body);
        Assert.Equal(HttpStatusCode.OK, savedResponse.StatusCode);
        using var savedDocument = JsonDocument.Parse(await savedResponse.Content.ReadAsStringAsync());
        var saved = savedDocument.RootElement;
        Assert.Equal("align", saved.GetProperty("campaign").GetProperty("stage").GetString());
        Assert.True(saved.GetProperty("campaign").GetProperty("owner_verified").GetBoolean());
        var action = Assert.Single(saved.GetProperty("campaign_actions").EnumerateArray());
        Assert.True(action.GetProperty("owner_verified").GetBoolean());
        Assert.Contains("\"causality\":\"not_established\"", action.GetProperty("payload_json").GetString());
        Assert.Equal(0, saved.GetProperty("project").GetProperty("token_used").GetInt32());
        using var replay = await client.PostAsJsonAsync($"/api/marketing/runway/{id}/campaign-observation", body);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var duplicate = await client.PostAsJsonAsync($"/api/marketing/runway/{id}/campaign-observation", new {
            requestId = "second-import", projectVersion = body.projectVersion, version = 2, observation });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var reopened = await client.GetAsync($"/api/marketing/runways/{id}");
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        using var reopenedDocument = JsonDocument.Parse(await reopened.Content.ReadAsStringAsync());
        Assert.True(reopenedDocument.RootElement.GetProperty("campaign_actions")[0].GetProperty("owner_verified").GetBoolean());
        using var secondOwnerClient = isolated.CreateClient(new() { BaseAddress = new("http://localhost:5179"), HandleCookies = false });
        secondOwnerClient.DefaultRequestHeaders.Add("Origin", "http://localhost:5179");
        var secondContext = new DefaultHttpContext();
        var secondOwner = isolated.Services.GetRequiredService<Security>().Issue(secondContext, "Second owner device", true);
        Assert.NotEqual(owner.Id, secondOwner.Id);
        secondOwnerClient.DefaultRequestHeaders.Add("Cookie", secondContext.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        secondOwnerClient.DefaultRequestHeaders.Add("X-CSRF", secondOwner.Csrf);
        using (var db = new SqliteConnection($"Data Source={Path.Combine(fixtureLedger, "hire.sqlite")}"))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO runway_campaign_actions VALUES($id,$campaign,0,'direct-unverified-observation',$digest,'manual_observation','owner_reported','forged-cli-actor',$payload,$time)";
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$campaign", id);
            command.Parameters.AddWithValue("$digest", new string('b', 64));
            command.Parameters.AddWithValue("$payload", action.GetProperty("payload_json").GetString()!);
            command.Parameters.AddWithValue("$time", now);
            command.ExecuteNonQuery();
        }
        var decisionBody = new { requestId = "owner-internal-decision-http",
            projectVersion = body.projectVersion, version = 2,
            action = "internal_decision", payload = new { decision = "pause",
                rationale = "This owner note cannot establish campaign impact" } };
        using var decidedResponse = await secondOwnerClient.PostAsJsonAsync($"/api/marketing/runway/{id}/campaign-internal-action", decisionBody);
        Assert.Equal(HttpStatusCode.OK, decidedResponse.StatusCode);
        using var decidedDocument = JsonDocument.Parse(await decidedResponse.Content.ReadAsStringAsync());
        var decided = decidedDocument.RootElement;
        Assert.Equal("learn", decided.GetProperty("campaign").GetProperty("stage").GetString());
        var decision = decided.GetProperty("campaign_actions").EnumerateArray().Last();
        Assert.True(decision.GetProperty("owner_verified").GetBoolean());
        Assert.Equal(secondOwner.Id, decision.GetProperty("actor_id").GetString());
        using var decisionPayload = JsonDocument.Parse(decision.GetProperty("payload_json").GetString()!);
        Assert.Single(decisionPayload.RootElement.GetProperty("observation_action_ids").EnumerateArray());
        Assert.Equal(action.GetProperty("id").GetString(),
            decisionPayload.RootElement.GetProperty("observation_action_ids")[0].GetString());
        Assert.False(decisionPayload.RootElement.GetProperty("execution_granted").GetBoolean());
        using var repeatedDecision = await secondOwnerClient.PostAsJsonAsync($"/api/marketing/runway/{id}/campaign-internal-action", decisionBody);
        Assert.Equal(HttpStatusCode.OK, repeatedDecision.StatusCode);
        using var capabilityResponse = await secondOwnerClient.PostAsJsonAsync($"/api/marketing/runway/{id}/campaign-internal-action", new {
            requestId = "owner-capability-request-http", projectVersion = body.projectVersion, version = 3,
            action = "capability_request", payload = new { blockedTask = "Publish the approved draft",
                requiredScope = "One named channel and account", expectedBenefit = "Learn from a bounded release",
                costStatus = "unknown", costNote = "No account or price verified" } });
        Assert.Equal(HttpStatusCode.OK, capabilityResponse.StatusCode);
        using var capabilityDocument = JsonDocument.Parse(await capabilityResponse.Content.ReadAsStringAsync());
        var capability = capabilityDocument.RootElement;
        Assert.Equal("learn", capability.GetProperty("campaign").GetProperty("stage").GetString());
        var requestAction = capability.GetProperty("campaign_actions").EnumerateArray().Last();
        Assert.True(requestAction.GetProperty("owner_verified").GetBoolean());
        using var requestPayload = JsonDocument.Parse(requestAction.GetProperty("payload_json").GetString()!);
        Assert.False(requestPayload.RootElement.GetProperty("capability_granted").GetBoolean());
        using var lessonResponse = await secondOwnerClient.PostAsJsonAsync($"/api/marketing/runway/{id}/campaign-internal-action", new {
            requestId = "owner-internal-lesson-http", projectVersion = body.projectVersion, version = 4,
            action = "internal_lesson", payload = new { decisionId = decision.GetProperty("id").GetString(),
                lesson = "Ask about controls before claiming outcomes", context = "One owner notebook entry",
                uncertainty = "No launch or control group", revisitCondition = "New authorized evidence",
                nextAction = "Keep the draft internal" } });
        Assert.Equal(HttpStatusCode.OK, lessonResponse.StatusCode);
        using var lessonDocument = JsonDocument.Parse(await lessonResponse.Content.ReadAsStringAsync());
        var learned = lessonDocument.RootElement;
        Assert.Equal("complete", learned.GetProperty("campaign").GetProperty("stage").GetString());
        Assert.True(learned.GetProperty("campaign_actions").EnumerateArray().Last()
            .GetProperty("owner_verified").GetBoolean());
        Assert.Equal(0, learned.GetProperty("project").GetProperty("token_used").GetInt32());
        var realLesson = learned.GetProperty("campaign_actions").EnumerateArray().Last();
        using (var db = new SqliteConnection($"Data Source={Path.Combine(fixtureLedger, "hire.sqlite")}"))
        {
            db.Open();
            using var forged = db.CreateCommand();
            forged.CommandText = "INSERT INTO runway_campaign_actions " +
                "(id,runway_id,version,request_id,payload_digest,action,status,actor_id,payload_json,created_at) " +
                "VALUES($id,$campaign,-1,'unreceipted-lesson','untrusted','internal_lesson','proposed_lesson',$actor,$payload,$time)";
            forged.Parameters.AddWithValue("$id", new string('9', 32));
            forged.Parameters.AddWithValue("$campaign", id);
            forged.Parameters.AddWithValue("$actor", owner.Id);
            forged.Parameters.AddWithValue("$payload", realLesson.GetProperty("payload_json").GetString()!);
            forged.Parameters.AddWithValue("$time", now + 100);
            Assert.Equal(1, forged.ExecuteNonQuery());
        }
        using var lessonsResponse = await client.GetAsync($"/api/marketing/campaign-lessons?audience=founders&excludeCampaignId={new string('0', 32)}");
        Assert.Equal(HttpStatusCode.OK, lessonsResponse.StatusCode);
        using var lessonsDocument = JsonDocument.Parse(await lessonsResponse.Content.ReadAsStringAsync());
        var retrieved = Assert.Single(lessonsDocument.RootElement.GetProperty("lessons").EnumerateArray());
        Assert.Equal(realLesson.GetProperty("id").GetString(), retrieved.GetProperty("action_id").GetString());
        Assert.Equal("Ask about controls before claiming outcomes",
            retrieved.GetProperty("lesson").GetProperty("lesson").GetString());
        Assert.Equal("Notebook entry 17", retrieved.GetProperty("observations")[0]
            .GetProperty("source_reference").GetString());
        using (var db = new SqliteConnection($"Data Source={Path.Combine(fixtureLedger, "hire.sqlite")}"))
        {
            db.Open();
            using var changed = db.CreateCommand();
            changed.CommandText = "UPDATE runway_campaign_revisions SET brief_json=$brief WHERE runway_id=$id AND version=1";
            changed.Parameters.AddWithValue("$id", id);
            changed.Parameters.AddWithValue("$brief", JsonSerializer.Serialize(new {
                audience = "Founders altered without an owner receipt", metric_definition = "Count relevant replies" }));
            Assert.Equal(1, changed.ExecuteNonQuery());
        }
        using var alteredResponse = await client.GetAsync($"/api/marketing/campaign-lessons?audience=founders");
        using var alteredDocument = JsonDocument.Parse(await alteredResponse.Content.ReadAsStringAsync());
        Assert.Empty(alteredDocument.RootElement.GetProperty("lessons").EnumerateArray());
        using (var db = new SqliteConnection($"Data Source={Path.Combine(fixtureLedger, "hire.sqlite")}"))
        {
            db.Open();
            using var restored = db.CreateCommand();
            restored.CommandText = "UPDATE runway_campaign_revisions SET brief_json=$brief WHERE runway_id=$id AND version=1";
            restored.Parameters.AddWithValue("$id", id);
            restored.Parameters.AddWithValue("$brief", briefJson);
            Assert.Equal(1, restored.ExecuteNonQuery());
        }
        using var excludedResponse = await client.GetAsync($"/api/marketing/campaign-lessons?audience=founders&excludeCampaignId={id}");
        using var excludedDocument = JsonDocument.Parse(await excludedResponse.Content.ReadAsStringAsync());
        Assert.Empty(excludedDocument.RootElement.GetProperty("lessons").EnumerateArray());
        using var reopenedAfterLesson = await client.GetAsync($"/api/marketing/runways/{id}");
        Assert.Equal(HttpStatusCode.OK, reopenedAfterLesson.StatusCode);
        using var reopenedAfterLessonDocument = JsonDocument.Parse(await reopenedAfterLesson.Content.ReadAsStringAsync());
        Assert.Equal("complete", reopenedAfterLessonDocument.RootElement.GetProperty("campaign").GetProperty("stage").GetString());
    }

    [Fact]
    public void WorkerMeterPreflightRequiresTheExactReadyRoute()
    {
        using var ready = JsonDocument.Parse("""{"ready":true,"guardInstalled":true,"nativeGuarded":true,"version":"marketing-meter-v6","accountingMode":"post_response","responseReceipts":true,"route":"openai/gpt-5.6-luna","transport":"sse"}""");
        Assert.True(MarketingBackend.RunwayMeterReady(ready.RootElement));
        using var nativeBypass = JsonDocument.Parse("""{"ready":true,"guardInstalled":true,"nativeGuarded":false,"version":"marketing-meter-v6","accountingMode":"post_response","responseReceipts":true,"route":"openai/gpt-5.6-luna","transport":"sse"}""");
        Assert.False(MarketingBackend.RunwayMeterReady(nativeBypass.RootElement));
        using var unloaded = JsonDocument.Parse("""{"ready":false,"guardInstalled":false,"nativeGuarded":false,"version":"marketing-meter-v6","accountingMode":"post_response","responseReceipts":true,"route":"openai/gpt-5.6-luna","transport":"sse"}""");
        Assert.False(MarketingBackend.RunwayMeterReady(unloaded.RootElement));
        using var changed = JsonDocument.Parse("""{"ready":true,"guardInstalled":true,"nativeGuarded":true,"version":"marketing-meter-v6","accountingMode":"post_response","responseReceipts":true,"route":"openai/gpt-6-luna","transport":"sse"}""");
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
    public void ProviderUsageNeedsMatchingSavedResponseReceipt()
    {
        var digest = new string('a', 64);
        JsonElement Receipt(string status, int? tokens, string? providerDigest) => JsonSerializer.SerializeToElement(new {
            request = new { status, reported_tokens = tokens, request_digest = digest },
            response_receipt = providerDigest == null ? null : new { request_digest = providerDigest }
        });
        Assert.Equal(8, MarketingBackend.ConfirmedProviderTokens(Receipt("reported", 8, digest), null));
        foreach (var invalid in new[] { Receipt("unknown", null, null), Receipt("reported", 8, null),
            Receipt("reported", 8, "mismatch"), Receipt("overrun", 25001, digest), Receipt("reported", -1, digest) })
            Assert.Throws<IOException>(() => MarketingBackend.ConfirmedProviderTokens(invalid, null));
        Assert.Throws<IOException>(() => MarketingBackend.ConfirmedProviderTokens(null, "ledger unavailable"));
    }

    [Fact]
    public async Task ExplicitShortPilotModeStillRefusesAssignmentWithoutReadyMeter()
    {
        Assert.False(factory.Services.GetRequiredService<MarketingBackend>().RunwayLiveInferenceEnabled);
        using var pilot = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Thaddeus:Data", Path.Combine(root, "pilot"));
            builder.UseSetting("Marketing:RunwayPilotMode", "v6-post-response-pilot");
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
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/marketing/runway/fixture/campaign-observation", new { owner = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/marketing/runway/fixture/campaign-internal-action", new { owner = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/marketing/campaign-lessons?audience=Founders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/marketing/runway/fixture/campaign-adopt-revision", new { owner = true })).StatusCode);
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
            qualitativeReview = new { audienceFit = "Provisional founder fit from two comments",
                clarity = "One clear draft opening", productTruth = "No outcome proof",
                channelSuitability = "Internal draft only", desiredAction = "Request owner review" },
            nextOwnerDecision = "Choose whether to test", recommendation = "Review before proceeding",
            nextStepProposal = new { hypothesis = "A reviewable draft saves founder time", evidenceGap = "No customer interviews",
                intendedAudience = "Technical founders (assumption)", estimatedWork = "One internal draft and owner review",
                continueOrStop = "continue", reason = "One founder responds to the draft" } });
        Assert.Equal(2, MarketingBackend.ValidateRunwayArtifact("review_packet", packet, claim).SourceUrls.Length);
        Assert.Throws<InvalidOperationException>(() => MarketingBackend.ValidateRunwayArtifact("review_packet",
            packet.Replace("\"continue\"", "\"expand\""), claim));
        Assert.Throws<InvalidOperationException>(() => MarketingBackend.ValidateRunwayArtifact("review_packet",
            packet.Replace("\"desiredAction\":\"Request owner review\"", "\"desiredAction\":\"\""), claim));
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
        Assert.Equal(HttpStatusCode.Forbidden, empty.StatusCode);

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
        Assert.Equal(HttpStatusCode.Forbidden,
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
