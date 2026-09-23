# Back up and restore a study

The packaged host can make a restorable copy without an SDK or database tool.

## In the application

On the computer hosting Thaddeus, open **Settings → Storage & backups → Review
maintenance**. Choose a verified backup or closing without a new backup, then
review the study and backup locations. Active tasks must finish or be cancelled
first. Maintenance refuses overlapping edits/provider requests and never cancels
work automatically. Saved approvals remain in the ledger.

After confirmation, the product closes its database, worker services and event
streams. A small local maintenance screen remains available to the initiating
owner browser. It uses the same process and local origin, with no model, worker,
or product-store service registered. It displays the actual verified backup
receipt, supports a page reload, and offers **Reopen study** or **Finish and close
Thaddeus**. Reopening reconstructs the normal host against the same data and
launch configuration. Keys stored only until the host stops must be entered again.

Backups go in a private sibling folder named after the study, with `-backups`
appended. Every copy has a fresh dated directory. Its receipt is also retained
beside it. Failure keeps the original and incomplete copy; the screen does not
claim success. Maintenance accepts only the initiating local owner session,
requires CSRF for actions, and expires after at most one hour. Force-closing the
process can interrupt a copy; inspect its manifest/receipt before treating it as
complete. Phone clients cannot initiate or control host maintenance.

## Restore through the maintenance screen

After the study has closed, choose **Restore a backup → Find saved backups**.
Select a recorded backup and choose **Review selected backup**. The review shows
the backup date, file count and a new sibling study folder. Confirmation binds
that exact manifest; a changed manifest or payload cannot become a verified copy.
The original study, later edits and existing backups are preserved.

**Restore as a separate study** verifies the files and database before installing
the new folder. A published package also prepares a separate, compactly named
`thaddeus-launcher-…` sibling folder. Its profile points to the full restored-study
name; a long study name is not repeated in the Windows script path. If the parent
folder or application package path itself is too long for Windows PowerShell,
launcher creation reports that limitation while preserving the restored study.
Choose **Open restored study** to close maintenance and start that copy directly.
The checked **Open in my default browser** option opens a short-lived login link;
uncheck it to reconnect in the existing browser. The tab reloads the selected app's
assets, or shows its unlock screen if that backup lacks the current session.
For later use, keep the displayed launcher. On Windows
it is `Start restored study.cmd`, on Mac `Start restored study.command`, and on
Linux `start-restored-study.sh`. The launcher uses the current complete application
package; keep that package in its recorded location. It opens the restored study
with the same local ports. Close a running host before using a saved launcher. **Reopen study** always
returns to the original study, including its later edits.

The picker includes up to 100 receipts created through this study's maintenance
screen. The offline command below supports a backup stored elsewhere. Development
source launches can restore data but do not prepare a packaged launcher. Restoring
does not start an agent, resume worker tasks, install a VM or copy provider keys.

The backup folder retains a restore intent before copying and a result or failure
receipt afterward. The same confirmation is never automatically replayed. Reloading
the maintenance page preserves the current attempt's view. After the entire host
is interrupted, inspect those retained receipts and any incomplete copy; the next
host does not reconstruct an earlier attempt's screen or retry it automatically.
A verified copy remains available if launcher creation subsequently fails.

The restore review can also select another application package; see the guided
version steps below. Signing and automatic downloads remain open. A process
switch requires an explicit owner click. The offline commands below remain available
for an explicitly chosen new restore directory.

## Offline commands

Stop the host that owns the source data first. Closing its browser is not the
same as stopping the host. For a foreground Unix launch, use Ctrl+C in its
terminal. For a background Windows development instance, use its known owned
process/launch record; do not stop unrelated hosts. The backup command refuses
a live store or launcher and never stops a process itself.

Keep data and backups outside the replaceable package. Use absolute paths, choose
a new destination, and make sure its parent folder exists. In PowerShell:

