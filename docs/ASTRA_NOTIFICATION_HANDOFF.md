# Astra notification handoff

**DEFERRED BY OWNER: Astra handoff; visual acceptance remains open.**

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
