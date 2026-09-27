# Marketing assets handoff — September 27, 2026

The silent marketing preview and recording script are ready for owner review.
No application code was changed. No assets were uploaded or registered publicly.

Open `artifacts/marketing-demo-20260927/deliverables/index.html` in Edge or Chrome
to watch both cuts, view the listing images, and read the script in one place.
The full recording directions and optional teaser voice script are in
[NARRATION.md](NARRATION.md).

## Deliverables

- `hirezero-marketing-preview-silent.mp4`: exactly 84 seconds, 1920 × 1080,
  H.264, approximately 2.25 MiB, zero audio tracks.
- `hirezero-teaser-silent.mp4`: exactly 20 seconds, same format,
  approximately 0.79 MiB, zero audio tracks.
- Landscape cover and campaign images, 1200 × 675 social image, 512 × 512 logo,
  and eight scene images.
- Eight-scene narration script, plain-text recording copy, and editable storyboard.

These are composed marketing videos using the real interface and clearly
labelled fictional example content. They are not a recording of a successful
live model run, phone conversation, or native multiplayer handoff. The source
capture used commit `a2447ee585355c5d2496c1679042e4638d212b6b` with the local
changes listed in `receipt.json`; it is not evidence for a frozen release build.

## Checked

- Eight desktop/phone UI captures: layout checker passed, no browser JS errors.
- Eleven scene/listing compositions: no text overflow or overlapping content.
- Both MP4s: complete decode passed; exact duration, resolution and no audio
  confirmed with ffprobe; playback sampled in installed Edge.
- All eight main-video scene samples match their reviewed source compositions.
- Disposable host on 5185 stopped; host scratch, raw recording scratch and the
  diagnostic image removed. Source captures and verification frames retained
  for owner narration timing and the later live-footage edit.

Receipts: `receipt.json`, `render-receipt.json`, `playback-receipt.json`,
`frame-comparison.json`, and `cleanup-receipt.json`, all under the artifact root.

## Remaining for the final edit

1. Owner records the eight short voice clips. Retime the edit to natural speech.
2. Release task supplies verified, non-sensitive campaign output and native
   multiplayer/phone footage. Replace the example sections identified in
   [README.md](README.md#final-competition-edit).
3. Review the voiced cut, then obtain publication authorization before uploading.

Source additions are confined to `docs/demo/` and committed as one separate
change on main. Generated assets stay in ignored `artifacts/`. No unrelated
changes were staged, committed, reverted, or pushed; this task made no push.
