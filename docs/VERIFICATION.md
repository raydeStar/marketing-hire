# Verification · 2026-09-09

Executed on Windows, .NET SDK 10.0.203, Node 22.15.0, Chromium through Playwright.
No local model/GPU was used. `raydeStar/sir-thaddeus-2` was created PRIVATE under
the authenticated `raydeStar` account; remote visibility is checked before/after push.
The v1 reference checkout remains clean at `d0cac1e1d67e5ffa94543747825992f0a0cc8a95`.

| Check | Observed result |
|---|---|
| `dotnet build --no-restore` | 0 warnings, 0 errors |
| `dotnet test --nologo` | 65 passed, 0 failed, 0 skipped |
| `npm --prefix web run build` | TypeScript and production bundle passed |
| `npm --prefix web run test:e2e` | 6 passed, 0 failed, 0 skipped |
| NuGet vulnerable transitive package check | No known vulnerable packages returned |
| npm audit | 0 vulnerabilities returned |
| `scripts/doctor.ps1` | Host 200; frontend/key present; no provider API key or phone origin configured |
| Scripted Lab | 16 records; 15 scored expected outcomes, 1 intentional false-success negative control |
| Luna High live workflow | 3 smoke runs; all reached exact-write success, 1 model call each |
| Host restart | Completed history survived a real host stop/start; unit tests separately exercise paused/unknown outcomes |

Luna receipt files in ignored `artifacts/luna/`:

| Receipt | Input | Output | Result |
|---|---:|---:|---|
| `1788989499464.json` | 15,693 | 839 | Exact approved write verified |
| `1788989964786.json` | 15,693 | 544 | Exact approved write verified |
| `1788990095396.json` | 12,968 | 851 | Exact approved write verified |

Model: `gpt-5.6-luna`, reasoning `high`, development CLI bridge. No observed tool
execution inside the CLI provider; product tools ran in the backend. Input includes
CLI transport/context overhead and varies; these are product smokes, **not** frozen
paired efficacy trials. Cost unknown. The initial browser harness returned before
the first run finished; its saved run was subsequently inspected and approved,
without reissuing that model call. Later capture selection was fixed to use run IDs.

Tests cover approve/deny, expired/tampered/stale actions, source and target revision
changes, write policy Off, eight concurrent approval attempts, concurrent executors,
budgets, cancellation/timeout, repair, provider error, malformed streamed tool args,
revision conflicts, path traversal/ADS/reparse directory rejection, persistent
events/replay/cursors, interrupted write recovery, one-time pairing/host confirmation,
HttpOnly sessions/revocation, API authentication, CSRF/origin, untrusted Markdown,
offline UI and browser-to-browser event updates.

Earlier failures were retained during work and fixed: vulnerable SQLite/Vite versions,
duplicate detection preceding demo fault injection, missing browser binaries,
offline detection, badge-dependent accessible names, and screenshot selection of an
older identically titled run. Final counts above describe the corrected runs.

## Browser artifacts

Actual captures, with fictional content, under `artifacts/screenshots/`:

- `home-1440.png`, `home-768.png`, `home-390.png`
- `activity-1440.png`, `activity-768.png`, `activity-390.png`
- `approval-1440.png`, `approval-768.png`, `approval-390.png`
- `plan-1440.png`, `plan-768.png`, `plan-390.png`
- `active-luna-1440.png`, `active-luna-390.png`, `approval-luna-390.png`
- `disconnected.png`

Desktop Home, mobile approval, mobile Activity, editable desktop plan, and actual
running Luna receipts were visually inspected. Automated checks reject horizontal
overflow at the requested widths. These are **emulated viewport captures**, not
physical-phone or installation evidence. A few representative captures are committed
in `docs/media/`. Full test output: `artifacts/browser-results.json`; full scripted
Lab events: `artifacts/lab/report.json`. CI recreates deterministic evidence without
credentials, live calls or GPUs.

## Development-cycle additions

- General conversation: live two-turn Luna High context recall passed without
  knowledge changes or tool calls. Receipt: `artifacts/conversation-luna.json`.
  Reported usage: 28,204 input / 84 output tokens across those two turns. An earlier
  harness attempt returned before completion; that extra first-turn reply remains
  in history and is not included in this two-turn total. The polling and screenshot
  waits were fixed; no complete-state screenshot claim relies on a stale UI.
- Backend count now includes real local TLS pairing/confirmation/revocation,
  trusted-proxy boundaries, bounded/truncated stream parsing, conversation
  cancellation/context and token reservations, and crash-injection reconciliation.
- Six browser flows pass in isolated `artifacts/e2e-cycle`, including conversion
  from conversation to scoped goal and direct-edit Activity. The separate recovery
  browser smoke uses a Store-injected after-projection failure and verifies one
  retained revision: `artifacts/reconciliation-browser.json` and
  `artifacts/screenshots/reconciliation-390.png`.
- Frozen Luna comparison: 12/12 registered runs completed, 11 exact writes, one
  minimal-policy structural rejection. Both policies passed both disjoint
  validation cases. No evidence-policy repair was exercised; no causal repair
  advantage is established. Reported usage: 158,158 input / 7,604 output tokens,
  all twelve calls known. Elapsed campaign time: 183.128 seconds. Cost unknown.
  [Registration](evidence/luna-registration.json), [report](evidence/luna-report.json).
  Full private receipts: `artifacts/lab-live-cycle-1/runs.jsonl`. Core source at
  `ed69cd3` contains the campaign implementation; its runtime source was frozen
  before dispatch. No selective failures were repeated. Verdict INCONCLUSIVE.
- Resource measurements include per-run wall time, harness CPU/peak memory and
  artifact bytes. CLI/provider memory and GPU are not measured, and the hard remote
  token-bound gate is NOT_EVALUATED. Aggregate admission is implemented; strict
  mode refuses the uncertified CLI bridge. Unknown usage is never silently zeroed.

## Remaining manual verification / limits

Physical phone sign-in, trusted end-to-end Tailscale deployment, iOS/Android
installation and physical disconnect/reconnect are pending the user's final manual
step. See [phone setup](PHONE_SETUP.md). Automated TLS uses a narrowly pinned
local test certificate without changing the operating-system trust store; it does
not certify the physical phone or a live Tailscale deployment.

Hard CLI token limits, OS-adversary resistance, full MCP transport, background
scheduling and a production security audit are not claimed. Original milestone
exclusions remain exclusions. Content and SQLite use an explicit durable intent /
projection / reconciliation protocol, not a claim of a cross-filesystem atomic
transaction or exactly-once external delivery.

## Desktop follow-up: complete long histories

66 backend tests and seven browser tests passed after the receipt pagination fix.
The storage regression records 2,005 events plus an interleaved run; paginated reads
and complete export preserve every event. The browser test supplies two cursor
pages and verifies the final receipt count. Stale task-detail requests cannot
replace a newer selection. Phone verification is deferred by explicit user request.
