import {copyFile, readFile, writeFile} from 'node:fs/promises';
import {hostedPersona} from './persona.mjs';
import {adaptIndexClient} from './index-client.mjs';
import {adaptIndexReporter} from './index-reporter.mjs';

const indexClientPath = '/opt/plow/agent-index-client.py';
await writeFile(indexClientPath, adaptIndexClient(await readFile(indexClientPath, 'utf8')));
await copyFile('/opt/hirezero/index_worker.py', '/opt/plow/index_worker.py');
const indexReporterPath = '/opt/plow/boot/agent-index.js';
await writeFile(indexReporterPath, adaptIndexReporter(await readFile(indexReporterPath, 'utf8')));

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
// The hosted additions live beside this script so a check can render exactly what the image does.
await writeFile('/opt/plow/prompt/AGENTS.md', hostedPersona(prompt, await readFile('/opt/hirezero/hosted-prompt.md', 'utf8')));
