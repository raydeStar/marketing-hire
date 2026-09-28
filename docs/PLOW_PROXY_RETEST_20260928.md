# Plow cloud proxy retest — September 28, 2026

The corrected image completed a real, bounded cloud shift through Plow. The
previous public-address readiness failure is resolved. This qualifies the proxy
and meter path; it does not qualify the draft's quality or organizer admission.

## Published candidate

- Source: `ddbea99` on `main`.
- Tag: `ghcr.io/raydestar/hirezero-marketing:v0.1.0-plow.8`.
- Immutable image:
  `ghcr.io/raydestar/hirezero-marketing@sha256:ba4d41e27ce550e5f4d55e7ff49b5b3c18e068bd54343e73e5a07dd484d912f2`.
- Anonymous registry access to the exact manifest passed.

This is a small meter-only overlay on the existing v7 image. Provider policy,
the exact completion URL and the channel transport allowlist now derive from
the deployment's `PLOW_API_BASE`, including `/v1/chat/completions`. The GLM 5.2
model, exact session, deadline, token reservation, one-physical-send rule,
no-tools policy, no-fallback policy and durable response receipts are retained.
No organizer flags or listing image promotion were changed.

## Evidence

- 27 focused request-guard checks passed.
- Nine Gateway/installed-SDK checks passed inside the exact image with networking
  disabled and synthetic responses. The packaged test uses the production plugin
  path and permissions unchanged. It covers a private HTTP proxy, rejects the
  public API when a proxy is configured, and preserves policy refusals.
- A separate owner installation ran the published digest on Plow. The retained
  shared workspace and its earlier documentation were not replaced.
- README.md, INSTALL.md and HIREZERO_COMPANION.md were uploaded. The owner-facing
  business brief and an internal company-facts summary supplied the bounded
  worker context; uploading a file alone is not proof the worker read it.
- One task requested one 80–120 word LinkedIn draft for owner review. The shift
  allowed four model turns and at most 100,000 tokens, with no recurring schedule.
- The shift completed at its four-turn limit and is **off shift**. The four
  individual provider request receipts report **3,581 + 4,253 + 7,350 + 1,295 =
  16,479 tokens**. No unknown-usage warning or outstanding reservation remained
  in the refreshed receipt view. No unmetered chat or alternate provider was used.
- One draft and a shift report were saved. Nothing was approved or published.
- The public Index API still returned empty `blessed_at` and `deployable_at`.

## Quality failures still open

The self-review response was not valid JSON, so the draft has no successful
rubric review. The draft also included internal proxy, image and test details
despite the assignment's explicit instruction to omit them. It remains pending
owner review. The usage-by-stage display grouped that failed review's tokens
with the shift report; the individual provider receipts and overall total agree.

Keep verification and deployment flags off pending the organizer's retest.
Use the immutable candidate above when requesting it. The existing shared
entrance is still attached to the retained v7 workspace; this acceptance run
used the new installation's authenticated native Plow cockpit. No other workspace
has been upgraded or migrated as part of this meter fix.

Local receipts and screenshots are retained under
`artifacts/plow-package-proxy-20260928` and
`artifacts/plow-meter-check-proxy-packaged-20260928`. The build context and both
owned offline fixture containers were removed. No unrelated containers, images,
volumes or owner data were removed.
