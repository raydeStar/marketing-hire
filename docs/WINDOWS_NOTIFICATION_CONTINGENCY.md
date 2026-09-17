# Windows notification contingency

**Astra investigation resumed September 17; visual acceptance remains open.** Preserve
this diagnostic history and the existing implementation. Do not run more probes,
change Windows registration/identity, alter machine settings, or rebuild a package
solely to retry notifications from the non-notification MVP workstream.

This contingency was executed after the owner checked Windows Notification
Center and confirmed that the September 16 `Shell_NotifyIcon` balloon was not
visible.

## Current evidence

The prior package scheduled and dispatched occurrence
`7124f7ca2935f92da75db7ce46d6be419353af9d7312df431f73d841e6583c23`
once at its exact due time. Windows accepted `Shell_NotifyIcon`, and Thaddeus
retained an unread successful result. The current mechanism is a classic tray
balloon with no AppUserModelID or WinRT app-notification registration. Shell
acceptance therefore does not prove that Windows presented the balloon. The
owner checked the correct Windows Notification Center surface and did not find a
Thaddeus entry, closing the classic-balloon path as a failure.

Receipts are retained under
`artifacts/reminder-notification-acceptance-20260916-a`.

## Replacement implemented

The Windows package now includes a separate `Thaddeus.Notifications.exe` helper
using `AppNotificationManager` from the pinned Windows App SDK. The host sends a
bounded title and message over the helper's standard input, receives a bounded
JSON receipt, and stores the notification identifier, mechanism, and Windows
setting without exposing message content on the process command line.

The replacement preserves the scheduler contract: the reminder is saved as an
unread in-app result even if Windows refuses presentation, and an uncertain
presentation is never replayed automatically.

The clean final helper now waits for Windows and calls `GetAllAsync`, which
Microsoft defines as the notifications currently displayed in Action Center.
It reports success only when its assigned notification identifier is present.
An earlier note claimed owner visual confirmation of the stable
`raydeStar.Thaddeus` path, but the retained package receipt has no corresponding
visual observation. That claim is not sufficient to pass acceptance.
Release G then sent notification 37539 from its exact packaged helper; Windows
reported `activeCount: 2` and `retainedInNotificationCenter: true`, and updated
the registered Thaddeus icon to the release G package. The focused delegation
tests passed. G3 is now deferred by the owner; Astra owns the remaining visual
acceptance and coordinated final-pass evidence.

Microsoft currently recommends `AppNotificationManager` for WPF, WinForms, and
unpackaged Win32 applications. It works without package identity, but it depends
on the Windows App SDK Singleton package and needs explicit registration before
`Show`. Unlike a tray balloon, `Show` places the notification in Notification
Center. Official references checked September 16, 2026:

- https://learn.microsoft.com/en-us/windows/apps/develop/notifications/
- https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.register
- https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.show
- https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.setting

The dependency and its official license are included in the generated legal
notice inventory. Release G was the clean replacement archive.

September 17: the stable identity's COM activation target was found pointing to
a removed diagnostic helper. The SDK reused that stale target in newer packages.
The helper now refreshes its own existing activation target after registration,
without resetting notification settings/history. The before/after receipts and
current visual gate are recorded in [Astra handoff](ASTRA_NOTIFICATION_HANDOFF.md).
