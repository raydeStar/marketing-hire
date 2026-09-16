using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Runtime
{
    public Run RetryConversation(string sourceId, string operationId, ProviderSnapshot provider)
    {
        if (!Guid.TryParseExact(operationId, "N", out _)) throw new ArgumentException("A retry needs a valid operation ID.");
        lock (conversationGate)
        {
            var source = store.Get(sourceId) ?? throw new ArgumentException("That reply no longer exists.");
            var rootId = source.ConversationRetry?.RootId ?? source.Id;
            var existing = store.List().SingleOrDefault(r => r.ConversationRetry?.OperationId == operationId);
            if (existing != null)
            {
                if (existing.ConversationRetry!.SourceId != sourceId) throw new ArgumentException("This retry ID belongs to another message.");
                return existing;
            }
            if (source.Goal.Kind != "conversation" || source.Execution != null || source.SuggestIdeas || source.ArtifactResult != null || source.Approval != null)
                throw new InvalidOperationException("This action cannot be regenerated. Describe the change you want in a new message.");
            if (source.State is not (RunState.Failed or RunState.Cancelled or RunState.NeedsAttention or RunState.Succeeded))
                throw new InvalidOperationException("Wait for this reply to stop before retrying it.");
            var tasks = store.List();
            if (tasks.Any(r => (r.ConversationRetry?.RootId ?? r.Id) == rootId && r.ArtifactResult != null))
                throw new InvalidOperationException("An attempt already acted on an app. Describe the change you want in a new message.");
            if (tasks.Any(r => r.Goal.Kind == "conversation" && r.State is RunState.Running or RunState.Queued &&
                (!r.Background || (r.ConversationRetry?.RootId ?? r.Id) == rootId)))
                throw new InvalidOperationException("A reply is already running. Wait for it or cancel it first.");

            var run = Build(source.Goal with { Provider = provider });
            run.UploadIds = source.UploadIds.ToArray(); store.Attachments(run.UploadIds);
            run.ConversationContext = source.ConversationContext.ToList();
            run.ArtifactContext = store.ArtifactContext(source.ArtifactContext?.Selected?.Id,
                source.ArtifactContext?.LocalDate ?? DateTime.Now.ToString("yyyy-MM-dd"));
            run.ConversationWebUrls = publicWeb == null ? [] : ConversationWeb.Links(run.Goal.Objective);
            run.ConversationRetry = new(rootId, sourceId, operationId);
            // A second attempt gets its own ledger entry, not a second copy of the user's words.
            store.Save(run, "conversation.retry.accepted", new { rootId, sourceId, operationId, provider, budget = run.Goal.Limits });
            return run;
        }
    }

    private List<ChatMessage> ConversationHistory()
    {
        var messages = store.Chats();
        var byId = messages.ToDictionary(m => m.Id);
        var conversations = store.List().Where(r => r.Goal.Kind == "conversation").ToArray();
        var roots = conversations.Where(r => r.ConversationRetry == null).ToDictionary(r => r.Id);
        var families = conversations.ToLookup(r => r.ConversationRetry?.RootId ?? r.Id);
        var answerIds = conversations.Select(r => r.Id + "-assistant").ToHashSet();
        var history = new List<ChatMessage>();
        foreach (var message in messages)
        {
            if (answerIds.Contains(message.Id)) continue;
            history.Add(message);
            if (!message.Id.EndsWith("-user", StringComparison.Ordinal) || !roots.TryGetValue(message.Id[..^5], out var root)) continue;
            var answer = families[root.Id].Where(r => r.State == RunState.Succeeded && byId.ContainsKey(r.Id + "-assistant"))
                .OrderBy(r => r.Created).LastOrDefault();
            if (answer != null) history.Add(byId[answer.Id + "-assistant"]);
        }
        return history.TakeLast(20).ToList();
    }
}
