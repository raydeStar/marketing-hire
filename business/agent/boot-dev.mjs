// Dev boot: the same image, persona, skills and tools as production, without Plow.
// No identity lookup, no Plow model or chat channel, no Agent Index reporting.
// The OpenClaw Control UI is enabled so you can talk to the hire in a browser,
// and the model is whatever account you connect there (for example Codex).
import { spawn } from "node:child_process";
import { mkdir, readFile, rm, writeFile } from "node:fs/promises";

const port = Number(process.env.DEV_GATEWAY_PORT || 18795);
const token = process.env.DEV_GATEWAY_TOKEN;
if (!token || token.length < 24) {
  console.error("dev-boot: set DEV_GATEWAY_TOKEN (24+ characters) in dev/.env");
  process.exit(1);
}

const config = {
  meta: {},
  gateway: {
    mode: "local",
    // lan so Docker's port mapping can reach it; compose publishes it on 127.0.0.1 only.
    bind: "lan", port,
    controlUi: { enabled: true },
    auth: { mode: "token", token: "${DEV_GATEWAY_TOKEN}" },
    reload: { mode: "off" },
  },
  agents: {
    ownership: "explicit",
    entries: {
      main: { identity: { name: process.env.DEV_AGENT_NAME || "Marketing agent" } },
      // The standing assignment receives only a bounded host work packet. It cannot
      // use shell, browser, messaging, or account tools from a model turn.
      "runway-worker": { identity: { name: "Marketing employee" }, workspace: "/var/lib/plow/runway-room",
        tools: { deny: ["*"] }, params: { maxTokens: 1800 },
        // Keep the owner's Chat route intact. The future request meter needs the
        // worker's JavaScript transport, not an opaque native Codex subprocess.
        models: { "openai/gpt-5.6-luna": { agentRuntime: { id: "openclaw" },
          // The request meter guards HTTP fetch, so WebSocket egress is outside this worker's policy.
          params: { transport: "sse" } } } },
      "meeting-ceo": { identity: { name: "CEO" }, workspace: "/var/lib/plow/meeting-room", tools: { deny: ["*"] } },
      "meeting-marketing": { identity: { name: "Marketing planner" }, workspace: "/var/lib/plow/meeting-room", tools: { deny: ["*"] } },
      "meeting-worker": { identity: { name: "Marketing meeting worker" }, workspace: "/var/lib/plow/meeting-room", tools: { deny: ["*"] } },
    },
    defaults: {
      workspace: "/var/lib/plow/workspace", skipBootstrap: true, sandbox: { mode: "off" },
      systemAgent: { agentId: "main" }, heartbeat: { agentId: "main", every: "0m" },
      // The current subscription catalog materializes this exact route; GPT-6 Luna did not.
      model: { primary: "openai/gpt-5.6-luna", fallbacks: [] },
    },
  },
  session: { dmScope: "per-account-channel-peer", groupScope: "per-group" },
  memory: { search: { rememberAcrossConversations: false } },
  cron: { enabled: false },
  plugins: {
    load: { paths: ["/app/marketing-meter"] },
    entries: { "memory-core": { config: { dreaming: { enabled: false } } },
      "marketing-request-meter": { enabled: true } },
  },
  skills: { load: { extraDirs: ["/opt/plow/skills"] }, allowBundled: ["plow-no-bundled-skills"] },
  // Production's tools minus the Plow channel tool, which needs a Plow line.
  tools: { profile: "messaging", sessions: { visibility: "tree" }, alsoAllow: ["read", "write", "edit", "exec", "cron"], deny: ["ask_user"] },
};

await mkdir("/var/lib/plow/workspace", { recursive: true });
await mkdir("/var/lib/plow/meeting-room", { recursive: true });
await mkdir("/var/lib/plow/runway-room", { recursive: true });
await writeFile("/var/lib/plow/meeting-room/AGENTS.md", "You participate in business planning meetings. The host provides your role, agenda, ethos, and transcript. Ask clear questions, identify assumptions, propose bounded internal work, and review plans honestly. You have no tools and cannot execute actions, spend money, contact people, or approve on the owner's behalf. CEO review is advisory. Only an explicit owner grant for the exact plan permits the host to dispatch the restricted meeting worker. Never claim task completion from discussion alone.\n");
for (const name of ["BOOTSTRAP.md", "SOUL.md", "IDENTITY.md", "USER.md"]) {
  await rm(`/var/lib/plow/workspace/${name}`, { force: true });
}
const prompt = await readFile("/opt/plow/prompt/AGENTS.md", "utf8");
const devNote = `
## Development mode

You are running locally in the owner's Marketing cockpit, not on a Plow phone
line. Plow tools such as plow_start_thread are unavailable. Do not imply a
phone line, external group thread, email, or Latch connection is active.
`;
await writeFile("/var/lib/plow/workspace/AGENTS.md", prompt + devNote);
await writeFile("/var/lib/plow/openclaw.json", JSON.stringify(config, null, 2) + "\n", { mode: 0o600 });
console.log(`dev-boot: gateway on port ${port}; Control UI enabled; no Plow connection`);

const child = spawn(process.execPath, ["/app/openclaw.mjs", "gateway", "run", "--port", String(port), "--bind", "lan"], { stdio: "inherit" });
for (const signal of ["SIGTERM", "SIGINT"]) process.on(signal, () => child.kill(signal));
child.on("exit", code => process.exit(code ?? 0));
