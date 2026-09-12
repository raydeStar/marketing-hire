using System.Security.Cryptography;
using System.Text;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public record WorkerGrant(string RunId, string TokenHash, DateTimeOffset Expires, bool Revoked = false);
public sealed class WorkerAuthorization(Store store)
{
    public string Issue(string runId, TimeSpan lifetime)
    {
        if (store.Get(runId)?.Execution == null) throw new ArgumentException("Worker grants require an execution task.");
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromHours(1)) throw new ArgumentException("Worker grants expire within one hour.");
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        store.Setting("worker-grant:" + runId, Wire.Pack(new WorkerGrant(runId, Wire.Hash(token), DateTimeOffset.UtcNow.Add(lifetime))));
        return token;
    }
    public bool Authenticate(string runId, string? token)
    {
        if (token == null || token.Length != 64 || store.Get(runId) is not { Execution: not null } run ||
            run.State is not (RunState.Running or RunState.AwaitingInput or RunState.AwaitingApproval)) return false;
        var saved = store.Setting("worker-grant:" + runId);
        if (saved == null) return false;
        var grant = Wire.Unpack<WorkerGrant>(saved);
        return !grant.Revoked && grant.Expires > DateTimeOffset.UtcNow && grant.RunId == runId &&
            CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(grant.TokenHash), Encoding.ASCII.GetBytes(Wire.Hash(token)));
    }
    public void Revoke(string runId)
    {
        if (store.Setting("worker-grant:" + runId) is { } saved)
            store.Setting("worker-grant:" + runId, Wire.Pack(Wire.Unpack<WorkerGrant>(saved) with { Revoked = true }));
    }
}
