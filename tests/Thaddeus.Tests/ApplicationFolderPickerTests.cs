using Thaddeus.Host;

namespace Thaddeus.Tests;

public sealed class ApplicationFolderPickerTests
{
    private sealed class Dialog(bool available = true) : IApplicationFolderDialog
    {
        public bool Available => available;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string?> Choose(CancellationToken cancellation)
        {
            Started.TrySetResult();
            try { return await Result.Task.WaitAsync(cancellation); }
            finally { Stopped.TrySetResult(); }
        }
    }

    [Fact] public async Task ASecondChooserCannotStartWhileOneIsOpenAndStaleCancellationCannotCloseIt()
    {
        var dialog = new Dialog(); await using var picker = new ApplicationFolderPicker(dialog);
        var started = picker.Begin(default); await dialog.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(picker.Busy); Assert.Equal("choosing", started.Phase);
        Assert.Throws<InvalidOperationException>(() => picker.Begin(default));
        Assert.Throws<InvalidOperationException>(() => picker.Cancel("stale")); Assert.False(dialog.Stopped.Task.IsCompleted);
        picker.Cancel(started.Id!); await picker.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(dialog.Stopped.Task.IsCompleted); Assert.False(picker.Busy); Assert.Equal("cancelled", picker.View.Phase);
        var second = picker.Begin(default); Assert.NotEqual(started.Id, second.Id);
        Assert.Throws<InvalidOperationException>(() => picker.Cancel(started.Id!));
        picker.Cancel(second.Id!); await picker.Completion;
    }

    [Fact] public async Task SelectionReturnsOnlyAPathWithoutOpeningOrCreatingTheTarget()
    {
        var dialog = new Dialog(); await using var picker = new ApplicationFolderPicker(dialog);
        var folder = Path.Combine(Path.GetTempPath(), "fictional-thaddeus-" + Guid.NewGuid().ToString("N"));
        picker.Begin(default); dialog.Result.SetResult(folder); await picker.Completion;
        Assert.Equal("selected", picker.View.Phase); Assert.Equal(folder, picker.View.Directory); Assert.False(Directory.Exists(folder));
    }

    [Theory] [InlineData(null)] [InlineData("")] [InlineData("relative/path")] [InlineData("/bad\nfolder")]
    public async Task CancelAndInvalidResultsNeverProduceASelectedFolder(string? result)
    {
        var dialog = new Dialog(); await using var picker = new ApplicationFolderPicker(dialog);
        picker.Begin(default); dialog.Result.SetResult(result); await picker.Completion;
        Assert.Equal(result == null ? "cancelled" : "failed", picker.View.Phase); Assert.Null(picker.View.Directory);
    }

    [Fact] public async Task ShutdownWaitsUntilTheOwnedDialogHasStopped()
    {
        var dialog = new Dialog(); var picker = new ApplicationFolderPicker(dialog);
        picker.Begin(default); await dialog.Started.Task;
        await picker.DisposeAsync(); Assert.True(dialog.Stopped.Task.IsCompleted);
        Assert.Throws<ObjectDisposedException>(() => picker.Begin(default));
    }

    [Fact] public async Task MaintenanceExpiryCancelsTheDialogAndUnavailableDesktopDoesNotStartOne()
    {
        var dialog = new Dialog(); await using var picker = new ApplicationFolderPicker(dialog);
        using var lifetime = new CancellationTokenSource(); picker.Begin(lifetime.Token); await dialog.Started.Task;
        lifetime.Cancel(); await picker.Completion.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal("cancelled", picker.View.Phase);
        var unavailable = new Dialog(false); await using var other = new ApplicationFolderPicker(unavailable);
        Assert.False(other.View.Available); Assert.Throws<InvalidOperationException>(() => other.Begin(default));
        Assert.False(unavailable.Started.Task.IsCompleted);
    }
}
