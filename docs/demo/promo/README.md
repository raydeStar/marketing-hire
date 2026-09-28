# HireZero promo (68 s)

A beat-synced motion promo: kinetic cold open, a logo drop, then the product in
seven moves: brief, shift, results, review desk, revision, Approve all, and
phone and team. It ends on the URL. The picture and the generated music share
one 120 BPM, 34-bar clock ([timeline.mjs](timeline.mjs)), so every cut, click
and pop lands on a beat.

```powershell
node docs/demo/promo/music.mjs artifacts/promo-20260927/music.wav   # ~4 s, offline synth
node docs/demo/promo/render.mjs                                      # ~5 min, CPU only
node docs/demo/promo/render.mjs --stills 9,21,44.2                   # review frames
node docs/demo/promo/render.mjs --music other.wav --out alt.mp4      # swap the music bed
```

The output is `artifacts/promo-20260927/deliverables/hirezero-promo.mp4`
(1920 × 1080, 30 fps, H.264 plus AAC normalized to −14 LUFS). Frames are seeked
deterministically in Chromium and piped straight to ffmpeg; the staging folder
is removed after each run. UI captures come from
`artifacts/marketing-demo-20260927/captures` (run `docs/demo/capture-preview.mjs`
if they have been pruned) and `docs/media/shell-team-roles.png`.

Copy, timings and scenes live in [promo.html](promo.html). The optional narration
script and a note on what is a real capture versus a motion recreation are in
[VOICEOVER.md](VOICEOVER.md). A replacement music bed must be 68 s at 120 BPM
with drops at 0:08 and 0:44, so the picture stays in sync.

## YuE2 music takes

With the Framewright YuE2 worker running (`scripts\start-yue2-worker.ps1` in
the storyboard-studio repo, needing about 11 GB of VRAM), `yue2-bed.mjs` submits
the hand-written 34-bar ABC score (the Vocal part is all rests, so the bed is
instrumental) once per seed. `align-bed.mjs` keeps a score-locked render as it
is, re-fits a drifted one on drop 2, reports how hard each picture hit lands,
and muxes the take onto the rendered picture. `build-compare.mjs` writes
`deliverables/compare.html`; serve it with `node docs/demo/promo/serve.mjs`
(it needs range requests, so the page can't be opened as a file).

```powershell
node docs/demo/promo/yue2-bed.mjs 777001 90210
node docs/demo/promo/align-bed.mjs artifacts/promo-20260927/yue2/hirezero-promo-777001.flac take-b
node docs/demo/promo/build-compare.mjs "Take B|yue2/take-b-aligned.wav|note"
```

The first batch (September 27): seed 777001 was score-locked with drops at 8.02
and 44.02 s. Seed 90210 hit hardest but ran 2 bars long; bars 20–21 were
trimmed. Seed 424242 had the wrong arc. The `SCORE=v2` arrangement (seeds 314159
and 271828) drifted and was rejected.
