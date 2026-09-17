# MVP release handoff

## Current candidate - September 17 final acceptance

**READY FOR OWNER ACCEPTANCE. Not accepted or published.** Astra's completed
ChatGPT conversation, **Define Thaddeus magic**, was the coordination handoff;
the owner confirmed it was completed work. No competing app update was started.

- Source: `ea911352eaac74277caf9ad8c98350bebc574630` (clean at packaging); schema 11, unsigned Windows x64.
- ZIP: `artifacts/portable-local-stable-google-package-r1/thaddeus-win-x64.zip` (102,389,425 bytes).
- SHA256: `83e25a8f6fee8981bce488bd42cfda75e6d1201a8ee9f675bb35a68f49a3b235`.
- Manifest SHA256: `2c9e28b91d54d2e8fa8295b23343fe144f51d7541d8805d80181d1ea85988818`.
- Package evidence: `artifacts/local-check-stable-google-package-r1/native/verified.json`
  (17 native checks), `artifacts/local-check-stable-google-package-r1/credentials/verified.json`
  (5 credential checks), and `artifacts/stable-google-package-browser-r2/suite.json`
  (52/52 isolated browser workflows). Live model calls and GPU inference remained zero.
- Focused Google/connection regression: 40 passed, none failed/skipped. It covers
  omitted token scopes, multi-permission consent, partial consent, stable Gmail and
  Calendar API reads, old-catalog compatibility, revocation and approved sending.
- Current core evidence: `artifacts/local-check-stable-google-read-r2`, with
  1,103/1,103 backend tests plus protocols, frontend production build,
  notification build and tracked-file secret scan.
- The guarded owner launcher is pinned to this manifest and accepts the currently
  running `portable-local-final-google-r5` manifest as its rollback source. Its
  read-only `-CheckOnly` preflight passed. Earlier handoff and archive receipts
  retain provenance for their original candidates. Tested environment: Windows 11
  Pro 10.0.26200, current Windows user with fresh fictional app data.

The app observed at `localhost:5179` still runs `portable-local-final-google-r5`,
not this candidate. The owner study/vault were not modified during packaging. That
host has the saved Google app setup and connected Gmail read, Gmail send and Calendar
grants. The preceding package, prior tested candidates and owner backups remain.
Do not downgrade the database in place. The owner update helper makes and verifies
a new private backup before replacement and refuses active work/unrelated hosts.

Run from **normal Windows Terminal or File Explorer**, outside Codex:

```powershell
& "C:\Users\Ayric\Documents\ChatGPT\Thaddeus 2.0\artifacts\Start-Thaddeus.cmd"
```

Refresh existing browser tabs afterward. This temporary owner-study wrapper also
reuses the existing Luna development bridge; it is not part of the public ZIP.
Fresh-user testing uses the extracted ZIP's `Start Thaddeus.cmd` and a separately
configured compatible model, following `MODEL_CONNECTIONS.md`. No SDK, Node or
GPU is needed to open the host. Model/network access and an isolated research
worker are separate prerequisites, not silently supplied by the archive.

## Fixed blockers and disclosed limits

- Exact action reviews now render directly in Chat. Owner controls are **Deny
  once**, **Always deny this type**, **Allow once**, and **Always allow this
  type**. Remembered choices are bound to the reviewed action category and, for
  connectors, the connector/tool version. A changed scope asks again.
- Settings -> Permissions & devices contains one plain removable list. Removing
  a choice restores ask-each-time. “Show my approval settings” opens that list
  locally without a model call.
- Focused packaged evidence is
  `artifacts/browser-inline-approval-focused-r2/verified.json` and
  `screenshots/approval-rules-check.json`: inline exact review, 390-pixel layout,
  remembered allow/deny, automatic matching reuse, chat-opened settings, and
  removal restoring review all passed with five synthetic and zero live model
  calls.

The owner-observed Google callback failure was reproduced. OAuth permits a token
response to omit `scope` when the granted scope is identical to the request;
Thaddeus previously treated that omission as zero grants and discarded the
connection. The candidate now retains the exact requested grant in that case,
keeps an explicit partial grant authoritative, and reports the signed-in account.
Chat answers missing Gmail/Calendar capability requests locally, says the
capability is not connected, and offers a dismissible Connect card without using
the model. One consent card can select Gmail read, Gmail send and Calendar
together; successful child capabilities remain independently visible and
disconnectable. The callback page now says verification is still in progress
instead of claiming connection success prematurely. These behaviors passed
focused and packaged fixture tests. A real Google account is still required to
close live consent/read/send acceptance.

Reproduced unreadable/partial inbox data becoming quiet success, loss of earlier
new thread replies, full-batch progress loss, `sender` excluding valid read tools,
and missing assessment usage receipts. Before/after TRX files are retained. The
first repro run had ten failures; one proposed unknown-quote rejection was
withdrawn because it changed the existing provider contract, rather than fixing
a regression. Unknown usage is now explicit, never claimed as zero cost.

