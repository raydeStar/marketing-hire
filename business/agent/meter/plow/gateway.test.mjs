import assert from 'node:assert/strict';
import test from 'node:test';
import {spawn, spawnSync} from 'node:child_process';
import {randomBytes} from 'node:crypto';
import {mkdir, readFile, writeFile, cp, chmod, readdir} from 'node:fs/promises';
import {renderConfig, syncConfig} from '/opt/plow/boot/config.js';
import {probeIdentity} from '/opt/plow/boot/probe-fixture.js';
import {plowWorkerSession} from './fetch.mjs';

test('real Gateway refuses ungranted sends and saves one synthetic completion through the pinned meter', {timeout: 120000}, async () => {
  const started = Date.now();
  const progress = message => console.log(`[offline gateway +${Date.now() - started}ms] ${message}`);
  process.env.PLOW_AGENT_TOKEN = 'fictional-offline-agent';
  process.env.OPENCLAW_GATEWAY_PASSWORD = randomBytes(32).toString('hex');
  await writeFile('/tmp/plow-meter-mode.json', JSON.stringify({execution: 'a'.repeat(32), admit: false}));
  const config = renderConfig(probeIdentity, 'https://api.plow.co');
  config.channels.plow.apiBase = 'http://127.0.0.1:1';
  // Windows bind mounts appear as mode 0777. Copy only fixture code into an
  // owned ephemeral directory so the real loader's custody checks stay enabled.
  const fixtureMeter = '/app/offline-marketing-meter';
  await cp('/app/marketing-meter', fixtureMeter, {recursive: true});
  const metadata = JSON.parse(await readFile(fixtureMeter + '/package.json', 'utf8'));
  metadata.openclaw.extensions = ['./plow/index.mjs'];
  await writeFile(fixtureMeter + '/package.json', JSON.stringify(metadata));
  async function protect(directory) {
    await chmod(directory, 0o755);
    for (const item of await readdir(directory, {withFileTypes: true})) {
      assert.equal(item.isSymbolicLink(), false);
      const target = directory + '/' + item.name;
      if (item.isDirectory()) await protect(target); else await chmod(target, 0o644);
    }
  }
  await protect(fixtureMeter);
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
      idempotencyKey: id});
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
    assert.equal(sends[0].body.max_tokens, 1800); assert.equal(sends[0].redirect, 'error');
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
