import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createServer} from 'node:net';
import {mkdir, writeFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {chromium} from '../web/node_modules/playwright/index.mjs';
import {companionConfig, runCompanion, checkCompanionHost} from '../packaging/plow/companion-connector.mjs';
import {cleanArtifactPaths, requireArtifactSpace} from './artifact-storage.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const landing = path.resolve(process.argv[2]);
const name = process.argv[3];
assert.match(name || '', /^companion-check-[a-z0-9-]+$/);
const evidence = path.join(repo, 'artifacts', name), scratch = path.join(evidence, 'scratch');
await requireArtifactSpace(repo, 128 * 1024 ** 2, 'Fictional shared-workspace browser check');
for (const port of [5183, 5184]) {
  const server = createServer();
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(port, '127.0.0.1', resolve); });
  await new Promise(resolve => server.close(resolve));
}
await mkdir(scratch, {recursive: true});
const children = [], logs = [];
function launch(command, args, env) {
  const child = spawn(command, args, {cwd: repo, env: {...process.env, ...env}, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe']});
  child.stdout.on('data', data => logs.push(data.toString())); child.stderr.on('data', data => logs.push(data.toString()));
  children.push(child); return child;
}
const controller = new AbortController();
let browser, connection, failure, finishing = false;
const observations = [];
try {
  const cms = launch('py', ['-3', path.join(landing, 'cms/tests/companion_fixture.py'), scratch]);
  const paired = await new Promise((resolve, reject) => {
    let line = '';
    const timer = setTimeout(() => reject(new Error('Fixture did not start')), 20000);
    cms.once('exit', () => { clearTimeout(timer); reject(new Error('Fixture exited')); });
    cms.stdout.on('data', data => { line += data; if (line.includes('\n')) { clearTimeout(timer); resolve(JSON.parse(line.split('\n')[0])); } });
  });
  const secret = 'fictional-local-companion-signing-secret-for-this-test';
  launch('dotnet', [path.join(repo, 'src/Thaddeus.Host/bin/Debug/net10.0/Thaddeus.Host.dll'), '--contentRoot', path.join(repo, 'src/Thaddeus.Host')], {
    TMP: scratch, TEMP: scratch,
    Thaddeus__Data: path.join(scratch, 'host'), Thaddeus__LocalOrigin: 'http://localhost:5184', Thaddeus__PhoneMode: 'direct',
    Thaddeus__CompanionOrigin: paired.origin, Thaddeus__CompanionWorkspace: paired.workspace,
    Thaddeus__CompanionOwner: 'owner-fixture', Thaddeus__CompanionSecret: secret,
    Marketing__FixtureLedger: path.join(scratch, 'ledger'), Marketing__FixtureRunwayScript: path.join(repo, 'business/agent/hire/bin/runway.py'),
    Marketing__ShiftPump: 'off', Marketing__Container: 'nonexistent-companion-fixture', Thaddeus__ApiRequestsPerMinute: '3000'
  });
  const config = companionConfig({brokerOrigin: 'http://127.0.0.1:5183', workspaceOrigin: paired.origin,
    workspace: paired.workspace, credential: paired.token, ownerUid: 'owner-fixture', ingressSecret: secret, development: true});
  for (let attempt = 0; ; attempt++) {
    try { await checkCompanionHost(config); break; } catch (error) { if (attempt >= 60) throw error; await new Promise(resolve => setTimeout(resolve, 250)); }
  }
  connection = runCompanion(config, {signal: controller.signal});
  browser = await chromium.launch({headless: true});
  async function context() {
    const context = await browser.newContext({serviceWorkers: 'block'});
    await context.addInitScript(() => {
      // A route.fetch response cannot stay open as a browser EventSource. The
      // HTTP relay stream/revocation contract has separate real-socket tests.
      window.EventSource = class extends EventTarget {
        readyState = 1;
        constructor() { super(); setTimeout(() => this.onopen?.(new Event('open')), 0); }
        close() { this.readyState = 2; }
      };
    });
    // Only DNS/TLS is substituted. HTML, cookies, invitation registry, Node
    // connector and .NET authorization all run as shipped in separate processes.
    await context.route(paired.origin + '/**', async route => {
      try {
      const request = route.request();
      if (new URL(request.url()).pathname === '/api/events') return route.fulfill({status: 200, contentType: 'text/event-stream', body: ': fixture heartbeat\n\n'});
      const response = await route.fetch({url: 'http://127.0.0.1:5183' + new URL(request.url()).pathname + new URL(request.url()).search,
        headers: {...await request.allHeaders(), host: new URL(paired.origin).host}, timeout: 45000, maxRedirects: 0});
      await route.fulfill({response});
      } catch (error) { if (!finishing) logs.push('Fixture browser request ended: ' + error.message.split('\n')[0] + '\n'); await route.abort().catch(() => {}); }
    });
    return context;
  }
  const ownerContext = await context(), memberContext = await context(), otherContext = await context();
  const owner = await ownerContext.newPage(), member = await memberContext.newPage(), other = await otherContext.newPage();
  async function login(page, phone, url = 'http://127.0.0.1:5183/account/') {
    await page.goto(url); await page.getByLabel('Phone number', {exact: true}).fill(phone);
    await page.getByRole('button', {name: 'Send me a code', exact: true}).click();
    await page.getByLabel('Sign-in code').fill('01234567'); await page.getByRole('button', {name: 'Open my workspace', exact: true}).click();
  }
  await login(owner, '2025550101');
  await owner.waitForURL(paired.origin + '/');
  await owner.getByRole('button', {name: 'Close onboarding'}).click();
  await owner.getByRole('button', {name: 'Team', exact: true}).click();
  await owner.getByRole('button', {name: 'Invite', exact: true}).click();
  await owner.getByRole('link', {name: 'Invite or manage teammates'}).click();
  await owner.getByLabel('Teammate’s name').fill('Alex Reviewer');
  await owner.getByLabel('Who can use the invitation?').selectOption('phone');
  await owner.getByLabel('Their phone number').fill('2025550102');
  await owner.getByRole('button', {name: 'Create invitation', exact: true}).click();
  const invitation = await owner.getByLabel('Invitation link').inputValue();
  for (const width of [1440, 390]) {
    await owner.setViewportSize({width, height: width === 390 ? 844 : 1000});
    assert.equal(await owner.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, 'Invitation layout overflow');
    await owner.screenshot({path: path.join(evidence, `invite-${width}.png`), fullPage: true});
  }
  await login(other, '2025550103', invitation); await other.getByRole('button', {name: 'Join workspace', exact: true}).click();
  await other.getByRole('alert').filter({hasText: 'unavailable'}).waitFor();
  observations.push('Forwarding an invitation to a different phone grants no access.');
  await login(member, '2025550102', invitation); await member.getByRole('button', {name: 'Join workspace', exact: true}).click();
  await member.waitForURL(paired.origin + '/');
  await member.getByText('Opening your workspace…', {exact: true}).waitFor({state: 'hidden'});
  await member.waitForTimeout(1500);
  const permission = await member.evaluate(async () => ({session: await (await fetch('/api/session')).json(), forbidden: (await fetch('/api/export')).status}));
  assert.equal(permission.session.owner, false); assert.equal(permission.forbidden, 403);
  observations.push('A second browser joins as its own native Reviewer account; private export is refused.');
  for (const width of [1440, 390]) {
    await member.setViewportSize({width, height: width === 390 ? 844 : 1000});
    assert.equal(await member.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, 'Member layout overflow');
    await member.screenshot({path: path.join(evidence, `member-${width}.png`), fullPage: true});
  }
  await owner.reload();
  owner.once('dialog', dialog => dialog.accept());
  await owner.getByRole('button', {name: 'Remove Alex Reviewer', exact: true}).click();
  await owner.getByRole('button', {name: 'Remove Alex Reviewer', exact: true}).waitFor({state: 'hidden'});
  assert.equal(await member.evaluate(async () => (await fetch('/api/session')).status), 401);
  observations.push('Owner revocation ends the already-open teammate session.');
} catch (error) {
  failure = error.stack;
  await writeFile(path.join(evidence, 'failure.txt'), failure);
  for (const [index, context] of (browser?.contexts() || []).entries()) {
    const page = context.pages()[0];
    if (page && !page.isClosed()) {
      await page.screenshot({path: path.join(evidence, `failure-${index}.png`)}).catch(() => {});
      await writeFile(path.join(evidence, `failure-${index}.txt`), await page.locator('body').innerText().catch(() => 'Page closed'));
    }
  }
}
finally {
  finishing = true;
  await browser?.close(); controller.abort(); await connection;
  for (const child of children.reverse()) {
    if (child.exitCode === null && child.signalCode === null) {
      const ended = new Promise(resolve => child.once('exit', resolve));
      if (child.spawnargs[0] === 'py') child.stdin.end('\n'); else child.kill();
      await ended;
    }
  }
  const removed = await cleanArtifactPaths(evidence, ['scratch']);
  await writeFile(path.join(evidence, 'receipt.json'), JSON.stringify({failure, observations, removed, liveModels: false, livePlow: false, tls: 'fictional DNS/TLS routing only', sse: 'synthetic heartbeat'}, null, 2));
  // Fictional launch/session keys are omitted from retained process output.
  await writeFile(path.join(evidence, 'process.log'), logs.join('').replace(/#launch=\w+/g, '#launch=[fixture]').replace(/"token":\s*"[^"]+"/g, '"token":"[fixture]"'));
}
if (failure) throw new Error(failure);
console.log('Shared-workspace browser check passed; the fictional household has been packed away.');
