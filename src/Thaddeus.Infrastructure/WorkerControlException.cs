namespace Thaddeus.Infrastructure;

/// <summary>Host-defined operation names, without native output or credentials in product receipts.</summary>
internal sealed class WorkerControlException : IOException
{
    public string FailureCode { get; }

    private WorkerControlException(string stage, Exception cause)
        : base("Worker control was not confirmed at " + stage + ". No automatic replay.", cause)
    {
        FailureCode = stage + ":" + (cause is OperationCanceledException ? "interrupted" : cause.GetType().Name);
    }

    public static async Task During(string stage, Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception error) when (error is not (OutOfMemoryException or WorkerControlException))
        {
            // Keep the first failed step. A second label must not obscure the original locked door.
            throw new WorkerControlException(stage, error);
        }
    }
}
