using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public static class ProviderDiagnostics
{
    public static object Describe(Store store, ProviderSnapshot profile)
    {
        var matching = store.List().Where(r => r.Goal.Provider == profile).ToArray();
        var chat = matching.FirstOrDefault(r => r.Goal.Kind == "conversation" && r.State == RunState.Succeeded);
        var proposal = matching.FirstOrDefault(r => r.Approval != null && r.Goal.Kind == "plan");
        var usage = matching.FirstOrDefault(r => r.ModelCalls > 0 && r.InputTokens != null && r.OutputTokens != null);
        // Observation proves a specific run, not a permanent promise from a distant server.
        return new
        {
            profile,
            simulated = profile.Kind == "scripted",
            conversation = new { observed = chat != null, runId = chat?.Id },
            typedProposal = new { observed = proposal != null, runId = proposal?.Id },
            reportedUsage = new { observed = usage != null, runId = usage?.Id },
            tokenAdmission = profile.Kind == "scripted" ? "Certified zero inference" : "Uncertified input/output bounds; strict mode refuses inference",
            stream = "Adapter accepts SSE deltas. A completed reply does not prove incremental delivery; the Luna development bridge buffers final output.",
            scope = "Only receipts matching this exact saved profile are considered. No fallback or capability inference from model discovery."
        };
    }
}
