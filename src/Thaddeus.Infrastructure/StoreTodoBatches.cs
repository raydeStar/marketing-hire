using System.Globalization;
using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed record TodoBatchItem(string Title, string Notes, string? Due, string? Ambiguity);
public sealed record TodoBatchProposal(string SourceReference, string SourceVersion, TodoBatchItem[] Items);
public sealed record TodoBatchOperation(string Id, string InputHash, string SourceReference, string SourceVersion,
    string[] ItemIds, DateTimeOffset Created);

public sealed partial class Store
{
    public TodoBatchOperation[] TodoBatchOperations()
    {
        lock (gate) return Query("SELECT body FROM todo_batch_operations ORDER BY rowid").Select(Wire.Unpack<TodoBatchOperation>).ToArray();
    }

    public (TodoBatchOperation Operation, LibraryItem[] Items) CreateTodoBatch(string operationId, TodoBatchProposal proposal)
    {
        lock (gate)
        {
            var batchOperationId = operationId ?? throw new ArgumentException("Invalid To-do batch operation ID.");
            if (!Regex.IsMatch(batchOperationId, "\\A[a-f0-9]{32}\\z")) throw new ArgumentException("Invalid To-do batch operation ID.");
            ValidateTodoBatch(proposal);
            var inputHash = Wire.Hash(Wire.Pack(proposal));
            var existingOperation = Query("SELECT body FROM todo_batch_operations WHERE id=$id", ("$id", batchOperationId))
                .Select(Wire.Unpack<TodoBatchOperation>).SingleOrDefault();
            if (existingOperation != null)
            {
                if (existingOperation.InputHash != inputHash) throw new InvalidOperationException("A To-do batch identity cannot be reused for different items.");
                var existingItems = existingOperation.ItemIds.Select(id => Library().SingleOrDefault(item => item.Id == id) ??
                    throw new InvalidOperationException("A previously created To-do is missing. Review the batch before repairing it.")).ToArray();
                return (existingOperation, existingItems);
            }

            var created = new List<LibraryItem>();
            for (var index = 0; index < proposal.Items.Length; index++)
            {
                var proposalItem = proposal.Items[index];
                var id = Wire.Hash(Wire.Pack(new { operationId = batchOperationId, index }))[..32];
                var due = proposalItem.Due == null ? (DateOnly?)null : DateOnly.ParseExact(proposalItem.Due, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var sourceLine = "Source: " + proposal.SourceReference + " (version " + proposal.SourceVersion[..12] + ")";
                var content = proposalItem.Notes.Trim() + "\n\n" + sourceLine +
                    (string.IsNullOrWhiteSpace(proposalItem.Ambiguity) ? "" : "\n\nUnresolved: " + proposalItem.Ambiguity.Trim());
                var url = Uri.TryCreate(proposal.SourceReference, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https"
                    ? proposal.SourceReference : null;
                var current = Library().SingleOrDefault(item => item.Id == id);
                if (current == null)
                {
                    current = EditLibrary(id, new("todo", proposalItem.Title, content, "open", url, due, "absent", new("daily", "none")));
                }
                else if (current.Kind != "todo" || current.Title != proposalItem.Title.Trim() || current.Content != content ||
                    current.Status != "open" || current.Url != url || current.Due != due)
                    throw new InvalidOperationException("A deterministic To-do ID already contains different content. Review instead of overwriting it.");
                created.Add(current);
            }
            testFault?.Invoke("before-todo-batch-operation");
            var operation = new TodoBatchOperation(batchOperationId, inputHash, proposal.SourceReference, proposal.SourceVersion,
                created.Select(item => item.Id).ToArray(), DateTimeOffset.UtcNow);
            Exec("INSERT INTO todo_batch_operations VALUES($id,$body)", ("$id", operation.Id), ("$body", Wire.Pack(operation)));
            return (operation, created.ToArray());
        }
    }

    private static void ValidateTodoBatch(TodoBatchProposal proposal)
    {
        if (proposal.SourceReference.Length is < 1 or > 2048 || !Regex.IsMatch(proposal.SourceVersion ?? "", "\\A[a-f0-9]{64}\\z") ||
            proposal.Items.Length is < 1 or > 12)
            throw new ArgumentException("A To-do batch needs one reviewed source and between one and twelve items.");
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in proposal.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 160 || item.Notes == null || item.Notes.Length > 10_000 ||
                item.Ambiguity?.Length > 1000 || item.Due != null && !DateOnly.TryParseExact(item.Due, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new ArgumentException("Each To-do needs a bounded title and notes; due dates use YYYY-MM-DD or remain unresolved.");
            if (!signatures.Add(Wire.Hash(Wire.Pack(new { title = item.Title.Trim(), notes = item.Notes.Trim(), item.Due }))))
                throw new ArgumentException("The proposed To-do batch contains a duplicate item.");
        }
    }
}
