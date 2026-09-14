# Worker distribution preparation

The host/browser notice bundle does not cover the separately packaged execution
worker. The current combined ZIP remains a private development artifact. Before
shipping a worker to other users, identify the included components, preserve
their notices, and supply the applicable corresponding sources and build
materials. A package version or upstream homepage alone does not establish that
the exact distributed binary has matching source material.

## Inspect the existing inputs without another image build

On the Windows development machine:

```powershell
node scripts/worker-notices.mjs artifacts/guest-shutdown-20260913/installation.json FRESH-NAME
if ($LASTEXITCODE -ne 0) { throw 'Worker notice inspection failed; inspect its receipt.' }
```

This reads the explicit pinned installation, then uses the already cached
diagnostic image to run Python and `debugfs`. The container has no network,
capabilities or writable root; only its fresh small output directory is writable.
The guest disk is attached as a read-only file. It is neither mounted as a
filesystem nor booted, and none of its programs run. The operation does not
install Docker, pull an image, start a worker VM, change the running application,
or call a model. Docker Desktop must already be healthy.

The tool budgets 64 MiB plus the standard 10 GiB free-space reserve. It hashes
the actual guest disk before and after reading metadata, checks all declared
Windows QEMU runtime files and the kernel/initrd pins, and preserves its source
hashes. The runtime's exact file inventory must match its manifest.

Within the guest it reads the installed dpkg database, exact binary/source
package versions, package copyright files and shared license texts. It also
walks OpenClaw's installed npm graph, its extension packages and conventional
global npm directory, collecting package metadata and root notice files.
Duplicate package inodes and repeated text bytes are deduplicated. Guest
symlinks resolve only within the image; traversal escapes, loops, ambiguous
records and oversized metadata stop inspection.

Output is under `artifacts/worker-notices-FRESH-NAME`:

- `inventory.json`: input identities and summary for guest, runtime and boot files.
- `guest/inventory.json`: installed package records, original metadata/text
  references, missing-notice findings and explicit coverage limits.
- `guest/texts` and `runtime`: content-addressed original bytes.
- `receipt.json`: exact commands, parser-test results, inspection outcome and
  container cleanup.

`inspectionPassed` means these bounded reads and integrity checks succeeded.
`redistributionComplete` remains false. Finding a file named LICENSE does not
prove that all applicable terms, copyright statements or embedded dependencies
are covered. No package is assigned invented terms when its notice is missing.
Source archive availability and equivalence are not checked by this inspector.

The owned container is removed after exit, including after a failed collection.
The compact evidence stays. There is no filesystem export, disk copy, overlay,
SDK build or application package to clean up. Failed inspection remains failed;
partial text collection cannot be promoted to a completed release bundle.

## September 14 inspection

`artifacts/worker-notices-20260914-b` passed against the existing shutdown-B
worker disk and the 3,389-file Windows QEMU runtime. Eight parser tests passed.
The disk's SHA-256 matched before and after inspection; kernel and initrd pins
also matched. The diagnostic container was removed, and the running notices-B
host and Luna bridge retained their process identities.

The collected graph contains 577 dpkg packages and 1,225 npm package instances,
including OpenClaw 2026.9.4. Its 2,289 distinct metadata/notice files total
8,114,103 bytes. These counts include package metadata, not just license text.
There are 155 findings requiring review:

| Finding | Count | Meaning |
|---|---:|---|
| Installed copyright file missing | 3 | Docker CLI, Buildx and Compose packages lack the expected dpkg copyright path. |
| No root license/notice text found | 120 | Includes OpenClaw itself; text may need locating elsewhere or obtaining from pinned upstream sources. |
| Module directory without package.json | 28 | Identity/provenance requires inspection; do not invent a package identity. |
| Dangling module link | 4 | A workspace target was omitted from this distributed layout; review its relationship to bundled output. |

These findings are not a count of legal violations or product runtime defects.
Conversely, a package absent from this list is not certified for redistribution.
The inspector conservatively records what it found and what it could not find.

The first attempt, `worker-notices-20260914-a`, stopped on a dangling upstream
workspace link. Its failed receipt and cleanup remain recorded. The collector
now records absent link targets explicitly; link loops and escapes still fail.
The rerun reused every large input and produced no image or application copy.
An additional tiny wrong-pin fixture was rejected before filesystem inspection;
the labeled failure-cleanup path removed its owned container and the disposable
fixture file was removed. Its negative-control proof is
`artifacts/worker-notice-rejection-20260914/verified.json`.

## Remaining preparation

1. Resolve every reported missing notice against the exact installed package.
   Capture reviewed upstream text with immutable provenance when the package
   omits it. Preserve Debian-format common-license references alongside texts;
   their convention is documented in
   [Debian Policy](https://www.debian.org/doc/debian-policy/ch-docs.html#copyright-information).
2. Account separately for standalone Node, embedded/minified code, Python, Go,
   Java and other payloads outside the inspected dpkg/npm graphs. The current
   inherited base includes development tools, so copying only OpenClaw's notice
   is insufficient. A deliberately smaller future base needs fresh execution
   qualification; do not silently replace the QA worker.
3. Obtain a complete mapping for the Windows QEMU vendor tree and firmware,
   including its library versions, original notices and relevant source/build
   materials. The [vendor's build page](https://qemu.weilnetz.de/w64/) identifies
   an MSYS2-based build whose dependency packages are updated for new builds;
   that page is not an exact dependency manifest for our pinned archive.
4. Cover the Alpine kernel and initrd separately, as well as any future native
   Linux or Mac runtime. The kernel/initrd hashes are verified here, but their
   source and notice coverage is still unverified.
5. Assemble and independently verify a worker-specific notice/source deliverable
   bound to the final worker manifest. Keep these requirements separate from
   publisher signing, host installation and sandbox security qualification.

QEMU's [license overview](https://www.qemu.org/docs/master/about/license.html)
describes the project-level licensing. It does not replace reviewing the
licenses of the particular libraries and firmware in a distributed bundle.