Supported empty collections stay quiet without inference. Malformed results,
pagination and full batches pause without advancing progress. The stable Gmail
message adapter supplies precise provider timestamps and message IDs; correct-account
links remain a live acceptance check. No pagination engine, scheduler replacement,
new connector framework or notification transport changes were added. A reproduced
release blocker in the preceding candidate rejected an
explicit `right now` reminder because its frozen request timestamp was no longer
in the future when approved. The corrected review says `Immediately after
approval`; the scheduler binds the actual due time only when that exact review is
approved. Ordinary past reminders remain rejected. Focused reminder, scheduler
and notification tests pass 24/24, including one dispatch exactly once after a
deliberately delayed approval. Evidence is retained in
`artifacts/immediate-reminder-20260917/immediate-reminder.trx`.

Fixtures additionally prove profile persistence/correction/forgetting and that
Identity/Soul/User text cannot bypass connected-tool review. They do not prove
subjective live-model personality behavior. No live Google/model/GPU call was made.

## Consolidated remaining owner acceptance

1. Launch the command above, refresh, and confirm the candidate opens the existing
   study. Allow the safe backup/upgrade to finish; retain its receipt.
2. Use the already-consented Google account with the stable Gmail and Calendar API
   adapters in the next candidate. The Desktop client, test user, Gmail API,
   Calendar API and all three bounded product grants are configured. No Developer
   Preview enrollment or reconnection is required. Use only the owner's approved
   account, controlled recipient and agreed model allowance. Account consent is
   distinct from exact task approval.
3. Through the product, observe one exact delayed send with the browser closed,
   bounded email/calendar brief, quiet inbox check, important-message result and
   correct original-email link. Record provider acceptance separately from
   recipient delivery. Revoke access last and confirm queued work cannot dispatch.
4. Click a scheduled notification from this package only after its sending helper
   has exited, keeping the host running. Display is already owner-confirmed for
   the matching helper implementation; Q's E2 was a warm click, not a cold pass.
   Notification sources/build inputs match Q; newly compiled executable/DLL
   hashes differ. Prior display is implementation evidence, not a claim that
   the owner has seen this exact new binary.
   No standalone notification probe, registration or Windows setting was changed.
5. Test actual new Windows-user setup with the exact ZIP: configure model, get a
   real reply/useful task, close/reopen and safely restart the host with a pending
   reminder. Fresh app data under the current account does not satisfy this gate.

Use `MVP_DELEGATION_MANUAL_QA.md` for exact steps, including cancellation, denied
edits, source-linked To-dos and real host restart. Once these agreed checks pass,
record ACCEPTED FOR WINDOWS PREVIEW, freeze this candidate and stop development.
Until then the mandatory gates stay open; another synthetic suite cannot close them.

## Submission state

`artifacts/publication-final-acceptance-20260917` is now a historical local-only
payload for the preceding candidate. Do not publish it or its checksum. Refresh
the publication payload only after owner acceptance of this candidate. Existing
reviewed fictional gallery images remain usable with their original provenance.
See `PUBLICATION_HANDOFF.md` and `PRODUCT_HUNT_SUBMISSION_DRAFT.md`.
No public repository, release, listing, scheduling or visibility change was made.

## Check commands, retained failures and cleanup

The focused command was `dotnet test tests/Thaddeus.Tests/Thaddeus.Tests.csproj
--no-restore --configuration Release --filter
'FullyQualifiedName~InboxWatchTests|FullyQualifiedName~Delegation|FullyQualifiedName~UserTests|FullyQualifiedName~SoulTests|FullyQualifiedName~IdentityTests|FullyQualifiedName~ContextTests|FullyQualifiedName~ConnectedToolConversationTests|FullyQualifiedName~Google'
--logger 'trx;LogFileName=focused-final.trx' --results-directory
artifacts/final-acceptance-20260917`. It passed 116/116.

`node scripts/check-local.mjs package final-acceptance-20260917` passed publication,
17 extracted native checks, native credential-store cleanup, MCP fixture build
and 51/51 isolated browser cases. Every step exit code is zero. The earlier
`inbox-before.trx` (10 failed/12 passed), `inbox-after.trx` (22/22) and
`focused.trx` (115/115) are retained; the last additional case covers nested
thread pagination. `node scripts/scan-secrets.mjs` and `git diff --check` passed.

No live model/search/Google, GPU, worker, hosted CI or native notification probe
ran. The browser suite deliberately excludes its opt-in live research, native
picker, notification and study-handoff tests; prior evidence remains scoped to
its original inputs. A non-fatal Vite chunk-size warning remains; this is not an
invitation for an optimization cycle.

The archive's initial generic secret-pattern scan matched a marker inside
`System.Private.CoreLib.dll`. The file is byte-identical to installed .NET 10.0.7
and carries a valid Microsoft signature; no owner credential was found. A local
handoff script initially used Windows' default text decoding; the check caught
it and the draft was regenerated as UTF-8 before verification. Neither issue
changed product code or the accepted test inputs.

