import assert from 'node:assert/strict';
import test from 'node:test';
import {mkdtempSync, existsSync, readFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {admitText, clearTextTurn, isTextTurn, markTextTurn, readWork, registerTextMode, settled, TEXT_BUSY, TEXT_GATE_TIMEOUT_MS, TEXT_WAIT_MS, textModeBlock} from './text-mode.mjs';

test('only texts over Plow to the main employee are text turns; cockpit chat and the worker are not', () => {
  assert.equal(isTextTurn({agentId: 'main', channel: 'plow', sessionKey: 'agent:main:main'}, {}), true);
  assert.equal(isTextTurn({agentId: 'main', messageProvider: 'plow'}, {}), true);
  assert.equal(isTextTurn({agentId: 'main', sessionKey: 'agent:main:main'}, {}), false);            // the cockpit's own chat turn
  assert.equal(isTextTurn({agentId: 'runway-worker', channel: 'plow'}, {}), false);
  // A private test install's scripted sessions stand in for texts, and only those.
  const probe = {HIREZERO_TEXT_EVAL_SESSIONS: 'agent:main:texteval-'};
  assert.equal(isTextTurn({agentId: 'main', sessionKey: 'agent:main:texteval-after-1'}, probe), true);
  assert.equal(isTextTurn({agentId: 'main', sessionKey: 'agent:main:main'}, probe), false);
  assert.equal(isTextTurn({agentId: 'main', sessionKey: 'agent:main:main'}, {HIREZERO_TEXT_EVAL_SESSIONS: 'agent'}), false);
});

test('with no saved facts the rule is to ask; with facts, to queue the work; the open tasks carry their ids and questions', () => {
  const empty = textModeBlock({profile: {product_summary: '', claims: ''}, tasks: []});
  assert.match(empty, /Saved facts you may state: none yet\./);
  assert.match(empty, /If they ask for copy, reply only with at most four numbered questions/);
  assert.match(empty, /Never write posts, emails, ads, captions/);
  const known = textModeBlock({profile: {product_summary: 'Walnut standing desk, $1,290', claims: 'Adjusts 25 to 50 inches'},
    tasks: [{id: 'a1', version: 3, title: 'Launch posts', status: 'needs_you', blocker: 'What is the launch date?'}, {id: 'b2', version: 1, title: 'Old', status: 'done'}]});
  assert.match(known, /product: Walnut standing desk, \$1,290; facts and claims: Adjusts 25 to 50 inches/);
  assert.match(known, /"Launch posts" \(waiting on the owner, id a1, version 3\): it asked "What is the launch date\?"/);
  assert.doesNotMatch(known, /"Old"/);
  assert.match(known, /hire task create --input-json/);
  assert.doesNotMatch(known, /The brief is empty/);
  assert.match(empty, /The brief is empty/);
  assert.match(empty, /what they most want from marketing right now/);                 // saved as goals, so the cockpit's onboarding counts it done
  assert.match(known, /ask once more for just that, numbered, with the go option; ask at most twice for one piece/);
  assert.match(known, /hire task answer --id <id> --text/);
  assert.match(known, /never an assumption or a guess/);
  assert.doesNotMatch(empty, /not an assistant/);
  assert.match(known, /offer their first shift: run hire cockpit propose --input-json '\{"type":"first_shift"\}'/);
  assert.match(textModeBlock({profile: {product_summary: '', version: 4}, tasks: []}), /"version":4,"product_summary"/);
  assert.match(textModeBlock(null), /the ledger could not be read; state no product facts/);
});

test('a text turn gets the block beside its message and may not save files; other turns are untouched', () => {
  const hooks = {};
  registerTextMode({on: (name, handler) => { hooks[name] = handler; }}, () => ({profile: {product_summary: ''}, tasks: []}));
  const text = {agentId: 'main', channel: 'plow'};
  assert.match(hooks.before_prompt_build({}, text).appendContext, /^\[HIREZERO TEXT MODE/);
  assert.equal(hooks.before_prompt_build({}, {agentId: 'main'}), undefined);
  assert.equal(hooks.before_tool_call({toolName: 'write'}, text).block, true);
  assert.equal(hooks.before_tool_call({toolName: 'sessions_spawn'}, text).block, true);   // nobody else does the work out of sight
  assert.equal(hooks.before_tool_call({toolName: 'exec'}, text), undefined);
  assert.equal(hooks.before_tool_call({toolName: 'write'}, {agentId: 'main'}), undefined);
  assert.equal(readWork(() => { throw new Error('down'); }), null);
});

test('a text turn marks itself for the worker and clears only its own mark; a worker turn is waited out, not refused', async () => {
  const file = path.join(mkdtempSync(path.join(tmpdir(), 'text-turn-')), 'text-turn.json');
  markTextTurn('run-a', file);
  clearTextTurn('run-b', file);
  assert.equal(existsSync(file), true);          // another turn's end leaves it
  clearTextTurn('run-a', file);
  assert.equal(existsSync(file), false);
  const hooks = {};
  registerTextMode({on: (name, handler) => { hooks[name] = handler; }}, () => null, file);
  markTextTurn('run-c', file);
  hooks.agent_end({}, {agentId: 'main', channel: 'plow', runId: 'run-c'});
  assert.equal(existsSync(file), false);
  let reads = 0;
  const clear = await settled(() => (++reads < 3 ? {execution_id: 'w1'} : {execution_id: null}), 2000, 10);
  assert.equal(clear.execution_id, null);
  const still = await settled(() => ({execution_id: 'w1'}), 50, 10);
  assert.equal(still.execution_id, 'w1');       // past the limit it gives up, and the meter refuses as before
});

test('a text sent mid-shift holds the worker from its first wait, outwaits the step in flight, and is answered', async () => {
  const file = path.join(mkdtempSync(path.join(tmpdir(), 'text-turn-')), 'text-turn.json');
  let reads = 0, markedWhileWaiting = false;
  const admitted = await admitText('run-d', () => {
    if (++reads === 2) markedWhileWaiting = existsSync(file);   // the worker sees the mark before its next step
    return reads < 4 ? {execution_id: 'w1'} : {execution_id: null};
  }, file, 2000, 10);
  assert.equal(admitted, true);
  assert.equal(markedWhileWaiting, true);
  assert.equal(JSON.parse(readFileSync(file, 'utf8')).runId, 'run-d');   // still held for the reply itself
  // A step that never settles: the text is refused with words for the owner, and releases the worker.
  assert.equal(await admitText('run-e', () => ({execution_id: 'w2'}), file, 50, 10), false);
  assert.equal(existsSync(file), false);
  await assert.rejects(admitText('run-f', () => { throw new Error('ledger down'); }, file, 50, 10));
  assert.equal(existsSync(file), false);
  assert.ok(TEXT_GATE_TIMEOUT_MS > TEXT_WAIT_MS && TEXT_WAIT_MS > 135_000);   // outlasts a worker step, inside the hook's limit
  assert.match(TEXT_BUSY, /shift/);
});
