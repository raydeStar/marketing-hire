# Windows delegation MVP acceptance

## September 22 bounded live Chrome check on F

Through ordinary Chat, a read-only Chrome task was reviewed for only
`https://example.com/`, with eight model calls, twelve browser actions, and a
two-hour permission window. The exact scope review was approved in the product
under the owner's authorization for this non-notification manual pass.
The saved task receipt shows one browser action and two model calls; Chat
returned the observed heading `Example Domain`. The product's `Close Chrome`
control was then used. This passes bounded public-page open, result, receipt,
and close on F. The task finished in six active seconds, before Pause, Take
over, and Resume could be exercised live; those controls retain fixture
coverage and are not claimed as human-observed acceptance. No account,
form, download, or other site was used.

A second, still read-only two-domain task reviewed `example.com` and its
single IANA link. In this live run, Pause showed `AI is stopped`, Take over
showed `Your turn in Chrome`, and Resume started a fresh page observation.
The second page did **not** complete: after the model call was interrupted,
the receipt retained a `Budget unknown charge`, then reported `Aggregate token
budget exhausted before dispatch` (5:32 PM, 30,172 reported tokens, two model
calls, three browser actions). The browser was closed with results retained.
`Runtime.cs` deliberately charges an interrupted, unmeasured model reservation;
the UI's Resume control did not provide a successful continuation within this
task's fixed allowance. This is a reproducible live-control limitation, not a
completed IANA read or a reason to erase the uncertain charge.

## September 22 live recurring-read check and narrow watch setup repair

On running package E, Chat approved a weekday email-and-calendar brief for
5:20 PM America/Denver. Its exact review bound Gmail read to at most 10 newest
messages in the preceding 24 hours and Calendar to at most 10 primary-calendar
events in the next 24 hours; read-only authorization was granted once. Chat
confirmed the persisted schedule. Its first occurrence is recorded below.

The first ordinary inbox-watch request failed before review with the internal
error `Use {{sinceUtc}} in the reviewed mail time field.` A second request that
explicitly supplied that template reached the exact review and was enabled at
five-minute intervals with the owner's importance rule and a 10-message
read-only Gmail limit. Its first occurrence completed at 5:13:31 PM: operation
`ca3edd166edb4f49ad1f28579320de40`, `assessedMessages=0`, `alerts=[]`,
`modelUsed=false`, `sourceMutation=false`, `notificationStatus=quiet`. Upcoming
showed `Last successful check 9/22/2026, 5:13:31 PM` and no attention alert.
The exact controlled self-email due at 5:17 PM was accepted by Gmail with the
browser closed. The second watch occurrence, due `23:18:30.2786665Z`, completed
at `23:18:42.0849993Z` (operation
`05b91aebb6384e9087f1b2ab878a87f7`). It assessed one new message using
one bounded model call, surfaced exactly one unread attention result with the
controlled sender, exact subject, short deadline reason and original Gmail
link, and recorded `sourceMutation=false`, `readOnly=true`. The notification
helper returned `accepted`, but no human visual delivery is claimed from this
agent-launched host. Restart and brief results are recorded below.

After the guarded E-to-F restart, watch occurrence 3 completed at 5:23:44 PM
with operation `77a8513148354912839b195aa4d1e020`. Its sole alert ID
`1a0cb6ad963173e0` differs from the controlled alert ID
`1a0cb68899e32d63`; the processed/alerted identity sets persisted across the
restart. The watch was then paused in Upcoming. This does not identify or
publish the later message's private content.

The brief's first occurrence completed at `23:20:33.1695697Z`, operation
`49400e123ef24fe09d8bad3554a5924e`, with both Gmail and Calendar marked
available, `sourceMutation=false`, a saved unread result and a model receipt.
Chat then targeted `Weekday morning brief` for pause, showed the next occurrence
and current state in the exact change review, and persisted `Paused` without
removing the completed result.

The setup failure is a release usability defect: an owner should not have to
type an internal placeholder. `InboxWatchConversation.cs` now inserts the
activation-forward `{{sinceUtc}}` bound into the exact arguments before review
when the chosen read tool has a supported time field. A model-supplied older
timestamp cannot expand the watch. Forty focused email/watch tests pass. Package
`artifacts/portable-portable-inbox-bound-20260922-f/thaddeus-win-x64` passed
17 native checks at `artifacts/portable-check-inbox-bound-20260922-f/verified.json`;
ZIP SHA256 `a985f91938da0e6faf6eb69d08d219250829255849e5c0ccf26d019aa6f74b1a`,
manifest SHA256 `e74f76ed48e6da054613ef6747ac965a83ef8ad0706ff3eef47c369a8ef1bf69`.
Its captured C# runtime source differs from E only in
`InboxWatchConversation.cs`. Guarded launcher
`artifacts/preview-inbox-bound-20260922/Start-Preview.cmd` passed `-CheckOnly`
and retains E as rollback. F now runs at localhost:5279, PID 2056, after a
verified schema-12 owner-study backup at
`.data-backups/20260922-232204-desktop-update-fbe27050a1134abd929e89b2154ce14a`;
activation receipt:
`artifacts/preview-inbox-bound-20260922/runs/20260922-232202-497ce026/result.json`.
This is an agent launch and cannot establish visual notification delivery. A
plain-language watch request on F reached exact review without the owner typing
`{{sinceUtc}}`; its reviewed arguments contained the host-inserted bound. The
duplicate proposal was declined, leaving the original paused watch as the only
test job. Native notification testing
remains last at the owner's direction. Publication remains paused.

## September 22 explicit-time guard candidate

Package D reached the exact scheduled-email review in Chat, proving the
send-intent loop was fixed. The proposed instant was `2026-09-23T00:00:00Z`,
shown as 6:00 PM locally, although the request specified 5:00 PM Denver time.
That review was **denied**; no email job or send was authorized. The host had
checked relative delays but had no equivalent explicit local-time comparison.