Free space was 96.60 GiB before the 2 GiB package budget plus 10 GiB reserve,
96.24 GiB after checks. Publisher `scratch-cleanup.json` records removal of staging
bin/obj/node_modules. Native `scratch-cleanup.json` confirms extracted scratch
removed and zero owned processes; browser cases removed their disposable studies.
Compact receipts, captured source, package, rollback, owner data and backups are
retained. Earlier policy-rejected legacy cleanup was not retried or bypassed.

## Historical review fixes and evidence (identities below are not current)


- **September 17 ordinary chat outage recovered:** the owner study still ran U,
  with model endpoint `http://127.0.0.1:5184/v1` saved correctly, but no process
  listening on that port. The host-only temporary launcher omitted the separate
  Luna development bridge. Restored the existing bridge at the unchanged endpoint
  using the already signed-in Codex CLI; no provider setting or credential was
  changed, and the host was not restarted. Nine synthetic bridge/protocol tests
  passed. One targeted live retry through the product UI completed successfully:
  `3180918c5a8245fd985d907f5be15249` replied to the owner's swallow question.
  Exact receipt: `artifacts/chat-recovery-20260917-z/verified.json`.
  The temporary `artifacts/Start-Thaddeus.cmd` now runs its existing safe app
  launcher followed by `chat-recovery-20260917-z/Ensure-Luna.ps1`. The latter only
  starts the recorded Luna configuration, checks captured script hashes and CLI
  sign-in, uses a hidden process, refuses other port owners, and reuses its own
  running bridge. Re-entry preserved the same PID/start time; see
  `reopen-check.json`. Current bridge PID is 38456; retain that folder's `bridge`
  scripts and manifest as active runtime inputs. This is an owner development
  launcher repair, not bundled model access or a new production startup service.
  Ordinary chat works on U without rebuilding; the pending Y2 desktop upgrade
  remains necessary for the inline-card UI and Google consent-start repair.

- **September 17 inline connection card:** Google/service setup now belongs to
  the relevant assistant reply and scrolls with chat instead of occupying the
  composer. The compact rounded card keeps permission review, secure import,
  consent status/errors and one-time setup. New cards leave typing focus in the
  composer; closing and reopening a card preserves its message association and
  provides keyboard focus on explicit reopen. Credentials remain host-only.
  Candidate Y2 source `5b45d934f915e128b254dccb55e217ae78815ee4`, package
  `artifacts/portable-mvp-inline-connection-20260917-y2`, ZIP SHA-256
  `f4e40235cf8285bb2275264a6f9b1851b8ceab28331a49bf8a45fb5dbe26c042`.
  Both packaged connection browser tests pass in
  `inline-connection-20260917-y2-browser/verified.json`, with desktop/mobile
  screenshots. Checks cover message association while chat continues, close/
  reopen, focus, secure import/reload, a simulated consent denial and secret-free
  exports. No live Google, external model, GPU or notification call was made.
  Earlier Y passed these checks, then screenshot review prompted focus polish.
  Build intermediates and owned browser/host processes were cleaned. Automatic
  approval review rejected the subsequent disposable-file removal command as
  "blocked by policy"; no alternate deletion was attempted. Exact retained paths
  are in `artifacts/inline-connection-20260917-y2-owner/CLEANUP.md`.
  `artifacts/Start-Thaddeus.cmd` now targets Y2; read-only preflight against U
  passed in `inline-connection-20260917-y2-owner/preflight.json`. It did not restart
  or authenticate to the owner host. Run that command in normal Windows Terminal,
  refresh the app, and reopen Connect Google from its chat reply. Saved study and
  Google app registration are reused. Live Google acceptance remains open.

- **September 17 Google consent startup:** the owner's downloaded Desktop app
  setup was imported successfully into the existing host vault. A live connection
  attempt exposed anonymous MCP discovery skipping OAuth entirely. Candidate X
  starts Google's maintained PKCE flow explicitly before catalogue discovery;
  it preserves scope review, verified account identity and host credential custody.
  Source `aff90ed`, package `artifacts/portable-mvp-google-signin-20260917-x`, ZIP
  SHA-256 `ab045359a76666edeac8d87503c57076e0c0039721c45596948746d2abd2e60b`.
  Forty-one focused Google/connection checks pass in
  `google-explicit-signin-20260917-x/google-verified.trx`; seventeen extracted
  Windows package checks pass in `google-explicit-signin-20260917-x-native/verified.json`.
  Publisher intermediates and extracted scratch were removed with cleanup receipts.
  These use the current Windows user with fresh fictional app data, not a fresh
  Windows user or a clean machine. No native notification probe or live mail call
  was made. At this stage, `artifacts/Start-Thaddeus.cmd` targeted X and passed read-only
  preflight against the running U owner study; the prior verified update logic is
  unchanged. Run it from normal Windows Terminal, refresh the app, then choose
  Continue with Google. The one-time app setup is already saved. Live consent and
  service enablement/terms approval remain open; this is not a connected-account pass.

