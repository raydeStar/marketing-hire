using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public sealed class MarketingBackend
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

    public async Task<IResult> State(CancellationToken cancellation)
    {
        var tasks = await Hire(cancellation, null, "task", "list", "--limit", "1000");
        var connectionStatus = "connected";
        string? detail = null;
        if (tasks.Error != null)
        {
            connectionStatus = "disconnected";
            detail = tasks.Error;
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
        if (connectionStatus == "connected" && pending) connectionStatus = "busy";
        return Results.Ok(new
        {
            employee = new { name = "Marketing Hire", model, sessionKey = MainSession },
            connection = new { status = connectionStatus, detail },
            taskStoreAvailable = tasks.Error == null,
            tasks = tasks.Value ?? JsonSerializer.SerializeToElement(Array.Empty<object>()),
            messages,
            requests
        });
    }

    public async Task<IResult> CreateTask(JsonElement input, CancellationToken cancellation)
    {
        var body = TaskMutation(input, false);
        var result = await Hire(cancellation, JsonSerializer.Serialize(body), "task", "create", "--input-json", "-");
        return TaskResponse(result);
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
        if (result.Error.Contains("request_id already used", StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { error = "requestId already belongs to another task operation." }, statusCode: 409);
        if (result.Error.Contains("task not found", StringComparison.OrdinalIgnoreCase))
            return Results.NotFound(new { error = "Task not found." });
        if (result.Error.StartsWith("--", StringComparison.Ordinal) ||
            result.Error.StartsWith("invalid ", StringComparison.Ordinal) ||
            result.Error.StartsWith("unknown task fields", StringComparison.Ordinal) ||
            result.Error.StartsWith("positive integer", StringComparison.Ordinal) ||
            result.Error.StartsWith("blocked tasks", StringComparison.Ordinal) ||
            result.Error.StartsWith("task update has", StringComparison.Ordinal))
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
        string message = content;
        if (taskId != null)
        {
            var task = await Hire(cancellation, null, "task", "get", "--id", taskId);
            if (task.Error != null) return TaskResponse(task);
            var item = task.Value!.Value;
            message = $"Current task context (version {item.GetProperty("version").GetInt32()}): " +
                $"{item.GetProperty("title").GetString()}; status {item.GetProperty("status").GetString()}; " +
                $"priority {item.GetProperty("priority").GetString()}; next action {item.GetProperty("next_action").GetString()}; " +
                $"action state {item.GetProperty("action_state").GetString()}; blocker {item.GetProperty("blocker")}.\n\n" + content;
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
