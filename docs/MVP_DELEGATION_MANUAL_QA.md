# Delegation MVP manual QA

The current Windows QA study is served at <http://localhost:5179/> from the
final packaged candidate. Activation created and verified a backup before the
schema 8 to schema 10 migration. Existing study table counts match the backup.
No model call or worker task was started during activation.

Use fictional, reversible content. Keep the host awake while a scheduled action
is due. The current package is unsigned and Windows-only; phone and Mac setup are
outside this checkpoint.

## Fifteen-minute local pass

1. **Reminder scheduler and in-app result.** In Chat, ask: "Remind me in two
   minutes to check the raven's tea." Review the exact date, time and timezone,
   approve it, close the browser while leaving the host running, then reopen it.
   Confirm exactly one unread result in **raven -> Upcoming**, open it and mark it
   read. Native notification delivery is a separate deferred Astra gate and is
   not part of this pass.
2. **Natural-language management.** Create two fictional future reminders.
   Ask "cancel the second one" and confirm that Chat first identifies the exact
   job and presents a review. Deny once and verify nothing changes. Repeat,
   approve, and confirm Upcoming changes. Reschedule the remaining reminder in
   Chat and verify its old authority is replaced rather than duplicated.
3. **Reading to To-dos.** Attach a small text file containing two actions, one
   with an intentionally vague date. Ask Thaddeus to turn it into To-dos. Deny
   the first batch, then repeat and approve it. Confirm two editable To-dos,
   source links, and an unresolved-date note rather than an invented deadline.
4. **Restart durability.** Schedule a reminder several minutes out, close the
   browser tab, reopen Thaddeus from the same desktop entry, and confirm the job
   remains in Upcoming. Let it run once. Do not end the host process from Task
   Manager; the host must remain awake to dispatch local work.
5. **Failure clarity.** Open a completed, failed, notification-failed, or unknown
   item in Upcoming. The readable summary should come first. Technical details
   should remain behind disclosure. **Review in Chat** must preserve any draft,
   and an unknown external result must offer no automatic retry.

## Owner-authorized connector pass

This pass needs an owner-controlled test inbox and calendar. Do not use a real
recipient as the first target.

1. In **Settings -> Connections -> Google Workspace**, use a Google Cloud
   **Desktop app** OAuth client and connect Gmail and Calendar with an explicitly
   approved test account. Confirm the account and granted permissions are shown.
   Refresh credentials stay in the operating-system credential store and access
   tokens stay in host memory; neither may appear in SQLite, Chat, receipts, or
   export.
2. Ask: "Email my boss in five minutes with a reminder about the fictional tea
   inventory." Thaddeus must ask for the exact recipient address. Supply the
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

## Five-minute demo route

1. Show the hidden-by-default sidebar and open Chat, To-do, Artifacts, and Feed.
2. Create and approve a two-minute reminder in natural language.
3. Turn a two-line fictional attachment into reviewed, editable To-dos.
4. Open the raven, show Upcoming, pause/reschedule a job through Chat, and open a
   readable result with its technical receipt collapsed.
5. If the test connector is configured, show the exact email/brief review and a
   previously completed provider receipt. Do not send a new external action just
   for the demo.

## Bug report notes

Record the action, expected result, actual result, approximate time, and the job
or occurrence ID shown in technical details. Include whether reload changed the
behavior. Screenshots are useful; omit host keys, bearer tokens, private source
content, and raw provider credentials.

Automated evidence for the package is indexed in
`docs/MVP_DELEGATION_ACCEPTANCE.md`. The 45-case suite used isolated fictional
studies, a synthetic model, and an official MCP transport fixture. It made no
live model, mail, calendar, search, worker, GPU, or GitHub Actions call.
