# Non-notification MVP handoff

Release H is the active Windows candidate. Publication remains paused and native
notification acceptance remains assigned to Astra.

## Verified locally

- Release H source is `fe251b3b88aa23cf93d106422a1fee940f169d86`.
- `artifacts/local-check-delegation-release-20260917-h/verified.json` records a
  clean-source package pass with no live model call, worker start, or GitHub
  runner. Its native gate passed 17 checks and its packaged browser suite passed
  all 45 ordinary workflows. Disposable extractions and fictional studies were
  removed after their processes exited.
- The focused delegation/OAuth/Gmail suite passed 61 tests. It includes a short
  real-clock host-pump dispatch with no browser, one-shot/restart/cancellation,
  missed and unknown outcomes, timezone recurrence, grant rotation, connector
  drift, refresh/revocation, exact Gmail MIME, and ambiguous-send suppression.
- The live owner study made a verified schema 10 backup, then moved from Release
  G to Release H. `artifacts/activation-delegation-release-20260917-h/activation.json`
  binds process, ports, backup, source, archive hash, and served client hash. The
  existing history, model connection and Brave allowance remained visible.

## Scheduler in use

- `src/Thaddeus.Infrastructure/DelegationScheduler.cs`: creates and dispatches
  reviewed one-shot reminders/email and weekday briefs.
- `src/Thaddeus.Host/DelegationPump.cs`: checks due work while the host runs,
  independently of browser tabs.
- `src/Thaddeus.Infrastructure/StoreDelegations.cs`: persists jobs, grants,
  occurrences, versions, cancellation/edit state, restart recovery, missed-time
  decisions, and terminal outcomes.
- `src/Thaddeus.Host/HostDelegationDispatcher.cs`: routes an occurrence to the
  approved host capability.

A late one-shot is recorded as missed instead of being sent after its usefulness
window. An interrupted or ambiguous external effect is recorded as unknown and
is never blindly retried. Edits and cancellation change the persisted version
and revoke obsolete authority.

## Google status

The Windows Settings path now uses Google's Desktop app OAuth flow with system
browser consent, a `127.0.0.1` loopback callback, PKCE through the maintained MCP
OAuth client, short-lived single-use state, exact scope validation, verified
account display, refresh/reconnect/disconnect, and Windows credential storage.
Access tokens stay in host memory. Google MCP tools are restricted to advertised
reads. Exact approved email uses Gmail's supported `users.messages.send` endpoint
and records provider acceptance separately from observed recipient delivery.

Local implementation and synthetic safety evidence are complete. Live Google
acceptance is open because the owner study has no Google connector. Google Cloud
must have Gmail and Calendar APIs enabled, a Desktop app OAuth client, an
appropriate consent audience/test user, and any required Workspace MCP Developer
Preview enrollment. Gmail read access is restricted and send access is sensitive;
public use can require Google's verification and, for restricted data, additional
security assessment. A developer-account success does not establish public
availability.

## Open acceptance

1. In Settings, connect an explicitly approved Google test account for Gmail and
   Calendar, then use an owner-controlled recipient for one delayed email. Record
   Gmail's message ID separately from observed recipient delivery. Revoke access
   once and confirm dispatch is blocked.
2. Run one bounded recurring inbox/calendar brief against test data and retain its
   source/status receipt in Thaddeus.
3. Run launch, setup, persistence and scheduled-dispatch checks from an actual
   fresh Windows user profile. The existing package tests use fresh data under the
   current Windows account; Windows Sandbox remains unavailable.
4. Integrate Astra's human-observed notification result through
   `ASTRA_NOTIFICATION_HANDOFF.md`, then perform one coordinated final acceptance
   pass.

Exact next action: create or select the Google Cloud **Desktop app** OAuth client,
add the intended test account to the consent audience, and enter the client ID and
secret only in **Settings → Connections → Google Workspace**. Connect Gmail and
Calendar, then identify the owner-controlled test recipient for live acceptance.