The narrow guard in `DelegationEmailConversation.cs` now compares an explicit
owner-supplied local time/date with the proposed UTC instant using the frozen
host timezone. A mismatch becomes a visible clarification before review. It
also refuses to infer an ambiguous bare hour. Fourteen focused email tests pass,
including wrong hour, wrong date, matching hour, later affirmative send intent,
and later refusal. The Windows package
`artifacts/portable-email-time-20260922-e/thaddeus-win-x64` passed 17 native
checks with disposable extraction removed:
`artifacts/portable-check-email-time-20260922-e/verified.json`. ZIP SHA256:
`0ea5456716ddf8624ef46ea3d551bfa205311b40a1b07778f9a7a3bdc4d41926`;
manifest SHA256:
`6b4baa3b65db3f041a1a7fedfea3cf023e57a39a69ec8c233f5c57a101c4ea52`.
The guarded owner-study transition retained D as rollback and made verified
backup `.data-backups/20260922-224345-desktop-update-a92dc54f61b144bd86bbcfb519649218`.
Receipt `artifacts/preview-email-time-20260922/runs/20260922-224344-c6c6a09c/result.json`
records E ready at localhost:5279, PID 22908. This **agent launch does not
verify Windows notification delivery**. E's captured source differs from D in
only `DelegationEmailConversation.cs`; 15 selected Google, scheduler and inbox
source files match C exactly, so their prior fixture evidence is reused with
its original limits. The controlled send and separate Inbox check below passed;
recurring work, revocation, fresh-user and native notification gates remain open.

The E chat review bound the sender's Gmail send connection, recipient
`mark.b.hall.1117@gmail.com`, exact subject/body, 5:00 PM America/Denver
(`23:00Z`), and one-send authority. After approval, Upcoming displayed one
persisted job. Both in-app browser tabs were closed while PID 22908 continued
running. Occurrence
`fd4ff556f4c268ea51777c25f1e7f2e5688bbd6871d1e925c4a26bc4b0164baa`
for job `a26271f56cda4ea4bd17e7754dc0505e` was claimed at
`23:00:00.0005719Z` and completed at `23:00:01.3964889Z` with
`dispatchState=accepted`. Gmail `messages.send` returned message ID
`1a0cb58faa9aaf54`. A separate exact, read-only Gmail Inbox search,
operation `connected-75b97ae38ae549a4ba8215514a119c66`, returned
`isError=false`, one matching message with that same ID, `INBOX` and `UNREAD`
labels, and `receivedAt=23:00:01Z`. This observes arrival in the controlled
self Inbox; it does not claim that a person read the email or that every
recipient/provider has exactly-once delivery.

## September 22 live Calendar read and delayed-email repair candidate

The owner launched `portable-tray-tip-20260922-c` from the normal desktop.
`artifacts/preview-acceptance-20260922/runs/20260922-222141-8eb48d97/result.json`
records the exact running package, verified private backup, and readiness on
localhost:5279. The owner then confirmed the raven tooltip and Open path.
This is tray acceptance, not native notification delivery or click acceptance.

On that same package, the normal Chat interface requested an exact bounded
Calendar read for September 22 in America/Denver. The owner-review surface
showed `calendar.events.list`, `calendarId=primary`, the local-day UTC bounds,
and `maxResults=50`. The accepted action recorded operation
`connected-5ecf64635d3f41388f1f68885bdb8019` at
`2026-09-22T22:23:56.6170758Z` with `isError=false` and one returned event.
Event content and participant identities are deliberately excluded here. This
proves one live read, not recurring dispatch or later token refresh.

An exact delayed send to the owner's controlled inbox repeatedly asked for
send authority even after an explicit follow-up. No email was scheduled or
sent. `DelegationEmailConversation.cs` combined all earlier user messages for
the send-intent check, so an earlier “do not send yet” vetoed a later affirmative
instruction. Candidate `portable-email-intent-20260922-d` evaluates the newest
owner instruction first and still refuses a later “do not send.” Thirteen focused
`DelegationEmailConversationTests` pass; the packaged Windows x64 check passed
17 native cases with fixture cleanup at
`artifacts/portable-check-email-intent-20260922-d/verified.json`. ZIP SHA256 is
`7defa0dcbc3ff537eb3765ceb6cd9cd43ae57a955a09f5b52f69799f0286686c`;
manifest SHA256 is
`48eeb9d15bb18b41af76529578218f559b5f43600e61941365b4b9fdc174be42`.
The guarded normal-desktop launcher at
`artifacts/preview-email-intent-20260922/Start-Preview.cmd` passed `-CheckOnly`.
The owner then launched D; receipt
`artifacts/preview-email-intent-20260922/runs/20260922-223322-8601a594/result.json`
records PID 3620, the exact package manifest, `agentLaunch=false`, and verified
schema-12 backup
`.data-backups/20260922-223324-desktop-update-31d408f870d5494d810016d435873dfd`.
C remains rollback. Live delayed-send acceptance stays
open until an exact in-product approval, timed dispatch, Gmail acceptance, and
separate recipient observation. Publication remains paused.

## September 22 tray tooltip candidate - packaged check passed, desktop acceptance open

The owner host is still `portable-tray-icon-20260922-b` at localhost:5279.
`portable-tray-tip-20260922-c` changes only `WindowsTray.cs` to request the
Windows shell tooltip for a version-4 tray icon. The guarded launcher selects C
and retains B as its immediate rollback. Its `-CheckOnly` verified both package
manifests and every payload file without touching the running study. The C ZIP
SHA256 is `2c03578d961ceac7f6c57677a58d0b6f3c73d84952872f09e82f5fa476d701d0`;
manifest SHA256 is `c109c88c8c65d0dd28d73de54fe3cf0653c7d7fe2c6c1b7a05810189b706280e`.
`artifacts/desktop-reopen-check-tray-tip-20260922-d/verified.json` passes the
packaged native second launch, single-use login, profile/listener refusal, and
tray Exit with the fictional study retained. It removed its own fixture study.
The owner must still launch C from the normal desktop and observe the tooltip,
Open/login, and eventual Exit; this fixture does not establish those visual
results or native notification delivery. Publication remains paused.

## September 22 Windows tray - native fixture passed, visual acceptance open

The active owner preview is `portable-tray-20260922-a`; exact identity and
backup are in `NON_NOTIFICATION_MVP_HANDOFF.md`. A focused native menu-command
test and the packaged fresh-study check passed. The latter sent Exit to the
verified tray window, observed the owned host exit, retained the SQLite study,
and cleaned its fictional fixture. Receipt:
`artifacts/desktop-reopen-check-tray-20260922-b/verified.json`. Human inspection
of the tray icon and popup menu is still open. Existing release gates remain open;
this check does not validate native notification delivery or Whisper STT.


## September 22: bounded continuity and status polish - locally verified

