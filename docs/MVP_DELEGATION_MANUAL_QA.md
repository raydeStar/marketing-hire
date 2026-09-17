# Delegation MVP manual QA

Use the current candidate identified in `NON_NOTIFICATION_MVP_HANDOFF.md`,
launched from File Explorer or a normal Windows desktop terminal. Codex's MSIX
process environment redirected earlier notification registrations into its private
Windows state; the owner confirmed that the unchanged helper displays correctly
outside that environment. Package acceptance used isolated fictional
studies and did not alter the owner study. Back up the owner study before replacing
its currently active package; no model or worker task is required for activation.

Use fictional, reversible content. Keep the host awake while a scheduled action
is due. The current package is unsigned and Windows-only; phone and Mac setup are
outside this checkpoint.

## Fifteen-minute local pass

1. **Reminder scheduler and in-app result.** In Chat, ask: "Remind me in two
   minutes to check the raven's tea." Review the exact date, time and timezone,
   approve it, close the browser while leaving the host running, then reopen it.
   Confirm exactly one unread result in **raven -> Upcoming**, open it and mark it
   read. Confirm the named notification is visible, click it after its sending
   helper has exited, and verify it opens this study's Upcoming results.
2. **Natural-language management.** Create two fictional future reminders.
   Ask "cancel the second one" and confirm that Chat first identifies the exact
   job and presents a review. Deny once and verify nothing changes. Repeat,
   approve, and confirm Upcoming changes. Reschedule the remaining reminder in
   Chat and verify its old authority is replaced rather than duplicated.
3. **Reading to To-dos.** Attach a small text file containing two actions, one
   with an intentionally vague date. Ask Thaddeus to turn it into To-dos. Deny
   the first batch, then repeat and approve it. Confirm two editable To-dos,
   source links, and an unresolved-date note rather than an invented deadline.
4. **Actual host restart.** With no external action in flight, schedule a harmless
   reminder several minutes out. Use **Settings -> Storage & backups -> Review
   maintenance** to safely stop the study, then **Finish and close Thaddeus**.
   Launch the same package/profile from its normal Windows desktop entry and
   record the changed host process ID. See `STUDY_BACKUPS.md` for safe shutdown.
   Confirm history and the pending job survive, then let it execute once. Closing
   and reopening a browser alone does not pass this check. Never force-kill the
   host or change the system clock.
5. **Failure clarity.** Open a completed, failed, notification-failed, or unknown
   item in Upcoming. The readable summary should come first. Technical details
   should remain behind disclosure. **Review in Chat** must preserve any draft,
   and an unknown external result must offer no automatic retry.

## Owner-authorized connector pass

This pass needs an owner-controlled test inbox and calendar. Do not use a real
recipient as the first target.

1. Reuse the connected Google Gmail read, Gmail send, and Calendar grants. If a
   connection was deliberately removed, reconnect it through Chat as a separate
   least-privilege connection using the saved Google Cloud **Desktop app** OAuth
   client and explicitly approved test account. Confirm the account and exact
   granted permissions are shown for each connection.
   Refresh credentials stay in the operating-system credential store and access
   tokens stay in host memory; neither may appear in SQLite, Chat, receipts, or
   export.
2. Ask: "Send a test email in five minutes about the fictional tea inventory."
   Thaddeus must ask for the exact recipient address. Supply the
   owner-controlled test address, inspect sender, recipient, subject, body,
   local time, timezone, provider, and one-send authority, then approve. Close
   the browser while the host stays running. Verify one Gmail-accepted message,
   inspect its provider message ID, and separately record whether the controlled
   recipient observed delivery.
3. Ask for a weekday morning brief without naming a time. Thaddeus must ask.
   Choose a near-future time for QA and approve only bounded read access to the
   test inbox/calendar. Verify one brief with source status, links where safe,
   and unavailable sources distinguished from empty sources.
4. Pause the brief in Chat, resume it, then change its time and email count.
   Each mutation must show a new review. Removing or changing the connection
   must stop future dispatch and leave actionable recovery text.
5. Ask Thaddeus to check the test inbox every five minutes and surface only direct
   requests, time-sensitive changes, and meaningful deadlines. Approve the exact
   recurring read scope once. Verify one empty/routine check stays quiet, one new
   important message creates a durable result with sender, subject, reason, and a
   link to the original, then restart the host and confirm it is not announced
   again. Revoke access and confirm the watch pauses with a visible error.

## Five-minute demo route

1. Show the hidden-by-default sidebar and open Chat, To-do, Artifacts, and Feed.
2. Create and approve a two-minute reminder in natural language.
3. Turn a two-line fictional attachment into reviewed, editable To-dos.
4. Open the raven, show Upcoming, pause/reschedule a job through Chat, and open a
   readable result with its technical receipt collapsed.
5. If the test connector is configured, show the exact email/brief review and a
   previously completed provider receipt. Do not send a new external action just
   for the demo.

## Fresh Windows user pass

Use an actual newly created local Windows user or Windows Sandbox. A fresh data
folder under the existing account is useful fixture evidence but does not satisfy
this gate.

1. Copy the exact ZIP and SHA256 identified in the current section of
   `NON_NOTIFICATION_MVP_HANDOFF.md` into the fresh account. Do not use an older
   download or the owner's development wrapper.
2. Extract it into a normal user-owned folder and open `Start Thaddeus.cmd`
   from File Explorer. Keep the complete extracted folder together. Record
   any SmartScreen or prerequisite prompt; do not call an unsigned-build warning
   a product failure.
3. Confirm first launch creates only the new account's
   `%LOCALAPPDATA%\Thaddeus2` study, opens the local page, and displays model and
   connection setup without exposing a host key in the URL.
4. Configure a test model, create one harmless To-do and one near-future reminder,
   close/reopen the browser, then close/restart the host. Confirm the item, history,
   scheduled job, and unread result survive.
5. Complete Google sign-in only with the approved test account. Confirm account,
   permission, reconnect, and disconnect status. Do not copy the owner's existing
   credential vault or study into this profile.
6. Save the package hash, Windows edition/build, account type, observed prompts,
   data path, and result IDs. Remove only this test account's disposable study
   after its processes exit and after its compact acceptance receipt is retained.

## Bug report notes

Record the action, expected result, actual result, approximate time, and the job
or occurrence ID shown in technical details. Include whether reload changed the
behavior. Screenshots are useful; omit host keys, bearer tokens, private source
content, and raw provider credentials.

Automated evidence for the current package is indexed in
`docs/MVP_DELEGATION_ACCEPTANCE.md`. The packaged suite uses isolated fictional
studies, a synthetic model, and an official MCP transport fixture. It made no
live model, mail, calendar, search, worker, GPU, or GitHub Actions call.
