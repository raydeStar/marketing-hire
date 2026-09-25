using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Thaddeus.Host;

public sealed partial class MarketingBackend
{
    private const int WorkerResponsePreviewLimit = 12000;

    internal string RecordWorkerResponse(JsonElement claim, JsonElement response)
    {
        var (reply, _, _) = ReadRunwayReply(response);
        if (reply == null) throw new InvalidOperationException("OpenClaw returned no confirmed deliverable.");
        var execution = claim.GetProperty("execution_id").GetString()!;
        var project = claim.GetProperty("project").GetProperty("id").GetString()!;
        var step = claim.GetProperty("step").GetProperty("id").GetString()!;
        var kind = claim.GetProperty("step").GetProperty("kind").GetString()!;
        if (!TaskIdPattern.IsMatch(execution) || !TaskIdPattern.IsMatch(project) || !TaskIdPattern.IsMatch(step) ||
            kind is not ("audience_note" or "post_angles" or "review_packet" or "revision_angles"))
            throw new InvalidOperationException("Worker response has an invalid execution binding.");
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reply)));
        // Keep the returned text before examining its bill. A missing timesheet
        // must not send the employee's work into the fireplace.
        lock (gate)
        {
            using var db = Open(); using var command = db.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO marketing_worker_responses " +
                "VALUES($execution,$project,$step,$kind,$model,$content,$digest,$length,$time)";
            command.Parameters.AddWithValue("$execution", execution); command.Parameters.AddWithValue("$project", project);
            command.Parameters.AddWithValue("$step", step); command.Parameters.AddWithValue("$kind", kind);
            command.Parameters.AddWithValue("$model", model);
            command.Parameters.AddWithValue("$content", reply[..Math.Min(reply.Length, WorkerResponsePreviewLimit)]);
            command.Parameters.AddWithValue("$digest", digest); command.Parameters.AddWithValue("$length", reply.Length);
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
            if (command.ExecuteNonQuery() == 0)
            {
                command.CommandText = "SELECT project_id,step_id,kind,digest FROM marketing_worker_responses WHERE execution_id=$execution";
                using var prior = command.ExecuteReader();
                if (!prior.Read() || prior.GetString(0) != project || prior.GetString(1) != step ||
                    prior.GetString(2) != kind || prior.GetString(3) != digest)
                    throw new InvalidOperationException("An existing worker response cannot be replaced.");
            }
        }
        return reply;
    }

    private object[] WorkerResponses(string projectId)
    {
        using var db = Open(); using var command = db.CreateCommand();
        command.CommandText = "SELECT execution_id,step_id,kind,configured_model,content,digest,original_characters,received_at " +
            "FROM marketing_worker_responses WHERE project_id=$project ORDER BY received_at LIMIT 100";
        command.Parameters.AddWithValue("$project", projectId);
        using var reader = command.ExecuteReader();
        var results = new List<object>();
        while (reader.Read()) results.Add(new {
            execution_id = reader.GetString(0), step_id = reader.GetString(1), kind = reader.GetString(2),
            configured_model = reader.GetString(3), content = reader.GetString(4), digest = reader.GetString(5),
            original_characters = reader.GetInt32(6), truncated = reader.GetInt32(6) > WorkerResponsePreviewLimit,
            received_at = reader.GetDouble(7)
        });
        return results.ToArray();
    }
}
