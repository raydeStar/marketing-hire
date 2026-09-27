import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import {spawn, execFileSync} from 'node:child_process';
import {fileURLToPath, pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';
import {chromium} from '../../web/node_modules/playwright/index.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const evidence = path.join(repo, 'artifacts/marketing-demo-20260927');
const delivery = path.join(evidence, 'deliverables');
const board = JSON.parse(await fs.readFile(path.join(repo, 'docs/demo/storyboard.json'), 'utf8'));
const render = JSON.parse(await fs.readFile(path.join(evidence, 'render-receipt.json'), 'utf8'));
assert.equal(render.passed, true, 'Finish the render before checking playback.');
assert.equal(render.stillsOnly, false, 'Stills are splendid, but this check needs moving pictures.');
const escape = value => value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
const stamp = seconds => `${String(Math.floor(seconds / 60)).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`;
let elapsed = 0;
const scenes = board.scenes.map(scene => {
  const start = elapsed;
  elapsed += scene.seconds;
  return {...scene, start, end: elapsed};
});
const script = scenes.map((scene, i) => `${String(i + 1).padStart(2, '0')} | ${stamp(scene.start)}-${stamp(scene.end)} | ${scene.step}\n\n${scene.narration}`).join('\n\n');
await fs.writeFile(path.join(delivery, 'recording-script.txt'), 'HIREZERO — YOUR RECORDING SCRIPT\n\nRecord eight separate clips. Leave a second of quiet before and after each take. Speak naturally; the edit can follow your voice. No audio has been generated.\n\n' + script + '\n');
const index = `<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>HireZero · marketing preview</title><style>
*{box-sizing:border-box}body{margin:0;background:#f4f3ef;color:#172033;font:16px/1.65 "Segoe UI",Arial,sans-serif}main{max-width:1160px;margin:auto;padding:38px 24px 80px}header{display:flex;align-items:center;gap:15px}header img{width:52px;height:52px}h1{font-size:32px;letter-spacing:-1px;margin:0}h2{margin:42px 0 16px;font-size:24px}p{max-width:780px}a{color:#235cca}video{display:block;width:100%;background:#111a2a;border-radius:12px;margin-top:24px}.meta{color:#526077}.grid{display:grid;grid-template-columns:1fr 1fr;gap:20px}.grid img{width:100%;border-radius:10px;display:block}.grid a{display:block}article{border-top:1px solid #d6dbe4;padding:24px 0}article h3{font-size:18px;margin:0 0 12px}article blockquote{margin:0;font-size:21px;line-height:1.6;max-width:880px}.timing{color:#235cca;font-size:15px;font-weight:600}.links{display:flex;gap:22px;flex-wrap:wrap}.note{padding:18px 22px;border:1px solid #d6dbe4;border-radius:10px;background:white}.teaser{max-width:780px}@media(max-width:650px){.grid{grid-template-columns:1fr}main{padding:24px 16px}h1{font-size:27px}article blockquote{font-size:19px}}</style><main>
<header><img src="hirezero-logo-512.png" alt="HireZero mark"><h1>Marketing preview</h1></header>
<p class="meta">84 seconds · 1080p · silent master · September 27, 2026</p>
<p>Prepared work. A clear decision. A marketing lead with the owner in charge.</p>
<video id="main-video" controls preload="metadata" poster="hirezero-cover-1920x1080.png" src="hirezero-marketing-preview-silent.mp4" aria-label="84-second HireZero marketing preview"></video>
<p class="links"><a href="hirezero-marketing-preview-silent.mp4" download>Save main video</a><a href="hirezero-teaser-silent.mp4" download>Save 20-second teaser</a><a href="recording-script.txt" download>Save recording script</a><a href="#script">Read the script</a></p>
<p class="note">The real interface with fictional example content. This composed preview does not demonstrate live execution, customer results, phone messaging, or multiplayer. Those recordings belong in the final competition edit. No narration, music, or other audio has been generated.</p>
<h2>20-second teaser</h2><video class="teaser" controls preload="metadata" poster="hirezero-cover-1920x1080.png" src="hirezero-teaser-silent.mp4" aria-label="20-second HireZero teaser"></video>
<h2>Listing images</h2><div class="grid"><a href="hirezero-cover-1920x1080.png"><img src="hirezero-cover-1920x1080.png" alt="HireZero marketing lead cover">Cover · 1920 × 1080</a><a href="hirezero-campaign-1920x1080.png"><img src="hirezero-campaign-1920x1080.png" alt="HireZero campaign review with claims and grades">Campaign review · 1920 × 1080</a><a href="hirezero-social-1200x675.png"><img src="hirezero-social-1200x675.png" alt="HireZero landscape social image">Social image · 1200 × 675</a><a href="hirezero-logo-512.png">HireZero mark · 512 × 512</a></div>
<h2 id="script">Your recording script</h2><p>Record one take per scene, leaving a second of quiet at each end. Speak naturally to one founder. The edit can follow your voice. Pronounce HireZero as “Hire Zero.”</p>
${scenes.map((scene, i) => `<article><div class="timing">${stamp(scene.start)}–${stamp(scene.end)}</div><h3>${String(i + 1).padStart(2, '0')} · ${escape(scene.step)}</h3><blockquote>${escape(scene.narration)}</blockquote></article>`).join('')}
</main></html>`;
await fs.writeFile(path.join(delivery, 'index.html'), index);

const receipt = {checkedAt: new Date().toISOString(), audioGenerated: false, checks: [], playback: [], frames: []};
const ffmpeg = process.env.HIREZERO_DEMO_FFMPEG || 'ffmpeg';
const ffprobe = process.env.HIREZERO_DEMO_FFPROBE || 'ffprobe';
let browser;
try {
  for (const [file, duration] of [['hirezero-marketing-preview-silent.mp4', 84], ['hirezero-teaser-silent.mp4', 20]]) {
    const absolute = path.join(delivery, file);
    const probe = JSON.parse(execFileSync(ffprobe, ['-v', 'error', '-show_streams', '-show_format', '-of', 'json', absolute], {encoding: 'utf8'}));
    assert.equal(probe.streams.length, 1);
    assert.equal(probe.streams[0].codec_type, 'video');
    assert.equal(probe.streams[0].codec_name, 'h264');
    assert.equal(probe.streams[0].width, 1920);
    assert.equal(probe.streams[0].height, 1080);
    assert.ok(Math.abs(Number(probe.format.duration) - duration) < 0.1);
    const child = spawn(ffmpeg, ['-hide_banner', '-v', 'error', '-threads', '2', '-i', absolute, '-an', '-f', 'null', '-'], {windowsHide: true, stdio: ['ignore', 'ignore', 'pipe']});
    let errors = '';
    child.stderr.on('data', bytes => errors += bytes);
    const code = await new Promise((resolve, reject) => {child.once('error', reject); child.once('exit', resolve);});
    assert.equal(code, 0, errors);
    assert.equal(errors.trim(), '', 'Full video decode reported errors.');
    receipt.checks.push({file, duration: Number(probe.format.duration), width: 1920, height: 1080, audioStreams: 0, fullDecode: 'passed', sha256: createHash('sha256').update(await fs.readFile(absolute)).digest('hex')});
  }
  // Use the installed H.264 decoder; bundled Chromium leaves this orchestra pit empty.
  browser = await chromium.launch({channel: 'msedge'});
  const page = await browser.newPage({viewport: {width: 1920, height: 1080}});
  await page.goto(pathToFileURL(path.join(delivery, 'index.html')).href);
  for (const selector of ['#main-video', '.teaser']) {
    await page.locator(selector).evaluate(async video => {
      if (video.readyState < 1) await new Promise((resolve, reject) => {video.addEventListener('loadedmetadata', resolve, {once: true}); video.addEventListener('error', reject, {once: true});});
      await video.play();
    });
    await page.waitForTimeout(1500);
    const playback = await page.locator(selector).evaluate(video => {
      video.pause();
      return {selector: video.id || video.className, currentTime: video.currentTime, duration: video.duration, decodedFrames: video.getVideoPlaybackQuality().totalVideoFrames, error: video.error?.message || null};
    });
    assert.ok(playback.currentTime > 0.5);
    assert.ok(playback.decodedFrames > 0);
    assert.equal(playback.error, null);
    receipt.playback.push(playback);
  }
  await page.locator('#main-video').evaluate(video => {
    document.body.style.cssText = 'margin:0;padding:0;background:#111a2a';
    video.style.cssText = 'position:fixed;inset:0;width:1920px;height:1080px;margin:0;z-index:99;border-radius:0';
    video.controls = false;
  });
  const frames = path.join(evidence, 'verified-frames');
  await fs.mkdir(frames, {recursive: true});
  for (const scene of scenes) {
    const time = scene.start + Math.min(2, scene.seconds / 2);
    await page.locator('#main-video').evaluate(async (video, time) => {
      await new Promise(resolve => {video.addEventListener('seeked', resolve, {once: true}); video.currentTime = time;});
    }, time);
    await page.waitForTimeout(150);
    await page.screenshot({path: path.join(frames, scene.id + '.png')});
    receipt.frames.push({scene: scene.id, at: time, file: `verified-frames/${scene.id}.png`});
  }
  receipt.passed = true;
} catch (error) {
  receipt.error = error.stack;
  throw error;
} finally {
  if (browser) await browser.close();
  await fs.writeFile(path.join(evidence, 'playback-receipt.json'), JSON.stringify(receipt, null, 2) + '\n');
}
console.log('Both videos decode, play in Edge, and contain no audio. The microphone awaits its rightful owner.');
