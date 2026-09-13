using Thaddeus.Core;

namespace Thaddeus.Host;

public sealed record BrowserLaunchTicket(string Ticket, DateTimeOffset Expires);

/// <summary>A launcher transfers one short-lived capability, never the durable host key, to the browser.</summary>
public sealed class BrowserLaunchTickets(TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly Dictionary<string, DateTimeOffset> pending = [];
    public BrowserLaunchTicket Issue()
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            foreach (var entry in pending.Where(entry => entry.Value <= now).ToArray()) pending.Remove(entry.Key);
            if (pending.Count >= 8) throw new InvalidOperationException("Too many pending launch links. Wait a minute before opening another.");
            var ticket = Security.Random(); var expires = now.AddMinutes(1);
            pending.Add(Wire.Hash(ticket), expires);
            return new(ticket, expires);
        }
    }
    public bool Claim(string? ticket)
    {
        if (ticket == null || ticket.Length != 48 || ticket.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) return false;
        lock (gate) return pending.Remove(Wire.Hash(ticket), out var expires) && expires > clock.GetUtcNow();
    }
}
