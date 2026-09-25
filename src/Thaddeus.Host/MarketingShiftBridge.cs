using System.Text.Json;

namespace Thaddeus.Host;

public sealed partial class MarketingBackend
{
    /// <summary>The employee's work ledger, for shifts: the same `hire` records the cockpit shows.</summary>
    internal async Task<(JsonElement? Value, string? Error)> ShiftHire(string? input, params string[] arguments)
    {
        var result = await Hire(CancellationToken.None, input, arguments);
        if (result.Error == null && arguments is ["task", "create" or "update", ..] or ["draft", "add", ..] or ["event", ..]) InvalidateState();
        return result;
    }

    /// <summary>Shifts, direct chat and the campaign runner never run model turns at the same time.
    /// Returns false when another turn owns execution, so the stage waits for the next cycle.</summary>
    internal async Task<bool> TryEnterExecution(CancellationToken cancellation) => await executionGate.WaitAsync(0, cancellation);
    internal void LeaveExecution() => executionGate.Release();
}
