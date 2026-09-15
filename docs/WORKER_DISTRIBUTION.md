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

## Assemble an offline guest reference bundle

The checked-in [worker supplement catalog](../third-party/worker/README.md)
adds reviewed upstream text to the frozen installed notices without another
download, Docker operation, worker boot or application build. Python 3.11 or
newer is required. From the repository root, choose a fresh output directory:

```powershell
python -B -m unittest discover -s tools/worker-notices -p test_assemble.py
if ($LASTEXITCODE -ne 0) { throw 'Notice assembly checks failed.' }
python -B tools/worker-notices/assemble.py artifacts/worker-notices-20260914-d/guest/inventory.json third-party/worker/catalog.json artifacts/worker-notice-bundle-FRESH-NAME
if ($LASTEXITCODE -ne 0) { throw 'Notice assembly failed.' }
```

The assembler verifies the inventory/catalog pins, every captured input hash and
byte count, npm metadata identities and declarations, and each supplement's exact
installed path and metadata hash. It refuses duplicate bindings, changed text,
path traversal, reparse/linked inputs and existing output. It bounds input/output
bytes and retains the 10 GiB disk reserve. A write failure removes only files and
directories created by that invocation; prior inputs and neighboring files stay.

The output includes `THIRD-PARTY-NOTICES.md`, `bundle.json` and original files
under `texts/`. Every local notice link is included. Metadata hashes identify
the original evidence without dangling links to uncopied package metadata.
Provenance and additional review requirements remain in the manifest. Original
inventory findings are retained even when upstream text has now been supplied.

The September 14 bundle at `artifacts/worker-notice-bundle-20260914-a` covers the
1,802 inspected dpkg/npm entries. It contains 1,081 distinct notice/common-license
texts totaling 4,393,154 bytes. The catalog supplements 86 package instances,
including 60 OpenClaw entries whose manifests match the OCI-declared revision
byte for byte. All 155 original findings remain; 69 have no matching supplement.
These are counts of located text, not remaining legal obligations. Full native,
transitive and corresponding-source coverage is still unverified, including for
entries with a supplement. QEMU/runtime and boot notices are outside this bundle.

Twelve focused assembly tests passed, including an actual Windows junction,
modified metadata/text, changed pins, existing-output preservation, low-space
refusal and injected write-failure cleanup. An independent verifier checked
output hashes, all notice links, source-to-output mappings and retained findings.
The active QA application and worker inputs were not repackaged or replaced.

## Corrected notice detection and current bundle

The current catalog pins `artifacts/worker-notices-20260914-d/guest/inventory.json`.
The updated scanner also recognizes names such as `MIT-License.txt` and
`THIRD-PARTY-LICENSE`. The same immutable disk contains two additional texts:
Panzoom's MIT notice and Rolldown's third-party notice. No package identity,
metadata or previously collected text changed. Panzoom's earlier missing-file
finding is corrected; the original inspection and its 155 findings remain as
historical evidence. The new inspection reports 154 findings.

Six original READMEs from previously checked npm archives contain complete MIT
notices: tokenizer/token, agent-base, data-uri-to-buffer, fastdom,
https-proxy-agent and lru_map. The catalog preserves the entire unchanged files,
including attribution and disclaimer, bound to their exact installed metadata,
locked archive integrity and retained acquisition receipts. No new archive was
downloaded. This adds located text, not binary/source-equivalence certification.

That checkpoint's bundle is `artifacts/worker-notice-bundle-20260914-b`: 92 supplemented
package instances, 1,089 texts totaling 4,427,132 bytes, and 62 findings without
supplements. All 154 current findings remain explicit. Independent verification
checks the complete bundle, links, source bindings and inventory reconciliation.
Ten parser checks pass, and the read-only inspection also verifies the unchanged
disk. The unchanged assembler reuses its prior twelve-test evidence. Its new
catalog was assembled and the resulting bytes were independently checked.

The first corrected inspection, `worker-notices-20260914-c`, stopped before
reading the disk: unrestricted test discovery also selected the separate
assembler's temporary-file tests inside the read-only diagnostic container.
The runner now explicitly selects `test_collect.py`; assembler checks remain a
separate local command. Both owned containers were removed. No guest boot,
model call, image copy or application rebuild occurred. Reconciliation evidence
is `artifacts/worker-notice-detection-20260914-a/reconciliation.json`.

## Legacy archive recovery and current bundle

The current bundle is `artifacts/worker-notice-bundle-20260914-c`, with 94
supplemented package instances, 1,091 texts totaling 4,433,168 bytes, and 60
findings without supplements. All 154 original findings remain explicit. It
adds the full, unchanged MIT notices embedded in the original `isarray@1.0.0`
and `strictdom@1.0.1` READMEs; all earlier bindings/texts remain verified.

The initial acquisition helper had rejected five old npm archives because their
registry metadata lacked `unpackedSize`. The checked-in collector now accepts
that absence with a fixed 64 MiB unpacked limit, plus separate entry/file/capture
limits. It streams tar members without extracting archive paths or executing
package code, captures only bounded original notice/metadata/README bytes, and
refuses existing output. Seven focused tests cover missing size, oversize data,
traversal, duplicate paths, links and preservation of existing evidence.

`artifacts/worker-notice-legacy-20260914-a` retains the five fresh acquisition
receipts, with each archive matching the frozen pnpm integrity and the earlier
failed-inspection hash. All five package manifests match installed bytes and
all five archives were removed after inspection. Three packages yielded no
complete notice candidate and remain unresolved. No image, application build,
container, worker or model was started. The earlier failures remain historical.

A separate Standard Webhooks binding attempt stopped on a version mismatch:
the registry-declared source commit's library manifest says 1.3.0, while the
installed package says 1.0.0. Its library-level MIT notice was located, but this
attempt adds no supplement and the finding stays open.

Independent verification checks all 1,938 local notice links, exact output and
source bytes, recovered archive bindings, unchanged inventory and assembler
inputs, and absence of temporary archives. The prior twelve assembler tests
remain applicable after source-hash comparison. This remains a reference
bundle, with distribution/source completeness explicitly false.

## Remaining preparation

1. Resolve the remaining reported findings against the exact installed package.
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
5. Extend the guest reference bundle into a complete worker notice/source deliverable
   bound to the final worker manifest. Keep these requirements separate from
   publisher signing, host installation and sandbox security qualification.

QEMU's [license overview](https://www.qemu.org/docs/master/about/license.html)
describes the project-level licensing. It does not replace reviewing the
licenses of the particular libraries and firmware in a distributed bundle.
