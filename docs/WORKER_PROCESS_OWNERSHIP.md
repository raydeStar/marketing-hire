# Windows worker process ownership

`WindowsJobProcess` is the host infrastructure primitive for the next QEMU backend.
It starts a trusted adapter executable, never a model-selected host command.
It is used by the [managed QEMU backend](QEMU_MANAGED_BACKEND.md) in NativeCheck,
and remains outside product admission.
Docker remains the configured adapter and its qualification gate remains open.

The process joins an unnamed Windows job **during CreateProcess**, using the
`PROC_THREAD_ATTRIBUTE_JOB_LIST` creation attribute. The job has kill-on-close
enabled, no breakaway flag, and one non-inherited owner handle. This avoids the
orphan window between starting a process and assigning it to a job. The initial
thread stays suspended until managed stream and process handles are established.
Any construction failure closes the job. A normal root exit also closes it,
terminating remaining descendants.

Only three child pipe handles are inherited. Arguments use Windows argv quoting,
without a shell; the executable and working directory must be absolute. The
environment is supplied explicitly instead of copying host credentials/settings.
The owner supplies a bounded lifetime, and each output stream enforces a cumulative
byte limit when drained. Undrained output blocks on the pipe; callers must drain
stdout and stderr concurrently. Lifetime or cancellation closes the job even if
the caller is blocked on pipe I/O.

The completion record keeps the stop reason separate from the OS exit code:
Windows job termination was observed to return zero in these checks. A cancelled,
expired or output-limited process cannot report `Succeeded`, even with exit code
zero. An owner crash still requires durable runtime reconciliation; it cannot
produce an in-process completion receipt.

## Verification

The standalone check uses real processes and retained handles, with no inference:

```powershell
dotnet run --project tools/Thaddeus.ProcessCheck -- artifacts/process-check-FRESH
```

An optional second argument tests the pinned QEMU executable prepared by
`scripts/prepare-qemu-probe.mjs`:

```powershell
dotnet run --project tools/Thaddeus.ProcessCheck -- artifacts/process-qemu-FRESH artifacts/qemu-inputs-script-check/qemu/qemu-system-x86_64.exe
```

Ten helper checks cover literal arguments/Unicode/stdin/stdout/stderr, explicit
environment, refusal to inherit an unrelated inheritable event handle, cancelled
startup, missing executable, descendant cleanup on normal exit/cancellation/
deadline/disposal, output overflow and abrupt owner death. The optional eleventh
check initializes QEMU/WHPX with one CPU, 512 MiB, no NIC and QMP over inherited
stdio, then terminates **only its owner**. The worker must stop while an unrelated
control process remains alive. The probe uses no guest disk, GPU, model or app data.
Both QEMU and helper checks ran successfully on this Windows host. The Windows CI
job runs the ten helper checks; it does not claim a hypervisor test.

This qualifies a Windows lifecycle mechanism only. It does not establish a
production sandbox, complete release package integrity, authenticated guest
transport, guest resource enforcement, durable recovery, or macOS/Linux ownership.
Those remain separate backend and delivery requirements. Job objects are process
ownership, not a security boundary around arbitrary host-side agent execution.

Sources: [Microsoft process creation attributes](https://learn.microsoft.com/en-us/windows/desktop/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute),
[job objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects),
[CreateProcess](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw).
