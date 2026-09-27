import {readFile, writeFile} from 'node:fs/promises';
import {connectPlowPersona} from './persona.mjs';

const meterPath = '/app/marketing-meter/package.json';
const meter = JSON.parse(await readFile(meterPath, 'utf8'));
if (JSON.stringify(meter.openclaw.extensions) !== '["./index.mjs"]') throw new Error('Marketing meter package changed; review its Plow entry.');
meter.openclaw.extensions = ['./plow/index.mjs'];
await writeFile(meterPath, JSON.stringify(meter, null, 2) + '\n');

const path = '/opt/plow/boot/config.js';
const original = await readFile(path, 'utf8');
const marker = 'export function renderConfig(';
if (original.split(marker).length !== 2) throw new Error('Plow config interface changed; review the new base before building.');
await writeFile(path, "import {configureCockpit} from '/opt/hirezero/configure.mjs';\n" +
  original.replace(marker, 'function renderBaseConfig(') +
  '\nexport function renderConfig(identity, apiBase) { return configureCockpit(renderBaseConfig(identity, apiBase)); }\n');

const prompt = await readFile('/opt/plow/prompt/AGENTS.md', 'utf8');
await writeFile('/opt/plow/prompt/AGENTS.md', connectPlowPersona(prompt) + `

## Hosted cockpit

Read the current company brief with hire profile get before targeted work. The
cockpit owns company onboarding, campaigns, documents, media and owner decisions.
Keep durable work in the shared hire ledger and /var/lib/plow/cockpit; boot-rendered
workspace files are not durable company settings. The owner's phone DM and the
cockpit's general chat use the main session; task conversations remain scoped.
Use the cockpit for approvals. A text reply or a model judgment is never an owner
approval receipt. Publishing needs the existing explicit owner authorization.
Do not start autonomous shifts, enable heartbeat work or claim a spending ceiling
until the host has admitted an owner-granted, metered shift. If the meter is not
ready, describe the blocker plainly and prepare proposals only.
`);
