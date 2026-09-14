using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class GatewayReadinessTests
{
    [Fact]
    public async Task ReadinessAfterTenProbesSucceedsWithinTheWindow()
    {
        var clock = new StartupTestClock(); var attempts = 0;
        var pending = GatewayReadiness.Wait((attempt, _) =>
        {
            attempts = attempt; return Task.FromResult(new SandboxCommandResult(attempt == 11 ? 0 : 1, "", ""));
        }, clock, default);
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            await StartupTestClock.Until(() => attempts == attempt && clock.PendingTimers == 2);
            clock.Advance(TimeSpan.FromSeconds(2));
        }
        Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(11, attempts); Assert.Equal(0, clock.PendingTimers);
    }

    [Fact]
    public async Task DeadlineCancelsAStalledProbe()
    {
        var clock = new StartupTestClock(); CancellationToken probeToken = default;
        var pending = GatewayReadiness.Wait(async (_, token) =>
        {
            probeToken = token; await Task.Delay(Timeout.Infinite, token); return new(0, "", "");
        }, clock, default);
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(probeToken.IsCancellationRequested); Assert.Equal(0, clock.PendingTimers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnerCancellationDuringProbeOrDelayRemainsCancellation(bool holdProbe)
    {
        var clock = new StartupTestClock(); using var cancel = new CancellationTokenSource();
        var pending = GatewayReadiness.Wait(async (_, token) =>
        {
            if (holdProbe) await Task.Delay(Timeout.Infinite, token);
            return new(1, "", "");
        }, clock, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(0, clock.PendingTimers);
    }

    [Fact]
    public async Task LateSuccessCannotAdmitTheWorkerEvenIfTheProbeIgnoresCancellation()
    {
        var clock = new StartupTestClock(); var reply = new TaskCompletionSource<SandboxCommandResult>();
        var pending = GatewayReadiness.Wait((_, _) => reply.Task, clock, default);
        clock.Advance(TimeSpan.FromSeconds(60));
        reply.SetResult(new(0, "late success", ""));
        Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(3)));
    }
}

// Timers and timestamps advance together; the raven need not spend a minute watching each clock.
internal sealed class StartupTestClock : TimeProvider
{
    private readonly List<Timer> timers = [];
    private long ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() { lock (timers) return ticks; }
    public int PendingTimers { get { lock (timers) return timers.Count; } }
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state); timer.Change(dueTime, period); return timer;
    }
    public void Advance(TimeSpan elapsed)
    {
        Timer[] due;
        lock (timers) { ticks += elapsed.Ticks; due = timers.Where(timer => timer.Due <= ticks).ToArray(); }
        foreach (var timer in due) timer.Fire();
    }
    internal static async Task Until(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!ready()) await Task.Delay(1, timeout.Token);
    }
    private sealed class Timer(StartupTestClock clock, TimerCallback callback, object? state) : ITimer
    {
        public long Due;
        private bool disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(Timeout.InfiniteTimeSpan, period);
            lock (clock.timers)
            {
                if (disposed) return false;
                Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.ticks + dueTime.Ticks;
                if (!clock.timers.Contains(this)) clock.timers.Add(this);
                return true;
            }
        }
        public void Fire()
        {
            lock (clock.timers) { if (disposed) return; disposed = true; clock.timers.Remove(this); }
            callback(state);
        }
        public void Dispose() { lock (clock.timers) { disposed = true; clock.timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