**POLISH COMPLETE AND RUNNING; RELEASE ACCEPTANCE REMAINS OPEN.** Current candidate:
`portable-polish-20260922-r4`, schema 12, with exact source/payload identity and
launch command in `NON_NOTIFICATION_MVP_HANDOFF.md`. Development is held for the
remaining owner acceptance session; no new feature or architecture cycle.

The pinned-tabs package reproducibly discarded generated-app input after
Activity -> pinned app (`artifacts/polish-repro-20260922-r1/browser-results.json`).
The fix retains one current app within this browser session, including form input
and scroll position. It refuses hidden-view writes, asks before replacing an app
or updated design with unfinished input, and keeps the loaded draft after an app
refresh failure. Native forms use the same navigation/replacement protection.
Browser reload/restart still requires saving; no new draft database was added.
Generated code must still honor its data/render contract; arbitrary JavaScript
state is not serialized. Task activity now separates working, waiting for review,
waiting for an answer, paused, completed and failed. Connections show saved setup
rather than implying a fresh provider check, with explicit reconnect/recovery.

Evidence: `artifacts/polish-20260922/verified-candidate-r4.json` records **10 passing
browser tests across nine isolated cases**, including a raw bridge attempt to
write from a hidden app, failed-refresh recovery, generated/native saves, draft
and scroll retention, app/design review, background completion/cancellation,
Google setup fixtures, and Chrome review controls. `artifact-api-final.trx` records
**9 passing artifact API checks**. Desktop and 390-pixel screenshots were inspected.
`candidate.json` verifies every packaged payload file (934) and runtime source file
(230), and lists the nine changed runtime files relative to pinned-tabs. Only the
artifact page bridge changed on the backend; scheduler, OAuth, credentials and
notification source is unchanged. Reuse the recorded prior evidence for these
unchanged parts, not a claim of fresh live acceptance.

`owner-runtime.json` confirms the exact served client at `http://localhost:5279`,
Luna High, a verified private backup, and retained 70 runs / 91 chat entries /
3 apps / 5 pages. Pinned-tabs remains the rollback package. Successful fixture
studies were cleaned by their runners; `cleanup.json` records removal of failed
and development studies and superseded polish R1-R3 payloads/archives, preserving
logs, hashes, source snapshots, owner data and rollback. No live model, Google,
notification or external Chrome action was performed by these checks.

**Open:** owner-authorized live delayed email / recurring brief / inbox watch and
revocation checks; fresh Windows-user setup; human Chrome workflow; later native
notification delivery and cold-click acceptance. Publication remains paused.

The local release handoff now matches this candidate at
`artifacts/publication-polish-20260922`. Its manifest and `review.json` record
archive inventory/text checks and desktop/mobile static-page verification. The
download remains disabled. Existing fictional gallery assets are explicitly
historical; no live check, fresh Windows-user check or public upload is implied.
Receipt reconciliation confirmed all 54 prior R3 UI case receipts and all nine
current focused case receipts. Comparing captured manifests, the artifact page
bridge is the only backend change since R3; all 25 recorded Google/delegation
components still match the earlier live-read package. This reuses evidence and
does not establish that today's tokens refresh or scheduled Google calls succeed.

## September 22: owner-reported pinned-page navigation correction

Historical checkpoint, superseded by the continuity/status candidate above.

**FIX VERIFIED AND RUNNING; RELEASE ACCEPTANCE STILL OPEN.** The current package
is `portable-pinned-tabs-20260922-r1`, identified by exact hashes and launcher in
`NON_NOTIFICATION_MVP_HANDOFF.md`. It supersedes R3 for UI acceptance.

Repro: pinning a page replaced the tab strip, expanded the pane, and the return
control cleared the saved pin. The sidebar now keeps a shared tab strip, Today
and a named pinned-app tab, consistent width, Back to Today for navigation, and
a separate Remove pin control. No backend, credential, scheduler or approval
policy changed.

`artifacts/pinned-tabs-20260922/verified.json` records six passing isolated packaged
cases: My page, native app forms/navigation, chat app creation/editing, Profile,
raven/reduced motion, and Chrome review controls. The extended My page case checks
unchanged pin/version after going back, tab selection, desktop width, draft/data
preservation, full screen, and 390-pixel layout. Screenshots were inspected.
`provenance.json` records the five frontend-only changes from R3; other evidence
below is retained with its original candidate and scope. Successful fixture
studies and package build intermediates were cleaned. No routine live model,
Google or notification calls occurred.

The running owner host was updated through maintenance after a verified backup;
`artifacts/pinned-tabs-20260922/owner-runtime.json` verifies the exact served
frontend, Luna High and retained history. Prior human/live acceptance gates and
publication pause remain unchanged.

## September 22: invited tester preview

The owner approved one bounded preview pass: **My page** (Today or one pinned
app), live side-by-side app updates, and a dedicated saved Chrome profile for
browsing with reviewed external actions. Reuse the scheduler, connector boundary,
budgets, and existing task/approval surfaces. This is not a general automation
marketplace or an architecture migration. After this pass, return to acceptance;
additional ideas belong in the existing post-release backlog.

Baseline is `bf67d4e3c465c432c7264debe5f4f9a984b8a688`, schema 11.
The preserved package is `artifacts/portable-local-daily-token-usage-package-r1/`
`thaddeus-win-x64`, archive SHA256
`ff7f458b5f93bb8d6c32f1facd6c51568f9191c0ebe619111545dec9e5355c78`.
Rollback remains `artifacts/portable-local-qwen-staged-package-r1/thaddeus-win-x64`.
The old ports 5179/5183 now belong to Framewright. The owner launch profile uses
`http://localhost:5279` and worker port 5283; no Framewright process is stopped.
Profile backup: `artifacts/tester-preview-20260922-baseline/launch-profile-before.json`.
The preview launcher below is the current entry point; the earlier baseline is preserved for rollback.

Status: **LOCAL PREVIEW RUNNING; ACCEPTANCE OPEN**. Existing evidence below
retains its original candidate and limits. The focused current baseline browser
receipt is `artifacts/browser-release-review-20260922-r1/verified.json` (navigation
and draft preservation, synthetic provider, disposable study). Native visibility,
cold notification click, live Google workflows, and fresh Windows user acceptance
remain separate owner gates. No fixture result closes those gates.

