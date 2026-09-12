import { readFileSync } from 'node:fs';
import { definePluginEntry } from 'openclaw/plugin-sdk/plugin-entry';
import { contextHooks } from './context.mjs';

export default definePluginEntry({
  id: 'thaddeus-context', name: 'Thaddeus prepared context',
  description: 'Deliver frozen product context to its bound native OpenClaw session.',
  register(api) {
    const settings = api.pluginConfig;
    const context = JSON.parse(readFileSync(settings.contextPath, 'utf8'));
    const hooks = contextHooks(settings, context);
    for (const [name, handler] of Object.entries(hooks)) api.on(name, handler);
  }
});
