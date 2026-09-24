// One isolated, local shared conversation domain. The owner Gateway keeps its
// existing state and OAuth route; this service receives no owner files or tools.
import { spawn } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";

const port = Number(process.env.DEV_GATEWAY_PORT || 18995);
// The host assigns each paired device one permanent slot. A slot is never
// recycled, so two devices cannot silently inherit the same Gateway person.
const nativeIdentities = ["owner@cockpit.local", ...Array.from({ length: 16 }, (_, index) =>
  `member-${String(index + 1).padStart(2, "0")}@cockpit.local`)];
const config = {
  gateway: {
    mode: "local", bind: "loopback", port,
    controlUi: { enabled: false, sessionObserver: false, allowedOrigins: [`http://localhost:${port}`] },
    trustedProxies: ["127.0.0.1"],
    auth: {
      mode: "trusted-proxy",
      identityScopes: Object.fromEntries(nativeIdentities.map(identity =>
        [identity, ["operator.read", "operator.write"]])),
      trustedProxy: {
        userHeader: "x-forwarded-user", requiredHeaders: ["x-forwarded-for"],
        allowUsers: nativeIdentities,
        allowLoopback: true
      }
    },
    roles: {
      default: "participant",
      definitions: {
        participant: {
          sessions: { others: "suggest" }, agents: ["shared-marketing"],
          scopes: ["operator.read", "operator.write"]
        }
      }
    },
    reload: { mode: "off" }
  },
  agents: {
    ownership: "explicit",
    entries: {
      "shared-marketing": {
        identity: { name: "Marketing employee" },
        workspace: "/var/lib/plow/shared-room", tools: { deny: ["*"] },
        params: { maxTokens: 1800 }
      }
    },
    defaults: {
      workspace: "/var/lib/plow/shared-room", skipBootstrap: true,
      sandbox: { mode: "off" }, heartbeat: { every: "0m" },
      model: { primary: "openai/gpt-5.6-luna", fallbacks: [] }
    }
  },
  cron: { enabled: false },
  plugins: { entries: { "memory-core": { config: { dreaming: { enabled: false } } } } },
  tools: { deny: ["*"] },
  skills: { allowBundled: ["plow-no-bundled-skills"] }
};

await mkdir("/var/lib/plow/shared-room", { recursive: true });
await writeFile("/var/lib/plow/shared-room/AGENTS.md", [
  "You are the Marketing employee in one local, private company conversation.",
  "Discuss only the host-supplied marketing project and approved shared material.",
  "You have no tools or authority to change the project ledger, approve drafts,",
  "spend money, publish, contact anyone, or access owner-private files.",
  "A suggestion is input for owner review, not a grant or completed assignment."
].join(" ") + "\n");
await writeFile("/var/lib/plow/openclaw.json", JSON.stringify(config, null, 2) + "\n", { mode: 0o600 });
console.log(`shared-boot: isolated Gateway on container loopback ${port}; no published port`);

const child = spawn(process.execPath, ["/app/openclaw.mjs", "gateway", "run", "--port", String(port), "--bind", "loopback"], { stdio: "inherit" });
for (const signal of ["SIGTERM", "SIGINT"]) process.on(signal, () => child.kill(signal));
child.on("exit", code => process.exit(code ?? 0));
