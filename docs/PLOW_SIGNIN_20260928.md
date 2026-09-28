# Landing-page sign-in fix — September 28, 2026

The phone sign-in front door is deployed at https://hirezero.app/account/.
It uses Plow's account OTP, lists only that account's HireZero agents, and asks
Plow for a one-time web ticket. Its source is in the separate landing repository,
commit `4997049719400d04995e457a2f6c781b932a2994`.

## Reproduced arrival defect and qualified fix

The current hosted `v0.1.0-plow.4` image rejects `Sec-Fetch-Site: cross-site`
at its entrance for every route. A legitimate web-ticket redirect from the
landing page therefore returns 403, even though phone verification and the
ticket exchange succeed. Direct navigation subsequently opens the authenticated
cockpit. This was reproduced with the real hosted account and the actual
packaged host without inference.

Commit `d18cf66cad69e2e355f14494da4e6a8349611b52` admits only an authenticated
`GET /` with browser metadata `navigate` / `document`. It leaves the browser
metadata intact. The host serves the static app shell and its subsequent API
requests originate from the cockpit. Cross-site API reads/writes, frames,
foreign Origin headers, wrong proxy identity and wrong CSRF remain refused.
No host code, model behavior, approval logic or owner data changed.

The small release overlay is public:

```text
ghcr.io/raydestar/hirezero-marketing:v0.1.0-plow.5
ghcr.io/raydestar/hirezero-marketing@sha256:723e2496543f4bfff047b644a90be79eb1c9b7f8cbdfa202144ea6fd15574958
```

It inherits the qualified v4 image and changes only `/opt/hirezero/entrance.mjs`
plus OCI release labels. The actual v5 image passed the same offline check;
anonymous digest pull passed. Three focused entrance tests passed. The packaged
negative control returned 403 on v4 and 200 on v5, with cross-site APIs/frames
still 403 and a legitimate saved brief still 200. Test containers ran with no
network and temporary data volumes and removed themselves on exit.

## Rollout status

**Follow-up: v5 is now running on a separate unused line.** The prior v4
workspace remains intact and is labelled as the previous workspace. The saved
HireZero brief and objectives were copied through the UI and persisted after
reopening. The local owner installation was unchanged.

The first real landing-page handoff into v5 succeeded. Two repeated ticket
handoffs later ended at the in-app browser's `ERR_INVALID_RESPONSE` page, while
direct authenticated navigation still opened the cockpit. A transient background
502 recovered. The remaining repeat-entry failure has not been localized to the
entrance or Plow's proxy. **Do not call repeated sign-in fully accepted yet.**

The original rollout constraints remain relevant: Plow's published agent API
supports creation, rename, settings and deletion, but no in-place image update.
No existing deployment was deleted and no model turn was started.

Before any replacement, the current hosted workspace and employee work ledger
were exported through Settings. Their private copies are retained under ignored
`artifacts/plow-signin-20260928/private-backup/`. These are JSON exports, not a
full VM/vault backup or an automated restore proof. The current hosted workspace
contains the HireZero brief and no completed work; do not claim its full runtime
state can be restored from these exports alone.

Finish repeated landing-to-cockpit acceptance before declaring this flow done.
[Shared browser access](PLOW_SHARED_ACCESS.md) needs a hosting decision or a
supported upstream member gate. Initial one-click admission and competition
verification remain separate organizer actions.
