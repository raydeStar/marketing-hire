# MVP acceptance checkpoint

The current objective is practical MVP parity, beginning with missing chat
recovery. The September 14 scope correction in [the implementation contract](IMPLEMENTATION_PLAN.md)
remains authoritative. This review does not add features or turn deferred
platform/benchmark work into prerequisites for local Windows QA.

Current local build: `portable-uploads-qol-20260916-a`, runtime changes committed
as `2e2cbcf`. Its recorded activation, backup and 178-file runtime source
comparison are in `artifacts/uploads-qol-20260916`. The live tab was refreshed.
The latest installer also contains `portable-uploads-qol-20260916-a`; its eight
native installation/removal cases pass. It remains an unsigned host-only preview.
`artifacts/mvp-acceptance-20260916/audit.json` records the hashes of 16 reviewed
receipts and confirms all 178 current runtime source files match the package's
original source manifest.

## Evidence reviewed

These are scoped implementation checks, not a declaration that every possible
model-generated app or research request succeeds. Older checks are identified
where later changes did not replace their relevant behavior. Do not repeat a
whole campaign simply to replace an earlier date with today's date.

| Requirement | Current evidence | Assessment |
|---|---|---|
| Chat Retry, Copy, Edit, previous attempts, Enter/Shift+Enter, cancellation, token separation | Current packaged `chat-qol.spec.ts`; `uploads-qol-chat-regression-20260916` passes with seven synthetic calls | Implemented; no duplicate user messages or silent provider retries |
| Preserve unfinished messages and their context | `draft-recovery-ui-20260916-final`; current chat/upload checks preserve drafts | Implemented for the same tab/sign-in; not a cross-device draft service |
| Base file uploads, partial failure recovery, image/document filters | Current `uploads-qol-ui-20260916-final-b`; earlier two-case `task-recovery-mvp-20260916` includes image and text attachments | Implemented; no audio/video/PDF expansion |
| Model-designed app pages, chat updates, local add/edit/delete and recovery | Earlier Luna creation/edit in `ux-pass-20260915/manual-ux.json`; generated-app and `ux-final-20260915` form/navigation checks; current chat check creates an app | Implemented, with model-specific quality still requiring owner QA |
| Close an app without losing its chat/draft; edit/delete/restore from Artifacts | `ux-final-20260915`, current chat check and saved-work search checks | Implemented; closing returns to the actual underlying workspace |
| Tracked, daily, weekly and overall goals with follow-up planning | Two-case `task-recovery-mvp-20260916`, `task-recovery-ui-20260916-final` and navigation check | Implemented, including exact Undo; dates are planning aids, not automatic reminders |
| Categorized Ideas, chat action and visible progress/failure/cancellation | Same MVP/recovery checks; `Ideas.tsx` and the accounted Ideas provider path | Implemented; generating suggestions uses model tokens |
| RSS subscriptions, saved links, date/category filtering and interest preferences | `draft-feed-reader-ui-20260916-final` passes both cases; retained Feed backend checks; 20 live stories inspected without duplicate URLs | Implemented; no guarantee that a publisher's feed stays available |
| Local saved-work search and artifact/file navigation | `study-search.spec.ts` and its recorded packaged evidence in `CHAT_QOL.md` | Implemented; uploaded file contents are not indexed |
| Easy-to-find token usage, readable log and task details | Current chat accounting checks, two-case MVP check and earlier token/log UI verification | Implemented; reported usage, reservations and unreported usage remain distinct |
| Save search allowance 999 and enforce a local monthly cap | `task-recovery-mvp-20260916` settings case; earlier focused search admission/credential checks | Implemented; no account-wide billing guarantee and no routine Brave calls |
| Read a supplied website; working worker MCP boundary | 124 focused backend checks in `chat-web-20260915`; actual Luna page reading receipt and `chat-web-ui-check-20260915-a` | Implemented for scoped public HTTPS text, with two calls/one read in live acceptance; arbitrary MCP connectors and interactive browsers are not implied |
| Backup, restore, version handoff and reopen the installed app | `browser-handoff-20260914-d` restores a separate study and preserves later original data; September 16 installer native cases; current activation backup/data fingerprints | Implemented on the verified Windows path |
| Responsive layout, collapsed rail, branding and light/dark controls | Recorded UX/MVP mobile checks and current upload screenshot review | Implemented; final visual preference remains the owner's acceptance decision |
| Nontechnical distribution for other people | Host-only installer: nine contract checks/eight native cases. Current worker bundle: 107 supplements, 15 named packages unresolved plus broader source/native coverage | Incomplete; do not describe the preview as a finished consumer release |

No new app build, worker boot, GPU work, model or Brave request was needed for
this audit or the subsequent installer refresh. The installer reuses the current
QA package; its verification is in `artifacts/installer-qa-sync-20260916`.
The separate Standard Webhooks comparison adds one reviewed notice
binding; its evidence and limits are in [worker distribution](WORKER_DISTRIBUTION.md).

## Remaining acceptance and delivery

1. Owner QA: exercise Chat, Apps, daily work, reading and recovery using the
   [manual guide](MANUAL_QA.md). Fix concrete blockers and assess generated-app
   quality against the owner's intended use. Automated fixtures do not decide
   subjective layout or app-design acceptance.
2. Distribution: complete the separately packaged worker's notice/source/native
   coverage, supported-host installation and publisher trust before advertising
   a full consumer install. The current installer does not include the worker.
3. Platform claims: Mac implementation is shelved, actual phone setup is deferred,
   and remaining Linux desktop acceptance is open. Keep the support matrix truthful.

The local preview is ready for those manual flows. The broader launch objective
is not complete, and no complete-goal claim is made by this checkpoint. The PC
must remain on; do not restore the earlier shutdown instruction.