New acceptance: Today/task projection and versioned edits; persisted pin and
return to Today; live data/design refresh; narrow-screen navigation and draft
preservation; browser task scope, exact mutation review, stale-page rejection,
takeover/pause/cancel/restart, and bounded usage. Shared Google test registration
must support invited testers without exporting owner credentials. Publication
remains paused. Freeze only after agreed product/setup checks pass.

### September 22 implementation receipts

- **PASS (fixtures): My page.** `artifacts/my-page-20260922/my-page-r2.trx`
  records 40 focused backend/app API tests. Pin storage survives restart, rejects
  stale changes, retains archived pins without deleting app data, and saves chat
  pin changes with their receipts in one transaction. Pinning executes no tasks.
- **PASS (packaged browser, synthetic study):**
  `artifacts/browser-my-page-20260922-r3/verified.json` covers Today, local midnight
  rollover without changing saved deadlines, completion/Undo/stale Undo, pinning
  through the app shelf, cross-window updates, opening a temporary app without
  replacing the pin, draft retention, data updates without iframe remount, design
  reload, full screen, and 390-pixel layout. Desktop/mobile screenshots are in its
  `screenshots` directory. No external model, Google, search or notification call.
  R2 failed because the test expected the shelf after closing an app opened beside
  Chat; the test now navigates to the shelf explicitly. No product behavior was
  weakened. R1/R2/R3 fictional studies were removed after process-exit checks;
  each directory retains its cleanup receipt.
- The My page test package is `artifacts/portable-my-page-20260922-r1/`
  `thaddeus-win-x64`. It captures the working-tree change on baseline `bf67d4e`,
  **not a frozen release revision**. It never replaced the owner's package. Its
  superseded binaries are now pruned with compact provenance retained.
- **PASS (fixtures), LIVE ACCEPTANCE OPEN: Chrome assistance.** The host adapter and action policy use
  `@playwright/mcp` 0.0.82 and a hash-pinned Node v24.21.0 runtime. Twelve policy
  fixtures pass in `artifacts/browser-policy-20260922/browser-policy-r2.trx`.
  The adapter is now wired to chat, approvals and package publication; full live
  browser-automation acceptance is still open. The real Chrome fixture uses
  intercepted synthetic pages, a fresh disposable profile and no model calls.
  `browser-adapter-r5.trx` now passes that fixture, including one persistent MCP
  session, a controlled click, stale-action refusal and closed-session refusal.
  Earlier failed receipts retain the Windows path-quoting bug, fixture module
  export correction and Chrome shutdown race. The adapter now explicitly closes
  Chrome before disposing stdio. None of this is live website/user acceptance.
  Standalone runtime binaries were pruned after process checks; the combined
  checkpoint below supplies that runtime. Local .NET intermediates and
  `tools/browser-runtime/node_modules` remain for the immediate Runtime-to-MCP
  fixture. Remove these inputs afterward. Package staging dependencies were cleaned.
- **PASS (host fixtures):** `artifacts/browser-policy-20260922/browser-conversation-r4.trx`
  records 66 focused browser/provider/API/migration checks. Runtime owns scope
  review, exact action review, pause/takeover/resume/close, one-task admission,
  persistent outcomes and restart invalidation. No remembered browser permission
  or automatic replay of uncertain actions is allowed. R2 also passed 100 related
  conversation/app API/My page checks. These are synthetic checks.
- **PASS (real Chrome, fictional intercepted pages):** `browser-sensitive-r2.trx`
  in that folder verifies the pinned adapter. R1 exposed upstream accessibility
  snapshots containing filled password values. The host now withholds sensitive
  forms for manual takeover and omits editable snapshot values; raw changes still
  invalidate page hashes. Only invented credentials were used in fixtures.
- **PASS (Runtime to actual MCP/Chrome):** `browser-runtime-integration-r1.trx`
  extends that fixture through the real Runtime, two distinct reviews, one real
  intercepted-page click, consumed approval, durable page receipt/chat result and
  close. It uses scripted model replies, no external site and no owner profile.
  Live model/site and human takeover acceptance remain separate.
- **PASS (packaged UI fixture):** `artifacts/browser-chat-card-20260922-r1/verified.json`
  covers exact review content and pause/takeover/resume/close controls on desktop
  and at 390px. Screenshots were inspected. It uses synthetic state/action
  responses, not a real website through chat. Its study was removed after process
  exit; `scratch-cleanup.json` records removal.
- Combined development checkpoint: `artifacts/portable-browser-preview-20260922-r1/`
  `thaddeus-win-x64`, captured dirty baseline `bf67d4e`, schema 12. ZIP SHA256:
  `1a43baad02bb4bb07b85d0ebc30ab121cf85cc14b3de4c3c0f17f27ba58c93be`.
  Manifest SHA256: `0b8b4362f14b0d447b176577aa91d59dde379d7e82a0fc2dd5245440fb32c0d3`.
  Native manifest verification passed (`package-verification-r1.json`). Node
  24.21.0, MCP 0.0.82 and dependencies/notices are included; installed Chrome is
  required. **Not frozen or promoted to the owner.** A further source fix counts
  elapsed active time before accepting an action; its test passes in r4 but
  postdates this package capture. Batch it into the next acceptance candidate.
- Old My page test package/ZIP and standalone runtime binaries were removed after
  process checks. Source, manifests, hashes and receipts remain, with
  `test-package-cleanup.json` / `scratch-cleanup.json` recording removal. Owner
  active and rollback packages remain intact.
- **Owner model correction:** the owner study selects hosted
  `gpt-5.6-luna`, high reasoning, `http://127.0.0.1:5181/v1`. No credential change
  or inference was performed. `artifacts/luna-high-20260922/provider-switch.json`
  and `previous-provider.json` retain read-back and rollback. The bridge is ready;
  its CLI reports signed in. `artifacts/Start-Thaddeus.cmd` starts Luna and the
  guarded preview launcher instead of loading Qwen. The subsequent verified launch
  upgraded the original study from schema 11 to 12; see the launch receipt below.
- **Google audience:** owner confirmed only their existing account for now. No
  tester additions. The actual Desktop client rejects code exchange without a
  client secret (`invalid_request: client_secret is missing.`), established by
  a fictional-code negative control in
  `artifacts/google-preview-20260922/public-client-preflight.json`. No account
  authorization or secret was sent. Preserve the existing vault-backed app setup;
  shared onboarding for additional testers remains open. No speculative OAuth
  replacement or credential bundling was introduced.
