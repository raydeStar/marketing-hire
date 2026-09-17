# Non-notification MVP handoff

Release K is the active Windows candidate. Publication remains paused and native
notification acceptance remains assigned to Astra.

## Verified locally

- Release K source is `553f4a7f0e5d8c34e2ea67cb19648cc3e8a17e62`.
- `artifacts/local-check-delegation-release-20260917-k/verified.json` records a
  clean-source package pass with no live model call, worker start, or GitHub
  runner. Its native gate passed 17 checks and its packaged browser suite passed
  all 45 ordinary workflows. Disposable extractions and fictional studies were
  removed after their processes exited.
- The complete backend suite passed 1,005 tests. The focused inbox-watch group
  passed five tests covering connector-neutral eligibility, exact recurring-read approval, important versus
  routine classification, empty-check model suppression, restart deduplication,
  new activity in an existing thread, and visible pause on revoked access. The
  earlier focused delegation/OAuth/Gmail group covers a short real-clock
  host-pump dispatch, one-shot/restart/cancellation, missed and unknown outcomes,
  timezone recurrence, grant rotation, connector drift, refresh/revocation,
  exact Gmail MIME, and ambiguous-send suppression.
- The live owner study made a verified schema 10 backup, then moved from Release
  H to Release K and schema 11. `artifacts/activation-delegation-release-20260917-k/activation.json`
  binds process, ports, backup, source, archive hash, and served client hash. The
  existing 36 runs, 38 chats, two artifacts, delegation, Luna selection, Brave
  credential, and 999-query allowance remained visible. Release H and that
  pre-migration backup are retained for rollback.

## Scheduler in use

- `src/Thaddeus.Infrastructure/DelegationScheduler.cs`: creates and dispatches
  reviewed one-shot reminders/email, weekday briefs, and five-minute inbox
  watches through the same persisted scheduler.
- `src/Thaddeus.Host/DelegationPump.cs`: checks due work while the host runs,
  independently of browser tabs.
- `src/Thaddeus.Infrastructure/StoreDelegations.cs`: persists jobs, grants,
  occurrences, versions, cancellation/edit state, restart recovery, missed-time
  decisions, terminal outcomes, and inbox message/alert identities.
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

The inbox watch is not Gmail-specific. It accepts an owner-selected connector
only when discovery exposes a bounded read-only mail list/search operation with
limit and since/query inputs. The approved connector/tool fingerprint is checked
again at every dispatch. Google remains the first live acceptance target because
its Desktop OAuth and narrow Gmail adapter are already implemented.

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
   source/status receipt in Thaddeus. Then approve one read-only inbox watch,
   observe one quiet check and one selective in-app attention result, and verify
   the original-message link.
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
