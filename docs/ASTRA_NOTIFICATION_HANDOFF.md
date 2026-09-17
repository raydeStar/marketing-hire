# Astra notification handoff

**September 17 owner follow-up: native display works outside Codex's virtualized
Windows environment. The final scheduled/click acceptance remains separate.**

## Confirmed visibility cause

The owner reported both D1 (Windows ID `37560`) and D2 (`37562`) missing. Adding
a Start-menu identity shortcut did not fix it. That shortcut was found under
`AppData/Local/Packages/OpenAI.Codex_2p2nqsd0c76g0/LocalCache/Roaming`, not the
desktop's actual Start menu. Registry reads in the same execution environment
also saw a private registration that the Windows shell could not see.

The owner then ran `artifacts/notification-review-20260917-e/Check-notification.cmd`
from File Explorer. This used the **unchanged Candidate O helper**. Its receipt
shows `registryIdentityExistedBefore: false`, successful registration, Windows
ID `37565`, and retained history. The owner explicitly confirmed **D3 appeared**.
This is the first positive human observation in this investigation. The receipt
and source/hash details are retained in that evidence directory.

Do not change Windows notification preferences or install a replacement
notification framework. Start the product and perform native integration
acceptance from the normal Windows desktop. A child launched from Codex can
inherit redirected registry/AppData behavior even when
`GetCurrentPackageFullName` reports no package. API/storage receipts from that
context cannot establish shell-visible registration. Microsoft's
[MSIX virtualization documentation](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization)
explains the distinction between private and shared writes.

The follow-up also fixes a separate activation bug: the helper subscribes before
`Register`, waits for the actual COM callback, and opens the sending study's
exact local origin with `?view=upcoming`. It does not launch a second host against
the default data directory. The host must still be running; normal browser
authentication remains required. A notification click grants no extra access.
Only loopback HTTP origins with an unprivileged port are accepted. The incoming
callback cannot choose another executable, an external website, or an API action.
This follows Microsoft's [activation lifecycle](https://learn.microsoft.com/en-au/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart?tabs=cs).

The investigation below is historical evidence. Its pending-visibility statements
are superseded by D3; its scheduled-dispatch results remain useful, but they were
not a normal-desktop visual acceptance pass.

The earlier boundary was **DEFERRED BY OWNER: Astra handoff; visual acceptance
remains open.** The owner explicitly resumed the investigation. The correction
has now passed final-candidate technical acceptance; further probes remain
unnecessary unless the exact final notification is not visible.

## September 17 investigation

- Exact active Release C helper, source `7bdee13`, returned ID `37552`, setting
  `Enabled`, and retained history. Windows' read-only notification database and
  PushNotification-Platform events independently confirm storage/delivery to the
  Windows notification subsystem. None proves on-screen presentation.
- The stable app identity's `CustomActivator` referenced CLSID
  `{3DFFE392-E7E1-4B95-915B-510E06946134}`. Its `LocalServer32` still pointed to
  `artifacts/notification-stable-appid-probe-20260916-a/helper/Thaddeus.Notifications.exe`,
  which no longer exists. The SDK reuses that CLSID without refreshing the target.
- Correcting only that activation target to the existing Release C helper gave
  ID `37553`. The original registration and both receipts are preserved in
  `artifacts/notification-astra-20260917-a`. Owner visual confirmation is pending;
  do not label the registration defect as a proven explanation for invisible UI
  until the before/after observation supports that conclusion.
- `src/Thaddeus.Notifications/NotificationRegistration.cs` refreshes the target
  after SDK registration. It preserves the CLSID, app identity, settings and
  retained notification history. The focused registration/scheduler tests pass
  (10 cases), and the helper builds with zero warnings/errors.
- `web/tests/native-notification.spec.ts` is an opt-in acceptance test: synthetic
  planning provider, normal chat/review interface, one real 30-second reminder,
  browser process closed before dispatch, retained unread result and provider
  receipt. It does not declare human visual acceptance from an API result.

## Packaged dispatch evidence