- **PASS (source checks):** `artifacts/local-check-invited-preview-20260922-r1/verified.json`
  records 1,153 backend passes, one intentionally skipped opt-in Chrome fixture,
  protocol checks, frontend production build, and secret scan. The skipped fixture
  has separate real-MCP fictional-page evidence above. No live model or GPU calls.
- **Restore blocker found and fixed:** `preview-upgrade-20260922-r3/verified.json`
  reproduced a newer host upgrading a restored copy to schema 12 while its receipt
  said 11; choosing an older app could then fail. The copy now reports its actual
  schema, and guided restore rejects an app unable to read the resulting format.
  Cross-schema rollback uses the older app's restore command and a pre-upgrade
  backup. Original studies/backups remain intact. Sixty targeted backup, guided
  restore, maintenance and package tests pass in
  `artifacts/preview-acceptance-20260922/restore-schema-r1.trx`.
  The first upgrade fixture (r2) only failed because its expected export omitted
  the new null browser fields; r3 exposed the real restore limitation. Both
  fictional studies were removed after owned processes exited. Rebuild and
  packaged recheck followed on the candidate below.
- **Current owner-preview candidate:**
  `artifacts/portable-invited-preview-20260922-r3/thaddeus-win-x64`, schema 12,
  captured dirty baseline `bf67d4e`. ZIP SHA256:
  `d62450dd3bf2c22e9749871f5a672f254894c00d7c09c82175ee1ce4c151b091`.
  Manifest SHA256:
  `4bac0e385e6112a801a76e9648b8089595172a7d5c49255f16d4b1653125de42`.
  `artifacts/preview-native-20260922-r3/verified.json` passes 17 native package
  checks. `artifacts/preview-upgrade-20260922-r4/verified.json` proves actual
  baseline schema-11 to candidate schema-12 preservation, truthful restore
  receipt, downgrade refusal without data changes, and separate rollback using
  the older app's restore command. Fictional studies were removed; no owner data,
  Google authorization, notification probe, or live inference was used.
- Guarded preview entry point:
  `artifacts/preview-acceptance-20260922/Start-Preview.cmd`. Its PowerShell helper
  verifies both packages and backs up the existing study before launch. Check-only
  passed (`launcher-check-only.log`). The owner subsequently asked Codex to launch
  it, explicitly accepting temporary notification limitations. `-AllowAgentLaunch`
  preserves the default desktop guard while recording this opt-in. The verified
  backup and exact host identity are recorded in
  `artifacts/preview-acceptance-20260922/runs/20260922-174002-d2427fbe/result.json`;
  `owner-runtime.json` in its parent acceptance directory confirms schema 12,
  Luna High, the exact served client, Chrome availability and retained history.
  Native notification visual/cold-click acceptance stays open. No model call,
  live Google operation or notification probe was performed by this launch.
- **PASS (54 packaged UI cases):**
  `artifacts/preview-browser-suite-20260922-r9/suite.json` records every ordinary
  UI case on the immutable R3 package, reusing 39 already-passing case receipts.
  This includes My page, live app updates, Chrome review controls, restored
  history, remembered permissions, mobile navigation and safe recovery.
  Fixtures now recognize the added browser capability, open Activity explicitly,
  scope duplicate Today/check-in controls to their workspace, and expect schema
  12 in exports. Earlier failures were retained; no product behavior was weakened
  and no rebuild was needed for these fixture corrections. Opt-in live web,
  native folder/notification, research and study-handoff cases were not run.
  Each passing case removed its fictional study after process-exit verification.
- **Cleanup pending:** automatic approval review rejected the command to remove
  superseded test binaries from `portable-browser-preview-20260922-r1` and
  `portable-invited-preview-20260922-r2` with `blocked by policy`. No part of that
  command ran, and no alternate deletion method was attempted. Those packages
  remain under `artifacts`, alongside the current candidate. Owner baseline,
  rollback, studies, credentials and unrelated services remain preserved.
- Cleanup of six small failed UI study folders was also rejected by automatic
  approval review (`blocked by policy`). No deletion or alternate method occurred.
  `artifacts/preview-acceptance-20260922/failed-ui-fixture-cleanup.json` records
  their exact paths and sizes; `MANUAL-CLEANUP.md` lists optional manual targets.
  Passing-case studies were removed by the original runner; diagnostic logs and
  screenshots remain. Local build/test dependencies are retained pending cleanup.
- **PASS (current-source/evidence audit):**
  `artifacts/preview-acceptance-20260922/acceptance-audit.json` verifies all 230
  captured runtime source files still match R3. Only GuidedRestore and StudyBackup
  differ from the earlier 1,153-test core receipt; the 60 focused restore tests,
  R3 native checks and real upgrade/rollback receipt cover those changes.
  Twenty-five selected Google, scheduler, notification and delegation-UI source
  files match the September 17 live-Gmail package. Historical live results remain
  bound to that package; source reuse is not a new live or human observation.
  The running owner's local metadata shows mail read, approved send and calendar
  enabled/ready, plus two succeeded jobs/occurrences and no active job. No token
  refresh, provider call, inference or new action occurred during this audit.
- **Next:** owner review of the running candidate, live Google, fresh-Windows-user
  and human notification acceptance. Publication remains paused; the complete MVP is
  not accepted.
- Automatic approval review rejected the attempted removal of the leftover
  `C:\Users\Ayric\AppData\Local\Temp\thaddeus-browser-fixture-0e89d6c52cf940b79f9ad4731b2990de`
  directory after the first adapter cleanup race. It remains intact; no alternate
  deletion mechanism was used. Later fixtures explicitly close Chrome first.

## Current decision - September 18 final acceptance

**READY FOR OWNER ACCEPTANCE**, not ACCEPTED FOR WINDOWS PREVIEW.
Exact candidate: `8fa2a51fb8f70982df7edb5eee3283998233bd83`, schema 11, archive SHA256 `7ae9a7937e46b48bacb005f3de733af5c84fba37792817b2f5a1c78512d3c7df`.
Launch/path/rollback are in `NON_NOTIFICATION_MVP_HANDOFF.md`.
The owner study still runs the preceding verified package; the guarded launcher
is pinned to this candidate and keeps that current package as rollback.

