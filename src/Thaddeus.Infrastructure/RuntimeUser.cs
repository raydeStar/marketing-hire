using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Runtime
{
    private bool HandleUserAction(Run run, ToolRequest action)
    {
        if (action.Name != UserConversation.ToolName) return false;
        var proposal = UserConversation.Parse(action);
        var current = store.User();
        if (proposal.BaseVersion != current.Version)
            throw new InvalidOperationException("The User profile changed while this proposal was being drafted. Ask again from the current version.");
        if (proposal.Content == current.Content)
            throw new ArgumentException("The proposed User profile is identical to the current profile.");
        if (string.IsNullOrWhiteSpace(proposal.Content) || proposal.Content.Length > Store.MaxUserCharacters)
            throw new ArgumentException("The proposed User profile is empty or exceeds the supported size.");

        var exactAction = new ToolRequest(UserConversation.ToolName, Store.UserFileName, proposal.Content);
        var expiry = clock.GetUtcNow().AddMinutes(15);
        var approvalId = Guid.NewGuid().ToString("N");
        var digest = UserApprovalDigest(run.Id, approvalId, exactAction, current.Version, expiry);
        run.Evidence.RemoveAll(item => item.Path == Store.UserFileName);
        run.Evidence.Add(new(Store.UserFileName, current.Version, current.Content));
        run.Approval = new(approvalId, run.Id, exactAction, digest, current.Version, expiry);
        run.State = RunState.AwaitingApproval;
        run.Summary = "User profile update drafted · review the exact before and after text";
        store.Save(run, "user.approval.requested", new { approval = run.Approval, permissionsChanged = false, source = "firsthand-conversation" });
        return true;
    }

    private static bool IsUserApproval(Approval approval) => approval.Action.Name == UserConversation.ToolName && approval.Action.Path == Store.UserFileName;
    private static string UserApprovalDigest(string runId, string approvalId, ToolRequest action, string version, DateTimeOffset expiry) =>
        Wire.Hash(Wire.Pack(new { runId, approvalId, action, scope = Store.UserFileName, version, expiry }));
}
