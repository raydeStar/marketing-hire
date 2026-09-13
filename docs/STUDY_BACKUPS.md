# Back up and restore a study

The packaged host can make a restorable copy without an SDK or database tool.

## In the application

On the computer hosting Thaddeus, open **Settings → Backups & shutdown → Review
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
the new folder. A published package also prepares a separate launcher folder.
Choose **Finish and close Thaddeus**, then open the displayed launcher. On Windows
it is `Start restored study.cmd`, on Mac `Start restored study.command`, and on
Linux `start-restored-study.sh`. The launcher uses the current complete application
package; keep that package in its recorded location. It opens the restored study
with the same local ports. Close the current host first. **Reopen study** always
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

Selecting another application version, automatic switching/rollback, signing and
automatic updates remain open. The offline commands below remain available for an
explicitly chosen new restore directory.

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

Keep the old package and a verified backup before opening data with a newer host.
Point the new package's `launch.json` at the intended data directory using its
documented launch profile. The host refuses a database newer than it supports.

For rollback, restore the pre-upgrade backup into a **new** data directory and
point a compatible older package at that copy. Do not point an older executable
at a database already migrated by a newer release, or overwrite later edits with
an old snapshot. Keep both directories so later work can be reviewed and recovered.
Automatic switching, signed updates and consumer recovery controls remain open.

The current limits are 20,000 files, 64 GiB of payload, 32 directory levels and an
8 MB manifest. Links, traversal paths, ambiguous device names, repeated/colliding
paths, unsupported formats and future database versions are refused. File content
and timestamps are verified after copying. Unix named pipes are refused before a
blocking read. Keep the source closed and unchanged throughout maintenance;
these commands do not protect against another process deliberately replacing
files between filesystem operations. These checks are not a claim of
power-loss durability on every filesystem or arbitrary cross-OS worker portability.

The native package CI exercises live-host refusal, offline backup, restart,
restoration through the actual product, identical exported history, later edits
remaining in the original, and a removed native credential remaining unusable.
Target receipts determine which OS/architecture has passed; a cross-compile does
not establish native verification.
