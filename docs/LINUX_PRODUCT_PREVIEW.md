# Linux packaged worker preview

The Linux x64 host now contains the systemd service supervisor and composes the
QEMU worker through the ordinary application factory. OpenClaw remains inside
the VM; the product, credentials, model admission and import approvals remain
outside it. This is an explicitly configured development preview. Default
installations still open ordinary chat without starting a worker.

## Host setup

Run the self-contained `Thaddeus.Host` through `start-thaddeus.sh`. The Linux
worker requires that actual application executable; a `dotnet` or test runner
cannot stand in for the supervisor. Its private `--linux-supervise` entry point
handles only the owned service request and exits before application startup.
Malformed service requests cannot fall through to opening a study or listener.

The configured installation uses the existing `kind: qemu` JSON with pinned
executable, image tool, kernel, initrd, base disk and full runtime manifest.
Linux requires the entire QEMU runtime and all boot inputs on read-only
filesystem storage. The development bundle and its trust limitations are
described in [Linux QEMU sessions](LINUX_QEMU_SESSION.md).

The current tested prerequisites are native Linux x64, accessible KVM, a systemd
255 user session and delegated cgroup v2 CPU, memory and process controllers.
The backend verifies actual resource values on each service start. It does not
downgrade missing controls, use software emulation, or start an unrestricted host
process. Installing these prerequisites for a nontechnical user is unfinished.

Set `developmentWorkerInstallation` in the portable launch profile to the
absolute installation JSON path, with a separate loopback `workerPort`. Then
open **Settings → Set up this host**, check the installed worker and explicitly
enable it. Configuration changes invalidate enrollment; the browser cannot choose
an arbitrary executable. See [host setup](HOST_SETUP_PREVIEW.md) for the shared
approval and expiration rules. Windows configuration digests remain compatible.

Linux registers the backend as `qemu-kvm`. macOS and Linux Arm workers remain
unavailable. A phone connects to a supported host; it does not run this worker.

## Native product check

```text
node scripts/linux-product-check.mjs FRESH-NAME
```

This explicit local check cross-publishes the captured Linux x64 host and fresh
PWA on the development computer, then executes that package in a disposable
Ubuntu 24.04/systemd VM. A read-only tools disk carries the package and Python
test driver, separate from the previously verified immutable runtime and worker
assets. An optional second argument reuses the verified base of an earlier
`linux-product-*` check; changed application and fixture files are still captured
and rebuilt. This is native execution evidence, not a claim of native compilation,
release signing or user acceptance on an independently installed Linux machine.

The test driver uses the product's authenticated HTTP API and a separate
loopback provider. It does not replace application services or the research
factory. It checks setup refusal before enrollment, actual VM resources, a
durable question across application restart, one rejected source quotation and
bounded correction, exact approved import, usage accounting and reviewed private
workspace removal. The deliberate malformed service and approval requests are
negative controls; no personal study is involved.

All six model replies are synthetic protocol fixtures, accounting for 780
synthetic tokens. The provider uses the development Luna High endpoint contract
inside the disposable VM; it never connects to Luna, another paid model, the
host's model bridge or a GPU. Both VMs have no network interface. Public research
and model quality are outside this fixture's evidence; the existing Windows
public-search workflow has its own receipts.

The outer VM uses two vCPUs and 6 GiB RAM, with a two-CPU/7-GiB container cap and
a bounded shutdown deadline. The actual worker requests two vCPUs and 4 GiB RAM;
its observed host service must enforce 5 GiB memory, zero swap, 200% CPU quota
and 128 processes. Only test-owned processes and fixture containers are stopped.
All disks, logs, source captures and failed attempts remain under `artifacts/`.

Consumer provisioning, publisher trust, signed installation/updates, macOS/Arm
worker qualification, final UI acceptance and physical-phone verification remain
open. The existing active-worker crash limits also remain; this workflow restarts
the app only after the product has confirmed a stopped worker.

## Evidence, September 13

All six native product checks passed in
`artifacts/linux-product-packaged-20260913-b/verified.json`. The captured package
used the actual application supervisor and research factory. Its full private
request/export evidence is retained in the diagnostic disk, with check summaries
in the outer console log. The first attempt is retained: its synthetic HTTP
server failed to read .NET's chunked request body. The corrected test server
passed without changing the application for that failure.

The same application code passed 639 backend tests, seven protocol tests and
the web build in `artifacts/local-check-linux-product-20260913/verified.json`.
A subsequent platform-neutral setup sentence is a web-only change; the native
workflow receipt retains its exact earlier PWA source. This is not final UI or
physical-device acceptance.
