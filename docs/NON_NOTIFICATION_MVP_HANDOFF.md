# MVP release handoff

**Candidate Q is running; Candidate R adds bounded raven polish. The MVP is not
fully accepted. Publication is paused.**
Live Google, a fresh Windows user, and notification activation after the sending
helper exits remain open. Normal-desktop display and a click while the sender
was still alive are now owner-confirmed.

## Review fixes and evidence

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
refresh/reconnect/disconnect and a narrow Gmail send adapter are implemented.
Briefs and connector-neutral inbox watches reuse bounded grants and the scheduler.
Fixtures do not establish Google-side enablement, real sends or delivery.

The earlier owner-study snapshot had schema 11, 36 runs, 38 chats and
**zero MCP connectors**. A subsequent normal chat request opened the read-only
Google connection card without a model call; it did not connect an account.
The owner authorized a dedicated Google test project, but desktop browser
automation stopped at its URL-policy boundary. No project or client was created.
Live acceptance needs a Desktop OAuth client, enabled
Gmail/Calendar APIs, a consent audience/test account, and any Google MCP preview
enrollment required by the selected services. Enter credentials only in the
host-owned connection card. Approve one exact delayed send to an owner-controlled
recipient, one bounded brief/watch, and a revocation check. Record Gmail acceptance
separately from recipient-observed delivery. Developer success is not public
verification or unrestricted availability.

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