```powershell
& 'C:\Thaddeus\Thaddeus.Host.exe' --study-backup 'C:\Studies\Thaddeus2' 'C:\Backups\Thaddeus-before-upgrade'
if ($LASTEXITCODE) { throw 'Backup did not complete. Keep the original study.' }

& 'C:\Thaddeus\Thaddeus.Host.exe' --study-restore 'C:\Backups\Thaddeus-before-upgrade' 'C:\Studies\Thaddeus-restored'
if ($LASTEXITCODE) { throw 'Restore did not complete. Keep the original study.' }
```

On macOS/Linux, use the same options with the packaged `Thaddeus.Host` executable:

```sh
./Thaddeus.Host --study-backup /absolute/study /absolute/backups/before-upgrade
./Thaddeus.Host --study-restore /absolute/backups/before-upgrade /absolute/restored-study
```

A successful command exits with code zero and returns a JSON receipt containing
the completed location, file count, database version and manifest hash. A failure
returns a nonzero code. Incomplete copies remain in private sibling folders with
`.incomplete-` in their names; the requested destination is not installed.

## What the copy contains

The backup is a directory containing `backup.json` and `data/`. It preserves the
database's stored rows and ordinary study file bytes and modification times.
SQLite's backup API folds committed write-ahead-journal content into one checked,
self-contained database. It does not migrate or rewrite the original database.
Restore verifies every file, its size and SHA-256, the exact inventory, database
integrity and supported schema before installing the new directory.

Backups include the owner's access key and stored browser sessions. They are
private and not application-encrypted. Windows copies grant access to the current
user and SYSTEM; Unix copies have an owner-only root directory. Hashes detect
changed contents but do not authenticate who supplied a backup. Preserve the
backup's privacy as carefully as the study itself.

Provider keys in the OS credential store are not exported. The backup retains
their endpoint-bound references. Restoring a backup cannot resurrect a removed
key; reconnect the provider if its native entry is missing or the backup is used
on another account/computer. Session-only keys must be entered again after any
host restart. See [model connections](MODEL_CONNECTIONS.md).

Process locks, stale launcher-instance records and database WAL/shared-memory
sidecars are not restored. New backups exclude the root `launcher-logs` directory:
the maintenance host may still be writing those diagnostics, and they remain in
the original study. Older backups containing diagnostic logs remain readable.
Existing worker disks and metadata are copied as data;
restore never boots a worker, replays a command or qualifies another computer's
worker installation. Retained work may still need the product's explicit recovery
and installation checks. Empty directories and original filesystem ACLs are not
reproduced; the copy receives private permissions.

## Upgrade and rollback

1. Download and fully extract the application version you intend to use. Keep the
   current application folder in place. These development packages are unsigned;
   use a download you trust.
2. In **Settings → Storage & backups**, make a verified backup and close the
   study into maintenance. For an upgrade, select this latest backup. For
   rollback, select the recorded backup made before the earlier upgrade.
3. Under **Restore a backup**, select **Use a different application version** and
   choose **Browse for application folder**, or paste its full location from your
   file manager. Windows **Copy as path** quotes are accepted. The chooser opens
   on the host computer; it does not upload a folder from another device.
4. Choose **Review selected backup**. The app verifies the exact package inventory
   and file hashes, its native platform, and its declared supported study versions.
   Restoring with this host upgrades the separate copy to this host's study
   format before disarming scheduled outbound actions. A package too old for
   that resulting format is refused, even if it could read the original backup.
   Review the chosen app,
   backup and separate destination, then **Restore as a separate study**.
5. Choose **Open restored study** to transfer to the selected app, or **Open
   original study** to open the original with its newer edits. The current host
   closes its listener and rechecks the recorded launcher files and complete app
   package before starting the target. Keep both displayed launchers for later
   use and keep both application folders available.

The selected app opens a new copy of the backup. The original study and all newer
edits remain in place. To return to that original study, close the new host and
use the newly prepared **Start original study** launcher. It directly uses the
previous app, avoiding a chain of dependencies on still older versions. Retain
the current and previous app folders plus their paired launchers and backups.
To restore another recorded point, use the same flow
with the compatible app and earlier backup; never overwrite the newer study.
This is a guided separate-copy workflow. Native credentials retain their existing
separate custody.

