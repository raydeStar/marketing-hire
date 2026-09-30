import assert from 'node:assert/strict';
import test from 'node:test';
import {spawn, spawnSync} from 'node:child_process';
import {randomBytes} from 'node:crypto';
import {mkdir, readFile, writeFile, cp, chmod, readdir, unlink} from 'node:fs/promises';
import {renderConfig, syncConfig} from '/opt/plow/boot/config.js';
import {probeIdentity} from '/opt/plow/boot/probe-fixture.js';
import {plowWorkerSession} from './fetch.mjs';

test('real Gateway refuses ungranted sends and saves one synthetic completion through the pinned meter', {timeout: 120000}, async () => {
  const started = Date.now();
  const progress = message => console.log(`[offline gateway +${Date.now() - started}ms] ${message}`);
  process.env.PLOW_AGENT_TOKEN = 'fictional-offline-agent';
  process.env.PLOW_API_BASE = 'http://127.0.0.1:43127/install-fixture';
  process.env.OPENCLAW_GATEWAY_PASSWORD = randomBytes(32).toString('hex');
  await writeFile('/tmp/plow-meter-mode.json', JSON.stringify({execution: 'a'.repeat(32), admit: false}));
  const config = renderConfig(probeIdentity, process.env.PLOW_API_BASE);
  const {policyReady} = await import('./index.mjs');
  assert.equal(config.models.providers.plow.baseUrl, 'http://127.0.0.1:43127/install-fixture/v1');
  assert.equal(policyReady(config), true, 'The supplied authenticated proxy must qualify');
  for (const mutate of [
    changed => { changed.models.providers.plow.baseUrl = 'https://api.plow.co/v1'; },
    changed => { changed.models.providers.plow.baseUrl += '/other'; },
    changed => { changed.agents.entries['runway-worker'].model.primary = 'plow/anthropic/claude-sonnet-5'; },
    changed => { changed.agents.entries['runway-worker'].model.fallbacks = ['plow/anthropic/claude-sonnet-5']; },
    changed => { changed.agents.entries['runway-worker'].params.maxTokens = 4097; },
    changed => { changed.agents.entries['runway-worker'].tools.deny = []; },
    changed => { changed.models.providers.plow.models.find(item => item.id === 'z-ai/glm-5.2').compat.supportsUsageInStreaming = false; },
    changed => { changed.models.providers.plow.models.find(item => item.id === 'z-ai/glm-5.2').compat.sendSessionAffinityHeaders = false; },
  ]) {
    const changed = structuredClone(config); mutate(changed);
    assert.equal(policyReady(changed), false, 'Proxy support cannot relax the worker policy');
  }
  config.channels.plow.apiBase = 'http://127.0.0.1:1';
  // Windows bind mounts appear as mode 0777. Copy only fixture code into an
  // owned ephemeral directory so the real loader's custody checks stay enabled.
  // Packaged checks must use the production path and permissions unchanged.
  const packaged = process.env.HIREZERO_METER_PACKAGED_CHECK === '1';
  const fixtureMeter = packaged ? '/app/marketing-meter' : '/app/offline-marketing-meter';
  async function protect(directory) {
    await chmod(directory, 0o755);
    for (const item of await readdir(directory, {withFileTypes: true})) {
      assert.equal(item.isSymbolicLink(), false);
      const target = directory + '/' + item.name;
      if (item.isDirectory()) await protect(target); else await chmod(target, 0o644);
    }
  }
  if (!packaged) {
    await cp('/app/marketing-meter', fixtureMeter, {recursive: true});
    const metadata = JSON.parse(await readFile(fixtureMeter + '/package.json', 'utf8'));
    metadata.openclaw.extensions = ['./plow/index.mjs'];
    await writeFile(fixtureMeter + '/package.json', JSON.stringify(metadata));
    await protect(fixtureMeter);
  }
  config.plugins.load.paths = config.plugins.load.paths.map(item => item === '/app/marketing-meter' ? fixtureMeter : item);
  await mkdir('/var/lib/plow/workspace', {recursive: true});
  await mkdir('/var/lib/plow/runway-room', {recursive: true});
  await syncConfig(config, '/var/lib/plow/openclaw.json', '/etc/plow/openclaw');
  const validation = spawnSync(process.execPath, ['/app/openclaw.mjs', 'config', 'validate', '--json'], {encoding: 'utf8', timeout: 15000});
  assert.equal(validation.status, 0, validation.stdout + validation.stderr);
  // Own this fixture process directly: a failed assertion must not wait for the
  // production supervisor's long drain period to end an offline test.
  const gateway = spawn(process.execPath, ['--import', fixtureMeter + '/plow/fixture-response.mjs', '/app/openclaw.mjs', 'gateway'], {env: process.env}); let output = '';
  gateway.stdout.on('data', data => {output += data;}); gateway.stderr.on('data', data => {output += data;});
  async function call(method, params) {
    progress(`calling ${method}${params ? ' ' + params.idempotencyKey : ''}`);
    const args = ['/app/openclaw.mjs', 'gateway', 'call', method, '--json', '--timeout', '15000'];
    if (params) args.push('--params', JSON.stringify(params), '--expect-final');
    const child = spawn(process.execPath, args, {env: process.env}); let stdout = '', stderr = '';
    child.stdout.on('data', data => stdout += data); child.stderr.on('data', data => stderr += data);
    const timeout = setTimeout(() => child.kill('SIGKILL'), 25000);
    const exit = await new Promise(resolve => child.on('close', resolve)); clearTimeout(timeout);
    progress(`${method} exit=${exit}`);
    return {exit, stdout, stderr};
  }
  try {
    for (let attempt = 0; attempt < 300 && !output.includes('[gateway] ready'); attempt++) {
      if (gateway.exitCode !== null) throw new Error('Gateway exited: ' + output);
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert.ok(output.includes('[gateway] ready'), output);
    progress('Gateway ready');
    const status = await call('marketing.meter.status'); assert.equal(status.exit, 0, status.stdout + status.stderr + output);
    const parsed = JSON.parse(status.stdout); const meter = parsed.result || parsed;
    assert.equal(meter.version, 'marketing-meter-plow-v1'); assert.equal(meter.ready, true);
    const worker = id => ({agentId: 'runway-worker', message: 'Offline synthetic worker packet',
      sessionId: 'model-run-' + id, sessionKey: 'agent:runway-worker:model-run-' + id, modelRun: true, promptMode: 'none',
      thinking: 'off', idempotencyKey: id});
    // The shift must use a level this pinned Plow model accepts. The Gateway
    // rejects "low" before a provider request; a working chat cannot prove this.
    await writeFile('/tmp/plow-meter-mode.json', JSON.stringify({execution: 'd'.repeat(32), admit: true}));
    const unsupported = await call('agent', {...worker('d'.repeat(32)), thinking: 'low'});
    assert.notEqual(unsupported.exit, 0, unsupported.stdout);
    assert.match(unsupported.stdout + unsupported.stderr, /Thinking level.*low.*not supported/);
    await assert.rejects(readFile('/tmp/plow-meter-sends.jsonl'));
    await assert.rejects(readFile('/tmp/plow-meter-reservations.jsonl'));
    await writeFile('/tmp/plow-meter-mode.json', JSON.stringify({execution: 'a'.repeat(32), admit: false}));
    const foreign = await call('agent', worker('b'.repeat(32)));
    assert.ok(foreign.stdout.includes('"hook_block"'), foreign.stdout + foreign.stderr);
    await assert.rejects(readFile('/tmp/plow-meter-reservations.jsonl'));
    const claimed = await call('agent', worker('a'.repeat(32)));
    assert.notEqual(claimed.exit, 0, claimed.stdout);
    assert.equal(JSON.parse(claimed.stdout).error.retryable, false);
    assert.ok(output.includes('Plow worker reservation refused dispatch'), output);
    await assert.rejects(readFile('/tmp/plow-meter-sends.jsonl'));
    const jsonLines = async path => (await readFile(path, 'utf8')).trim().split('\n').map(JSON.parse);
    const reservations = await jsonLines('/tmp/plow-meter-reservations.jsonl');
    assert.equal(reservations.length, 1); assert.equal(reservations[0].request_id, 'a'.repeat(32));
    assert.equal(reservations[0].reserved_tokens, 25000);
    await writeFile('/tmp/plow-meter-mode.json', JSON.stringify({execution: 'c'.repeat(32), admit: true}));
    const success = await call('agent', worker('c'.repeat(32)));
    assert.equal(success.exit, 0, success.stdout + success.stderr);
    assert.ok(success.stdout.includes('Prepared offline campaign'), success.stdout);
    const sends = await jsonLines('/tmp/plow-meter-sends.jsonl');
    assert.equal(sends.length, 1); assert.equal(sends[0].session, plowWorkerSession('c'.repeat(32)));
    assert.equal(sends[0].url, 'http://127.0.0.1:43127/install-fixture/v1/chat/completions');
    assert.equal(sends[0].body.max_tokens, 4096); assert.equal(sends[0].redirect, 'error');
    assert.equal(sends[0].body.stream_options.include_usage, true); assert.ok(!sends[0].body.tools?.length);
    const receipts = await jsonLines('/tmp/plow-meter-receipts.jsonl');
    assert.equal(receipts.length, 1); assert.equal(receipts[0].status, 'reported'); assert.equal(receipts[0].reported_tokens, 8);
    const after = await jsonLines('/tmp/plow-meter-reservations.jsonl');
    assert.equal(after.length, 2); assert.equal(after[1].request_digest, receipts[0].request_digest);
    assert.equal(receipts[0].response_receipt.terminal_type, 'chat.completion.done');
    // Gateway idempotency cannot create another paid request.
    await call('agent', worker('c'.repeat(32)));
    assert.equal((await jsonLines('/tmp/plow-meter-sends.jsonl')).length, 1);
    progress('refusal, exact packet, one physical send, durable receipt and replay verified');
    // Exercise the real CLI entrance used by cockpit chat as well as gateway RPC.
    // The agent command requires a real file, unlike agent exec's stdin option.
    await writeFile('/tmp/plow-meter-mode.json', JSON.stringify({execution: null, admit: true}));
    const prompt = 'Owner’s café 🦉\n' + 'A long onboarding brief. '.repeat(1500);
    const promptFile = '/tmp/hirezero-cli-prompt.txt';
    await writeFile(promptFile, prompt, {mode: 0o600, flag: 'wx'});
    const cli = spawn(process.execPath, ['/app/openclaw.mjs', 'agent', '--agent', 'main',
      '--session-key', 'agent:main:main', '--message-file', promptFile, '--model', 'plow/z-ai/glm-5.2',
      '--json', '--timeout', '30'], {env: process.env});
    let cliOutput = '', cliError = '';
    cli.stdout.on('data', data => {cliOutput += data;}); cli.stderr.on('data', data => {cliError += data;});
    cli.stdin.end();
    const cliTimer = setTimeout(() => cli.kill('SIGKILL'), 45000);
    const cliExit = await new Promise(resolve => cli.on('close', resolve)); clearTimeout(cliTimer);
    await unlink(promptFile);
    assert.equal(cliExit, 0, cliOutput + cliError);
    assert.match(cliOutput, /Prepared offline campaign/);
    const cliReply = JSON.parse(cliOutput);
    assert.equal(cliReply.status, 'ok');
    assert.ok(cliReply.result.payloads.some(payload => payload.text === 'Prepared offline campaign'));
    const chatSends = await jsonLines('/tmp/plow-meter-sends.jsonl');
    assert.equal(chatSends.length, 2);
    assert.ok(JSON.stringify(chatSends[1].body).includes('Owner’s café 🦉'));
    assert.equal((await jsonLines('/tmp/plow-meter-receipts.jsonl')).length, 1);
    progress('CLI file prompt reached the main agent and returned a confirmed reply');
  } catch (error) {
    console.error('Offline Gateway diagnostic output:\n' + output.slice(-18000));
    throw error;
  } finally {
    gateway.kill('SIGTERM');
    const kill = setTimeout(() => gateway.kill('SIGKILL'), 3000);
    if (gateway.exitCode === null && gateway.signalCode === null) await new Promise(resolve => gateway.once('exit', resolve));
    clearTimeout(kill);
    progress('owned Gateway stopped');
  }
});