- **September 17 reminder presentation:** the owner's clipped screenshot was a
  chat confirmation beneath a fixed heading. The heading now scrolls with the
  transcript; Latest messages stays anchored in the visible history area. The
  saved reminder dialog leads with its title, exact reminder message and time,
  keeps delivery errors visible, and collapses technical receipts. Mark result
  read updates the open dialog without dispatching again. Completed replaces
  the misleading generic Delivered label; provider acceptance still does not
  establish visual Windows delivery or recipient delivery.
  Candidate W is `artifacts/portable-mvp-reminder-presentation-20260917-w`, source
  `cb10ef28cf92f5f35bbfce8f49f8eb698aa36ed4`, ZIP SHA-256
  `0922d38cf0b84c2bf3a5b55c887b701cb0f4187b8161ce8829ce2309316fbb6c`.
  Existing delegated-work and notification-link checks passed in
  `reminder-presentation-20260917-w-browser-r3/browser-results.json`; both new
  presentation checks passed in `reminder-presentation-20260917-w-browser-r4/verified.json`.
  Desktop/mobile screenshots are beside that receipt. Earlier failed test
  attempts are retained: their selectors needed the new exact heading and the
  existing mobile log navigation. Runtime package W was unchanged between runs.
  All four browser fixture studies were cleaned after owned processes exited.
  These are synthetic UI checks, with no native toast, external model call or
  owner data mutation.
  The temporary `artifacts/Start-Thaddeus.cmd` then targeted W; the unchanged safe
  upgrade flow passed against U in `desktop-test-launcher-20260917-w/verified.json`.
  It preserves the owner study and makes a verified backup. Refresh existing
  tabs after running it: changing the host does not replace already-loaded JS.
  Latest action: run that command from the normal desktop, then inspect the
  reminder under Activity log > Upcoming. Native acceptance gates below remain
  separate; no registration or notification helper changes were made here.

- **Notification cause confirmed:** agent-launched D1/D2 wrote registration inside
  Codex's private Windows environment and were invisible. The owner launched the
  unchanged Candidate O helper from File Explorer and confirmed **D3 appeared**.
  Its receipt shows no desktop registration existed beforehand. Evidence:
  `artifacts/notification-review-20260917-e/owner-observed.json`, Windows ID 37565.
  No Windows notification preference was changed.
- **Click handling fixed:** the helper registers its event before COM registration,
  waits for the actual invocation, and opens the sending study's Upcoming results.
  It no longer launches a host against a guessed default data directory, and has
  no console window. Only local origins are allowed; normal browser authentication
  remains required and the host must still be running.
- **Desktop follow-up:** Q's real scheduled test passed with the browser closed
  (`notification-review-20260917-q-desktop/verified.json`, notification 37570).
  The owner reported visible delivery and a redirect to the sign-in page. E1
  received no callback; its failed receipt is preserved. E2 reached the exact
  confirmation URL and the owner confirmed it, but arrived 328 ms before the
  sender exited. `notification-review-20260917-q-click-e2/receipt.json` correctly
  leaves cold activation failed/unverified. Do not relabel it as a cold-click pass.
  The owner then launched Q against the existing study. The in-app browser was
  also unlocked through a one-use launch ticket; reload retained sign-in.
  Windows opens links in the default browser, whose cookies are separate from
  the Codex in-app browser. This is host/browser authentication, not OpenClaw.
- **Google permission display fixed:** receipts retain all scopes Google returned,
  rather than hiding an additional grant. Requested scopes and permitted tools
  remain restricted. The failing reproducer is `scope-before.trx`; seven Google
  safety fixtures pass in `google-review.trx` in the same evidence directory.
  These are synthetic tests, not live email evidence.
- **Focused regression checks:** `notification-final.trx` records 21 passing
  notification/scheduler tests. Three packaged browser workflows passed for
  notification links, delegation controls/results and navigation. Their receipt
  is `artifacts/notification-review-20260917-p-browser/verified.json`.
  `notification-review-20260917-e/reused-browser-evidence.json` binds unchanged
  notification source and byte-identical served client assets to Q. The temporary
  P binaries and fictional browser study were then removed.
- **Final package:** `artifacts/notification-review-20260917-q-native/verified.json`
  records 17 passing extracted-package checks on Windows 11 Pro 10.0.26200, current
  user with fresh isolated app data. Owned processes/extracted scratch were removed.
- Candidate O's previous 1,017-test backend and 46-workflow browser evidence remains
  preserved for unchanged behavior; it is not relabeled as a new full Q run.
- The unpublished download page had a stale checksum and incorrect bare-executable
  launch instructions. Its README, archive checksum and manifest now agree.

## Existing scheduler

`src/Thaddeus.Infrastructure/StoreDelegations.cs` persists jobs, exact grants,
versions, occurrences, cancellation/replacement and inbox progress/alert IDs.
`DelegationScheduler.cs` resolves and claims due work. The existing host
`src/Thaddeus.Host/DelegationPump.cs` checks independently of browser tabs, and
`HostDelegationDispatcher.cs` routes approved actions.

