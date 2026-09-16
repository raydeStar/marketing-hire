# Windows installer preview

The per-user installer installs an already verified Windows host package and
adds a Start menu entry and a Windows Installed apps entry. The shortcut opens
the native `Thaddeus.Host.exe --desktop` entry point. It needs no PowerShell
script permission, SDK, Node, Docker account or GPU to open the study. Model
connection and isolated research remain separate setup steps.

This is an unsigned **host-only development preview**. It does not include a
worker or establish publisher trust, production sandbox qualification, signed
updates or a finished cross-platform release. Do not bypass operating-system
security controls to open a downloaded copy. The current manually tested study
continues using its existing package; installing this preview is not required
to begin [Windows manual QA](MANUAL_QA.md).

## Installation and removal

The wizard installs for the current Windows account. Its default location is
`%LOCALAPPDATA%\Programs\Thaddeus2\preview-<package-id>`. The directory must be
new. The setup refuses an existing directory, duplicate registration or existing
shortcut for the same package. A different build gets a separate application
directory; setup does not replace or stop an earlier study.

The application is under `app/`, where its published file inventory is unchanged.
Installer ownership information, the uninstaller and installer notices live
alongside that folder. The default study data remains at
`%LOCALAPPDATA%\Thaddeus2`, outside replaceable application files. The installer
does not create or migrate a study, configure a provider, boot a worker, register
auto-start, or change system features or PowerShell policy.

Open the installed entry from Start. The native host runs in a minimized console;
keep it running while using the browser. Opening the same native desktop entry
again reopens its running study. A local, same-account channel returns a fresh
one-use login link; the launcher does not send the host key over HTTP to discover
an instance. The package, study, origins, worker port and installation must match.
A different build/study or unrelated occupied port is still refused; no process
is stopped and no port is silently changed. The archive's separate PowerShell
launcher retains its recorded-instance reuse behavior. See [desktop reopening](DESKTOP_REOPEN.md).

