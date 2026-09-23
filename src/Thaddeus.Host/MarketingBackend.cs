using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public sealed class MarketingBackend : ICompanyMeetingRuntime
{
    private const string MainSession = "agent:main:marketing-business-main";
    private static readonly Regex TaskIdPattern = new("^[a-f0-9]{32}$", RegexOptions.Compiled);
    private readonly object gate = new();
    private readonly string database;
    private readonly string container;
    private readonly string model;

    public MarketingBackend(Store store, IConfiguration config)
    {
        database = Path.Combine(store.Root, "marketing-chat.sqlite");
        container = config["Marketing:Container"] ?? "marketing-business-hire";
        model = config["Marketing:Model"] ?? "openai/gpt-5.6-luna";
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS chat_requests(
              request_id TEXT PRIMARY KEY, session_key TEXT NOT NULL, task_id TEXT,
              content TEXT NOT NULL, status TEXT NOT NULL, reply TEXT, error TEXT,
              created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS chat_requests_session ON chat_requests(session_key,created_at);
            CREATE TABLE IF NOT EXISTS owner_draft_decisions(
              request_id TEXT PRIMARY KEY, draft_id INTEGER UNIQUE NOT NULL,
              decision TEXT NOT NULL, revision INTEGER NOT NULL, digest TEXT NOT NULL,
              owner_session TEXT NOT NULL, status TEXT NOT NULL,
              result TEXT, created_at TEXT NOT NULL);
            UPDATE chat_requests SET status='unknown',error='Host restarted before the turn was confirmed',
              updated_at=strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE status='pending';
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString());
        db.Open();
        return db;
    }

    private static string TaskSession(string id)
    {
        if (!TaskIdPattern.IsMatch(id)) throw new ArgumentException("Invalid task ID.");
        return "agent:main:marketing-task-" + id;
    }

    private static async Task<(int Exit, string Output, string Error)> Docker(string container, string? input,
        TimeSpan timeout, CancellationToken cancellation, params string[] arguments)
    {
        using var process = new Process();
        var start = new ProcessStartInfo("docker")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add(container);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        process.StartInfo = start;
        if (!process.Start()) throw new IOException("Docker did not start.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(timeout);
        var outputTask = process.StandardOutput.ReadToEndAsync(limit.Token);
        var errorTask = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            if (input != null) await process.StandardInput.WriteAsync(input.AsMemory(), limit.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(limit.Token);
            return (process.ExitCode, await outputTask, await errorTask);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private async Task<(JsonElement? Value, string? Error)> Hire(CancellationToken cancellation, string? input, params string[] arguments)
    {
        try
        {
            var result = await Docker(container, input, TimeSpan.FromSeconds(30), cancellation, ["hire", .. arguments]);
            if (result.Exit != 0) return (null, result.Error.Trim() is { Length: > 0 } error ? error : "The hire command failed.");
            using var doc = JsonDocument.Parse(result.Output);
            return (doc.RootElement.Clone(), null);
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or JsonException or OperationCanceledException)
        {
            return (null, error is OperationCanceledException ? "The hire command timed out or was canceled." : error.Message);
        }
    }

    public async Task<IResult> State(bool owner, CancellationToken cancellation)
    {
        var snapshot = await Hire(cancellation, null, "snapshot");
        var connectionStatus = "connected";
        string? detail = null;
        if (snapshot.Error != null)
        {
            connectionStatus = "disconnected";
            detail = snapshot.Error;
        }
        else
        {
            try
            {
                var health = await Docker(container, null, TimeSpan.FromSeconds(8), cancellation,
                    "openclaw", "gateway", "health", "--json", "--timeout", "2000");
                if (health.Exit != 0 || !JsonDocument.Parse(health.Output).RootElement.GetProperty("ok").GetBoolean())
                { connectionStatus = "disconnected"; detail = "The OpenClaw Gateway is unavailable."; }
                else
                {
                    var route = await Docker(container, null, TimeSpan.FromSeconds(8), cancellation,
                        "openclaw", "models", "status", "--json");
                    if (route.Exit != 0)
                    { connectionStatus = "failed"; detail = "The model route could not be inspected."; }
                    else
                    {
                        using var routeJson = JsonDocument.Parse(route.Output);
                        var root = routeJson.RootElement;
                        var configured = root.TryGetProperty("resolvedDefault", out var selected) && selected.GetString() == model;
                        var auth = root.GetProperty("auth");
                        var usable = auth.GetProperty("runtimeAuthRoutes").EnumerateArray().Any(item =>
                            item.GetProperty("provider").GetString() == "openai" && item.GetProperty("status").GetString() == "usable");
                        var profileProblems = auth.GetProperty("unusableProfiles").GetArrayLength() > 0;
                        if (!configured)
                        { connectionStatus = "failed"; detail = "The configured model differs from the marketing model route."; }
                        else if (!usable || profileProblems)
                        { connectionStatus = "auth_required"; detail = "The OpenClaw model credential needs attention."; }
                    }
                }
            }
            catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or OperationCanceledException or JsonException or KeyNotFoundException)
            { connectionStatus = "failed"; detail = error is OperationCanceledException ? "Connection check timed out." : "The marketing connection check failed."; }
        }
        List<object> messages;
        List<object> requests;
        var pending = false;
        lock (gate)
        {
            using var db = Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT * FROM chat_requests ORDER BY created_at DESC LIMIT 200";
            using var reader = command.ExecuteReader();
            var rows = new List<ChatRow>();
            while (reader.Read()) rows.Add(Read(reader));
            rows.Reverse();
            messages = [];
            requests = [];
            foreach (var row in rows)
            {
                messages.Add(new { id = row.RequestId + ":user", sessionKey = row.SessionKey, taskId = row.TaskId,
                    role = "user", content = row.Content, createdAt = row.CreatedAt });
                if (row.Status == "succeeded" && row.Reply != null)
                    messages.Add(new { id = row.RequestId + ":assistant", sessionKey = row.SessionKey, taskId = row.TaskId,
                        role = "assistant", content = row.Reply, createdAt = row.UpdatedAt });
                requests.Add(new { requestId = row.RequestId, sessionKey = row.SessionKey, status = row.Status,
                    error = row.Error });
                if (row.Status == "pending") pending = true;
            }
        }
        List<object> ownerDecisions = [];
        lock (gate)
        {
            using var db = Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT request_id,draft_id,decision,revision,digest,status,created_at " +
                "FROM owner_draft_decisions ORDER BY created_at DESC LIMIT 100";
            using var reader = command.ExecuteReader();
            while (reader.Read()) ownerDecisions.Add(new
            {
                requestId = reader.GetString(0), draftId = reader.GetInt32(1), decision = reader.GetString(2),
                revision = reader.GetInt32(3), digest = reader.GetString(4), status = reader.GetString(5),
                createdAt = reader.GetString(6)
            });
        }
        if (connectionStatus == "connected" && pending) connectionStatus = "busy";
        var work = snapshot.Value;
        var employeeName = work is { } current ? current.GetProperty("profile").GetProperty("display_name").GetString() : "Marketing agent";
        return Results.Ok(new
        {
            employee = new { name = employeeName, model, sessionKey = MainSession },
            connection = new { status = connectionStatus, detail },
            canConfigure = owner,
            taskStoreAvailable = snapshot.Error == null,
            tasks = work?.GetProperty("tasks") ?? JsonSerializer.SerializeToElement(Array.Empty<object>()),
            profile = work?.GetProperty("profile") ?? JsonSerializer.SerializeToElement(new { }),
            drafts = work?.GetProperty("drafts") ?? JsonSerializer.SerializeToElement(Array.Empty<object>()),
            evidence = work?.GetProperty("evidence") ?? JsonSerializer.SerializeToElement(Array.Empty<object>()),
            activity = work is { } ledger && ledger.TryGetProperty("activity", out var activity)
                ? activity : JsonSerializer.SerializeToElement(Array.Empty<object>()),
            ownerDecisions,
            messages,
            requests
        });
    }

    public IResult History(string? before)
    {
        lock (gate)
        {
            using var db = Open();
            var cursor = before == null ? null : Find(db, before);
            if (before != null && cursor == null) throw new ArgumentException("The reply cursor no longer exists.");
            using var command = db.CreateCommand();
            command.CommandText = "SELECT * FROM chat_requests WHERE status='succeeded' AND reply IS NOT NULL " +
                (cursor == null ? "" : "AND (created_at < $time OR (created_at = $time AND request_id < $id)) ") +
                "ORDER BY created_at DESC,request_id DESC LIMIT 101";
            if (cursor != null)
            {
                command.Parameters.AddWithValue("$time", cursor.CreatedAt);
                command.Parameters.AddWithValue("$id", cursor.RequestId);
            }
            using var reader = command.ExecuteReader();
            var rows = new List<ChatRow>();
            while (reader.Read()) rows.Add(Read(reader));
            var page = rows.Take(100).ToArray();
            return Results.Ok(new { items = page.Select(row => new {
                id = row.RequestId + ":assistant", sessionKey = row.SessionKey, taskId = row.TaskId,
                role = "assistant", content = row.Reply, createdAt = row.UpdatedAt }),
                nextCursor = rows.Count > 100 ? page[^1].RequestId : null });
        }
    }

    public async Task<IResult> CreateTask(JsonElement input, CancellationToken cancellation)
    {
        var body = TaskMutation(input, false);
        var result = await Hire(cancellation, JsonSerializer.Serialize(body), "task", "create", "--input-json", "-");
        return TaskResponse(result);
    }

    public async Task<IResult> UpdateProfile(JsonElement input, CancellationToken cancellation)
    {
        var body = RecordMutation(input, "requestId", "version", "display_name", "product_summary", "audience",
            "voice", "goals", "guardrails", "channels");
        return TaskResponse(await Hire(cancellation, JsonSerializer.Serialize(body), "profile", "update", "--input-json", "-"));
    }

    public async Task<IResult> AddEvidence(string taskId, JsonElement input, CancellationToken cancellation)
    {
        TaskSession(taskId);
        var body = RecordMutation(input, "requestId", "url", "title", "note", "query", "source");
        return TaskResponse(await Hire(cancellation, JsonSerializer.Serialize(body), "evidence", "add", "--task-id", taskId,
            "--input-json", "-"));
    }

    public async Task<IResult> DecideDraft(int id, JsonElement input, DeviceSession owner, CancellationToken cancellation)
    {
        if (id < 1 || input.ValueKind != JsonValueKind.Object) throw new ArgumentException("Invalid draft decision.");
        var requestId = RequiredString(input, "requestId", 120);
        var decision = RequiredString(input, "decision", 16);
        var digest = RequiredString(input, "digest", 64);
        if (decision is not ("approved" or "rejected") || digest.Length != 64 ||
            !input.TryGetProperty("revision", out var revision) || !revision.TryGetInt32(out var version) || version < 1)
            throw new ArgumentException("Decision, revision or digest is invalid.");
        OwnerDecisionRow? recorded;
        lock (gate)
        {
            using var db = Open();
            recorded = FindOwnerDecision(db, requestId, id);
            if (recorded != null && (recorded.RequestId != requestId || recorded.DraftId != id ||
                recorded.Decision != decision || recorded.Revision != version || recorded.Digest != digest))
                return Results.Json(new { error = "This draft or request already has another owner decision." }, statusCode: 409);
            if (recorded?.Status == "confirmed" && recorded.Result != null)
                return Results.Ok(JsonSerializer.Deserialize<JsonElement>(recorded.Result));
        }
        var actor = "Owner session " + (recorded?.OwnerSession ?? owner.Id);
        if (recorded == null)
        {
            var current = await Hire(cancellation, null, "draft", "get", "--id", id.ToString());
            if (current.Error != null) return TaskResponse(current);
            var draft = current.Value!.Value;
            if (draft.GetProperty("status").GetString() != "pending" || draft.GetProperty("revision").GetInt32() != version ||
                draft.GetProperty("digest").GetString() != digest)
                return Results.Json(new { error = "The draft changed. Refresh and review it again." }, statusCode: 409);
            lock (gate)
            {
                using var db = Open();
                if (FindOwnerDecision(db, requestId, id) != null)
                    return Results.Json(new { error = "A decision is already being recorded. Refresh and retry it." }, statusCode: 409);
                using var command = db.CreateCommand();
                command.CommandText = "INSERT INTO owner_draft_decisions " +
                    "(request_id,draft_id,decision,revision,digest,owner_session,status,created_at) " +
                    "VALUES($request,$draft,$decision,$revision,$digest,$owner,'pending_sync',$time)";
                command.Parameters.AddWithValue("$request", requestId);
                command.Parameters.AddWithValue("$draft", id);
                command.Parameters.AddWithValue("$decision", decision);
                command.Parameters.AddWithValue("$revision", version);
                command.Parameters.AddWithValue("$digest", digest);
                command.Parameters.AddWithValue("$owner", owner.Id);
                command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
            }
        }
        var result = await Hire(cancellation, null, "draft", "decide", "--id", id.ToString(),
            "--decision", decision, "--by", actor, "--revision", version.ToString(),
            "--digest", digest, "--request-id", requestId);
        if (result.Error != null)
        {
            if (result.Error.Contains("stale draft", StringComparison.OrdinalIgnoreCase) ||
                result.Error.Contains("already ", StringComparison.OrdinalIgnoreCase) ||
                result.Error.Contains("no draft #", StringComparison.OrdinalIgnoreCase))
            {
                lock (gate)
                {
                    using var db = Open();
                    using var command = db.CreateCommand();
                    command.CommandText = "DELETE FROM owner_draft_decisions WHERE request_id=$request AND status='pending_sync'";
                    command.Parameters.AddWithValue("$request", requestId);
                    command.ExecuteNonQuery();
                }
                return TaskResponse(result);
            }
            return Results.Json(new { error = "The decision outcome is unknown. Refresh and retry the same request ID." }, statusCode: 503);
        }
        lock (gate)
        {
            using var db = Open();
            using var command = db.CreateCommand();
            command.CommandText = "UPDATE owner_draft_decisions SET status='confirmed',result=$result WHERE request_id=$request";
            command.Parameters.AddWithValue("$result", result.Value!.Value.GetRawText());
            command.Parameters.AddWithValue("$request", requestId);
            command.ExecuteNonQuery();
        }
        return Results.Ok(result.Value);
    }

    private static OwnerDecisionRow? FindOwnerDecision(SqliteConnection db, string requestId, int draftId)
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT request_id,draft_id,decision,revision,digest,owner_session,status,result " +
            "FROM owner_draft_decisions WHERE request_id=$request OR draft_id=$draft LIMIT 1";
        command.Parameters.AddWithValue("$request", requestId);
        command.Parameters.AddWithValue("$draft", draftId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new OwnerDecisionRow(reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
            reader.GetInt32(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7)) : null;
    }

    private sealed record OwnerDecisionRow(string RequestId, int DraftId, string Decision, int Revision,
        string Digest, string OwnerSession, string Status, string? Result);

    private static Dictionary<string, JsonElement> RecordMutation(JsonElement input, params string[] allowed)
    {
        if (input.ValueKind != JsonValueKind.Object) throw new ArgumentException("Record body must be an object.");
        var permitted = allowed.ToHashSet(StringComparer.Ordinal);
        var body = new Dictionary<string, JsonElement>();
        foreach (var item in input.EnumerateObject())
        {
            if (!permitted.Contains(item.Name)) throw new ArgumentException("Unknown record field: " + item.Name);
            body[item.Name == "requestId" ? "request_id" : item.Name] = item.Value.Clone();
        }
        if (!body.TryGetValue("request_id", out var requestId) || requestId.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(requestId.GetString())) throw new ArgumentException("requestId is required.");
        return body;
    }

    public async Task<IResult> UpdateTask(string id, JsonElement input, CancellationToken cancellation)
    {
        TaskSession(id);
        var body = TaskMutation(input, true);
        var result = await Hire(cancellation, JsonSerializer.Serialize(body), "task", "update", "--id", id, "--input-json", "-");
        return TaskResponse(result);
    }

    private static Dictionary<string, JsonElement> TaskMutation(JsonElement input, bool update)
    {
        if (input.ValueKind != JsonValueKind.Object) throw new ArgumentException("Task body must be an object.");
        var body = new Dictionary<string, JsonElement>();
        foreach (var property in input.EnumerateObject())
        {
            var key = property.Name == "requestId" ? "request_id" : property.Name;
            if (key is not ("request_id" or "title" or "status" or "priority" or "next_action" or "action_state" or "blocker" or "version"))
                throw new ArgumentException("Unknown task field: " + property.Name);
            if (!update && key == "version") throw new ArgumentException("Version is only valid on update.");
            body[key] = property.Value.Clone();
        }
        if (!body.TryGetValue("request_id", out var requestId) || requestId.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(requestId.GetString())) throw new ArgumentException("requestId is required.");
        if (update && (!body.TryGetValue("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out _)))
            throw new ArgumentException("A numeric version is required.");
        return body;
    }

    private static IResult TaskResponse((JsonElement? Value, string? Error) result)
    {
        if (result.Error == null) return Results.Ok(result.Value);
        if (result.Error.Contains("stale task version", StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { error = "Task changed. Refresh and try again." }, statusCode: 409);
        if (result.Error.Contains("stale profile version", StringComparison.OrdinalIgnoreCase) ||
            result.Error.Contains("stale draft revision or content", StringComparison.OrdinalIgnoreCase) ||
            result.Error.Contains("already approved", StringComparison.OrdinalIgnoreCase) ||
            result.Error.Contains("already rejected", StringComparison.OrdinalIgnoreCase) ||
            result.Error.Contains("already withdrawn", StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { error = "The record changed. Refresh and review it again." }, statusCode: 409);
        if (result.Error.Contains("request_id already used", StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { error = "requestId already belongs to another operation." }, statusCode: 409);
        if (result.Error.Contains("task not found", StringComparison.OrdinalIgnoreCase))
            return Results.NotFound(new { error = "Task not found." });
        if (result.Error.Contains("no draft #", StringComparison.OrdinalIgnoreCase))
            return Results.NotFound(new { error = "Draft not found." });
        if (result.Error.StartsWith("--", StringComparison.Ordinal) ||
            result.Error.StartsWith("invalid ", StringComparison.Ordinal) ||
            result.Error.StartsWith("unknown task fields", StringComparison.Ordinal) ||
            result.Error.StartsWith("positive integer", StringComparison.Ordinal) ||
            result.Error.StartsWith("blocked tasks", StringComparison.Ordinal) ||
            result.Error.StartsWith("task update has", StringComparison.Ordinal) ||
            result.Error.StartsWith("profile update requires", StringComparison.Ordinal) ||
            result.Error.StartsWith("evidence URL", StringComparison.Ordinal))
            return Results.BadRequest(new { error = result.Error });
        return Results.Json(new { error = result.Error }, statusCode: 503);
    }

    public async Task<IResult> Chat(JsonElement input, CancellationToken cancellation)
    {
        if (input.ValueKind != JsonValueKind.Object) throw new ArgumentException("Chat body must be an object.");
        var requestId = RequiredString(input, "requestId", 120);
        var content = RequiredString(input, "content", 20000);
        string? taskId = null;
        if (input.TryGetProperty("taskId", out var taskValue) && taskValue.ValueKind != JsonValueKind.Null)
        {
            if (taskValue.ValueKind != JsonValueKind.String) throw new ArgumentException("taskId must be a string.");
            taskId = taskValue.GetString();
            if (taskId == null) throw new ArgumentException("taskId is required.");
            TaskSession(taskId);
        }
        var session = taskId == null ? MainSession : TaskSession(taskId);
        lock (gate)
        {
            using var db = Open();
            if (Find(db, requestId) is { } prior) return ExistingChat(prior, requestId, session, content);
        }
        var profile = await Hire(cancellation, null, "profile", "get");
        if (profile.Error != null) return Results.Json(new { error = "The marketing brief is unavailable." }, statusCode: 503);
        var brief = profile.Value!.Value;
        string message = "Current owner-configured marketing brief (version " + brief.GetProperty("version").GetInt32() + "): " +
            string.Join("; ", new[] { "product=" + brief.GetProperty("product_summary").GetString(),
                "audience=" + brief.GetProperty("audience").GetString(),
                "voice=" + brief.GetProperty("voice").GetString(),
                "goals=" + brief.GetProperty("goals").GetString(),
                "guardrails=" + brief.GetProperty("guardrails").GetString(),
                "channels=" + brief.GetProperty("channels").GetString() }) + "\n\n" + content;
        if (taskId != null)
        {
            var task = await Hire(cancellation, null, "task", "get", "--id", taskId);
            if (task.Error != null) return TaskResponse(task);
            var item = task.Value!.Value;
            message = $"Current task context (version {item.GetProperty("version").GetInt32()}): " +
                $"{item.GetProperty("title").GetString()}; status {item.GetProperty("status").GetString()}; " +
                $"priority {item.GetProperty("priority").GetString()}; next action {item.GetProperty("next_action").GetString()}; " +
                $"action state {item.GetProperty("action_state").GetString()}; blocker {item.GetProperty("blocker")}.\n\n" + message;
        }
        ChatRow? existing;
        lock (gate)
        {
            using var db = Open();
            existing = Find(db, requestId);
            if (existing == null)
            {
                using var busy = db.CreateCommand();
                busy.CommandText = "SELECT COUNT(*) FROM chat_requests WHERE session_key=$session AND status='pending'";
                busy.Parameters.AddWithValue("$session", session);
                if ((long)busy.ExecuteScalar()! > 0) return Results.Json(new { error = "This chat is busy." }, statusCode: 409);
                using var insert = db.CreateCommand();
                insert.CommandText = "INSERT INTO chat_requests(request_id,session_key,task_id,content,status,created_at,updated_at) " +
                    "VALUES($id,$session,$task,$content,'pending',$time,$time)";
                insert.Parameters.AddWithValue("$id", requestId);
                insert.Parameters.AddWithValue("$session", session);
                insert.Parameters.AddWithValue("$task", (object?)taskId ?? DBNull.Value);
                insert.Parameters.AddWithValue("$content", content);
                insert.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
                insert.ExecuteNonQuery();
            }
        }
        if (existing != null)
            return ExistingChat(existing, requestId, session, content);
        try
        {
            var result = await Docker(container, message, TimeSpan.FromMinutes(11), cancellation,
                "openclaw", "agent", "--session-key", session, "--message-file", "/dev/stdin",
                "--model", model, "--json", "--timeout", "600");
            var reply = result.Exit == 0 ? ConfirmedReply(result.Output) : null;
            if (reply != null)
            {
                Finish(requestId, "succeeded", reply, null);
                return Results.Ok(new { requestId, status = "succeeded", reply, sessionKey = session });
            }
            var error = result.Exit == 0 ? "OpenClaw returned no confirmed reply." :
                (string.IsNullOrWhiteSpace(result.Error) ? "OpenClaw did not complete the turn." : result.Error.Trim());
            Finish(requestId, "unknown", null, error);
            return Results.Json(new { requestId, status = "unknown", error, sessionKey = session }, statusCode: 502);
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or OperationCanceledException or JsonException)
        {
            var status = error is System.ComponentModel.Win32Exception ? "failed" : "unknown";
            Finish(requestId, status, null, error.Message);
            return Results.Json(new { requestId, status, error = error.Message, sessionKey = session }, statusCode: 503);
        }
    }

    public async Task<string> MeetingReply(string role, string meetingId, string prompt, CancellationToken cancellation)
    {
        if (role is not ("ceo" or "marketing")) throw new ArgumentException("Unknown meeting role.");
        var agent = "meeting-" + role;
        var result = await Docker(container, prompt, TimeSpan.FromMinutes(11), cancellation,
            "openclaw", "agent", "--agent", agent, "--session-key", $"agent:{agent}:meeting-{meetingId}",
            "--message-file", "/dev/stdin", "--model", model, "--json", "--timeout", "600");
        return (result.Exit == 0 ? ConfirmedReply(result.Output) : null)
            ?? throw new IOException("The meeting role did not return a confirmed reply. Review the meeting before trying again.");
    }

    public async Task<bool> MeetingTaskReady(string id, CancellationToken cancellation)
    {
        TaskSession(id);
        var result = await Hire(cancellation, null, "task", "get", "--id", id);
        if (result.Error != null) throw new IOException(result.Error);
        var task = result.Value!.Value;
        return task.GetProperty("status").GetString() == "ready" && task.GetProperty("action_state").GetString() == "agent_ready";
    }

    public async Task<string> ReleaseMeetingTask(string requestId, string title, string action, CancellationToken cancellation)
    {
        var result = await Hire(cancellation, JsonSerializer.Serialize(new { request_id = requestId, title,
            status = "ready", priority = "normal", next_action = action, action_state = "agent_ready" }), "task", "create", "--input-json", "-");
        if (result.Error != null) throw new IOException(result.Error);
        return result.Value!.Value.GetProperty("id").GetString()!;
    }

    private static string? ConfirmedReply(string output)
    {
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("status", out var status) || status.GetString() != "ok")
            return null;
        if (!root.TryGetProperty("result", out var result)) return null;
        if (result.TryGetProperty("payloads", out var payloads) && payloads.ValueKind == JsonValueKind.Array)
        {
            var texts = payloads.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Object &&
                p.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                .Select(p => p.GetProperty("text").GetString()).Where(s => !string.IsNullOrWhiteSpace(s));
            if (string.Join("\n", texts) is { Length: > 0 } reply) return reply;
        }
        if (result.TryGetProperty("meta", out var meta) &&
            meta.TryGetProperty("finalAssistantVisibleText", out var visible) && visible.ValueKind == JsonValueKind.String)
            return visible.GetString();
        return null;
    }

    private static IResult ExistingChat(ChatRow row, string requestId, string session, string content)
    {
        if (row.SessionKey != session || row.Content != content)
            return Results.Json(new { error = "requestId already belongs to another chat request." }, statusCode: 409);
        return row.Status == "succeeded"
            ? Results.Ok(new { requestId, status = "succeeded", reply = row.Reply, sessionKey = session })
            : Results.Json(new { requestId, status = row.Status, error = row.Error, sessionKey = session },
                statusCode: row.Status == "pending" ? 202 : 409);
    }

    private static string RequiredString(JsonElement input, string name, int max)
    {
        if (!input.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > max)
            throw new ArgumentException($"{name} must be a nonempty string of at most {max} characters.");
        return value.GetString()!;
    }

    private void Finish(string id, string status, string? reply, string? error)
    {
        lock (gate)
        {
            using var db = Open();
            using var command = db.CreateCommand();
            command.CommandText = "UPDATE chat_requests SET status=$status,reply=$reply,error=$error,updated_at=$time " +
                "WHERE request_id=$id AND status='pending'";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$reply", (object?)reply ?? DBNull.Value);
            command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    private static ChatRow? Find(SqliteConnection db, string id)
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT * FROM chat_requests WHERE request_id=$id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static ChatRow Read(SqliteDataReader reader) => new(
        reader.GetString(reader.GetOrdinal("request_id")),
        reader.GetString(reader.GetOrdinal("session_key")),
        reader.IsDBNull(reader.GetOrdinal("task_id")) ? null : reader.GetString(reader.GetOrdinal("task_id")),
        reader.GetString(reader.GetOrdinal("content")),
        reader.GetString(reader.GetOrdinal("status")),
        reader.IsDBNull(reader.GetOrdinal("reply")) ? null : reader.GetString(reader.GetOrdinal("reply")),
        reader.IsDBNull(reader.GetOrdinal("error")) ? null : reader.GetString(reader.GetOrdinal("error")),
        reader.GetString(reader.GetOrdinal("created_at")),
        reader.GetString(reader.GetOrdinal("updated_at")));

    private sealed record ChatRow(string RequestId, string SessionKey, string? TaskId, string Content,
        string Status, string? Reply, string? Error, string CreatedAt, string UpdatedAt);
}