Current local evidence: `artifacts/local-check-qwen-staged-release-r1` passes
1,116/1,116 backend tests, protocol tests, frontend production build,
notification build and the tracked-file secret scan. The exact package passes
17 extracted native checks and five Windows Credential Manager checks in
`artifacts/local-check-qwen-staged-package-r1`; its packaged browser suite passes.
The unchanged focused Google/connection evidence covers stable Gmail/Calendar
REST reads, PKCE, multiple permissions, old-catalog compatibility, revocation and
approved sending. These checks use fixtures. A live owner request on the preceding
candidate proved the model emits an empty optional Gmail query and that the old
adapter rejected it before contacting Google. The exact payload is now a passing
regression. A disposable copy of the verified pre-update study and the upgraded
owner study both completed the same live Gmail read through that preceding
package; unchanged Google behavior is reused rather than relabeled as a new live
check.

The added local-model blocker is also closed for this candidate. A live disposable
study used the installed LM Studio `qwen3.8-27b` Q4_K_S model to plan with `low`,
emit with `none`, create and render **Neon Invaders**, and exercise the chat-side
panel and full-screen controls. The exact per-call receipt is retained at
`artifacts/live-space-invaders-qwen-staged-r2`. It does not change or satisfy the
separate native-notification owner gate.

Astra coordination used the completed ChatGPT handoff **Define Thaddeus magic**,
as confirmed by the owner. No concurrent work was overwritten. Nine original
inbox failures were fixed minimally; the retained tenth repro failure proposed
a stricter quote policy and was withdrawn, not counted as a fixed bug.

Notification source hashes match Q's helper/host dispatch implementation; its
owner-observed scheduled display is reused only for that implementation. The
new helper binaries have different hashes; exact current-package human
observation is not inferred. `handoff-verified.json` records the distinction. The
frontend has since changed and is rechecked by the package suite. E2's callback
preceded sender exit by 328 ms, so cold activation is still OWNER ACTION.
No native probe or machine registration/settings change was made in this pass.

The owner then reproduced one release blocker: an explicit `right now` reminder
was converted to the frozen request timestamp and rejected after clarification.
The current candidate presents `Immediately after approval` and binds the real
due time at approval. A delayed-approval regression dispatches once, while past
non-immediate reminders remain invalid. Focused evidence is
`artifacts/immediate-reminder-20260917/immediate-reminder.trx` (24/24); the clean
package gate and all 52 packaged browser workflows pass.

The final UX blocker moved exact approval review into Chat and added narrowly
bound remembered Allow/Deny choices. Settings presents those choices as one
removable list; removing a rule restores ask-each-time. The focused packaged
workflow verifies both remembered decisions, connector/action scoping behavior,
chat-opened settings, removal, and 390-pixel layout with zero live model calls.

The matrix below combines retained evidence with the September 22 update above;
historical receipts retain their original candidate names. Publication and submission remain separately paused.

## Release feature freeze

The final feature-freeze scope adds one connector-neutral, read-only inbox
watch. It binds one exact mail connector and tool version, checks at a five-minute
interval while the host is running and awake, assesses at most 20 new messages,
and keeps one owner-editable importance instruction. Its recurring-read grant
expires after 30 days or 8,640 checks. Durable message and alert identities prevent
repeat announcements across restarts, including when a new message arrives in an
existing thread. Empty checks make no model call. Connector, authorization, or
classification failures remain visible and pause the watch.

This is the feature boundary until release acceptance is complete. Only fixes for
reproducible bugs, security or data-loss risks, failed acceptance criteria, and
confusing setup inside the agreed scope may enter this release. Other feature or
architecture ideas belong in `BACKLOG.md` and require an explicit owner scope
change before implementation. Publication remains paused. Astra's notification
correction is integrated and owner-observed display is retained. Final packaged
cold-click acceptance remains separate and is not implied by an in-app result.

## Previous broad-suite candidate (O)

The current package, focused evidence and remaining owner actions are recorded
in NON_NOTIFICATION_MVP_HANDOFF.md. The O receipts below retain their original
revision and have not been relabeled as Q results.

- Source revision: `96a7667b71184422ea8df7591cb6d610c99360d4`, clean at publication.
- Windows package:
  `artifacts/portable-local-mvp-release-candidate-20260917-o/thaddeus-win-x64`.
- Package manifest: runtime `win-x64`, 738 packaged files, unsigned development
  package, published September 17, 2026 at 06:26 Mountain Time. The ZIP is
  101,624,222 bytes with SHA256
  `238b19bc324547bee071ba73197d690f1fe0c4b592f6777b930cb6f293983f6b`.
- Package gate:
  `artifacts/local-check-mvp-release-candidate-20260917-o/verified.json` reports
  `passed: true` for publish, 17 extracted native checks, native credential
  cleanup, locked MCP fixture restore/build, and the browser suite, with zero live
  model calls, zero GPU inference, and no worker qualification claim.
- Browser suite:
  `artifacts/local-check-mvp-release-candidate-20260917-o/browser/suite.json`
  reports 46/46 ordinary packaged workflows passed, with a fresh isolated study
  for every case. The current study was untouched and owned-process cleanup
  passed. Five opt-in cases remain outside this suite: live web/model research,
  the native folder picker, native notification, research, and study handoff.
- Connected delegation fixture: the official .NET MCP Streamable HTTP SDK
  advertised `send_email`, `search_email`, and `list_calendar_events`. Packaged
  Chat completed ten synthetic provider calls while the fixture recorded zero
  external calls.
- Owner-study activation:
  `artifacts/activation-delegation-release-20260917-k/activation.json` records a
  verified schema 10 backup, the schema 11 migration, Release K process identity
  on ports 5179/5183, and an exact served client hash. The owner study retained
  36 runs, 38 chats, two artifacts, its existing delegation, Luna provider
  selection, Brave credential, and 999-query allowance. Activation started no
  model, worker, notification probe, or publication action. Release H and the
  pre-migration backup remain the rollback pair.
- Cleanup:
  `artifacts/storage-cleanup-delegation-20260916-b/cleanup.json` records removal
  of five superseded packages and 91 disposable fictional studies. The final
  candidate, rollback, active study, compact evidence, and pinned worker/VM
  inputs remain.
  A later attempt to remove only superseded Candidate M/N package copies and
  their leftover fictional studies was rejected by automatic approval review;
  nothing was removed and no alternate route was attempted. Exact paths and the
  `blocked by policy` outcome are in
  `artifacts/storage-cleanup-mvp-release-20260917-a/blocked.json`.
