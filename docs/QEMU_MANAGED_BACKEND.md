# QEMU backend and authenticated transport

`QemuSandboxBackend` implements the shared `ISandboxBackend` in Infrastructure.
NativeCheck can now instantiate that implementation directly: the .NET process
owns QEMU, the disk overlay, the two authenticated channels and broker forwarding.
The earlier Node-owned VM fixture remains a separate development control.

This is an explicit Windows development backend, not an automatic replacement
for Docker Sandboxes or an enabled production research worker. The main app still
uses its existing adapter and data. All six delivery gates remain open.

## Host and guest boundary

Before launching anything, the adapter verifies SHA-256 pins for QEMU, qemu-img,
the kernel, initrd and base disk. Read handles prevent those files from changing
while this backend owns them. The full dependency/signing/update chain remains
unqualified; an executable digest is not a release trust chain.

The adapter records ownership and uncertain creation/boot/stop states before
dispatch. One active worker is permitted across the host registry. Worker IDs,
image identity and the task broker binding must match their persisted records.
Each worker gets a private directory and writable qcow2 overlay; the base is
attached read-only. Normal stop/start reuses that overlay. Uncertain or interrupted
states refuse automatic execution and need reconciliation, which is still a
separate implementation requirement. Removal requires a stopped, unlocked overlay
and retains host-side evidence.

QEMU uses explicit WHPX, two CPUs and 4 GiB for this integration, no default
devices, no guest NIC, no host filesystem shares and no GPU. The
[Windows process owner](WORKER_PROCESS_OWNERSHIP.md) creates QEMU and qemu-img
inside kill-on-close jobs, with explicit environment variables and bounded
lifetimes. QMP stays on inherited stdio; the guest cannot access that channel.

Control and console use separate mutually authenticated TLS character-device
connections over host loopback. Each boot creates new, channel-specific CA and
client/server identities. The host accepts only the exact issued client
certificate, verifies its chain and purpose, and rejects absent certificates.
QEMU verifies the private CA and the host's loopback IP certificate. Connections
allow TLS 1.2/1.3, disable resumption/renegotiation, and bound authentication time
and attempts. These TLS sockets are not guest network interfaces.

Only the client certificate/key and public CA are exported for QEMU, inside a
private directory. Windows grants directory access to the current user and SYSTEM;
Unix directory support uses owner-only permissions. Windows Schannel cannot use
the original ephemeral server key: this was observed and fixed using a separate,
non-exportable key in the current user's CNG key store. Its generated ownership
name is recorded before creation and it is deleted during normal disposal.
The cleanup helper can retire an abandoned key after its owner is known stopped;
automatic host-crash reconciliation is not wired yet. No certificate is installed
in system trust and no master credential or channel key enters the guest.

## Requests and receipts

The guest sends bounded, newline-framed JSON over its virtual serial device.
The host limits frames, command concurrency, broker concurrency, request IDs,
request/response bodies, headers, diagnostic output and worker lifetime. An
interrupted guest command terminates the owned worker and remains uncertain;
it is never replayed automatically. Host-side parser and proxy limits are
implemented, but adversarial resource qualification remains open.

The only forwarded paths are the current task's MCP and model endpoints on a
configured loopback broker port. Guest data cannot choose the destination origin.
The proxy rejects other routes, ignores unrelated headers, and uses no ambient
proxy, cookies or redirect following. Existing broker authorization, permission
checks, model admission and durable receipts remain authoritative.

Graceful stop requires both a guest-originated QMP shutdown event and a successful
owned-process completion record. Cancellation with OS exit code zero cannot pass.
The registry and private termination files retain the observed PID, exit outcome,
guest-shutdown flag and rejected-connection counts. Boot observations retain QMP
device/resource data and the negotiated protocol/certificate identities. Worker
greetings are identified separately as guest observations.

## Reproduction and evidence

Use the pinned input/disk preparation in [Native VM](NATIVE_VM.md), then:

```powershell
dotnet build tools/Thaddeus.NativeCheck --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Native fixture build failed' }
node scripts/qemu-managed-integration-check.mjs artifacts/qemu-inputs-script-check artifacts/qemu-worker-20260912-native scripted-web
if ($LASTEXITCODE -ne 0) { throw 'Managed VM integration failed' }
```

`artifacts/qemu-managed-scripted-web-1789258395443` passed through the actual
Infrastructure backend. The pinned OpenClaw engine consumed the selected note and
full broker-retrieved public source, asked a durable question, resumed after a
complete VM restart and produced an independently checked approved import. Five
scripted model responses were used; this is not inference or benchmark evidence.

QEMU PIDs 50748 and 42488 negotiated TLS 1.3 for both channels with four distinct
client certificates. Both termination receipts contain guest shutdown, exit code
zero and no forced-stop reason. The base disk remained at SHA-256
`7bfb7ec84f87c9fd29d07c6c813ef12147535d588f0a5e64881a1342e4110351`.
No client-key files or QEMU processes remained after cleanup.

The observed negative cases denied direct public TCP, the host app's loopback
port, product/controller proxy routes, an absent task grant and unauthenticated
access to the fixture command endpoint. Three additional TLS tests verify exact
encrypted byte transfer, refusal of missing/other-boot client certificates,
private-key cleanup and the Windows server key's non-exportable policy. They run
on Windows and Linux CI. These checks do not establish macOS/Linux VM hosting,
exhaustive escape resistance, production admission or physical-phone readiness.

Sources: [QEMU TLS](https://www.qemu.org/docs/master/system/tls.html),
[character-device and TLS options](https://www.qemu.org/docs/master/system/invocation.html),
[.NET server authentication](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslserverauthenticationoptions?view=net-10.0),
[Windows key creation](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cngkey.create?view=net-10.0).
