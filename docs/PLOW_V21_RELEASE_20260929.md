# v21: texting during a shift

Published September 29, 2026 (America/Denver) from main commit `65e90a6`. It is a
small overlay of v19 built with `scripts/build-plow-overlay.mjs`. v20's
onboarding change isn't in it: that change was reverted on main (`55f651b`)
after v20's hosted install lost its web page.

```text
ghcr.io/raydestar/hirezero-marketing:v0.1.0-plow.21
ghcr.io/raydestar/hirezero-marketing@sha256:1eb79fb6e91fd552b5f49c2cc0cf3493d79e235b487cf23bd8c952090d035b29
```

**Rolled back.** v21 was the one-click pin from 03:06 to 03:27 UTC on
September 30. The owner then moved the pin back to v19
(`sha256:e1f05fec62a2e1113e0944ff18cfb31ee87b8258c425aeeed2fce3803d4f3145`).
They accepted v19's known errors until after the competition.

The fresh v21 install on the Aspen line (`27ef54bb…`) went dark during website
onboarding, as v20's had. The page showed 504, then 502, and Plow reported
"This agent's machine did not answer". v21 contains none of v20's code: it is
v19 plus the texting gate. So the failure is probably the hosted website
onboarding run itself, not either image's change, but that hasn't been
confirmed. v19's hosted field test and the owner's v19 install both
survived website onboarding.

## What failed

On v19 the owner texted Chip during a shift and got back "Your message could
not be sent: blocked by marketing-request-meter".

While a worker step is in flight, the meter refuses every other model call.
That's how it proves the worker's tokens are metered. A text gave up after 13
seconds, but a GLM worker step can run for up to 135 seconds, so a text sent
during a shift was usually refused.

## What changed

- **A text takes the next turn at once.** It marks itself as soon as it
  arrives, so the worker starts no new step. It then waits up to 170 seconds
  for the step in flight. The gate hook registers a 190-second limit, set
  above that wait.
- **The worker holds off while the reply is written.** It waits up to four
  minutes, so a new step can't cut the reply off. A mark older than five
  minutes is ignored.
- **A step that never settles gets a plain answer.** The owner is told "I'm
  finishing a step of your shift. Text me again in a couple of minutes."
- **Metering is unchanged.** The worker's metering and the meter's refusal
  rules are untouched.
- **The overlay build carries the meter plugin.** Its runtime files now go into
  `/app/marketing-meter/plow/`.

## Verification

- **Unit and host tests.** Text-mode tests passed 5/5, including the new gate.
  The host's text-workflow and chat-wait tests passed 10/10.
- **Packaged meter check.** It passed on the released filesystem: the real
  Gateway, fictional replies, no network.
- **Live on the local test agent (GLM 5.2, v21).**
  - A text sent 4 seconds into a worker step marked itself while the step was
    still running. It was answered correctly in 11.5 seconds. The worker's next
    step started only after the reply finished.
  - With the ledger reporting a busy worker step for 40 seconds, a text held
    its mark for the full 40 seconds, well past the old 13-second limit. It then
    passed and was answered correctly after 47 seconds.
  - The temporary ledger shim used for that run was removed by recreating the
    container.
- **Hosted.** A fresh v21 install (`27ef54bb…`, Aspen line) opened and
  reported a connected GLM runtime.

Local evidence is in the ignored `artifacts/plow-v21-aspen-20260929/`
directory.

## Removed

At the owner's request, this image was deleted from GHCR on September 30, 2026 (UTC), and no installation uses it. Build nothing on it.
