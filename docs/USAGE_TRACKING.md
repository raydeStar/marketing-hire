# Employee tokens and shared allowance

The right company panel contains two separate records:

- **Shared allowance**: remaining percentages and scheduled reset times reported
  for the Codex account signed into this Windows host's CLI. Includes other work
  on the account. A percentage change cannot be assigned to one employee request.
- **Token usage**: provider-reported employee Chat and autonomous-work tokens,
  grouped by day, week and month. Unreported requests remain unknown. Reservations
  are displayed separately and are not measured consumption.

## Collection

`start-marketing.ps1` connects the installed Windows npm Codex CLI when present.
For another installation, set `Marketing__CodexUsageExecutable` to its absolute
native executable path before starting the host. This local integration is not
enabled by default in customer deployments.

Every five minutes while the host runs, a hidden stdio app-server child performs
only `initialize`, `account/read` and `account/rateLimits/read`. There are no model
turns, additional API keys, purchases, reset calls or external listening ports.
The helper is closed after each read, with a 20-second read timeout. A missing CLI,
unsupported auth or failed read is shown as unavailable or stale, never as zero.

The app-server owns authentication. Credentials and raw protocol diagnostics are
not copied into the application database or sent to a browser. Persisted account
identity is hashed; the displayed address is masked. Histories are separated when
the reported account identity changes. The read-only API is owner-only; campaign
collaborators cannot see account usage.

Official protocol reference: [Codex app-server auth endpoints](https://learn.chatgpt.com/docs/app-server#auth-endpoints).

## History and interpretation

Samples are stored in the private `.data/codex-allowance.sqlite` database and
survive host restarts. The interface and download expose the last 30 days for the
most recently observed account. Day/week/month ranges mean the last 24 hours,
seven days, and 30 days. The display lists changes; its JSON download includes
every observation in that range. Previous account records stay in the private
database and are not combined with the current account.

Snapshots older than ten minutes, disabled collection, or a failed check show a
saved/stale label. No history is reconstructed for periods before collection or
while the host is stopped. Reset-time changes are labeled as a changed window,
not spending; the feed alone cannot identify whether a credit or a scheduled
renewal caused a change. The tracker does not extrapolate time until exhaustion
or convert tokens to quota percentages.

The desktop sidebar may temporarily disagree with a fresh account reading. Check
the timestamp and saved/stale label before comparing them. On September 24 the
reset had completed before the instruction to wait arrived; the subsequent tool
and independent CLI reads both reported 0% used. The first actual saved sample
is a post-reset baseline. Earlier percentages have not been fabricated into the
history. Owner preference: wait until exhaustion and a fresh explicit instruction
before redeeming any future credit. This tracker cannot redeem credits at all.

## Verification and activation

- Eleven focused backend checks cover parsing, unknown/null fields, legacy versus
  named buckets, persistence, renewal, changed accounts, staleness, and owner-only
  API serialization/access.
- One browser check covers allowance changes/renewal, day/week/month selection,
  export, stale labels, unknown employee tokens, and absence of dispatch/reset
  mutations. Synthetic test values are not saved into the live database.
- The production frontend builds. A real read-only capture saved the initial
  100%-remaining observation without a model request.
- The running Windows web host still needs the normal startup script to load the
  new endpoint and periodic collector. The one-shot capture is not a replacement
  web host and does not start background model work.

Optional one-shot capture using a built host, without running a web server:

```powershell
& '<absolute-path-to-Thaddeus.Host.exe>' --capture-codex-allowance --data '<absolute-existing-data-directory>' --executable '<absolute-path-to-codex.exe>'
```
