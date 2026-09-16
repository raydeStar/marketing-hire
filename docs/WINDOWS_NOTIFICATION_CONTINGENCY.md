# Windows notification contingency

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

Direct source and packaged-helper probes returned `accepted: true`, mechanism
`AppNotificationManager`, and setting `Enabled`, with notification identifiers
37528 and 37529. The focused delegation and notice-bundle tests passed, the core
local gate passed, and the packaged native gate passed. These receipts prove
that Windows accepted modern app notifications; they do not prove what the owner
saw. G3 remains `IN PROGRESS` until the owner confirms that the new **Thaddeus
reminder test** entry is visible in Notification Center or a fresh scheduled
occurrence is observed.

Microsoft currently recommends `AppNotificationManager` for WPF, WinForms, and
unpackaged Win32 applications. It works without package identity, but it depends
on the Windows App SDK Singleton package and needs explicit registration before
`Show`. Unlike a tray balloon, `Show` places the notification in Notification
Center. Official references checked September 16, 2026:

- https://learn.microsoft.com/en-us/windows/apps/develop/notifications/
- https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.register
- https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.show
- https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.setting

The dependency and its official license are now included in the generated legal
notice inventory. The final release archive must be regenerated from the clean
committed revision before it replaces the prior candidate.
