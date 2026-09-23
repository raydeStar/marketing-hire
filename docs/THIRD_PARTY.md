# Third-party notices

No v1 source code or artwork was copied. Its mechanism audit is documented in
REUSE_LEDGER; no v1 license is imposed on new original code by this project.
No open-source license was selected for original private project code.

The native portable publisher now generates a dependency notice bundle before
sealing a host package. In an extracted package, open
`ThirdPartyNotices/Generated/THIRD-PARTY-NOTICES.txt`. Its index identifies each
dependency and links to the unchanged license/notice text under `texts`.
`bundle.json` records identities, hashes, provenance and the precise scope.
The package manifest covers the complete bundle and pins its index metadata.

The generator reads the published host's `.deps.json`, including its two
self-contained .NET runtime packs, and the captured npm production lock graph.
It additionally includes Vite because its module-preload helper is emitted into
the browser client. The npm list is conservative: type-only or tree-shaken
production entries may be listed even when they contribute no emitted code.
Development-only test runners, SDKs and compilers are not claimed as shipped.
Dependencies retain their upstream terms; this bundle selects no license for
original Thaddeus code or artwork.

NuGet archive hashes must match the cached archive checksums. The content hash
recorded by NuGet restore is separately matched to the published dependency
identity, when declared; both appear in the notice record. Signed archives can
have a different byte checksum from their content hash. The generator does not
replace locked restore or NuGet signature verification. Extracted `.nuspec` and license/notice files
must match the actual archive bytes. Packages declaring only a license expression
use reviewed upstream notices pinned in `third-party/nuget/catalog.json` at the
repository commit declared by that package. One SQLitePCLRaw bundle omits a
commit; its record explicitly identifies the resolved v3.0.5 tag and does not
claim exact package-source equivalence. Copyright metadata and additional
upstream notices remain intact. [NuGet license metadata](https://learn.microsoft.com/en-us/nuget/reference/nuspec#license)
distinguishes a license expression from a packaged license file.

Publishing reads only restored files and the checked-in notice catalog. It does
not fetch notices from a moving branch. Missing license text, changed package
identity, modified pinned text or an unsupported declaration stops publication.
The tool refuses to modify an already sealed application package or overwrite a
completed notice bundle. Changing a dependency requires reviewing its notices
alongside the dependency change. The publisher cleans its staging build output
after success or failure and retains the compact captured notice sources.

This bundle covers the host and browser dependency graph. It is not publisher
signing, a legal release approval, a source offer or a notice bundle for QEMU,
the OpenClaw worker image, Docker or separately installed software. Keep their
distribution and source-availability requirements as separate release work.
The raven and T mark are original SVG artwork authored for this project.

The experimental worker recipe also includes OpenClaw and Docker's sandbox base
image. Pinned manifests and upstream locations are in `workers/openclaw/versions.json`.
Their distributed images retain their own dependency notices. Docker Sandboxes
is installed separately under Docker's terms; it is not part of the original
Thaddeus source. A redistributed worker image/combined package still requires
its own complete notice bundle and applicable source provisions.

## Windows browser preview runtime

Windows preview packages also contain `browser-runtime/runtime-manifest.json`,
Node 24.21.0, and the locked `@playwright/mcp` 0.0.82 dependency tree. The runtime
manifest identifies each dependency and its retained license/notice files;
`browser-runtime/NODE-LICENSE.txt` is the pinned Node license bundle. The outer
package inventory hashes these files. Google Chrome itself is an installed
prerequisite and is not redistributed. This separate Node notice inventory is
in addition to the host/web notice bundle described above.
