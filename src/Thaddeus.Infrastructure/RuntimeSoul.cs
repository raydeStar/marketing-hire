using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Runtime
{
    private bool HandleSoulAction(Run run, ToolRequest action)
    {
        if (action.Name != SoulConversation.ToolName) return false;
        var proposal = SoulConversation.Parse(action);
        var current = store.Soul();
        if (proposal.BaseVersion != current.Version)
            throw new InvalidOperationException("The Soul changed while this proposal was being drafted. Ask again from the current version.");
        if (proposal.Content == current.Content)
            throw new ArgumentException("The proposed Soul is identical to the current Soul.");
        if (string.IsNullOrWhiteSpace(proposal.Content) || proposal.Content.Length > Store.MaxSoulCharacters)
            throw new ArgumentException("The proposed Soul is empty or exceeds the supported size.");

        var exactAction = new ToolRequest(SoulConversation.ToolName, Store.SoulFileName, proposal.Content);
        var expiry = clock.GetUtcNow().AddMinutes(15);
        var approvalId = Guid.NewGuid().ToString("N");
        var digest = SoulApprovalDigest(run.Id, approvalId, exactAction, current.Version, expiry);
        run.Evidence.RemoveAll(item => item.Path == Store.SoulFileName);
        run.Evidence.Add(new(Store.SoulFileName, current.Version, current.Content));
        run.Approval = new(approvalId, run.Id, exactAction, digest, current.Version, expiry);
        run.State = RunState.AwaitingApproval;
        run.Summary = "Soul edit drafted · review the exact before and after text";
        store.Save(run, "soul.approval.requested", new { approval = run.Approval, permissionsChanged = false });
        return true;
    }

    private static bool IsSoulApproval(Approval approval) => approval.Action.Name == SoulConversation.ToolName && approval.Action.Path == Store.SoulFileName;
    private static string SoulApprovalDigest(string runId, string approvalId, ToolRequest action, string version, DateTimeOffset expiry) =>
        Wire.Hash(Wire.Pack(new { runId, approvalId, action, scope = Store.SoulFileName, version, expiry }));
}
