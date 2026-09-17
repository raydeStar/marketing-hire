# Astra notification handoff

**Astra investigation resumed by owner, September 17. Visual acceptance remains open.**

The earlier boundary was **DEFERRED BY OWNER: Astra handoff; visual acceptance
remains open.** The owner explicitly requested this investigation; other
workstreams must still avoid notification changes or probes.

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
- `src/Thaddeus.Host/HostDelegationDispatcher.cs` routes reminders to the Windows
  dispatcher independently of Gmail and recurring-brief dispatch.
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
machine notification settings, or packaging from the non-notification workstream.
Coordinate any Astra source edits before integration, then run one final visual
acceptance pass. No notification failure should trigger an automatic second
delivery.
