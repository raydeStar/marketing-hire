import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import test from 'node:test';
import {connectPlowPersona} from './persona.mjs';

test('the committed employee persona adapts identically with LF, CRLF or mixed checkout endings', async () => {
  const original = await readFile(new URL('../../business/agent/prompt/AGENTS.md', import.meta.url), 'utf8');
  const lf = original.replace(/\r\n/g, '\n');
  const expected = connectPlowPersona(lf);
  assert.equal(connectPlowPersona(lf.replaceAll('\n', '\r\n')), expected);
  assert.equal(connectPlowPersona(original), expected);
  assert.ok(expected.includes('Use a unique `--request-id`'));
  assert.ok(expected.includes('Do not call `hire draft decide`'));
  assert.ok(!expected.includes('Plow Chat is a later hosted option.'));
});

test('a changed or ambiguous connection contract still refuses the package', () => {
  const marker = 'The local cockpit is the current connection;\nPlow Chat is a later hosted option.';
  assert.throws(() => connectPlowPersona('Unreviewed new persona.'), /Marketing persona changed/);
  assert.throws(() => connectPlowPersona(marker + '\n' + marker), /Marketing persona changed/);
});
