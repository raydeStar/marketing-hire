# Verification · 2026-09-09

Executed on Windows, .NET SDK 10.0.203, Node 22.15.0, Chromium through Playwright.
No local model/GPU was used. `raydeStar/sir-thaddeus-2` was created PRIVATE under
the authenticated `raydeStar` account; remote visibility is checked before/after push.
The v1 reference checkout remains clean at `d0cac1e1d67e5ffa94543747825992f0a0cc8a95`.

| Check | Observed result |
|---|---|
| `dotnet build --no-restore` | 0 warnings, 0 errors |
| `dotnet test --nologo` | 33 passed, 0 failed, 0 skipped |
| `npm --prefix web run build` | TypeScript and production bundle passed |
| `npm --prefix web run test:e2e` | 5 passed, 0 failed, 0 skipped |
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

## Unverified / intentionally incomplete

Physical phone, trusted TLS deployment, iOS/Android installation, reverse proxy,
hard CLI token limits, aggregate token/cost admission, general live chat, OS adversary
resistance, full MCP transport, automated reconciliation and a live paired Lab study.
Markdown/SQLite writes have a documented crash gap. No production-readiness or
security-audit claim. The next smallest milestone is general live conversation and
explicit goal creation through this same approval/runtime boundary.
