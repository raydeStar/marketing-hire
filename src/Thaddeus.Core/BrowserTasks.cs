namespace Thaddeus.Core;

public record BrowserTaskScope(string Objective, string StartUrl, string[] Hosts, Budget Limits);
public record BrowserPage(string Url, string Title, string Snapshot, string Version);
public record BrowserAction(string Kind, string PageVersion, string? Url = null, string? Target = null,
    string? Description = null, string? Text = null, string[]? Values = null, string? Key = null);
public record BrowserActionReceipt(string Id, BrowserAction Action, string State, DateTimeOffset At,
    string Summary, BrowserPage? Page = null);
public sealed class BrowserTaskState
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public required BrowserTaskScope Scope { get; set; }
    public string Phase { get; set; } = "review";
    public int Epoch { get; set; }
    public DateTimeOffset? AuthorizedUntil { get; set; }
    public BrowserPage? Page { get; set; }
    public BrowserAction? PendingAction { get; set; }
    public List<BrowserActionReceipt> Receipts { get; set; } = [];
    public double ActiveSeconds { get; set; }
    public DateTimeOffset? ActiveSince { get; set; }
}
public record BrowserToolContext(Budget Allowance, BrowserTaskState? Task = null);

public interface IBrowserSession : IAsyncDisposable
{
    bool Available { get; }
    bool IsOpen(string sessionId);
    Task<BrowserPage> Start(string sessionId, BrowserTaskScope scope, CancellationToken cancellation);
    Task<BrowserPage> Observe(string sessionId, CancellationToken cancellation);
    Task<BrowserPage> Act(string sessionId, BrowserAction action, CancellationToken cancellation);
    Task Close(string sessionId);
}