Existing fixtures and real-clock receipts cover one-shot execution, recurring
triggers, explicit UTC/timezone, restart recovery, persisted cancellation and
browser-independent dispatch. Late time-sensitive one-shots become missed;
unknown external outcomes are retained without automatic retries. No replacement
scheduler was introduced. Native visibility remains a separate check.

## Google and inbox watch

Desktop OAuth, loopback callback, PKCE/state validation, credential custody,
refresh/reconnect/disconnect, stable read-only Gmail/Calendar adapters and a narrow
Gmail send adapter are implemented. Briefs and connector-neutral inbox watches
reuse bounded grants and the scheduler. Fixtures do not establish real sends,
recipient delivery, or controlled live use of the new package.

The earlier owner-study snapshot had schema 11, 36 runs, 38 chats and
**zero MCP connectors**. A subsequent normal chat request opened the read-only
Google connection card without a model call; it did not connect an account.
The owner authorized a dedicated Google test project and signed in on September
17. `Thaddeus MVP Test` (`thaddeus-mvp-test-20260917`) now exists. The Google Auth
Platform app is named Thaddeus, External/Testing, with the owner as its sole test
user. The owner explicitly approved Google's API Services User Data Policy;
Google confirmed OAuth configuration creation. The Gmail API is enabled, and
the `Thaddeus Windows test` Desktop OAuth client was created. Scope declarations
match the implemented sign-in identity, Gmail read/send and Calendar read/freebusy
products; Google confirmed the save. Declaring scopes is not an account grant,
and read/send remain separate product authorizations.

The owner downloaded the Desktop setup file, and the secure import succeeded in
the running candidate: the UI confirmed saved setup and enabled Continue with
Google. No file content was copied into chat, source control or worker inputs by
the import. The first real Connect attempt failed before opening a browser with
"Google authorization completed without a reusable token."
Google's public MCP catalogue had returned successfully without an OAuth challenge.
The fix starts Google's maintained PKCE flow explicitly, validates single-use
state/issuer, and requires a refresh credential and the selected scopes before
committing tools. Canonical Google identity scope URLs count as the matching OIDC
scope without hiding the actual grant in the receipt.

Evidence: `artifacts/google-explicit-signin-20260917-x/anonymous-discovery-before.trx`
reproduces the exact failure against the old implementation. Later controlled
consent connected Gmail send and granted Gmail read and Calendar scopes, but actual
reads failed because Google's remote MCP servers require Workspace Developer Preview
enrollment. The stable REST adapters remove that release dependency without changing
the provider-neutral broker or stored grants. Focused Google/connection tests cover
PKCE, callback replay, denial, partial grants, stable read schemas, revocation and
approved sending. The new package still needs controlled live Gmail/Calendar reads.
Keep the downloaded setup file out of chat, source control and worker inputs. Approve
one exact delayed send to an owner-controlled recipient, one bounded brief/watch,
and a revocation check. Record Gmail acceptance separately from recipient-observed
delivery. Developer success is not public verification or unrestricted availability.

## Package and preservation

- Source: `e36a2c58add6e1f0d33544d36f17a68acf2b4475`, clean at capture.
- Package: `artifacts/portable-mvp-reviewed-20260917-q/thaddeus-win-x64`.
- ZIP: 101,627,076 bytes; SHA256
  `804a518920dacaafa36b74ecd5a3a564093061eeea0673b179ac9acd8a24e0a3`.
- Prepared publication materials: `artifacts/publication-handoff-20260917-f`.
  Nothing was published. Archive, checksum, site README and manifest agree.
- Owner data/credentials, Candidate O, Release H and its rollback backup remain.
  Publisher staging and native extractions were cleaned. This review removed its
  unsuccessful private shortcut, intermediate P binaries/ZIP and fictional browser
  study, retaining manifests and receipts. See `notification-review-20260917-e/cleanup.json`.
  Earlier policy-rejected cleanup targets were not retried.

## September 17 raven polish

The owner requested more expressive, child-friendly behavior while keeping major
features and architecture frozen. Candidate R changes the existing SVG/CSS bird:
a clearer eye, curious tilt while composing, a brief greeting wave, thinking dots,
and a one-time completion hop with two small sparks. Resting/error states stay
calm. Reduced motion and the existing log/detail click remain intact. No model,
GPU, sound, dependency, integration or task behavior was added.

- R source: `f63703ad237fdbd5d7d7a25a475247d14a80501b`, clean at package capture.
- Package: `artifacts/portable-mvp-raven-20260917-r/thaddeus-win-x64`.
- ZIP: 101,629,025 bytes; SHA256
  `2b21178b089cef6332baab0b1cf928026761a5b10580024129aedde9cc005469`.
- Frontend typecheck/build passed during publication. The packaged raven workflow
  passed at `artifacts/raven-polish-20260917-r/browser-recheck/verified.json`:
  composing state, work poses, keyboard task opening, reduced motion, task-state
  precedence, 390/1440 layouts, offline state and no unexpected writes/history
  changes. It used a fictional study and synthetic provider. The first run's
  failed locator targeted the intentionally hidden header; it was corrected to
  the visible activity companion. Its failed receipt is retained.