For rollback across a study-schema change, close the newer host and use the
**older application's** `--study-restore` command with the verified backup made
before upgrading. Choose a fresh destination and launch that separate copy with
the older application. Do not run the new application's restore command and then
expect an older app to open the upgraded copy. Keep the newer original study and
backup intact. The restore receipt reports the resulting copy's actual schema;
its manifest hash continues to identify the source backup.

Each owner-requested switch records an intent and a result with the target's
process identity. If startup fails, Thaddeus stops only the process it just
created and reopens maintenance after confirming that process has exited. Both
study copies remain available. It does not repeat the backup or launch attempt.
If exit cannot be confirmed, it leaves maintenance closed instead of starting a
competing host. Interrupted intents are never replayed automatically. Launch uses
the existing desktop interface, so a selected compatible older build does not
need to implement the new handoff API.

Windows acceptance covers a blocked target startup, recovery without another
backup, both Open buttons, two different compatible app builds, survival after
the original process exits, and matching restored/original histories. Mac/Linux
native handoff and different-schema migrations remain unqualified. The browser
test leaves default-browser opening unchecked; that launch-link mechanism is
shared with the existing desktop launcher.

The native chooser is available to the initiating local owner in maintenance.
Only one dialog can be open. It can be cancelled from the desktop or maintenance
screen, expires after five minutes, and closes during orderly host shutdown.
Windows also owns the helper in a kill-on-close job, so an abrupt host exit closes
the dialog. Reloading the browser recovers the pending chooser and its cancel
button. Opening a new chooser invalidates any previous restore review; cancelling
does not authorize that older review. A folder selection only supplies a path;
the existing package verification and confirmation still apply.

Windows packages use the system [Common Item Dialog](https://learn.microsoft.com/en-us/windows/win32/shell/common-file-dialog).
macOS uses the installed [choose folder adapter](https://developer.apple.com/library/archive/documentation/LanguagesUtilities/Conceptual/MacAutomationScriptingGuide/PromptforaFileorFolder.html).
Linux desktop sessions use an existing `/usr/bin/zenity` or `/usr/bin/kdialog`
([KDE documentation](https://develop.kde.org/docs/administration/kdialog/)). No
desktop dependency is installed automatically. Missing/headless adapters retain
manual path entry. Windows native acceptance is tracked separately from the
still-unqualified Mac/Linux dialog adapters.

Only portable packages with the new compatibility declaration participate in
guided selection. Earlier packages without that declaration are refused rather
than guessed compatible; the offline procedure remains available to an operator
who has verified the older package's actual schema support. Manifest hashes prove
file integrity, not publisher identity. A selected application's launch may
migrate its new study copy; the original remains on its existing schema.

Review and confirmation bind the exact package and backup manifests. Modified
payloads fail verification, stale confirmations cannot substitute another choice,
and an interrupted attempt is never replayed automatically. The launcher checks
the package again even if it changed after the copy was prepared. Missing original
verifier or selected application files stop launch without opening the study.

Backup/restore admission budgets the complete logical copy, a small metadata
allowance and a 10 GiB reserve. Copies check remaining space while writing; other
processes can still consume disk concurrently. A refused or interrupted copy
preserves the original and any incomplete output for inspection.

The current limits are 20,000 files, 64 GiB of payload, 32 directory levels and an
8 MB manifest. Links, traversal paths, ambiguous device names, repeated/colliding
paths, unsupported formats and future database versions are refused. File content
and timestamps are verified after copying. Unix named pipes are refused before a
blocking read. Keep the source closed and unchanged throughout maintenance;
these commands do not protect against another process deliberately replacing
files between filesystem operations. These checks are not a claim of
power-loss durability on every filesystem or arbitrary cross-OS worker portability.

The local native package check exercises live-host refusal, offline backup, restart,
restoration through the actual product, identical exported history, later edits
remaining in the original, and a removed native credential remaining unusable.
Target receipts determine which OS/architecture has passed; a cross-compile does
not establish native verification.
