"""Inspect and seed a fictional OAuth profile in a disposable offline Gateway.

Only run this inside a container without the owner's state volume or network.
The token values are inert fixtures, never real credentials.
"""

import base64
import json
import os
import sqlite3
import sys
import time


STATE = "/var/lib/plow/state/openclaw.sqlite"
MAIN = "/var/lib/plow/agents/main/agent/openclaw-agent.sqlite"
WORKER = "/var/lib/plow/agents/runway-worker/agent/openclaw-agent.sqlite"


def inspect() -> None:
    for path in (STATE, MAIN, WORKER):
        if not os.path.exists(path):
            print(json.dumps({"path": path, "exists": False}))
            continue
        with sqlite3.connect(f"file:{path}?mode=ro", uri=True) as db:
            tables = {row[0] for row in db.execute("SELECT name FROM sqlite_master WHERE type='table'")}
            info = {"path": path, "exists": True,
                    "auth_profile_store": "auth_profile_store" in tables,
                    "config_machine_state": "config_machine_state" in tables}
            if "auth_profile_store" in tables:
                info["auth_profile_rows"] = db.execute("SELECT COUNT(*) FROM auth_profile_store").fetchone()[0]
            if "config_machine_state" in tables:
                info["auth_state_keys"] = [row[0] for row in db.execute(
                    "SELECT state_key FROM config_machine_state WHERE state_key LIKE 'auth%'")]
            print(json.dumps(info))


def seed() -> None:
    # A denied worker attempt creates its full agent DB schema. Seed only that
    # disposable worker DB; never touch the owner's shared or main auth store.
    if not os.path.exists(WORKER):
        raise RuntimeError("Run a denied offline worker attempt before seeding")
    def encode(part: dict) -> str:
        return base64.urlsafe_b64encode(json.dumps(part).encode()).decode().rstrip("=")

    fake_jwt = ".".join((encode({"alg": "none"}),
                         encode({"https://api.openai.com/auth": {
                             "chatgpt_account_id": "offline-fixture-account"}}),
                         "offline"))
    profile_id = "openai:offline-jwt"
    credential = {"type": "oauth", "provider": "openai",
                  "access": fake_jwt,
                  "refresh": "dummy-offline-oauth-refresh",
                  "expires": int(time.time() * 1000) + 3_600_000,
                  "accountId": "offline-fixture-account"}
    store = {"version": 1, "profiles": {profile_id: credential},
             "order": {"openai": [profile_id]},
             "lastGood": {"openai": profile_id}}
    with sqlite3.connect(WORKER) as db:
        db.execute("INSERT OR REPLACE INTO auth_profile_store "
                   "(store_key, store_json, updated_at) VALUES (?, ?, ?)",
                   ("primary", json.dumps(store), int(time.time() * 1000)))
    print("fictional offline OAuth profile seeded")


if __name__ == "__main__":
    if sys.argv[1:] == ["inspect"]:
        inspect()
    elif sys.argv[1:] == ["seed"]:
        seed()
    else:
        raise SystemExit("usage: offline-oauth.py inspect|seed")