- `artifacts/raven-polish-20260917-r/index.html` is an interactive appearance
  preview rendered from the real component and stylesheet. The preview's
  expression selection and pause/resume controls were checked in the in-app
  browser. These controls are preview-only, not new product features.
- R has not replaced the running owner Q process. Q's native/Google acceptance
  boundaries still apply; this focused browser check is not fresh-user acceptance
  or a new full native package run. Keep Q and the existing rollback available.
- Publisher staging was removed (`scratch-cleanup.json`). Automatic approval
  review rejected deletion of the two new fictional browser studies with
  "blocked by policy". They remain at the exact paths in
  `artifacts/raven-polish-20260917-r/cleanup-blocked.json`; no alternate deletion
  was attempted. The previously blocked Q desktop fixture also remains.

## Exact next actions

### September 17 Google setup simplification (candidate U)

- Asking to connect Gmail or Calendar opens a focused card with the requested
  permission and **Continue with Google**. The activity drawer closes so it does
  not dim setup. Asking for a different service refreshes the card's permission.
- One-time developer setup imports Google's Desktop-app credentials JSON directly
  into the host credential vault. Normal sign-in sends only the selected product
  and settings version; the host supplies the saved client details. Setup survives
  restart and is reusable across Google services. Imported URLs cannot redirect
  credentials, and web/service-account files are refused. System-browser launch
  is restricted to Google's HTTPS account host; failure offers an explicit link.
- Evidence: 34 focused backend checks in
  `artifacts/google-setup-20260917-t-tests/google-setup.trx`; the changed chat copy
  was rechecked in `setup-copy.trx`. Two packaged browser tests passed against U:
  `artifacts/google-connect-20260917-u-browser/verified.json`. They cover missing
  setup, invalid file rejection, real fixture-vault import/removal, reload/reopen,
  prompted permission changes, denied-consent recovery, no credentials in exports,
  and a narrow viewport. Google consent/status are simulated; no live account,
  Google API, model, worker or GPU was used. This is fresh app data under the
  existing Windows user, not fresh-user or clean-machine acceptance.
- Source: `b40526da8df7d05dc1b687ad639aede66f611a25`, clean at capture.
  Package: `artifacts/portable-mvp-google-connect-20260917-u/thaddeus-win-x64`.
  ZIP SHA-256: `5256ad6156e71400e255dbf2e1c66333d264e4fcf4985b3657ed244d1d83143b`.
  U includes the prior retry correction and supersedes S/T as the activation
  candidate. The owner host remains Q (PID 17820, localhost 5179 when checked).
- Both browser fixtures exited and removed their vault credentials. Package
  staging intermediates were cleaned, and `dotnet clean` removed root test-build
  outputs. Automatic approval review blocked deletion of the T and U disposable
  browser studies with **blocked by policy**; each has `cleanup-blocked.json`.
  No alternate deletion was attempted. Owner-removable paths, after any fixture
  review: `artifacts/google-setup-20260917-t-browser/study` and
  `artifacts/google-connect-20260917-u-browser/study`. The intermediate T package
  is retained pending owner cleanup; it is not the active host or rollback.
- Activation now has an owner-requested temporary helper:
  `artifacts/Start-Thaddeus.cmd`, run from a normal Windows terminal after saving
  unsaved note edits. It verifies the pinned packages and running host/profile,
  closes Q through the product's maintenance API, makes an offline verified
  backup with the product CLI, then starts U against the same `.data` profile and
  opens the signed-in browser. It refuses busy work and unrelated port owners,
  and reuses U if already running. No maintenance-menu steps are required.
  It has been prepared, not executed against the owner study.
- Temporary helper verification:
  `artifacts/desktop-test-launcher-20260917-v5/verified.json` binds script SHA-256
  `aa5a66c0ebb86a454bca2e0ad381f2cb72f43ad62136026df709d9d14869440a`.
  The actual Q-to-U fixture transition preserved chats, runs and pages; backup,
  repeat-launch reuse, stopped-host startup, unrelated-port refusal and output
  credential checks passed. All owned fixture hosts exited and disposable V
  studies/backups were removed with cleanup receipts. Browser opening and visible
  notification delivery were not exercised by this `-NoBrowser` fixture.
  The owner host remained Q, PID 17820 when checked. No package rebuild, GPU,
  model call or notification probe was required.
- This helper is explicitly disposable after notification acceptance and the
  normal release launch path are settled. It is not a permanent updater, new
  development workstream or exception to the feature freeze. Preserve backups
  and compact receipts when retiring the helper.
- Superseded Google status: the dedicated test project, Desktop app registration,
  test user, Gmail API, Calendar API and product consents are complete. Live use
  proved Google's remote Gmail/Calendar MCP servers additionally require Workspace
  Developer Preview enrollment, so the release candidate replaces only those
  built-in reads with narrow stable REST adapters. Generic MCP support remains.
  Controlled live read/send/watch acceptance, native cold-click and actual
  fresh-Windows-user gates remain open. No production client is bundled;
  publication and shutdown stay paused.
