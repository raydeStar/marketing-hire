using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class DelegationSchedulerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-delegation-" + Guid.NewGuid().ToString("N"));
    private readonly MutableClock clock = new(new DateTimeOffset(2026, 9, 16, 16, 0, 0, TimeSpan.Zero));

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan value) => Now += value;
    }

    private sealed class Dispatcher : IDelegationDispatcher
    {
        public int Calls;
        public Func<DelegationJob, DelegationOccurrence, Task<DelegationDispatchResult>> OnDispatch = (_, occurrence) =>
            Task.FromResult(new DelegationDispatchResult("accepted", "Reminder delivered to Windows and saved in Thaddeus.", true,
                "toast-" + occurrence.OperationId, JsonSerializer.SerializeToElement(new { accepted = true }), "delivered"));
        public async Task<DelegationDispatchResult> Dispatch(DelegationJob job, DelegationOccurrence occurrence, CancellationToken cancellation)
        { Calls++; return await OnDispatch(job, occurrence); }
    }

    private sealed class RefusingNotificationSink : IWindowsNotificationSink
    {
        public int Calls;
        public WindowsNotificationReceipt Show(string title, string message)
        {
            Calls++;
            throw new System.ComponentModel.Win32Exception(5, "Notifications denied by the fixture.");
        }
        public void Dispose() { }
    }

    [Fact]
    public async Task PersistedReminderRunsOnceAfterRestartAndKeepsProviderReceipt()
    {
        string id;
        using (var first = new Store(root))
        {
            var scheduler = new DelegationScheduler(first, new Dispatcher(), clock);
            id = scheduler.CreateReminder("Call dentist", "Call the dentist.", clock.Now.AddHours(2), "America/Denver").Job.Id;
        }
        clock.Advance(TimeSpan.FromHours(2)); var dispatcher = new Dispatcher();
        using (var reopened = new Store(root))
        {
            var scheduler = new DelegationScheduler(reopened, dispatcher, clock);
            Assert.Equal(0, scheduler.Recover()); Assert.Equal(1, await scheduler.Tick()); Assert.Equal(0, await scheduler.Tick());
            var job = Assert.Single(reopened.DelegationJobs()); var occurrence = Assert.Single(reopened.DelegationOccurrences(id));
            Assert.Equal("succeeded", job.State); Assert.Equal("succeeded", occurrence.State);
            Assert.Equal("accepted", occurrence.DispatchState); Assert.True(occurrence.ActionSucceeded);
            Assert.Equal("delivered", occurrence.NotificationStatus); Assert.StartsWith("toast-", occurrence.ProviderId);
            Assert.Equal(1, reopened.DelegationGrant(job.GrantId)!.UsedOccurrences);
            Assert.Null(occurrence.ReadAt);
            var read = reopened.ReadDelegationOccurrence(occurrence.Id, occurrence.Version, clock.Now);
            Assert.Equal(clock.Now, read.ReadAt); Assert.Equal(occurrence.Version + 1, read.Version);
            Assert.Equal(1, dispatcher.Calls);
        }
        Assert.Equal(1, dispatcher.Calls);
    }

    [Fact]
    public async Task InterruptedDispatchBecomesUnknownAndIsNeverReplayed()
    {
        string id;
        using (var first = new Store(root))
        {
            var scheduler = new DelegationScheduler(first, new Dispatcher(), clock);
            id = scheduler.CreateReminder("Call dentist", "Call the dentist.", clock.Now.AddMinutes(1), "America/Denver").Job.Id;
            clock.Advance(TimeSpan.FromMinutes(1)); Assert.NotNull(first.ClaimDueDelegation(clock.Now));
        }
        var dispatcher = new Dispatcher();
        using var reopened = new Store(root); var recovered = new DelegationScheduler(reopened, dispatcher, clock);
        Assert.Equal(1, recovered.Recover()); Assert.Equal(0, await recovered.Tick()); Assert.Equal(0, dispatcher.Calls);
        Assert.Equal("unknown", reopened.DelegationJobs().Single(item => item.Id == id).State);
        var occurrence = Assert.Single(reopened.DelegationOccurrences(id));
        Assert.Equal("unknown", occurrence.State); Assert.Equal("unknown", occurrence.DispatchState);
    }

    [Fact]
    public async Task CancellationBeforeDueRevokesGrantAndPreventsDispatch()
    {
        using var store = new Store(root); var dispatcher = new Dispatcher(); var scheduler = new DelegationScheduler(store, dispatcher, clock);
        var created = scheduler.CreateReminder("Call dentist", "Call the dentist.", clock.Now.AddHours(2), "America/Denver");
        var cancelled = store.CancelDelegation(created.Job.Id, created.Job.Version, clock.Now);
        clock.Advance(TimeSpan.FromHours(3)); Assert.Equal(0, await scheduler.Tick());
        Assert.Equal("cancelled", cancelled.State); Assert.True(cancelled.CancellationRequested);
        Assert.True(store.DelegationGrant(created.Grant.Id)!.Revoked); Assert.Empty(store.DelegationOccurrences()); Assert.Equal(0, dispatcher.Calls);
    }

    [Fact]
    public async Task CancellationRaceReportsSuccessfulEffectInsteadOfPretendingItWasStopped()
    {
        using var store = new Store(root); var dispatcher = new Dispatcher(); var scheduler = new DelegationScheduler(store, dispatcher, clock);
        var created = scheduler.CreateReminder("Call dentist", "Call the dentist.", clock.Now.AddMinutes(1), "America/Denver");
        dispatcher.OnDispatch = (job, occurrence) =>
        {
            var current = store.DelegationJobs().Single(item => item.Id == job.Id);
            store.CancelDelegation(current.Id, current.Version, clock.Now);
            return Task.FromResult(new DelegationDispatchResult("accepted", "The reminder was already delivered when cancellation arrived.", true,
                "toast-race", JsonSerializer.SerializeToElement(new { accepted = true }), "delivered"));
        };
        clock.Advance(TimeSpan.FromMinutes(1)); Assert.Equal(1, await scheduler.Tick());
        var job = store.DelegationJobs().Single(); var occurrence = store.DelegationOccurrences().Single();
        Assert.True(job.CancellationRequested); Assert.Equal("succeeded", job.State); Assert.Equal("succeeded", occurrence.State);
        Assert.Equal("toast-race", occurrence.ProviderId);
    }

    [Fact]
    public async Task StaleReminderIsMarkedMissedWithoutCatchUpStorm()
    {
        using var store = new Store(root); var dispatcher = new Dispatcher(); var scheduler = new DelegationScheduler(store, dispatcher, clock);
        scheduler.CreateReminder("Call dentist", "Call the dentist.", clock.Now.AddMinutes(1), "America/Denver", TimeSpan.FromMinutes(5));
        clock.Advance(TimeSpan.FromMinutes(20)); Assert.Equal(0, await scheduler.Tick());
        Assert.Equal("missed", store.DelegationJobs().Single().State); Assert.Equal("missed", store.DelegationOccurrences().Single().State);
        Assert.Equal(0, dispatcher.Calls);
    }

    [Fact]
    public void WeekdayScheduleKeepsLocalTimeAcrossDaylightSavingChange()
    {
        var schedule = new DelegationSchedule("weekdays", null, "America/Denver", "09:00");
        var friday = new DateTimeOffset(2026, 3, 6, 17, 0, 0, TimeSpan.Zero); // 10:00 MST Friday.
        var monday = schedule.FirstDue(friday);
        Assert.Equal(new DateTimeOffset(2026, 3, 9, 15, 0, 0, TimeSpan.Zero), monday); // 09:00 MDT Monday.
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 15, 0, 0, TimeSpan.Zero), schedule.NextAfter(monday));
    }

    [Fact]
    public async Task ProviderUncertaintyStopsInUnknownWithoutRetry()
    {
        using var store = new Store(root); var dispatcher = new Dispatcher
        {
            OnDispatch = (_, _) => throw new DelegationOutcomeUnknownException("Accepted before connection ended.")
        };
        var scheduler = new DelegationScheduler(store, dispatcher, clock);
        scheduler.CreateReminder("Call dentist", "Call the dentist.", clock.Now.AddMinutes(1), "America/Denver");
        clock.Advance(TimeSpan.FromMinutes(1)); Assert.Equal(1, await scheduler.Tick()); Assert.Equal(0, await scheduler.Tick());
        Assert.Equal("unknown", store.DelegationJobs().Single().State); Assert.Equal("unknown", store.DelegationOccurrences().Single().State);
        Assert.Equal(1, dispatcher.Calls);
    }

    [Fact]
    public async Task NativeNotificationRefusalKeepsUnreadReminderSuccessfulAndDoesNotRetry()
    {
        using var store = new Store(root);
        var sink = new RefusingNotificationSink();
        using var dispatcher = new WindowsDelegationDispatcher(() => sink, () => true);
        var scheduler = new DelegationScheduler(store, dispatcher, clock);
        var created = scheduler.CreateReminder("Call dentist", "Call the dentist.", clock.Now.AddMinutes(1), "America/Denver");

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await scheduler.Tick());
        Assert.Equal(0, await scheduler.Tick());

        var job = Assert.Single(store.DelegationJobs());
        var occurrence = Assert.Single(store.DelegationOccurrences(created.Job.Id));
        Assert.Equal("succeeded", job.State);
        Assert.Equal("succeeded", occurrence.State);
        Assert.True(occurrence.ActionSucceeded);
        Assert.Equal("accepted", occurrence.DispatchState);
        Assert.Equal("failed", occurrence.NotificationStatus);
        Assert.Contains("Settings", occurrence.NotificationError);
        Assert.Contains("will not fire again automatically", occurrence.NotificationError);
        Assert.Null(occurrence.ReadAt);
        Assert.Equal(1, sink.Calls);
    }

    [Fact]
    public async Task RealClockPumpExecutesOneShotOnceWithoutAnyBrowserClient()
    {
        using var store = new Store(root); var dispatcher = new Dispatcher();
        var scheduler = new DelegationScheduler(store, dispatcher, TimeProvider.System);
        scheduler.CreateReminder("Pump fixture", "Run once without a browser.", DateTimeOffset.UtcNow.AddMilliseconds(750), TimeZoneInfo.Local.Id);
        using var pump = new DelegationPump(scheduler);
        await pump.StartAsync(default);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (dispatcher.Calls == 0 && DateTimeOffset.UtcNow < deadline) await Task.Delay(100);
            Assert.Equal(1, dispatcher.Calls);
            await Task.Delay(1250);
            Assert.Equal(1, dispatcher.Calls);
            Assert.Equal("succeeded", Assert.Single(store.DelegationOccurrences()).State);
        }
        finally { await pump.StopAsync(default); }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