- External-state audit:
  `artifacts/external-acceptance-state-20260916-a/receipt.json` records the live
  owner-study UI reporting zero MCP connectors, the exact final-package host
  still serving on port 5179, and the current Windows Sandbox boundary. No
  settings, credentials, owner data, model calls, or external services were
  touched. Windows Sandbox is disabled and its executable is absent; this shell
  is not elevated, so a fresh-profile pass cannot be created silently from this
  session.
- Live reminder occurrence:
  `artifacts/reminder-notification-acceptance-20260916-a/receipt.json` records an
  exact reviewed one-shot reminder through the owner study. Occurrence
  `7124f7ca2935f92da75db7ce46d6be419353af9d7312df431f73d841e6583c23`
  dispatched once at 15:42:06 Mountain Time, succeeded, and retained provider
  receipt `windows-shell:31096:1` with `Shell_NotifyIcon` accepted. The result
  remains unread. Human visual confirmation of the toast is still deliberately
  separate from Shell acceptance. The adjacent `diagnostic.json` records that
  this candidate uses a classic `Shell_NotifyIcon` tray balloon without an
  AppUserModelID or WinRT toast registration. A missing per-app registry entry
  is therefore expected, and Windows may suppress the visible balloon even
  after the shell accepts it.
- Modern notification replacement:
  `artifacts/notification-browser-release-20260917-d/screenshots/native-notification-receipt.json`
  binds the final runtime to a normal Chat review, a one-shot reminder, browser
  close before dispatch, and exactly one occurrence. Windows App SDK notification
  37558 reports setting `Enabled`, `activeCount: 7`, and
  `retainedInNotificationCenter: true`; the unread in-app result also remains.
  Windows registers the Thaddeus icon and activation target from Candidate O.

**Historical September 17 notification investigation status.** The original **DEFERRED BY OWNER: Astra handoff; visual acceptance remains
open** boundary is retained as history; the later owner observation above
supersedes display status, but does not close the final cold-click gate. Preserve prior
receipts; scheduler and durable in-app acceptance remain separate gates.

The preserved notification implementation, evidence paths, reproduction boundary,
and coordinated final-pass instructions are in
[`ASTRA_NOTIFICATION_HANDOFF.md`](ASTRA_NOTIFICATION_HANDOFF.md).

## Scheduler implementation map

- `src/Thaddeus.Infrastructure/DelegationScheduler.cs` creates reviewed reminder,
  email, and weekday-brief jobs and claims due occurrences for one dispatch.
- `src/Thaddeus.Host/DelegationPump.cs` is the hosted one-second due-work loop. It
  runs with the host and does not depend on an open browser.
- `src/Thaddeus.Infrastructure/StoreDelegations.cs` persists jobs, grants,
  occurrences, next-run UTC, local timezone semantics, versions, cancellation,
  edits, restart recovery, missed-time decisions, and terminal outcomes.
- `src/Thaddeus.Host/HostDelegationDispatcher.cs` routes the persisted action to
  its approved reminder, Gmail, or bounded-brief dispatcher.

Focused tests cover one-shot execution, a short real-clock host-pump dispatch,
host restart, interrupted/unknown dispatch, cancellation races, stale missed
work, daylight-saving recurrence, connection drift, exact payload preservation,
and authority rotation after editing. Missed one-shot work is recorded rather
than sent late. Unknown external outcomes are retained for inspection and are
never retried automatically.

## Acceptance matrix

