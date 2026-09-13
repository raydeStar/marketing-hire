# Retained research workspaces

Settings now lists retained research workspaces with their task titles. The owner
can inspect one, review its file count and logical size, then type
`REMOVE WORKSPACE` to remove that exact reviewed inventory. Imported Markdown,
task history, token accounting and approval receipts remain. This is irreversible
file removal, not secure erasure or a backup-deletion mechanism.

`IResearchWorkspaceStorage` is separate from execution admission. The QEMU storage
adapter can remove a retired workspace even when QEMU is unavailable or the host
refuses to start research. It never launches a VM, runs a shell or calls a model.
Other backend storage formats require their own adapter; they are not silently
treated as QEMU directories.

## Ownership and recovery

Both maintenance endpoints require the owner browser session, same-origin JSON
and CSRF token. A paired device or worker bearer token cannot remove a workspace.
The coordinator refuses maintenance while research is active, and storage takes
the same exclusive ownership lease used by the QEMU backend.

Inspection requires a finished task, its exact worker registration and broker
route, a pinned QEMU image identity, and a retired or previously interrupted
removal state. The path is derived beneath the private store. Links, unknown
entries and unretired credential directories are refused. The bounded inventory
recognizes the overlay, termination/recovery receipts, boot logs and observations.
It also recognizes the exact empty Windows cache-directory tree observed with the
pinned QEMU process's minimal environment; any contents are refused.

The review digest binds task/worker/image identity, registration version, relative
paths, lengths and modification times. It is not a hash of all private disk
contents. Removal rechecks this inventory, opens all files exclusively before the
first deletion, and persists an intention before effects. Files and empty
directories are removed individually; there is no recursive-delete fallback.
Interrupted removal retains its receipt and requires a fresh review. Startup only
reconciles an already verified removal into the task record; it never continues
deleting files. The private directory and ownership lease reduce accidental
interference; hostile same-user filesystem races are not qualified by these tests.

The final `research.workspace.removed` event carries the verified receipt into
replay/export. “Delete my data” is a separate confirmation and refuses retained
or orphaned worker state before deleting task ownership records. After verified
cleanup it removes correlated worker grants and diagnostic settings along with
notes, runs, chats and revisions. Provider settings and device sessions remain.
External exports, backups and development artifact directories are separate copies.

## Verification

The backend suite has 237 passing tests. New cases cover owner/CSRF authorization,
stale review and confirmation refusal, actual interrupted file removal, restart
without automatic deletion, exact empty-cache handling, locked files, links,
orphaned ownership, imported-note preservation and separate personal-data deletion.
The ten ordinary browser tests pass, including empty workspace Settings and token
accounting. These checks use fictional data and no live inference.

`artifacts/research-browser-removal-20260912-b` passed the real product browser and
OpenClaw/QEMU workflow in 41 seconds: public research, durable question,
continuation, exact approved import, reviewed workspace removal and page reload.
Five synthetic model responses were used. `browser-export.json` preserves the
pre-removal result; `browser-export-after-removal.json` and `verified.json` preserve
the surviving imported note and verified removal. The host independently checked
the purged registration, matching receipt, revoked grant and absent workspace.
Screenshots at 1440 and 390 pixels have no horizontal overflow. No VM process was
left running. Earlier fixture `-a` refused the initially unknown empty cache tree
before deletion; its workspace and pre-removal export remain as failure evidence.

This work does not qualify a production sandbox, broader crash recovery or
portable host distribution. The ordinary host's execution admission remains closed.