Remove a build through Windows Installed apps. The uninstaller verifies its
recorded location and ownership marker, rejects linked payload paths and checks
for busy application files before deletion. It removes only listed application
files, its own registration and its own shortcut. Study data, backups, other
builds and extra files added to the application directory remain. Directories
are removed only when empty; there is no recursive application-directory wipe.
Keep a verified backup and one previous compatible package before an upgrade.
Guided study restore/version selection remains the separate
[upgrade and rollback workflow](STUDY_BACKUPS.md#upgrade-and-rollback).

## Build from an existing package

The developer needs Node and the pinned NSIS 3.12 compiler directory. Download
the official [NSIS 3.12 ZIP](https://sourceforge.net/projects/nsis/files/NSIS%203/3.12/nsis-3.12.zip/download),
verify the archive SHA-256 below, and extract it into a dedicated folder. Pass the
`nsis-3.12` directory containing `Bin/makensis.exe` to the builder. The
[toolchain lock](../packaging/windows/toolchain.json) also pins all 441 extracted
files; the builder refuses missing, added, linked or changed compiler inputs.

```text
56581f90db321581c5381193d796fffcf2d24b2f8fed2160a6c6a3baa67f2c4f
```

From the repository root on Windows x64:

```powershell
node --test scripts/windows-installer.test.mjs
if ($LASTEXITCODE -ne 0) { throw 'Installer contract checks failed.' }
node scripts/windows-installer.mjs HOST_PACKAGE PINNED_NSIS_DIRECTORY FRESH-NAME
if ($LASTEXITCODE -ne 0) { throw 'Installer publication failed.' }
powershell.exe -NoProfile -NonInteractive -File scripts/windows-installer-check.ps1 -Publication artifacts/windows-installer-FRESH-NAME -Name FRESH-CHECK
if ($LASTEXITCODE -ne 0) { throw 'Native installer verification failed.' }
```

Publication verifies the complete host manifest and notice reference, captures
one exact input copy, compiles with the pinned toolchain and checks the original
package again. It builds no application, downloads no dependency and runs no
model or worker. Private launch configuration and worker images are refused.
Source filenames are escaped as NSIS literals, including dollar signs; input
names cannot become installer instructions.

The output contains the executable, SHA256SUMS, source snapshots, generated script,
compiler log, publication manifest and cleanup receipt. The manifest preserves
the original application's source identity separately from the installer builder.
It is not a claim that the application was rebuilt at the current commit.

Publication budgets two host copies plus 128 MiB and the 10 GiB reserve. It removes
its input copy after the compiler exits, including failed builds; incomplete
installer executables are removed on failure. Native verification budgets one
installed copy and fixture data, observes its owned host/installer processes,
then removes test files, fictional data and temporary user registrations after
exit. Small hash manifests and logs remain. Never use the running study as the
native check's fixture.

## Verification boundary

### September 16: installer matches the current QA app

The latest installer is
`artifacts/windows-installer-uploads-qol-20260916-a/Thaddeus-2-preview-eb48086022782b5a.exe`.
It contains the exact `portable-uploads-qol-20260916-a` host package running in
the owner's study, including upload progress, partial-success recovery and the
earlier chat, draft, Feed, To-do and Ideas fixes. Its SHA-256 is
`31103c12503b18ae58d6d2c4c66d7a3addb03581902ead193680ca143a60ebf0`;
the executable is 51,556,942 bytes. The verified input manifest is
`eb48086022782b5aafb366d537484b432f86da9230738455e3a53aab4a078140`.

All eight native installation/removal cases passed for these new payload bytes,
including the ownership-marker comparison, repeated launch and preservation of
seven fictional study files. The fixture removed its installation, study,
temporary registration and shortcut after all owned processes exited. The six
captured installer source inputs match the preceding nine-contract-test build
byte for byte; those unchanged contract tests were not repeated. Publication
reused the existing host and removed its temporary payload copy.

Evidence: `artifacts/windows-installer-check-uploads-qol-20260916-a/verified.json`,
its `cleanup.json`, and `artifacts/installer-qa-sync-20260916/verification.json`.
The running owner host and Luna bridge retained their process identities.
No model/search request, GPU inference, worker boot or application rebuild ran.
This is still an unsigned host-only preview, with the distribution and platform
limits described above. The prior task-recovery installer below is retained as
the previous verified preview.

### September 16: current installer and native ownership verification

The preceding installer is
`artifacts/windows-installer-mvp-20260916-a/Thaddeus-2-preview-8706d6c9b7210de4.exe`.
It contains the exact `portable-task-recovery-20260916-b` host package, including
chat retries/draft recovery, Feed deduplication, To-do Undo and Ideas recovery.
Its SHA-256 is
`28bf2109f78743bb036ae57a992101dd2fab5a844200131edc66b66b90f46850`;
the executable is 51,551,104 bytes. Publication verifies and reuses the existing
host; it does not rebuild the application or include private study data.

Nine installer contract tests and eight native cases passed. The native fixture
compares the installed ownership marker to the exact host manifest SHA-256, so
the September 15 marker correction is now exercised in a real installation and
removal cycle. It also verifies installed file hashes, Start menu registration,
duplicate/existing-folder refusal, write-failure cleanup, junction refusal,
same-process reopening, busy-app removal refusal and data-preserving uninstall.
Seven fictional study files and the extra application-folder file survive
uninstall; the fixture removes its own scratch and registration after exit.

Evidence: `artifacts/setup-mvp-20260916/verification.json`,
`artifacts/windows-installer-check-mvp-20260916-a/verified.json` and its
`cleanup.json`. The current build has a different manifest-derived identity
from the old `a5ee64148554d3bb` test installation. Its verification did not remove,
modify or reuse that owner-managed fixture. The running study and Luna bridge
kept their exact process identities. No worker, GPU, model or search call ran.

This closes native verification of the corrected ownership binding. The old
fixture's cleanup remains with the owner. The current build is still unsigned
and host-only; visual removal/wizard review, publisher trust, worker distribution
and wider platform acceptance are not established by these automated cases.

### September 15: ownership marker correction

Visual installation of the previous preview exposed an ownership-marker defect:
`install-owner.txt` contained the literal `@MANIFEST_SHA256@`. The generator's
placeholder matcher accepted letters and underscores but skipped digits, so both
the marker writer and the uninstaller comparison retained that literal. Existing
path, registration and file-lock guards were still present, but the marker did
not bind the package manifest as intended. Earlier successful removal fixtures
did not check the marker's bytes and therefore did not prove this binding.

The generator now recognizes numbered names in one substitution pass, preserving
literal macro-looking filenames. Two regression assertions failed before the fix;
all nine installer unit checks now pass. The native fixture also checks the exact
installed marker against the published manifest hash. Its PowerShell syntax is
checked, but its changed native assertion has not yet been exercised.

The historical corrected candidate is
`artifacts/windows-installer-owner-20260915-a/Thaddeus-2-preview-a5ee64148554d3bb.exe`.
It reuses the current host package without rebuilding or restarting the app.
Compilation, captured source hashes, output hash and generated marker writer/
reader checks pass; temporary publication payload files were removed. Evidence:
`artifacts/windows-installer-owner-20260915-a/marker-verification.json`.
At that checkpoint it still needed native installation/removal verification
after the prior test registration could be cleaned up. The September 16 package
above subsequently verifies the correction under its own independent identity.
Do not treat the older installer as a release candidate or generated-script
checks alone as native removal proof.

The visual pass verified welcome, destination selection, progress and completion
screens, including clearing the default Open Thaddeus checkbox. All 426 installed
application files and the exact test shortcut/registration matched. The native
UI tool blocked opening the removal wizard; automatic review then blocked direct
fixture cleanup. No uninstaller ran and no cleanup executed. The approximately
114 MiB test installation remains under `artifacts/windows-wizard-20260915-a/installed`.
The owner explicitly approved cleanup on September 15 and requested no repeated
permission prompts. Automatic review rejected the authorized checked cleanup
again with `blocked by policy` before execution. This is a tool restriction,
not missing consent. A subsequent read-only observation confirms 430 files /
119,426,591 file bytes, the exact registration and shortcut, and preservation of
the running host/bridge. The corrected candidate's native check was not started.
Evidence: `artifacts/windows-wizard-20260915-a/authorized-cleanup-rejection.json`.
Screenshots, the original defect and retained-fixture status
are recorded in `artifacts/windows-wizard-20260915-a`. Main study/bridge processes
were preserved. Visual removal acceptance remains open.

### Previous preview: desktop startup failures

The previous installer is
`artifacts/windows-installer-desktop-failure-20260914-a/Thaddeus-2-preview-a5ee64148554d3bb.exe`.
It adds visible Windows startup failures: an occupied port or invalid launch
profile leaves a dismissible explanation instead of disappearing with the
console. Port conflicts explain the maintenance path for switching versions.
`--no-browser`, ordinary host launches and noninteractive sessions keep console
reporting. Native Mac/Linux dialogs are not implemented by this change.

Twenty-one focused launch checks and three actual Windows executable cases pass:
occupied-port and invalid-profile dialogs show their expected text and close
through the visible OK action; unattended refusal exits without a dialog. The
unrelated listener remains bound, receives no connection, and no study is created.
Evidence is `artifacts/desktop-failure-20260914-a` and
`artifacts/desktop-failure-check-20260914-d`. Earlier observer failures are
retained: window creation preceded visibility, and this Windows message box's
OK button used ID 2 rather than the helper's assumed ID 1. The corrected observer
uses the actual visible button. All cases used the same application bytes.

The current installer was built from that exact verified application package
with the unchanged pinned installer implementation. Its hash and input manifest
were checked; the unchanged installer's native installation/removal cases below
were not repeated. Publisher trust, visual wizard review and full worker
distribution remain open. The updated application is running for manual QA;
activation preserved all study tables, the owner session and the Luna bridge.

The preceding installer includes native desktop reopening and the monthly search
allowance. It is
`artifacts/windows-installer-desktop-reopen-20260914-a/Thaddeus-2-preview-b701cf642a21e327.exe`.
Eight actual installation/removal checks pass, including repeated launch of the
installed entry without replacing the original process. Evidence is
`artifacts/windows-installer-check-desktop-reopen-20260914-b`; its cleanup confirms
owned processes, registrations and fictional study files were removed. The first
fixture failed to retain the fast child process's exit status; the corrected
observer passed against unchanged installer/application bytes. The earlier
checkpoint below is historical. Visual wizard/publisher-trust acceptance remains.

Eight contract checks cover exact inputs, changed bytes, missing notices, private
configuration, duplicate Windows filenames, traversal/device/stream paths,
literal escaping and a real filesystem link. The native check exercises a real
installation, its shortcut and registration, duplicate/existing-folder refusal,
a real NTFS write failure after taking ownership of the new directory, a
junction, startup of the installed host, busy-app refusal, and the normal
uninstaller's temporary child process. It compares the fictional study's exact
file hashes before and after uninstall and checks preservation of an extra file
inside the application folder.

The September 14 checkpoint passes all eight contract checks and seven native
cases. Its 51,304,992-byte installer is
`artifacts/windows-installer-20260914-e/Thaddeus-2-preview-5f971b7279ae5e6c.exe`.
The publication manifest binds its SHA-256, original host manifest and captured
builder inputs. Native evidence is
`artifacts/windows-installer-check-20260914-d/verified.json`; its cleanup receipt
confirms all owned processes exited and fixture data was removed. The executable's
manifest independently reports `asInvoker`, and its signature is `NotSigned`.
The original application was not rebuilt, and the running manual QA study and
Luna bridge were left in place. Superseded installer executables and diagnostic
payloads are removed while their compact failure/success receipts remain.

These are scoped native checks. The visual wizard, downloaded-file trust prompts,
signed publisher identity, upgrades across signed releases and native Arm/Mac
installation remain unverified. Earlier failed attempts and their cleanup are
retained. One failure exposed NSIS path normalization returning an empty string
for a new destination; the installer now uses the Windows path API and validates
its result before taking ownership of a directory.

Sources: NSIS [per-user execution level](https://nsis.sourceforge.io/Reference/RequestExecutionLevel),
[scripting and nonrecursive uninstall guidance](https://nsis.sourceforge.io/Docs/Chapter4.html),
[installer/uninstaller command-line behavior](https://nsis.sourceforge.io/Docs/Chapter3.html),
and [licensing](https://nsis.sourceforge.io/Docs/AppendixI.html). The compiler's
original COPYING file is included as `installer-notices.txt`; host dependency
notices remain in the unchanged application package.
