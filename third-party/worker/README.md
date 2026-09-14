# Worker guest notice supplements

This catalog preserves upstream notice texts missing from the frozen guest
inventory. It is an input to an offline review bundle, not a completed worker
redistribution package or a license for Thaddeus's private application code.

Each binding identifies one installed npm package by name, version, guest path,
metadata SHA-256 and original license declaration. Notice bytes are named by
SHA-256 and remain unchanged under `.gitattributes`. The catalog also pins the
entire guest inventory and disk identity. A newer worker needs a reviewed catalog;
the assembler does not download new texts or substitute matching version names.

The initial catalog contains 86 package-instance bindings and 26 distinct texts:

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

`standardwebhooks@1.0.0` is deliberately unresolved: the installed/registry
declaration says MIT, while the repository root at its declared commit has an
Apache 2.0 license. Do not silently replace the declaration or infer that the
root file settles the package's terms.

The retained acquisition evidence is under
`artifacts/worker-notice-source-preparation-20260914`. It includes frozen registry
metadata, source references, text hashes and explicit failures. All temporary
npm archives were removed after inspection. Five legacy archives could not be
inspected by that preparation helper because their registry metadata omitted
`unpackedSize`; none supplies a catalog binding. A successful metadata lookup is
not recorded as a successful archive inspection.

Use the [offline assembly command](../../docs/WORKER_DISTRIBUTION.md#assemble-an-offline-guest-reference-bundle)
to combine these supplements with the existing installed notices. The assembled
reference bundle preserves all original findings and always reports
`redistributionComplete: false`. Review standalone binaries, embedded libraries,
QEMU/firmware, boot assets and applicable source/build materials before a wider
worker distribution.
