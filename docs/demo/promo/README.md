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
