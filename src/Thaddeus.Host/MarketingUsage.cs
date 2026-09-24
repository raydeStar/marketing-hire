using System.Text.Json;

namespace Thaddeus.Host;

public sealed partial class MarketingBackend
{
    internal sealed record ChatTokenCounts(long? InputTokens, long? OutputTokens, long TotalTokens);

    // Chat can contain several provider calls. Preserve its reported aggregate;
    // counting it as one physical model request would make a rather creative ledger.
    internal static ChatTokenCounts? ReadChatTokenCounts(string output)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("result", out var result) ||
                result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("meta", out var meta) ||
                meta.ValueKind != JsonValueKind.Object || !meta.TryGetProperty("agentMeta", out var agent) ||
                agent.ValueKind != JsonValueKind.Object || !agent.TryGetProperty("usage", out var usage) ||
                usage.ValueKind != JsonValueKind.Object) return null;
            long? Count(params string[] names)
            {
                foreach (var name in names)
                    if (usage.TryGetProperty(name, out var value))
                        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) &&
                            n is >= 0 and <= 9_007_199_254_740_991 ? n : null;
                return null;
            }
            var input = Count("inputTokens", "input");
            var outputTokens = Count("outputTokens", "output");
            // Some transports include cache counts in total. Do not invent a
            // total from input/output when the provider did not report one.
            var total = Count("totalTokens", "total");
            if (total == null || (input ?? 0) + (outputTokens ?? 0) > total) return null;
            return new(input, outputTokens, total.Value);
        }
        catch (JsonException) { return null; }
    }

    internal void RecordChatDispatch(string requestId)
    {
        lock (gate)
        {
            using var db = Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO marketing_chat_usage(request_id,created_at,source) " +
                "VALUES($id,$time,'turn_aggregate')";
            command.Parameters.AddWithValue("$id", requestId);
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
            command.ExecuteNonQuery();
        }
    }

    internal void RecordChatUsage(string requestId, string output)
    {
        var counts = ReadChatTokenCounts(output);
        if (counts == null) return; // Missing evidence stays unknown, not zero.
        lock (gate)
        {
            using var db = Open();
            using var command = db.CreateCommand();
            command.CommandText = "UPDATE marketing_chat_usage SET input_tokens=$input,output_tokens=$output," +
                "total_tokens=$total WHERE request_id=$id AND total_tokens IS NULL";
            command.Parameters.AddWithValue("$id", requestId);
            command.Parameters.AddWithValue("$input", (object?)counts.InputTokens ?? DBNull.Value);
            command.Parameters.AddWithValue("$output", (object?)counts.OutputTokens ?? DBNull.Value);
            command.Parameters.AddWithValue("$total", counts.TotalTokens);
            command.ExecuteNonQuery();
        }
    }

    internal object[] ChatUsageHistory()
    {
        lock (gate)
        {
            using var db = Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT request_id,created_at,source,input_tokens,output_tokens,total_tokens " +
                "FROM marketing_chat_usage ORDER BY created_at";
            using var reader = command.ExecuteReader();
            var events = new List<object>();
            while (reader.Read()) events.Add(new {
                id = "chat:" + reader.GetString(0), kind = "chat", createdAt = reader.GetDouble(1),
                source = reader.GetString(2), inputTokens = reader.IsDBNull(3) ? (long?)null : reader.GetInt64(3),
                outputTokens = reader.IsDBNull(4) ? (long?)null : reader.GetInt64(4),
                totalTokens = reader.IsDBNull(5) ? (long?)null : reader.GetInt64(5),
                status = reader.IsDBNull(5) ? "unknown" : "reported" });
            return events.ToArray();
        }
    }

    public async Task<IResult> UsageHistory(CancellationToken cancellation)
    {
        var worker = await Runway("usage-history", null, cancellation);
        return Results.Ok(new {
            chat = ChatUsageHistory(), autonomous = worker.Value,
            autonomousAvailable = worker.Error == null,
            fixture = FixtureCampaignEnabled,
            updatedAt = DateTimeOffset.UtcNow,
        });
    }
}
