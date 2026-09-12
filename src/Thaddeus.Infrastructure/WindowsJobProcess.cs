using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Thaddeus.Infrastructure;

public sealed record OwnedProcessRequest(string Executable, IReadOnlyList<string> Arguments,
    string WorkingDirectory, IReadOnlyDictionary<string, string> Environment, TimeSpan Lifetime,
    int OutputLimit = 524288);
public sealed record OwnedProcessExit(int ExitCode, string? StopReason)
{
    // Windows may report zero when kill-on-close terminates a job. Cancellation is never success.
    public bool Succeeded => ExitCode == 0 && StopReason == null;
}

/// <summary>
/// Owns a trusted Windows adapter process, not an agent shell. Creation and job assignment
/// are atomic; even a crash during startup closes the sole job handle and kills its members.
/// This is lifecycle ownership, not a replacement for the VM boundary.
/// </summary>
[SupportedOSPlatform("windows10.0")]
public sealed class WindowsJobProcess : IAsyncDisposable
{
    private readonly SafeJobHandle job;
    private readonly Process process;
    private readonly CancellationTokenSource lifetime;
    private readonly CancellationTokenRegistration cancellationRegistration;
    private readonly object disposeGate = new();
    private Task? disposal;
    private string? stopReason;

    public int Id { get; }
    public Stream Input { get; }
    public Stream Output { get; }
    public Stream Error { get; }
    public Task<OwnedProcessExit> Completion { get; }
    public string? StopReason => Volatile.Read(ref stopReason);

    private WindowsJobProcess(SafeJobHandle job, Process process, FileStream input, FileStream output,
        FileStream error, OwnedProcessRequest request, CancellationToken cancellation)
    {
        this.job = job; this.process = process; Id = process.Id; Input = input;
        Output = new LimitedReadStream(output, request.OutputLimit, () => Stop("output-limit"));
        Error = new LimitedReadStream(error, request.OutputLimit, () => Stop("output-limit"));
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        cancellationRegistration = lifetime.Token.Register(() => Stop(cancellation.IsCancellationRequested ? "cancelled" : "lifetime"));
        lifetime.CancelAfter(request.Lifetime);
        Completion = ObserveExit();
    }