`artifacts/notification-browser-astra-20260917-b/screenshots/native-notification-receipt.json`
records the passing chat/review/browser-close test (36 seconds). Browser closed
at `2026-09-17T11:40:34.527Z`, due `2026-09-17T11:41:03.394Z`
(`Mountain Standard Time`, Windows' Denver zone), claimed at `11:41:04.068Z`,
completed at `11:41:07.032Z`. One occurrence
`d79c0e11bc2cb658266b3a813600cffaa48671df5bd9a46fc04d87ede5d2d054`
was accepted as Windows notification `37554`; the unread result remained, no
next run existed, and dispatch made no model call. Planning used two synthetic
responses and zero live model calls. The first fixture stopped before the due
time because its synthetic response omitted token usage; the corrected fixture
uses the normal reported-usage response. This was a test-fixture error.

Tested package: `artifacts/portable-notification-astra-20260917-a/thaddeus-win-x64`,
source `3b7e74ff2f0552b20ba31fb911c09eceb86f13e2`, clean source capture. ZIP SHA256:
`d9aea04115b7c33e92bf5ba79a363b158c118941c2f67eba62367cffa3b5cfc3`.
The test harness usage correction changes only the test, not packaged runtime
code. The prior Release C package and owner study are preserved.

## Final-candidate integration

Source `96a7667b71184422ea8df7591cb6d610c99360d4` packages the refreshed activation
registration, notification-click host activation, and selective notification
routing for reminders, relevant inbox-watch results, and successful recurring
briefs. Quiet and routine inbox checks do not notify. External provider evidence
remains separate from the nested native-notification receipt, so a notification
failure cannot rewrite a successful provider action or cause a replay.

`artifacts/notification-browser-release-20260917-d/screenshots/native-notification-receipt.json`
records a passing normal Chat/review/browser-close dispatch from Candidate O's
runtime: one occurrence, Windows notification `37558`, setting `Enabled`,
`retainedInNotificationCenter: true`, no dispatch-time model call, and a retained
unread in-app result. `artifacts/notification-browser-release-20260917-d/verified.json`
binds the run to clean package source `96a7667` and records owner-study isolation
and process cleanup. This is technical delivery evidence; `humanObserved` remains
`null`.

Candidate O is
`artifacts/portable-local-mvp-release-candidate-20260917-o/thaddeus-win-x64`.
Its ZIP is 101,624,222 bytes with SHA256
`238b19bc324547bee071ba73197d690f1fe0c4b592f6777b930cb6f293983f6b`.

The owner's host was not running after restart. Automatic approval review
rejected the attempt to launch this tested package with the existing owner
study/profile, giving only `blocked by policy`. No alternative launch was
attempted. A later status check confirmed no listeners on 5179/5183. The tested
package remains available; its notification activation registration points at
its existing helper. Do not delete that package while the registration uses it.

**Still open:** owner observation of `Thaddeus scheduled notification check`
(body begins `Astra acceptance C1`) from the final runtime. Run further diagnosis
only if that exact notification is invisible. The native visual gate and entire
MVP remain incomplete until this observation and the separate Google/fresh-user
gates close.

Automatic approval review also blocked disposal of the two fictional browser
studies and generated source build intermediates, before execution. No alternate
deletion route was attempted. Exact retained paths and blocked-action details are
in `artifacts/notification-astra-20260917-a/blocked-actions.md`. The package
publisher's own staging cleanup succeeded and has a separate receipt.

This handoff preserves the notification implementation and diagnostic trail while
the non-notification MVP work continues. Do not infer visual delivery from a
scheduler success, an unread in-app result, `Shell_NotifyIcon` acceptance, or an
`AppNotificationManager` receipt alone.

## Current implementation

- `src/Thaddeus.Host/WindowsDelegationDispatcher.cs` always saves the reminder as
  an unread in-app result. On Windows it invokes the packaged helper and records
  the helper receipt; notification failure never replays the reminder.
- `src/Thaddeus.Notifications/Program.cs` registers
  `AppNotificationManager`, shows one bounded notification, then verifies that
  Windows assigned an ID and retained it in Notification Center.
- `src/Thaddeus.Notifications/NotificationActivation.cs` validates the notification
  target and opens the sending study's retained results after the COM callback.
- `src/Thaddeus.Host/HostDelegationDispatcher.cs` routes reminders, relevant
  inbox-watch results, and successful briefs through the notification boundary.
  Email sends retain their provider receipt without claiming recipient delivery.
- `src/Thaddeus.Infrastructure/DelegationScheduler.cs` and
  `src/Thaddeus.Infrastructure/StoreDelegations.cs` own occurrence claiming,
  exactly-one application attempts, durable outcomes, and no automatic replay of
  unknown results.

## Preserved evidence

- `artifacts/reminder-notification-acceptance-20260916-a/receipt.json`: a reviewed
  one-shot occurrence dispatched once and remained an unread in-app result. Its
  classic `Shell_NotifyIcon` acceptance was not visually observed.
- `artifacts/notification-owner-observation-20260916-b/receipt.json`: the first
  App SDK helper attempt returned an accepted receipt; owner observation remained
  open.
- `artifacts/notification-final-package-g-20260916-a/receipt.json`: Release G's
  helper returned `AppNotificationManager`, setting `Enabled`, notification ID
  `37539`, `activeCount: 2`, and `retainedInNotificationCenter: true`.
- `artifacts/portable-local-delegation-release-20260916-g/thaddeus-win-x64`:
  known-good active package before the non-notification candidate.

## Reproduction boundary

The remaining gate is a human-observed Windows notification produced by one
reviewed scheduled reminder through the coordinated final candidate. Record the
package identity, occurrence ID, due time/timezone, helper receipt, and the
owner's observation. Preserve the durable in-app result separately.

Do not run further probes or change Windows registration, application identity,
machine notification settings, or packaging before the one final visual
acceptance pass. No notification failure should trigger an automatic second
delivery.