| ID | Status | Current evidence | Remaining acceptance |
|---|---|---|---|
| G1 Schedule and send email | PASS (controlled self-email) | E showed and approved the exact sender, recipient, subject/body, 5:00 PM Denver time (`23:00Z`) and one-send authority in Chat. One persisted job dispatched with both browser tabs closed; occurrence `fd4ff556f4c268ea51777c25f1e7f2e5688bbd6871d1e925c4a26bc4b0164baa` completed at `23:00:01.3964889Z`, `dispatchState=accepted`, Gmail `messages.send` ID `1a0cb58faa9aaf54`. Independent read-only Inbox search `connected-75b97ae38ae549a4ba8215514a119c66` returned that same ID with `INBOX` and `UNREAD` labels. Fourteen focused tests cover time/intent guards, restart, drift, uncertainty and no duplicate retry. | No human reading or universal exactly-once claim; cancellation/revocation remain separately tracked. |
| G2 Recurring morning brief | PASS (controlled live occurrence) | On E, one exact weekday 5:20 PM brief ran with the browser closed. Occurrence `49400e123ef24fe09d8bad3554a5924e` completed at `23:20:33.1695697Z`; Gmail and Calendar were both available under 10-item bounded read scopes, the source was not modified, and an unread real result was saved. Chat then reviewed and persisted a pause of the exact job while retaining the result. Backend tests cover DST, unavailable versus empty source, drift, recurrence after failure and grant rotation. | Future daily repetition is covered by scheduler fixtures; no second live day was waited for. Native attention delivery is a separate gate. |
| G3 Reminder delivery | OWNER ACTION | Q's owner-launched scheduled test passed with the browser closed (notification 37570); the owner confirmed visible delivery. E2 reached the exact confirmation URL and was owner-confirmed, but its callback arrived 328 ms before the sender exited. The current candidate also fixes explicit `right now` reminders by scheduling them at approval time; focused tests prove exactly one dispatch after delayed approval. Warm-click evidence and the failed cold-click receipt remain distinct. | Run the current candidate's normal Chat `right now` reminder once and complete one click after the helper exits. |
| G4 Reading to real To-dos | PASS | The final package suite covers upload/public-page/saved-note admission and actual editable source-linked To-do creation. Host read-back, changed-source refusal, unresolved dates, deterministic replay, and interrupted-batch recovery are covered by backend and packaged tests. | A live model pass is optional release QA, not missing host behavior. |
| G5 Conversational management | PASS | The final package suite covers read-only job listing, ambiguous references, ordinal choice, cancel, reminder reschedule, scheduled-email replacement, and recurring-brief pause/resume/edit. Every mutation remains version-bound and review-gated. | Live G1/G2 dispatch is tracked separately. |
| G6 Selective inbox watch | PASS (detection and durable result) | A five-minute, 10-message Gmail read grant was reviewed once. Occurrence 1 at 5:13:31 PM found zero new messages, issued no alert and used no model. A controlled self-email arrived after activation; occurrence 2 at 5:18:42 PM assessed one message, made one model call, and saved one unread attention result with sender, subject, reason and original link; `sourceMutation=false`. After E-to-F host restart, occurrence 3 at 5:23:44 PM advanced the last-successful-check and its alert had a different provider message ID, proving the prior alert was not repeated. The watch is now paused. Connector-neutral eligibility, routine quiet behavior, revoked access and incomplete-page refusal retain focused fixture coverage. | Native notification visual delivery remains a separate owner gate; this agent-launched helper acceptance does not pass it. |
| C1 Natural-language entry | PASS | Ordinary packaged Chat accepts reminder, connected-action, source-to-To-do, and job-management requests. Host checks independently constrain recipient, time, tool, job identity, and mutation. | None for the packaged host contract. |
| C2 Durable execution | PASS | Schema 12 retains the schema-11 persisted versioned jobs, grants, occurrences, inbox-watch progress/alert identities, UTC time, timezone semantics, dispatch intent, next run, and missed state. Package/native checks cover startup, archive/restore, restart, and one-host ownership. | None for the Windows package contract. |
| C3 Real verified actions | PASS (controlled Google work) | To-do writes are read back in package checks. Live Gmail `messages.send` accepted the exact controlled email and a separate read-only Inbox search observed the same provider ID; Calendar read, recurring brief and inbox-watch occurrences have real retained provider/source receipts. Proposals were not counted as success. | No claim of human email reading or visual Windows notification delivery. |
| C4 Bounded delegation grant | OWNER ACTION | Persisted typed grants bind owner, connection/tool fingerprints, target, schedule/version, occurrence count, expiry, external-call allowance, and model allowance. Package and backend tests cover drift, caps, rotation, pause/resume, races, stale versions, OAuth disconnect, partial consent, and revoked refresh credentials. | Exercise Google-side revocation once a live owner-authorized test connector exists. |
| C5 Visible and recoverable failure | PASS | The final package exposes scheduled, paused, working, needs-approval, succeeded, failed, unknown, missed, cancelled, and notification-failed states. Review in Chat preserves drafts; unknown outcomes cannot retry or cancel; notification failure retains the successful unread result. | Live connector recovery remains useful QA but is not needed to prove the UI/state contract. |
| C6 Duplicate-effect safety | PASS | Stable occurrence/operation IDs, claim-before-effect, authorization recheck, UNKNOWN/manual-review recovery, backup revocation, deterministic To-do IDs, and notification no-replay are covered. Package restart/restore checks passed. | No universal exactly-once delivery claim; ambiguous sends require inspection, not an automatic retry. |
| C7 Clean receipts | PASS (controlled live receipts) | The live email, independent Inbox search, quiet and important watch occurrences, and Gmail/Calendar brief expose readable summaries with technical operation IDs, source status, bounded arguments, model use and `sourceMutation=false`; credentials were not exposed in these surfaces. | Reconnect after deliberate revocation is a separate C4 owner gate. |
| C8 Visible and controllable work | PASS | Log -> Upcoming shows action, recurrence/timezone, host state, pause state, result, unread state, and versioned controls. Human-readable review cards keep canonical JSON behind disclosure. Desktop and mobile packaged cases passed. | None for the packaged UI contract. |
| R1 Preserve existing MVP | PASS | All 54 ordinary packaged R3 workflows passed (preview-browser-suite-20260922-r9/suite.json), including inline exact approval and removable remembered choices, Chat, apps, artifacts, notes, To-do, Ideas, Feed, uploads, search, settings, history, backup/restore, token UI, MCP connection UI, and responsive navigation. | Opt-in live/native workflows remain separately scoped. |
| R2 Clean-user Windows path | OWNER ACTION | The current candidate is an unsigned portable development package. It does not bundle a continuously running OpenClaw gateway or preconfigure mail/calendar credentials. | Verify setup from a fresh Windows user profile with owner-authorized test connectors. |
| R3 Freeze and handoff | OWNER ACTION | F's exact source/checksum, focused and packaged receipts, controlled live Gmail/Calendar/watch receipts, bounded Chrome result, unpublished publication handoff, and E rollback are recorded in NON_NOTIFICATION_MVP_HANDOFF.md. | Publication stays paused. Google-side revocation/reconnect, fresh Windows user, final normal-desktop scheduled/click notification acceptance, and a successful Chrome continuation after Pause/Take over/Resume remain open. |

### September 23 Chrome interruption recovery candidate

The source now refuses **Resume AI** before dispatch when a paused Chrome task's
aggregate token allowance is already spent. The in-chat Chrome card hides the
unavailable control and explains that an interrupted model call has unknown usage;
the reserved charge stays recorded. The owner can close that task and request a
new scope review. `BrowserConversationTests` passed 11/11 in Release and the web
production build passed. These are source-level checks only: package F still has
the earlier behavior until a new immutable candidate is built and checked. A
successful same-task live continuation is still open; this recovery explains and
contains the failure rather than proving that continuation.

## External state still required

1. `portable-portable-inbox-bound-20260922-f` is active. Controlled live
   delayed email, independent Inbox observation, bounded Gmail/Calendar brief,
   quiet and important inbox-watch polls, and a post-restart watch poll have
   separate receipts above. Both recurring test jobs are paused. Google-side
   revocation/reconnect remains open because the only live connection is the
   owner's normal account; preserving it takes priority over a destructive
   acceptance probe. Fixture coverage is not live revocation evidence.
2. One normal-desktop click after the notification helper exits. Q scheduled
   dispatch, visible delivery and warm activation have evidence; E2 was clicked
   just before sender exit and does not prove cold activation. Native notification
   acceptance remains deferred for the owner's later desktop session.
3. A fresh Windows user profile for installation/setup acceptance. Windows
   Sandbox is not currently available, so this requires either an owner-created
   local profile or an owner-enabled Sandbox.
4. F's live Chrome review, public-page result and Close control passed. A
   second live task visibly entered Pause, Take over and Resume, but could not
   finish its IANA read after the interrupted model reservation exhausted the
   fixed aggregate allowance. A successful continuation remains open;
   packaged UI and policy fixtures do not substitute for it.

The [official Product Hunt launch
guide](https://producthunt.s.gy/forum-astra-launch-guide) and [challenge
page](https://www.producthunt.com/contests/gpt-6-astra-challenge) were read on
September 16, 2026. The resulting field limits, launch checklist, honest Astra
attribution, gallery plan, and demo route are recorded in
`docs/PRODUCT_HUNT_SUBMISSION_DRAFT.md`.

These are the remaining release observations. More synthetic benchmark passes
would not resolve them.
