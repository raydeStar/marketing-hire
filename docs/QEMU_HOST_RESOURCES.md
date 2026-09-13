# Host resource limits for the Windows VM adapter

The managed QEMU worker now has an OS-enforced resource boundary for its Windows
process, in addition to the guest's CPU/RAM configuration. This addresses host
resource consumption; it does not turn a Windows job into a filesystem/network
sandbox or qualify arbitrary host execution.

Before creating QEMU, `WindowsJobProcess` configures and independently queries:

- A job-wide committed-memory cap of guest RAM plus 1 GiB: 5 GiB for the current
  4 GiB guest. This limits committed virtual memory, not only resident RAM.
- A hard CPU rate of `ceil(10000 * guest CPUs / host logical processors)`, clamped
  to Windows' supported 1–10,000 units. On the validated 32-processor host, two
  guest CPUs produce 625 units, or 6.25% of total host CPU capacity. An enclosing
  job can impose a tighter effective quota.
- One active Windows process. Guest processes remain inside that single VM;
  QEMU cannot launch an additional host process under this job.

Configuration/query failures or mismatched limits prevent launch. Assignment is
still atomic during process creation, before the initially suspended process can
execute. Kill-on-close, explicit environment/handles and lifetime/output limits
remain in effect. Trusted infrastructure requests without an explicit resource
policy retain their existing behavior; every managed QEMU boot supplies one.

Boot observations and normal shutdown receipts include queried job limits and
Windows' reported peak job commit counter. The shutdown snapshot is taken before
shutdown, so it is not a final post-exit peak measurement. Missing fields in old
receipts remain null. No old task or campaign is rewritten.

## Verification on September 13

Source `af15ab3` passed a fresh native public-source workflow at
`artifacts/qemu-managed-scripted-web-1789307315188`: real pinned OpenClaw/QEMU,
selected note and scoped public retrieval, durable question, complete VM shutdown,
new process on the retained overlay, continuation and exact approved import.
Both VM processes reported the same enforced 5 GiB/625-unit/one-process limits at
boot and before shutdown. Their reported peak job commit snapshots were
4,605,022,208 and 4,632,858,624 bytes. Both terminated normally; no VM or temporary
host remained. All five pinned VM inputs stayed unchanged and an independent
host-file hash matched the approval content.

The workflow used five synthetic responses / 650 synthetic tokens, zero live
inference and no GPU. It establishes compatibility with this bounded native
workflow, not a model-quality or performance improvement. Its existing direct
network, host-route and absent-authorization negatives also passed.

`Thaddeus.ProcessCheck` now has 16 real Windows checks. Its six added resource
cases cover a bounded memory control, allocation refusal, process-count positive
and negative controls, queried CPU hard-cap behavior and invalid-limit refusal.
The successful local receipt is
`artifacts/qemu-host-resource-20260913/process-check-c/ownership-receipt.json`.
The 256 MiB memory fixture committed 224 MiB of test allocations and then received
Windows error 1455; its independently read process private memory was 246,312,960
bytes. The uncapped control stopped deliberately at 384 MiB without refusal.
The one-process fixture refused its child with error 1816; the two-process control
started that same child. A six-second CPU fixture recorded 3,390.625 ms of CPU
against a nominal 3,207.322 ms allowance plus a declared 1,200 ms scheduling
tolerance. This observation is not a throughput benchmark.

The first two fixture runs are retained as failed verifier attempts. Windows'
peak job counter included the rejected commit attempt, reporting 280,727,552
bytes in the successful fixture even though current private memory stayed below
the 268,435,456-byte cap. The verifier therefore checks actual allocation refusal
and independently observed current private memory; it does not mistake an attempted
commit peak for successfully allocated memory. Raw observations remain available.

All 359 backend tests passed. The Windows CI ownership job also runs the new
resource fixtures; native WHPX evidence remains a separate local check.
Release package integrity, transport abuse/resource qualification, broader crash
coverage, production admission and macOS/Linux hosting remain open. The main
personal development instance was not restarted or changed for this check.

References: [Windows job memory limits](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_extended_limit_information),
[CPU hard caps](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_cpu_rate_control_information),
[active-process and kill-on-close flags](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_limit_information),
[querying applied job limits](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-queryinformationjobobject).
