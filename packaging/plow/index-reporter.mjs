/** Keep the official five-minute schedule, but never leave a collector daemon behind. */
export function adaptIndexReporter(source) {
  const patches = [
    ['env: { PATH: process.env.PATH , HOME: "/var/lib/plow" }',
      'env: { PATH: process.env.PATH , HOME: "/var/lib/plow", AGENTSVIEW_NO_DAEMON: "1" }'],
    ['child.on("error", error => { console.error(`agent-index: no collector sync, usage will read zero: ${error.message}`); resolve(); });',
      'child.on("error", error => { console.error(`agent-index: collector sync failed: ${error.message}`); resolve(1); });'],
    ['child.on("close", () => resolve());',
      'child.on("close", code => resolve(code ?? 1));', 2],
    ['    await sync();',
      '    if (await sync()) { console.error("agent-index: sync failed; no partial usage report was sent"); return; }'],
  ];
  for (const [before, after, afterCount = 1] of patches) {
    if (source.split(after).length === afterCount + 1 && !source.includes(before)) continue;
    if (source.split(before).length !== 2) throw new Error('Plow Index boot reporter changed; review its collector lifecycle.');
    source = source.replace(before, after);
  }
  return source;
}
