# Worker guest notice supplements

This catalog preserves upstream notice texts missing from the frozen guest
inventory. It is an input to an offline review bundle, not a completed worker
redistribution package or a license for Thaddeus's private application code.

Each binding identifies one installed npm package by name, version, guest path,
metadata SHA-256 and original license declaration. Notice bytes are named by
SHA-256 and remain unchanged under `.gitattributes`. The catalog also pins the
entire guest inventory and disk identity. A newer worker needs a reviewed catalog;
the assembler does not download new texts or substitute matching version names.

The initial catalog contained 86 package-instance bindings and 26 distinct texts:

- Sixty OpenClaw root/extension manifests match the source revision declared by
  the pinned OCI image byte for byte. That revision is
  `3a9d69db306cd7f081e06254cb89c4bcc14a7107`. The npm release with the same
  version declares a different revision, so its package cannot establish the
  installed image's source identity. Root project notices and their stated
  third-party exceptions are retained; matching manifests do not prove every
  compiled file is equivalent to source.
- Twenty-six other package instances use source revisions declared by npm
  `gitHead` or registry provenance. For the latter, the attestation subject
  matches the locked archive integrity, but signatures were not verified. The
  catalog states this limitation. Retrieved Git text bytes were checked against
  the returned blob identifiers.
- MPL declarations include the full MPL 2.0 text from the frozen guest's common
  licenses. This does not establish corresponding-source availability.
- Codex supplements include its Apache text and NOTICE, plus the MIT text for
  Ratatui 0.30.2 from an archive matching its declared source's Cargo.lock checksum.
  Coverage of other embedded/native dependencies remains separate.

`standardwebhooks@1.0.0` remains unresolved. Its declared source revision has an
MIT text under `libraries`, while the repository root uses Apache 2.0. The
library's source manifest reports version 1.3.0, however, whereas the installed
archive reports 1.0.0. That comparison is retained as a failed binding attempt;
neither text is silently substituted as a verified supplement for this package.

The current catalog has 102 bindings and 36 distinct texts. Six earlier full
READMEs preserve their embedded MIT notice and attribution without rewriting:
`@tokenizer/token`, `agent-base`, `data-uri-to-buffer`, `fastdom`,
`https-proxy-agent` and `lru_map`. Their versions and archive hashes are explicit
in the catalog. Existing acquisition receipts verify the locked archive integrity
and exact installed package metadata; that earlier update downloaded no archive. These
are notice supplements, not a claim about all compiled or embedded dependencies.

The catalog now pins the corrected `worker-notices-20260914-d` inventory of the
same unchanged disk. Its scanner also locates `MIT-License.txt` and
`THIRD-PARTY-LICENSE`, adding installed Panzoom and Rolldown texts. The previous
inventory, catalog and findings are preserved in local evidence. See the
[inventory reconciliation](../../docs/WORKER_DISTRIBUTION.md#corrected-notice-detection-and-current-bundle)
for the current counts and remaining limits.

The retained acquisition evidence is under
`artifacts/worker-notice-source-preparation-20260914`. It includes frozen registry
metadata, source references, text hashes and explicit failures. All temporary
npm archives were removed after inspection. Five legacy archives initially
failed because their registry metadata omitted `unpackedSize`. The bounded
collector now accepts a missing declaration while enforcing a 64 MiB unpacked
cap. Evidence at `artifacts/worker-notice-legacy-20260914-a` verifies the same
five archives against both locked integrity and their previous archive hashes;
all five now pass inspection and their temporary archives are removed. Two
complete original READMEs, `isarray@1.0.0` and `strictdom@1.0.1`, add the new
bindings. All five package manifests match the installed metadata exactly;
successful inspection alone does not supply a missing notice for the other three.

Use the [offline assembly command](../../docs/WORKER_DISTRIBUTION.md#assemble-an-offline-guest-reference-bundle)
to combine these supplements with the existing installed notices. The assembled
reference bundle preserves all original findings and always reports
`redistributionComplete: false`. Review standalone binaries, embedded libraries,
QEMU/firmware, boot assets and applicable source/build materials before a wider
worker distribution.

[module-layout.json](module-layout.json) supplements the frozen inventory with
read-only observations of all 32 module paths lacking metadata or resolution.
All are dangling symlinks, including 28 that the earlier scanner mislabeled as
metadata-less directories. The scanner now distinguishes those cases. Original
findings/counts and the worker itself remain unchanged; this structural evidence
does not supply licenses or establish runtime reachability and bundled-code scope.

Eight release-source supplements add the original Microsoft MIT and AWS Apache
texts. Five Teams 2.0.15 manifests from tag `v2.0.15` match the installed manifests
byte for byte after the release's documented version stamping. Three AWS SDK
manifests at `d760a00859a08b5d04590ee047510b49add12361` match after Yarn's documented
publication conversion of explicit `workspace:` dependency ranges. Neither
upstream code nor package lifecycle scripts were executed. The catalog preserves
the raw source hashes, exact transformations, release evidence, installed hashes
and prior locked-archive evidence. Metadata agreement does not prove compiled
source equivalence or complete embedded notices.

Evidence is `artifacts/worker-release-notices-20260914-a`; the assembled bundle is
`artifacts/worker-notice-bundle-20260914-e`. All 94 prior bindings and all 154
original findings are preserved. There are 52 findings without supplements:
32 dangling module links and 20 named packages. The bundle still contains 1,091
distinct texts: these two newly bound project texts were already present for
other installed components. No worker, runtime or application bytes changed.