- Exact next action: activate the newer stable-Google candidate recorded below,
  then run the controlled live Gmail read and Calendar read before the remaining
  delayed-send, watch, fresh-user and notification acceptance cases.

### September 17 connection-retry correction

Candidate S fixes an owner-observed dead end in a saved Google connection request.
The deterministic connection reply had already opened the host-owned secure form,
but ordinary chat controls still offered model Retry. Two such legacy attempts
were recorded and the last stopped at its model-call limit. The UI now offers
**Continue connection setup** for the whole reply family, including those existing
failed attempts, and explains that no model retry is needed. The server also
rejects direct retry requests for connection-setup families, so a stale page or
API client cannot spend more model calls on them.

- Source: `96da8e7b38accb30c640fee930797015ae987975`, clean at package capture.
- Package: `artifacts/portable-mvp-connection-retry-20260917-s/thaddeus-win-x64`.
- ZIP SHA256: `80bab740d23b13ac5715524ea591e2083264267910653c3d219857c13a78a501`.
- Twelve focused retry/API tests, frontend typecheck/build, tracked-file secret
  scan, and packaged `connection-chat.spec.ts` passed. The browser receipt is
  `artifacts/connection-retry-20260917-s-browser/verified.json`; its fictional
  study did not touch owner data and its owned processes exited.
- Automatic approval review rejected deletion of that disposable browser study
  with "blocked by policy" after its path and process state were checked. It is
  retained and recorded in the adjacent `cleanup-blocked.json`; no workaround
  was attempted.
- Candidate Q remains the running known-good host. To activate S without repeating
  the Codex-launched Windows notification identity problem, close Q through
  Settings > Storage & backups > Review maintenance, then run
  `artifacts/connection-retry-20260917-s-owner/Open-fixed-study.cmd` from File
  Explorer. The wrapper verifies the package and existing launch profile and
  refuses while Q still owns its ports. It preserves Q as rollback.

1. Retain the passing Q scheduled test and E2 warm-click observation. For the
   remaining cold-click gate, use a fresh label/evidence directory with the
   existing click runner from a normal desktop terminal and click only after
   **Click now**. Do not rerun the already-passing scheduled test.
2. The existing owner study is running from Q. To reopen it, use
   `artifacts/notification-review-20260917-e/Open-existing-study.cmd` from File
   Explorer. It validates Q and the existing `.data` launch profile, then opens
   localhost 5179 without creating a replacement study. The owner ran it
   successfully. Earlier automatic review rejected an agent owner-host launch;
   the rejection remains recorded in the Astra handoff.
3. Create the owner-authorized Google test project/client, then complete the
   controlled-recipient pass above. Microsoft/GitHub browser-consent adapters
   are not implemented by the generic bearer-token MCP connection form.
4. Use an actual fresh Windows account for setup, useful work, history,
   close/reopen and scheduled dispatch. Fresh app-data tests under the existing
   user do not pass that gate. No account, password, OS policy or VM was changed.

See `ASTRA_NOTIFICATION_HANDOFF.md` for the precise failures and confirmed desktop
environment diagnosis. The computer stays on; publication stays paused.

## September 17 stable Google read correction and frozen candidate

The owner completed Gmail read, Gmail send and Calendar consent. The preceding
host recorded the correct account and scopes, but its first real Gmail read proved
that Google's remote Workspace MCP endpoint requires separate Developer Preview
enrollment. That release blocker is removed with the smallest provider-specific
change: built-in Google reads now use stable official Gmail and Calendar REST APIs
behind the existing `IConnectedToolBroker`. Generic remote Streamable HTTP MCP,
host credential custody, exact review, scheduler and approval architecture remain
unchanged. Previously stored Google accounts and refresh grants are reused after
restart; their old remote catalog is exposed as the new bounded stable tools, and
the connection version invalidates obsolete reviewed tool bindings.

- Frozen source: `ea911352eaac74277caf9ad8c98350bebc574630`.
- Package: `artifacts/portable-local-stable-google-package-r1/thaddeus-win-x64`.
- ZIP: `artifacts/portable-local-stable-google-package-r1/thaddeus-win-x64.zip`,
  102,389,425 bytes, SHA-256
  `83e25a8f6fee8981bce488bd42cfda75e6d1201a8ee9f675bb35a68f49a3b235`.
- Package manifest SHA-256:
  `2c9e28b91d54d2e8fa8295b23343fe144f51d7541d8805d80181d1ea85988818`;
  it records clean `win-x64` source `ea91135` and 746 files.
- Exact normal-desktop launch command from this repository:
  `artifacts\Start-Thaddeus.cmd`. Its owner launcher validates every package file,
  the existing launch profile and current rollback, then uses the product's normal
  maintenance API and verified backup before starting the candidate. `-CheckOnly`
  passed without login, shutdown, backup or launch. The owner host was deliberately
  left on `portable-local-final-google-r5` pending this action.
- `artifacts/local-check-stable-google-read-r2` passes the secret scan, locked
  restores, notification Release build, 1,103/1,103 backend tests, protocols and
  frontend production build. The focused Google/connection subset passes 40/40.
