import {spawnSync} from 'node:child_process';
import {readFileSync, rmSync, writeFileSync} from 'node:fs';

// Texts over Plow get their state and this turn's rules from code, beside the owner's message: a small model follows what
// sits next to the message far more reliably than a long persona. Cockpit chat is framed by the host and isn't touched.

/** A message that reached the employee over Plow (the owner's phone, a thread), not cockpit chat. */
export function isTextTurn(context, env = process.env) {
  if (context?.agentId !== 'main') return false;
  if (context.channel === 'plow' || context.messageProvider === 'plow') return true;
  // Only a private test install sets this: its scripted sessions stand in for texts. Production never does.
  const probe = env.HIREZERO_TEXT_EVAL_SESSIONS;
  return typeof probe === 'string' && probe.startsWith('agent:main:') && typeof context.sessionKey === 'string' && context.sessionKey.startsWith(probe);
}

function hire(...args) {
  const result = spawnSync('python3', ['/opt/hire/bin/hire.py', ...args], {encoding: 'utf8', timeout: 5000, maxBuffer: 1 << 20});
  if (result.error || result.status !== 0) throw new Error('The work ledger is unavailable.');
  return JSON.parse(result.stdout);
}

/** The brief and the open tasks, read from the ledger; null when it can't be read. */
export function readWork(run = hire) {
  try { return {profile: run('profile', 'get'), tasks: run('task', 'list', '--limit', '50')}; } catch { return null; }
}

const cut = (text, length) => { const flat = String(text ?? '').replace(/\s+/g, ' ').trim(); return flat.length > length ? flat.slice(0, length) + '…' : flat; };
const labels = {ready: 'queued', working: 'being worked on', needs_you: 'waiting on the owner', paused: 'paused'};
const queued = 'On it. I\'ll text you here when the drafts are ready.';

/** This turn's block: what the owner has told it (the only product facts it may state), the open tasks, and what to do,
 * with the exact commands, since a small model otherwise spends turns guessing their syntax. */
export function textModeBlock(work) {
  const profile = work?.profile ?? {};
  const facts = [['product', profile.product_summary], ['facts and claims', profile.claims], ['audience', profile.audience], ['channels', profile.channels]]
    .filter(([, value]) => String(value ?? '').trim()).map(([name, value]) => `${name}: ${cut(value, 700)}`);
  const open = (Array.isArray(work?.tasks) ? work.tasks : []).filter(task => labels[task.status]).slice(0, 6)
    .map(task => `- "${cut(task.title, 90)}" (${labels[task.status]}, id ${task.id}, version ${task.version})` + (task.status === 'needs_you' && task.blocker ? `: it asked "${cut(task.blocker, 200)}"` : ''));
  const known = Boolean(String(profile.product_summary ?? '').trim());
  const version = Number.isInteger(profile.version) ? profile.version : '<version from hire profile get>';
  return [
    '[HIREZERO TEXT MODE: written by code from the work ledger, not by the owner]',
    work ? `Saved facts you may state: ${facts.length ? facts.join('; ') : 'none yet.'}` : 'Saved facts: the ledger could not be read; state no product facts.',
    `Open tasks: ${open.length ? '\n' + open.join('\n') : 'none.'}`,
    'For this reply:',
    '- Never write posts, emails, ads, captions or other copy customers will see, not even a sample line; never save a file or hand the work to another session. Your background worker drafts from a task, and its drafts are checked against the facts. Always end with a reply to the owner.',
    ...known ? [] : ['- The brief is empty. If they only said hello or asked what you do, introduce yourself in one line as their marketing employee and ask, numbered: what they sell and their website, who buys it, the facts you may state (price, key specs, dates, where to buy), and where they want to show up and what they most want from marketing right now.'],
    '- If they tell you about their business without asking for a piece, save it (what they want from marketing goes in goals). Then offer their first shift: run hire cockpit propose --input-json \'{"type":"first_shift"}\', and reply with one line on what you saved followed by its confirmText exactly. If it says the brief needs more, ask for that instead. Create no task yourself.',
    '- Talk it through like a good marketer would: questions are about this business and this piece, not a generic form.',
    known
      ? `- If they ask for copy: when their messages and the saved facts give what the piece needs (what it is and what's special, the price or offer, when and where to get it), or they say go, queue it and reply only "${queued}" Otherwise ask for just what's missing (at most four numbered questions), then "Or reply go and I'll draft now with [brackets] where those go."`
      : `- If they ask for copy, reply only with at most four numbered questions for the facts the piece needs (what it is and what's special, the price or offer, when and where to get it), then "Or reply go and I'll draft now with [brackets] where those go."`,
    `- When they answer your questions: save what they gave (hire profile update). If the piece still lacks something it can't do without, ask once more for just that, numbered, with the go option; ask at most twice for one piece. Otherwise, or when they say go, queue it with their answers word for word in the next action and reply only "${queued}"`,
    '- If they answer a question from an open task, or ask for changes to drafts it sent: hire task answer --id <id> --text "<their words>". Say it\'s going back to work.',
    `- Save facts they give about their business: hire profile update --input-json '{"request_id":"<new unique id>","version":${version},"product_summary":"...","claims":"..."}' (fields: product_summary, audience, voice, goals, guardrails, channels, claims, examples; send only those that change). Save only what they said, in their words: never an assumption or a guess. Never state a product detail that isn't in the saved facts or their own messages.`,
    '- Queue work: hire task create --input-json \'{"request_id":"<new unique id>","title":"...","status":"ready","priority":"normal","action_state":"agent_ready","next_action":"<their words and every fact they gave>"}\'',
    '- Approving, rejecting, posting, scheduling, shifts, working hours, the weekly plan and brief edits go through hire cockpit (see "The cockpit by text"): propose the change, send its confirmText exactly, and confirm only after they reply yes.',
  ].join('\n');
}

