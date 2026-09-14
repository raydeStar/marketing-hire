using System.Text.Json;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public sealed record FolderPickerView(bool Available, string Phase, string Message, string? Id = null, string? Directory = null);
public sealed record FolderPickerCancellation(string Id);
public interface IApplicationFolderDialog
{
    bool Available { get; }
    Task<string?> Choose(CancellationToken cancellation);
}

/// <summary>Only the local owner can ring this bell. Picking a folder does not trust or run its contents.</summary>
public sealed class ApplicationFolderPicker(IApplicationFolderDialog dialog) : IAsyncDisposable
{
    private readonly object sync = new();
    private FolderPickerView state = new(dialog.Available, "idle", dialog.Available
        ? "Browse opens a folder chooser on this computer. You can also paste its full path."
        : "A desktop folder chooser is unavailable here. Paste the full folder path on the host computer.");
    private CancellationTokenSource? operation;
    private Task completion = Task.CompletedTask;
    private bool disposed;
    public FolderPickerView View { get { lock (sync) return state; } }
    public bool Busy => View.Phase is "choosing" or "cancelling";
    public Task Completion { get { lock (sync) return completion; } }

    public FolderPickerView Begin(CancellationToken lifetime)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            lifetime.ThrowIfCancellationRequested();
            if (!dialog.Available) throw new InvalidOperationException(state.Message);
            if (Busy) throw new InvalidOperationException("A folder chooser is already open. Select a folder or cancel it first.");
            var current = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            operation = current;
            current.CancelAfter(TimeSpan.FromMinutes(5));
            state = new(true, "choosing", "Choose the extracted application folder in the desktop dialog, or cancel below.", Guid.NewGuid().ToString("N"));
            completion = Task.Run(() => Choose(current), CancellationToken.None);
            return state;
        }
    }

    public FolderPickerView Cancel(string id)
    {
        lock (sync)
        {
            if (id != state.Id) throw new InvalidOperationException("The folder chooser changed. Refresh this screen before cancelling.");
            if (!Busy) return state;
            state = state with { Phase = "cancelling", Message = "Closing the desktop folder chooser…" };
            operation!.Cancel();
            return state;
        }
    }

    private async Task Choose(CancellationTokenSource current)
    {
        try
        {
            var path = await dialog.Choose(current.Token);
            current.Token.ThrowIfCancellationRequested();
            if (path != null && (path.Length > 2048 || path.IndexOfAny(['\0', '\r', '\n']) >= 0 || !Path.IsPathFullyQualified(path)))
                throw new InvalidOperationException("The folder chooser did not return a usable absolute path.");
            lock (sync) state = state with { Phase = path == null ? "cancelled" : "selected", Directory = path,
                Message = path == null ? "Folder selection cancelled. Your previous path is unchanged." : "Folder selected. Review the backup to check this application's contents." };
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        { lock (sync) state = state with { Phase = "cancelled", Directory = null, Message = "Folder selection cancelled or timed out. You can browse again or paste a path." }; }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or JsonException or System.ComponentModel.Win32Exception)
        { lock (sync) state = state with { Phase = "failed", Directory = null, Message = "The desktop folder chooser could not finish. Try again or paste the folder's full path." }; }
        finally { lock (sync) { current.Dispose(); if (operation == current) operation = null; } }
    }

    public async ValueTask DisposeAsync()
    {
        Task pending;
        lock (sync) { disposed = true; operation?.Cancel(); pending = completion; }
        await pending;
    }
}
