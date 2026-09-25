#!/usr/bin/env python3
"""Bind one validated customer login as owner on this private Windows host.

Run after that person has completed the Auth0 callback. The existing local host
key remains the recovery administrator. Never print the subject or secret.
"""
from __future__ import annotations

import argparse
import json
import os
import sqlite3
import subprocess
import tempfile
from pathlib import Path


def private_replacement(target: Path, body: str) -> None:
    """Restrict a new file before placing any Auth0 credential into it."""
    fd, raw = tempfile.mkstemp(prefix=".customer-login-owner-", suffix=".tmp", dir=target.parent)
    os.close(fd)
    scratch = Path(raw)
    try:
        account = f"{os.environ['USERDOMAIN']}\\{os.environ['USERNAME']}:(F)"
        for arguments in (["/inheritance:r"], ["/grant:r", account]):
            result = subprocess.run(["icacls.exe", str(scratch), *arguments], capture_output=True, text=True)
            if result.returncode:
                raise RuntimeError("Could not restrict the replacement credential file")
        scratch.write_text(body, encoding="utf-8")
        os.replace(scratch, target)
    finally:
        scratch.unlink(missing_ok=True)


def bind(root: Path, email: str, prefix: str) -> dict[str, object]:
    root = root.resolve(strict=True)
    target = root / "customer-login.json"
    database = root / "ledger.sqlite"
    if not target.is_file() or not database.is_file() or target.is_symlink():
        raise ValueError("Expected the existing private customer login and host ledger")
    config = json.loads(target.read_text(encoding="utf-8-sig"))
    login = config.get("CustomerLogin")
    if not isinstance(login, dict) or login.get("Enabled") != "true" or not login.get("ClientSecret"):
        raise ValueError("Customer login is not fully configured")
    if login.get("Origin") != "https://hope.tail47397a.ts.net" or login.get("Authority") != "https://marketing-hire-dev.us.auth0.com/":
        raise ValueError("This helper only handles the configured private development identity tenant")
    old_body = target.read_text(encoding="utf-8-sig")
    with sqlite3.connect(database, timeout=10) as db:
        db.execute("BEGIN IMMEDIATE")
        row = db.execute("SELECT body FROM settings WHERE key=?", ("customer-accounts",)).fetchone()
        accounts = json.loads(row[0]) if row else []
        candidates = [item for item in accounts if
            item.get("id", "").startswith(prefix) and
            item.get("issuer") == login["Authority"] and
            item.get("subject", "").startswith("google-oauth2|") and
            (item.get("email") or "").casefold() == email.casefold() and
            item.get("emailVerified") is True and item.get("revoked") is False]
        if len(candidates) != 1 or any(item.get("owner") is True and item is not candidates[0] for item in accounts):
            raise ValueError("A unique validated Google account was not found, or another owner is already bound")
        account = candidates[0]
        subject = account["subject"]
        if login.get("OwnerSubject") not in ("", None, subject):
            raise ValueError("A different owner subject is already configured")
        if account["owner"] is True and login.get("OwnerSubject") == subject:
            return {"bound": True, "accountPrefix": account["id"][:8], "alreadyBound": True}
        account["owner"] = True
        login["OwnerSubject"] = subject
        next_body = json.dumps(config, ensure_ascii=False, indent=2) + "\n"
        try:
            private_replacement(target, next_body)
            db.execute("UPDATE settings SET body=? WHERE key=?", (json.dumps(accounts, separators=(",", ":")), "customer-accounts"))
            db.commit()
        except Exception:
            private_replacement(target, old_body)
            raise
        return {"bound": True, "accountPrefix": account["id"][:8], "alreadyBound": False}


def main() -> int:
    parser = argparse.ArgumentParser(description="Bind a validated Google customer account as the local workspace owner")
    parser.add_argument("--data", type=Path, required=True)
    parser.add_argument("--email", required=True)
    parser.add_argument("--account-prefix", required=True)
    args = parser.parse_args()
    if os.name != "nt" or len(args.account_prefix) != 8 or any(ch not in "0123456789abcdef" for ch in args.account_prefix):
        parser.error("Windows and an eight-character account ID prefix are required")
    try:
        print(json.dumps(bind(args.data, args.email, args.account_prefix)))
        return 0
    except (ValueError, KeyError, OSError, sqlite3.Error, RuntimeError) as error:
        print(json.dumps({"bound": False, "error": str(error)}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
