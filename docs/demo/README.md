# HireZero marketing preview

A silent, 84-second marketing walkthrough and a 20-second teaser, with listing
images and an [owner-recorded narration script](NARRATION.md). This work changes
no application code and publishes nothing.

## Delivery

The local outputs live under `artifacts/marketing-demo-20260927/deliverables/`:

| File | Use |
| --- | --- |
| `hirezero-marketing-preview-silent.mp4` | 84-second main cut, 1920 × 1080, H.264, no audio track |
| `hirezero-teaser-silent.mp4` | 20-second short cut, same format |
| `hirezero-cover-1920x1080.png` | Landscape listing image / video cover |
| `hirezero-campaign-1920x1080.png` | Campaign review image |
| `hirezero-social-1200x675.png` | Smaller landscape share image |
| `hirezero-logo-512.png` | Current HireZero mark |
| `scene-*.png` | Eight full-resolution storyboard frames |

The renderer also saves a self-contained `preview.html` above the delivery
folder. `deliverables/index.html` puts the playable videos, images and recording
script together. The source UI captures and small receipts remain there for retiming and
the later real-footage edit. These ignored artifacts are local deliverables;
committing this documentation alone does not distribute the videos.

## What the preview demonstrates

The current interface, business brief, prepared opportunity, campaign review,
draft comparison, and narrow browser layout. All business content is a fictional
example, disclosed on every frame. This is a composed marketing walkthrough
made from UI captures, not a recording of autonomous execution. The phone frames
are responsive browser captures, not SMS or native-app evidence.

The assignment, draft grades, claims, and example responses come from bounded
test fixtures. They are not customer results, live employee judgments, or proof
of a successful real campaign. The UI capture receipt records the source commit,
any local edits, the reused host assembly hash, and layout results.

## Reproduce and retime

Use the installed Node, web dependencies, Chromium, .NET host assembly and
ffmpeg. Run from the repository root:

```powershell
node docs/demo/capture-preview.mjs
node docs/demo/render-preview.mjs --stills-only
node docs/demo/render-preview.mjs
node docs/demo/verify-preview.mjs
```

`capture-preview.mjs` uses a disposable host on **5185**. It refuses an occupied
port, builds the web UI into its own scratch folder, intercepts fixture APIs,
blocks browser requests to other origins, and disables the shift pump. It never
calls a model or uses the owner installation. It removes that host and its
scratch after exit. Do not use ports 5183, 5190, or 5192 for this work.

`render-preview.mjs` composes native HTML/CSS around the UI captures, renders
each scene, and encodes an exact timeline with restrained 0.4-second dissolves
using CPU-only H.264. Its `-an` export contains no audio. Set `HIREZERO_DEMO_FFMPEG` and `HIREZERO_DEMO_FFPROBE` if those binaries are
not on PATH. Scratch is removed after the browser closes; encoded deliverables, source UI
captures, scene images, hashes and receipts remain. `verify-preview.mjs` checks
full decoding, samples playback in installed Edge, and creates the review page. The scripts admit their bounded
allocation while preserving the repository's 10 GiB free-space floor.

Edit `storyboard.json` to change copy or timings. Once the owner records the
eight narration clips, retime each scene to its matching clip and create a
separate voiced export. Do not synthesize missing lines or overwrite the silent
master. The current render script deliberately has no audio-generation path.

## Final competition edit

The [event page](https://luma.com/zhkhsnpa) calls for a demo of at least 60 seconds
and a real startup use case with OpenClaw 2.0 native multiplayer. The
[Agent Index publishing guide](https://aiworthusing.com/agent-index/publish) and
[client README](https://github.com/plow-pbc/agent-index-client) describe the
listing, usage reporting, verification, and asset registration.

This preview exceeds the duration requirement. It does **not** by itself prove
the other requirements. Before presenting it as the competition demonstration:

1. Replace 00:19–00:53 with the verified assignment, prepared output, and review
   from the separate release-finalization task. Use approved, non-sensitive work.
2. Replace or extend 00:53–01:06 with an actual revision when available.
3. Insert the verified OpenClaw native multiplayer handoff and phone continuation.
   Use explicit labels if these are separate takes. Do not substitute the mobile
   browser composition for messaging evidence.
4. Add the owner's recording, review the final claims, and retain a silent cut.
5. After publication is approved, upload the final video to YouTube and register
   its ID, a public image, and the install URL using the Agent Index client.

Source policy pages were reviewed on September 27, 2026. Public licensing,
deployment, registration and verification belong to the release task, not this
asset renderer.