    public static WindowsJobProcess Start(OwnedProcessRequest request, CancellationToken cancellation = default)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException("Owned worker launch requires Windows 10 or newer.");
        Validate(request); cancellation.ThrowIfCancellationRequested();
        using var input = Pipe.Create(parentReads: false);
        using var output = Pipe.Create(parentReads: true);
        using var error = Pipe.Create(parentReads: true);
        var job = Native.CreateJobObjectW(0, null);
        if (job.IsInvalid) { job.Dispose(); throw ErrorFor("create-job"); }
        Process? process = null; FileStream? stdin = null, stdout = null, stderr = null;
        WindowsJobProcess? owner = null;
        try
        {
            var limits = new Native.ExtendedLimit { Basic = new() { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE; no breakaway.
            if (!Native.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Native.ExtendedLimit>()))
                throw ErrorFor("configure-job");
            using var attributes = new Attributes();
            attributes.Add(0x2000D, [job.DangerousGetHandle()]); // JOB_LIST: assigned by CreateProcess itself.
            attributes.Add(0x20002, [input.Child.DangerousGetHandle(), output.Child.DangerousGetHandle(), error.Child.DangerousGetHandle()]);
            var startup = new Native.StartupInfoEx
            {
                Info = new() { Size = (uint)Marshal.SizeOf<Native.StartupInfoEx>(), Flags = 0x100,
                    Input = input.Child.DangerousGetHandle(), Output = output.Child.DangerousGetHandle(), Error = error.Child.DangerousGetHandle() },
                Attributes = attributes.Pointer
            };
            var environment = string.Join('\0', request.Environment.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key + "=" + p.Value)) + "\0\0";
            var environmentPointer = Marshal.StringToHGlobalUni(environment);
            Native.ProcessInfo info;
            try
            {
                var command = new StringBuilder(string.Join(' ', new[] { request.Executable }.Concat(request.Arguments).Select(Quote)));
                // Suspended only to establish managed stream/handle ownership before running. Job membership is already atomic.
                if (!Native.CreateProcessW(request.Executable, command, 0, 0, true,
                        0x08000000 | 0x00080000 | 0x00000400 | 0x00000004, environmentPointer,
                        request.WorkingDirectory, ref startup, out info)) throw ErrorFor("create-owned-process");
            }
            finally { Marshal.FreeHGlobal(environmentPointer); }
            using var processHandle = new SafeProcessHandle(info.Process, true);
            using var threadHandle = new SafeWaitHandle(info.Thread, true);
            if (!Native.IsProcessInJob(processHandle, job, out var isMember) || !isMember) throw ErrorFor("verify-job-membership");
            process = Process.GetProcessById((int)info.ProcessId);
            _ = process.Handle; // Cache a stable handle while the owned process is still suspended; never act on a recycled PID.
            stdin = input.TakeParent(FileAccess.Write); stdout = output.TakeParent(FileAccess.Read); stderr = error.TakeParent(FileAccess.Read);
            owner = new WindowsJobProcess(job, process, stdin, stdout, stderr, request, cancellation);
            if (Native.ResumeThread(threadHandle) == uint.MaxValue) throw ErrorFor("resume-owned-process");
            return owner;
        }
        catch
        {
            job.Dispose(); // The estate closes even if construction fails halfway through.
            if (owner != null) owner.DisposeAsync().AsTask().GetAwaiter().GetResult();
            else { stdin?.Dispose(); stdout?.Dispose(); stderr?.Dispose(); process?.Dispose(); }
            throw;
        }
    }

    public void Stop(string reason = "stopped")
    {
        Interlocked.CompareExchange(ref stopReason, reason, null);
        job.Dispose(); // An unnamed, non-inherited handle; closing it affects only this job's members.
    }

    private async Task<OwnedProcessExit> ObserveExit()
    {
        try { await process.WaitForExitAsync(); return new(process.ExitCode, StopReason); }
        finally { lifetime.CancelAfter(Timeout.InfiniteTimeSpan); job.Dispose(); } // Also reap descendants after a normal root exit.
    }

    public ValueTask DisposeAsync()
    {
        lock (disposeGate) return new ValueTask(disposal ??= DisposeCore());
    }

    private async Task DisposeCore()
    {
        Stop();
        try { await Completion; }
        finally
        {
            cancellationRegistration.Dispose(); lifetime.Dispose();
            Input.Dispose(); Output.Dispose(); Error.Dispose(); process.Dispose();
        }
    }

    private static void Validate(OwnedProcessRequest request)
    {
        if (!Path.IsPathFullyQualified(request.Executable) || !Path.IsPathFullyQualified(request.WorkingDirectory) ||
            !request.Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            request.Lifetime <= TimeSpan.Zero || request.Lifetime > TimeSpan.FromMinutes(15) ||
            request.OutputLimit is < 1 or > 2_000_000 || request.Arguments.Count > 256 || request.Environment.Count > 64)
            throw new ArgumentException("Owned process paths or limits are invalid.");
        if (new[] { request.Executable, request.WorkingDirectory }.Concat(request.Arguments).Any(s => s.Contains('\0')) ||
            string.Join(' ', new[] { request.Executable }.Concat(request.Arguments).Select(Quote)).Length >= 32767 ||
            request.Environment.Any(p => string.IsNullOrEmpty(p.Key) || p.Key.Contains('=') || p.Key.Contains('\0') || p.Value.Contains('\0')) ||
            request.Environment.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Environment.Count ||
            request.Environment.Sum(p => (long)p.Key.Length + p.Value.Length + 2) > 32760)
            throw new ArgumentException("Owned process arguments or explicit environment are invalid.");
    }

    private static string Quote(string value)
    {
        // Windows argv rules, not shell escaping. Quotes and trailing backslashes must survive literally.
        var result = new StringBuilder("\""); var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); result.Append(c); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static Win32Exception ErrorFor(string operation) => new(Marshal.GetLastWin32Error(), "Owned process operation failed: " + operation);

    private sealed class Pipe(SafeFileHandle parent, SafeFileHandle child) : IDisposable
    {
        private SafeFileHandle? parent = parent;
        public SafeFileHandle Child { get; } = child;
        public static Pipe Create(bool parentReads)
        {
            var security = new Native.SecurityAttributes { Size = Marshal.SizeOf<Native.SecurityAttributes>(), Inherit = true };
            if (!Native.CreatePipe(out var read, out var write, ref security, 0)) throw ErrorFor("create-pipe");
            var pipe = new Pipe(parentReads ? read : write, parentReads ? write : read);
            if (!Native.SetHandleInformation(pipe.parent!, 1, 0)) { pipe.Dispose(); throw ErrorFor("protect-parent-pipe"); }
            return pipe;
        }
        public FileStream TakeParent(FileAccess access)
        {
            var stream = new FileStream(parent!, access, 4096, isAsync: false);
            parent = null; return stream;
        }
        public void Dispose() { parent?.Dispose(); Child.Dispose(); }
    }

    private sealed class Attributes : IDisposable
    {
        public nint Pointer { get; }
        private readonly List<nint> values = [];
        public Attributes()
        {
            nuint bytes = 0; Native.InitializeProcThreadAttributeList(0, 2, 0, ref bytes);
            if (bytes == 0) throw ErrorFor("size-attributes");
            Pointer = Marshal.AllocHGlobal(checked((int)bytes));
            if (!Native.InitializeProcThreadAttributeList(Pointer, 2, 0, ref bytes))
            { Marshal.FreeHGlobal(Pointer); throw ErrorFor("initialize-attributes"); }
        }
        public void Add(nuint key, nint[] handles)
        {
            var value = Marshal.AllocHGlobal(handles.Length * nint.Size); values.Add(value);
            Marshal.Copy(handles, 0, value, handles.Length);
            if (!Native.UpdateProcThreadAttribute(Pointer, 0, key, value, (nuint)(handles.Length * nint.Size), 0, 0)) throw ErrorFor("set-attribute");
        }
        public void Dispose() { Native.DeleteProcThreadAttributeList(Pointer); foreach (var value in values) Marshal.FreeHGlobal(value); Marshal.FreeHGlobal(Pointer); }
    }

    private sealed class SafeJobHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle() => Native.CloseHandle(handle);
    }

    private sealed class LimitedReadStream(Stream inner, int limit, Action exceeded) : Stream
    {
        private long read;
        private int Count(int count)
        {
            if (Interlocked.Add(ref read, count) > limit) { exceeded(); throw new IOException("Owned process output exceeded its bound."); }
            return count;
        }
        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Count(await inner.ReadAsync(buffer, cancellationToken));
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct SecurityAttributes { internal int Size; internal nint Descriptor; [MarshalAs(UnmanagedType.Bool)] internal bool Inherit; }
        [StructLayout(LayoutKind.Sequential)] internal struct StartupInfo
        {
            internal uint Size; internal nint Reserved, Desktop, Title;
            internal uint X, Y, Width, Height, XChars, YChars, Fill, Flags;
            internal ushort Show, ReservedBytes; internal nint ReservedPointer, Input, Output, Error;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct StartupInfoEx { internal StartupInfo Info; internal nint Attributes; }
        [StructLayout(LayoutKind.Sequential)] internal struct ProcessInfo { internal nint Process, Thread; internal uint ProcessId, ThreadId; }
        [StructLayout(LayoutKind.Sequential)] internal struct BasicLimit
        {
            internal long ProcessTime, JobTime; internal uint Flags; internal nuint MinimumWorkingSet, MaximumWorkingSet;
            internal uint ActiveProcessLimit; internal nuint Affinity; internal uint Priority, Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct IoCounters { internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] internal struct ExtendedLimit
        {
            internal BasicLimit Basic; internal IoCounters Io; internal nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeJobHandle CreateJobObjectW(nint attributes, string? name);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetInformationJobObject(SafeJobHandle job, int informationClass, ref ExtendedLimit information, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsProcessInJob(SafeProcessHandle process, SafeJobHandle job, [MarshalAs(UnmanagedType.Bool)] out bool result);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes attributes, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool InitializeProcThreadAttributeList(nint list, uint count, uint flags, ref nuint size);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, nint value, nuint size, nint previous, nint returnSize);
        [DllImport("kernel32.dll")] internal static extern void DeleteProcThreadAttributeList(nint list);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcessW(string application, StringBuilder command, nint processAttributes, nint threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment, string directory, ref StartupInfoEx startup, out ProcessInfo information);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint ResumeThread(SafeWaitHandle thread);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(nint handle);
    }
}
