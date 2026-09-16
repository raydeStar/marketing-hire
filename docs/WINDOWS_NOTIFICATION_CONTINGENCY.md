# Windows notification contingency

This is a prepared fallback for G3. Do not change the frozen candidate unless
the owner reports that the September 16 `Shell_NotifyIcon` balloon was not
visible.

## Current evidence

The final package scheduled and dispatched occurrence
`7124f7ca2935f92da75db7ce46d6be419353af9d7312df431f73d841e6583c23`
once at its exact due time. Windows accepted `Shell_NotifyIcon`, and Thaddeus
retained an unread successful result. The current mechanism is a classic tray
balloon with no AppUserModelID or WinRT app-notification registration. Shell
acceptance therefore does not prove that Windows presented the balloon.

Receipts are retained under
`artifacts/reminder-notification-acceptance-20260916-a`.

## If the owner saw the balloon

Record the observation in the receipt and mark G3 verified. Keep the candidate
unchanged. A future product milestone may still adopt modern app notifications,
but that is outside this frozen MVP.

## If the owner did not see the balloon

Replace only the notification sink with the current Microsoft-supported local
app-notification path for unpackaged desktop applications:

1. Add the pinned `Microsoft.WindowsAppSDK` dependency and update its lockfile
   and third-party notice inventory.
2. Implement an `AppNotificationManager` sink. Register it once before calling
   `Show`, read `AppNotificationManager.Setting`, and unregister during orderly
   disposal. Preserve the existing `IWindowsNotificationSink` seam and scheduler
   semantics.
3. Store a safe provider receipt containing the notification identifier,
   mechanism, setting, and API acceptance. Do not treat API acceptance as visual
   observation.
4. Keep notification failure separate from successful reminder execution. Never
   replay the reminder automatically because presentation failed or was
   uncertain.
5. Add focused unit coverage for available, disabled, failed, disposal, and
   duplicate-dispatch cases. Run the smallest package check that exercises the
   new native dependency, then repeat the same owner-observed one-shot reminder.
6. Re-freeze the Windows archive, release manifest, rollback package, media
   hashes, landing-page download checksum, and acceptance matrix only after the
   replacement passes.

Microsoft currently recommends `AppNotificationManager` for WPF, WinForms, and
unpackaged Win32 applications. It works without package identity, but it depends
on the Windows App SDK Singleton package and needs explicit registration before
`Show`. Unlike a tray balloon, `Show` places the notification in Notification
Center. Official references checked September 16, 2026:

- https://learn.microsoft.com/en-us/windows/apps/develop/notifications/
- https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.register
- https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.show
- https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.setting

This dependency is not present in the current host. Adding it is a release
change, not a documentation-only tweak, and must trigger notice, package-size,
startup, extracted-package, and manual notification verification.
