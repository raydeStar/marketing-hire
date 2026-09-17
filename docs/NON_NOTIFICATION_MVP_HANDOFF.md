# MVP release handoff

**Candidate Q is packaged. The MVP is not fully accepted. Publication is paused.**
Live Google, a fresh Windows user, and the final normal-desktop scheduled/click
pass remain open.

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

The current owner study was inspected read-only: schema 11, 36 runs, 38 chats,
**zero MCP connectors**. Live acceptance needs a Desktop OAuth client, enabled
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

## Exact next actions

1. Run `artifacts/notification-review-20260917-e/Finish-desktop-notification-check.cmd`
   from **File Explorer**, not a Codex terminal. It now targets Q, schedules one
   synthetic reminder with the browser closed, then requests a cold click on E1.
   Confirm the scheduled card is visible; the click fixture records its actual
   local destination. This manual run has not yet been received.
2. To return to the existing owner study, use
   `artifacts/notification-review-20260917-e/Open-existing-study.cmd` from File
   Explorer. It validates Q and the existing `.data` launch profile, then opens
   localhost 5179 without creating a replacement study. It is prepared, not
   agent-executed. Earlier automatic review rejected an owner-host launch;
   the rejection remains recorded in the Astra handoff.
3. Complete Google setup and the controlled-recipient pass above. The owner has
   been asked whether a Desktop OAuth client and test account are ready.
4. Use an actual fresh Windows account for setup, useful work, history,
   close/reopen and scheduled dispatch. Fresh app-data tests under the existing
   user do not pass that gate. No account, password, OS policy or VM was changed.

See `ASTRA_NOTIFICATION_HANDOFF.md` for the precise failures and confirmed desktop
environment diagnosis. The computer stays on; publication stays paused.