- `artifacts/local-check-stable-google-package-r1/native/verified.json` passes all
  17 extracted native checks; `credentials/verified.json` passes all five Windows
  Credential Manager checks and removes its fictional entries. The initial package
  browser run retained one unrelated 15-second background-task timing failure after
  four passes. Its exact fresh rerun passed, and
  `artifacts/stable-google-package-browser-r2/suite.json` passes all 52/52 ordinary
  packaged workflows with isolated studies and cleanup.
- Publisher staging and build intermediates were removed by the established
  publisher. Automatic approval review rejected deletion of two remaining
  fictional fixture studies with `blocked by policy`; no workaround was attempted.
  Owner-removable paths are
  `artifacts/local-check-stable-google-package-r1/browser/05-background-chat-10/study`
  and `artifacts/stable-google-package-background-retry-r1/study`.
- Live status remains honest: consent and account grants passed on the preceding
  host; controlled Gmail and Calendar reads through this exact candidate have not
  yet been observed. Delayed send, brief/watch, revocation, exact-package cold
  notification click and fresh-Windows-user setup remain owner actions. No live
  model/GPU call, owner-study mutation, publication, submission or shutdown occurred
  in this correction pass.
- Exact next action: from a normal Windows Terminal or File Explorer, run
  `artifacts\Start-Thaddeus.cmd`; refresh Thaddeus, ask it to read the latest Gmail
  message and today's Calendar, then continue the remaining checklist in
  `MVP_DELEGATION_MANUAL_QA.md`. Stop development unless that acceptance reveals a
  reproducible agreed-scope release blocker.

## September 17 live Gmail empty-query correction

The owner activated the stable-Google candidate and requested one latest Gmail
message. Receipt `375b33fbe64c4a22b3b2b544ed493277` proves that the model chose the bounded
`gmail.messages.search` tool with `{"query":"","maxResults":1,"unreadOnly":false}`,
the remembered owner approval dispatched it, and the host rejected it with an
`ArgumentException` before any Gmail response was accepted. The cause was the
adapter treating an empty optional string as invalid. Optional empty strings now
normalize to omission; required strings still fail through the existing required
field check.

- Frozen source: `0d0e463acac3b8621267eb8cbb1ee02ff790c00a`.
- Package: `artifacts/portable-local-gmail-empty-query-package-r1/thaddeus-win-x64`.
- ZIP: `artifacts/portable-local-gmail-empty-query-package-r1/thaddeus-win-x64.zip`,
  102,389,554 bytes, SHA-256
  `912b7ad53147c74a49cf7df08a6438031c1a477af7d716d854c51d091bf9b81f`.
- Package manifest SHA-256:
  `1dc830726d8b1d6bcef2e4870111ec9d2eb444a8748de2cbfa993d6b5a9ebf3a`;
  it records clean `win-x64` source `0d0e463` and 746 files.
- `artifacts/local-check-gmail-empty-query-fix-r1` passes the secret scan,
  locked restores, notification Release build, 1,104/1,104 backend tests,
  protocols and frontend production build. The focused Google/OAuth/connected-tool
  set passes 44/44, including the exact live argument shape.
- `artifacts/local-check-gmail-empty-query-package-r1` passes publication,
  17 extracted native checks, five Windows Credential Manager checks, and all
  52/52 ordinary packaged browser workflows. It made no live model or Google call.
- `artifacts\Start-Thaddeus.cmd` now validates this package and retains the
  stable-Google package as rollback. `-CheckOnly` passed without login, backup,
  shutdown or launch. The existing owner host and data were left untouched.
- Owner-study activation remains open until the owner launches this candidate
  from a normal desktop terminal. The exact-package live Gmail read passed in the
  isolated acceptance below. Publication remains paused; unchanged notification
  and broader acceptance evidence is not relabeled.

### Exact-package live Gmail acceptance

The same candidate was launched on alternate loopback ports against a disposable
copy of the verified pre-update owner study. The prior exact request was approved
once, the saved Google authorization refreshed successfully, and run
`fe8e0ef443b94f9daadb0d42c0b8861d` reached `succeeded` with a non-error
`gmail.messages.search` receipt. The returned envelope contained the documented
`messages`, `nextPageToken`, `resultSizeEstimate` and `readOnly` fields. Sanitized
evidence is retained at `artifacts/live-gmail-empty-query-acceptance-r2.json`; it
contains no email content, account address or credential. This proves the bounded
live Gmail read for the exact packaged binary. It does not prove Calendar, delayed
send, recurring brief/watch, revocation, notifications or fresh-user setup.

The isolated host exited and ports 5279/5283 were released. Automatic approval
review rejected recursive deletion of the two disposable folders, so no alternate
deletion method was used. The owner should manually delete
`artifacts/live-gmail-empty-query-acceptance-r2`, which contains the private study
copy, and `artifacts/live-gmail-empty-query-acceptance-r1`, which contains the empty
failed setup. The compact sanitized JSON outside those folders should remain.
