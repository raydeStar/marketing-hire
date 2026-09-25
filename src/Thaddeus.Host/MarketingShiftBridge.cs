using System.Text.Json;

namespace Thaddeus.Host;

public sealed partial class MarketingBackend
{
    /// <summary>The employee's work ledger, for shifts: the same `hire` records the cockpit shows.</summary>
    internal async Task<(JsonElement? Value, string? Error)> ShiftHire(string? input, params string[] arguments)
    {
        var result = await Hire(CancellationToken.None, input, arguments);
        if (result.Error == null && arguments is ["task", "create" or "update", ..] or ["draft", "add", ..] or ["event", ..]) InvalidateState();
        return result;
    }

    /// <summary>Shifts, direct chat and the campaign runner never run model turns at the same time.
    /// Returns false when another turn owns execution, so the stage waits for the next cycle.</summary>
    internal async Task<bool> TryEnterExecution(CancellationToken cancellation) => await executionGate.WaitAsync(0, cancellation);
    internal void LeaveExecution() => executionGate.Release();

    /// <summary>The employee's metered receipt ledger always lives in its container, even when a local
    /// stand-in ledger holds the work records: real model spend is recorded where the meter reads it.</summary>
    private async Task<JsonElement> MeterLedger(string command, object input, CancellationToken cancellation)
    {
        var result = await Docker(shiftContainer, JsonSerializer.Serialize(input), TimeSpan.FromSeconds(35), cancellation, "python3", "/opt/hire/bin/runway.py", command);
        if (result.Exit != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "The metered ledger refused the request." : result.Error.Trim());
        using var document = JsonDocument.Parse(result.Output);
        return document.RootElement.Clone();
    }

    /// <summary>One live shift turn: a metered claim, one tool-less worker run, confirmed provider usage, settlement.
    /// The caller already holds the execution gate. Uncertain outcomes stop the shift instead of retrying.</summary>
    internal async Task<ShiftTurnResult> LiveShiftTurn(ShiftTurnRequest request, CancellationToken cancellation)
    {
        if (!await ShiftTransportReady(cancellation))
            throw new ShiftTurnNotSentException("The metered worker route isn't ready, so no live turn was sent. Check that the employee container is running.");
        JsonElement grant, claim;
        try
        {
            grant = await MeterLedger("shift-open", new { request_id = "shift-grant-" + request.ShiftId, owner_actor = request.Owner, turn_limit = request.TurnBudget,
            token_limit = Math.Clamp(request.TokenBudget ?? request.TurnBudget * 25000L, 25000, 20_000_000), deadline_at = request.EndsAt.ToUnixTimeMilliseconds() / 1000.0,
            actor_owner = true, accept_post_response_accounting = true }, cancellation);
            claim = await MeterLedger("shift-claim", new { runway_id = grant.GetProperty("id").GetString() }, cancellation);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or JsonException)
        { throw new ShiftTurnNotSentException("The meter did not grant a turn: " + error.Message); }
        var executionId = claim.GetProperty("execution_id").GetString()!;
        var sent = false;
        try
        {
            var turn = await Docker(shiftContainer, null, TimeSpan.FromSeconds(135), cancellation,
                "openclaw", "gateway", "call", "agent", "--params", JsonSerializer.Serialize(new
                {
                    agentId = "runway-worker", sessionId = "model-run-" + executionId, sessionKey = "agent:runway-worker:model-run-" + executionId,
                    message = request.Prompt, thinking = "low", modelRun = true, promptMode = "none", cleanupBundleMcpOnRunEnd = true, idempotencyKey = executionId
                }), "--expect-final", "--json", "--timeout", "120000");
            sent = true;
            if (turn.Exit != 0)
            {
                // A request the gateway refused before inference spent nothing and can settle as failed.
                var refused = false;
                try { using var rejected = JsonDocument.Parse(turn.Output); refused = rejected.RootElement.TryGetProperty("error", out var failure) && failure.TryGetProperty("code", out var code) && code.GetString() == "INVALID_REQUEST"; }
                catch (JsonException) { }
                var detail = (turn.Error + " " + turn.Output).Trim();
                await MeterLedger("shift-settle", new { execution_id = executionId, status = refused ? "failed" : "unknown", error = detail[..Math.Min(400, detail.Length)] }, CancellationToken.None);
                if (refused) throw new ShiftTurnNotSentException("The gateway refused the turn before inference.");
                throw new InvalidOperationException("The live turn did not settle; the shift stops until it is reconciled.");
            }
            using var response = JsonDocument.Parse(turn.Output);
            var (reply, _, _) = ReadRunwayReply(response.RootElement);
            var metered = await MeterLedger("model-inspect", new { request_id = executionId }, cancellation);
            var tokens = ConfirmedProviderTokens(metered, null);
            await MeterLedger("shift-settle", new { execution_id = executionId, status = reply == null ? "failed" : "succeeded", error = reply == null ? "Empty reply" : null }, CancellationToken.None);
            if (reply == null) throw new InvalidOperationException("The employee returned an empty reply.");
            return new ShiftTurnResult(reply, tokens);
        }
        catch (Exception error) when (error is IOException or JsonException or OperationCanceledException)
        {
            // Transport or receipt uncertainty: bill the reservation and hold further turns.
            try { await MeterLedger("shift-settle", new { execution_id = executionId, status = sent ? "unknown" : "failed", error = error.Message }, CancellationToken.None); } catch { }
            throw new InvalidOperationException("The live turn's outcome is uncertain (" + error.Message + "); the shift stops until it is reconciled.");
        }
    }