// One turn at a time on the meter: while a text is being answered the worker holds its next turn, and a text that arrives
// during a worker turn waits for it rather than being refused (a refused text gets no reply at all).
export const TEXT_TURN_FILE = '/var/lib/plow/text-turn.json';
export function markTextTurn(runId, file = TEXT_TURN_FILE, now = Date.now()) {
  try { writeFileSync(file, JSON.stringify({runId: String(runId ?? ''), at: now})); } catch { /* the worker's wait is a courtesy; the meter still enforces */ }
}
export function clearTextTurn(runId, file = TEXT_TURN_FILE) {
  try { const marked = JSON.parse(readFileSync(file, 'utf8')); if (!runId || marked.runId === String(runId)) rmSync(file, {force: true}); } catch { /* nothing marked */ }
}
/** Polls until the worker's metered turn has settled, for at most `limit` ms (the hook itself has 15 seconds). */
export async function settled(read, limit = 13_000, step = 500) {
  const until = Date.now() + limit;
  let active = read();
  while (active.execution_id && Date.now() < until) { await new Promise(resolve => setTimeout(resolve, step)); active = read(); }
  return active;
}

// A text turn answers the owner itself: no files they can't open, and no other session doing the work out of their sight.
const offText = new Set(['write', 'edit', 'apply_patch', 'sessions_spawn', 'sessions_yield', 'sessions_send', 'subagents']);

export function registerTextMode(api, read = readWork, file = TEXT_TURN_FILE) {
  api.on('agent_end', (_event, context) => { if (isTextTurn(context)) clearTextTurn(context.runId, file); });
  api.on('before_prompt_build', (_event, context) => isTextTurn(context) ? {appendContext: textModeBlock(read())} : undefined);
  api.on('before_tool_call', (event, context) => isTextTurn(context) && offText.has(event?.toolName)
    ? {block: true, blockReason: 'Not in a text conversation: queue the work with hire task create (the background worker drafts it), then reply to the owner yourself.'}
    : undefined);
}
