using System.Net;
using Microsoft.AspNetCore.Http;
using Thaddeus.Core;
using Thaddeus.Host;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class MaintenanceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-maintenance-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    private readonly MaintenanceControl control = new();
    private static DeviceSession Owner => new("owner", Wire.Hash("fictional-cookie"), "fictional-csrf", "Fixture", true, DateTimeOffset.UtcNow.AddDays(1));
    public MaintenanceTests() => store = new(Path.Combine(root, "study"));
    private MaintenancePlan Prepare(string? version = null, string mode = "backup", DeviceSession? owner = null) =>
        control.Prepare(store, owner ?? Owner, new(version ?? control.View(store).Version, mode), "http://localhost:5179", root);

    [Fact] public void AnAdmittedOperationPreventsClosureAndClosurePreventsLaterAdmission()
    {
        using var request = control.Admit(); var writer = control.Admit();
        Assert.Throws<InvalidOperationException>(() => Prepare()); Assert.Null(control.Plan);
        writer!.Dispose(); writer.Dispose();
        var plan = Prepare(); Assert.NotNull(plan);
        Assert.Null(control.Admit()); Assert.NotNull(control.Admit(observation: true));
        Assert.Equal("closing", control.View(store).Phase);
        Assert.False(Directory.Exists(plan.BackupRoot)); Assert.False(Directory.Exists(plan.Destination));
        control.CloseStreams(); Assert.True(control.Closing.IsCancellationRequested);
    }
    [Theory] [InlineData(RunState.Queued)] [InlineData(RunState.Running)]
    public void ActiveWorkCannotBeSilentlyCancelledByMaintenance(RunState state)
    {
        var run = new Run { Goal = new("Fictional work", [], "plans/", [], new(), new()), State = state };
        store.Save(run, "fixture", new { }); var before = Wire.Pack(store.Get(run.Id));
        using var request = control.Admit();
        Assert.Throws<InvalidOperationException>(() => Prepare());
        Assert.Equal(before, Wire.Pack(store.Get(run.Id))); Assert.Null(control.Plan); Assert.False(control.View(store).CanStart);
    }
    [Fact] public void ARemainingReservationBlocksMaintenanceEvenWhenTheRunLooksFinished()
    {
        var run = new Run { Goal = new("Fictional work", [], "plans/", [], new(), new()), State = RunState.Succeeded, ReservedTokens = 12 };
        store.Save(run, "fixture", new { }); using var request = control.Admit();
        Assert.Throws<InvalidOperationException>(() => Prepare()); Assert.Null(control.Plan);
    }
    [Fact] public void SavedApprovalIsPreservedWhenAnIdleStudyCloses()
    {
        var run = new Run { Goal = new("Fictional approval", [], "plans/", [], new(), new()), State = RunState.AwaitingApproval };
        store.Save(run, "fixture", new { }); var before = Wire.Pack(store.Get(run.Id));
        using var request = control.Admit(); var plan = Prepare();
        Assert.Equal(before, Wire.Pack(store.Get(run.Id))); Assert.Equal(store.Root, plan.Source);
        Assert.True(plan.Owner.Expires <= DateTimeOffset.UtcNow.AddHours(1));
    }
    [Fact] public void StaleOrInvalidRequestsLeaveTheStudyOpen()
    {
        using var request = control.Admit();
        Assert.Throws<InvalidOperationException>(() => Prepare("stale")); Assert.Throws<ArgumentException>(() => Prepare(mode: "overwrite"));
        Assert.Null(control.Plan); using var next = control.Admit(); Assert.NotNull(next);
    }
    [Theory] [InlineData("device")] [InlineData("expired")] [InlineData("revoked")]
    public void OnlyACurrentOwnerCanPrepareMaintenance(string kind)
    {
        var owner = kind switch { "device" => Owner with { Owner = false }, "expired" => Owner with { Expires = DateTimeOffset.UtcNow.AddMinutes(-1) }, _ => Owner with { Revoked = true } };
        using var request = control.Admit(); Assert.Throws<InvalidOperationException>(() => Prepare(owner: owner)); Assert.Null(control.Plan);
    }
    [Theory] [InlineData("cookie")] [InlineData("origin")] [InlineData("remote")] [InlineData("funnel")] [InlineData("cross-site")]
    public void OfflineScreenKeepsTheLocalOwnerBoundary(string attack)
    {
        var plan = new MaintenancePlan("id", "backup", store.Root, root, Path.Combine(root, "copy"), "http://localhost:5179", root, Owner);
        var context = new DefaultHttpContext(); context.Request.Scheme = "http"; context.Request.Host = new("localhost:5179"); context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers.Cookie = "thaddeus-session=fictional-cookie";
        Assert.True(MaintenanceScreen.Authorized(context, plan));
        if (attack == "cookie") context.Request.Headers.Cookie = "thaddeus-session=another-owner";
        if (attack == "origin") context.Request.Headers.Origin = "https://other.example";
        if (attack == "remote") context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.5");
        if (attack == "funnel") context.Request.Headers["Tailscale-Funnel-Request"] = "1";
        if (attack == "cross-site") context.Request.Headers["Sec-Fetch-Site"] = "cross-site";
        Assert.False(MaintenanceScreen.Authorized(context, plan));
    }
    public void Dispose() { control.Dispose(); store.Dispose(); Directory.Delete(root, true); }
}