    /// <summary>The meter's own readiness report, allowing for a cold container's first command.</summary>
    private async Task<bool> ShiftTransportReady(CancellationToken cancellation)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var meter = await Docker(shiftContainer, null, TimeSpan.FromSeconds(40), cancellation, "openclaw", "gateway", "call", "marketing.meter.status", "--json", "--timeout", "30000");
                if (meter.Exit == 0) { using var status = JsonDocument.Parse(meter.Output); if (RunwayMeterReady(status.RootElement)) return true; }
            }
            catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or JsonException or OperationCanceledException) { }
        }
        return false;
    }

    /// <summary>The working context chat shares with shifts (goals, the last shift, the notebook, the owner's verdicts).
    /// Set once the app is built, because those services depend on this one.</summary>
    public Func<CancellationToken, Task<string>>? WorkContext { get; set; }

    /// <summary>The employee's own keyless research tool: Hacker News, Reddit and Google News mentions for a topic.
    /// Returns null when the container is unreachable, so the caller can fall back to the host's own search.</summary>
    internal async Task<ResearchSource[]?> PulseResearch(string topic, CancellationToken cancellation, string? sources = null)
    {
        try
        {
            var scan = await Docker(shiftContainer, null, TimeSpan.FromSeconds(70), cancellation, ["pulse", "scan", "--query", topic, "--limit", "15", .. sources != null ? new[] { "--sources", sources } : []]);
            if (scan.Exit != 0) return null;
            var listed = await Docker(shiftContainer, null, TimeSpan.FromSeconds(20), cancellation, "pulse", "items", "--query", topic, "--limit", "10");
            if (listed.Exit != 0) return null;
            using var document = JsonDocument.Parse(listed.Output);
            var cutoff = DateTimeOffset.UtcNow.AddDays(-180);
            return [.. document.RootElement.GetProperty("items").EnumerateArray().Select(item =>
            {
                var created = DateTimeOffset.TryParse(item.TryGetProperty("created_at", out var at) ? at.GetString() : null, out var when) ? when : DateTimeOffset.MinValue;
                var url = item.TryGetProperty("url", out var link) ? link.GetString() ?? "" : "";
                var title = item.TryGetProperty("title", out var name) ? name.GetString() ?? "" : "";
                var snippet = item.TryGetProperty("snippet", out var text) ? text.GetString() ?? "" : "";
                var source = item.TryGetProperty("source", out var from) ? from.GetString() ?? "" : "";
                // Social posts (Bluesky, Mastodon) have no title: their opening words stand in for one.
                if (title.Trim().Length == 0) title = snippet.Length > 120 ? snippet[..120].TrimEnd() + "…" : snippet;
                var via = source switch { "reddit" => "Reddit", "news" => "Google News", "hackernews" or "hn" => "Hacker News", "bluesky" => "Bluesky", "mastodon" => "Mastodon", _ => source.Length > 0 ? source : "Public web" };
                return new ResearchSource(url, title.Length > 200 ? title[..200] : title, snippet.Length > 500 ? snippet[..500] : snippet, null, created, via);
            }).Where(item => item.Url.StartsWith("https://", StringComparison.Ordinal) && item.PublishedAt >= cutoff && item.Excerpt.Length > 20)];
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    internal async Task CloseShiftGrant(string shiftId, CancellationToken cancellation)
    {
        try { await MeterLedger("shift-close", new { request_id = "shift-grant-" + shiftId }, cancellation); }
        catch (Exception error) when (error is InvalidOperationException or IOException or JsonException) { /* The grant stays open until its deadline; the meter still bounds it. */ }
    }
}
