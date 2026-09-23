#!/usr/bin/env python3
"""The Marketing hire's ledger: watch list, drafts awaiting approval, activity.

Every consequential step leaves a receipt in the activity feed, which the
Thaddeus cockpit reads. Drafts are proposals; only a recorded human decision
moves one to approved, and only a recorded post moves it to posted.
"""
from __future__ import annotations

import argparse
import json
import os
import sqlite3
import sys
import time
from contextlib import closing
from pathlib import Path

SCHEMA = """
CREATE TABLE IF NOT EXISTS events(
  id INTEGER PRIMARY KEY, ts REAL NOT NULL, kind TEXT NOT NULL,
  title TEXT NOT NULL, data TEXT NOT NULL DEFAULT '{}');
CREATE TABLE IF NOT EXISTS watch(
  query TEXT PRIMARY KEY, sources TEXT, reason TEXT, added REAL NOT NULL);
CREATE TABLE IF NOT EXISTS drafts(
  id INTEGER PRIMARY KEY, created REAL NOT NULL, channel TEXT NOT NULL,
  destination TEXT NOT NULL, content TEXT NOT NULL, rationale TEXT NOT NULL,
  rules_url TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'pending',
  revision INTEGER NOT NULL DEFAULT 1, decided_by TEXT, decided_at REAL,
  note TEXT, posted_url TEXT);
"""
STATUSES = {"pending", "approved", "rejected", "posted", "withdrawn"}
LIMITS = {"channel": 60, "destination": 500, "content": 4000, "rationale": 1000,
          "rules_url": 500, "by": 80, "note": 500, "title": 200, "reason": 300}


def db() -> sqlite3.Connection:
    root = Path(os.environ.get("HIRE_STATE", "/var/lib/plow/hire"))
    root.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(root / "hire.sqlite", timeout=10, isolation_level=None)
    conn.row_factory = sqlite3.Row
    conn.executescript(SCHEMA)
    return conn


def bounded(field: str, value: str | None, required: bool = True) -> str | None:
    if value is None or not value.strip():
        if required:
            raise SystemExit(f"--{field.replace('_', '-')} is required")
        return None
    value = value.strip()
    if len(value) > LIMITS[field]:
        raise SystemExit(f"--{field.replace('_', '-')} exceeds {LIMITS[field]} characters")
    return value


def record_event(kind: str, title: str, data: dict | None = None, conn: sqlite3.Connection | None = None) -> int:
    own = conn is None
    conn = conn or db()
    try:
        cur = conn.execute("INSERT INTO events(ts,kind,title,data) VALUES(?,?,?,?)",
                           (time.time(), kind, title[:LIMITS["title"]], json.dumps(data or {}, ensure_ascii=False)))
        return cur.lastrowid
    finally:
        if own:
            conn.close()


def row(r: sqlite3.Row) -> dict:
    d = dict(r)
    if "data" in d:
        d["data"] = json.loads(d["data"])
    return d


def draft_add(a) -> dict:
    fields = {f: bounded(f, getattr(a, f)) for f in ("channel", "destination", "content", "rationale", "rules_url")}
    with closing(db()) as conn:
        conn.execute("BEGIN IMMEDIATE")
        if a.revise:
            old = conn.execute("SELECT * FROM drafts WHERE id=?", (a.revise,)).fetchone()
            if not old or old["status"] != "pending":
                raise SystemExit("only a pending draft can be revised")
            conn.execute("UPDATE drafts SET status='withdrawn', note='superseded by revision' WHERE id=?", (a.revise,))
            revision = old["revision"] + 1
        else:
            revision = 1
        cur = conn.execute("INSERT INTO drafts(created,channel,destination,content,rationale,rules_url,revision) "
                           "VALUES(?,?,?,?,?,?,?)", (time.time(), *fields.values(), revision))
        draft_id = cur.lastrowid
        record_event("draft", f"Draft #{draft_id} for {fields['channel']} awaits approval",
                     {"draft": draft_id, "revision": revision, "replaces": a.revise, **fields}, conn)
        conn.execute("COMMIT")
    return {"draft": draft_id, "status": "pending", "revision": revision}


def draft_decide(a) -> dict:
    by = bounded("by", a.by)
    note = bounded("note", a.note, required=False)
    with closing(db()) as conn:
        conn.execute("BEGIN IMMEDIATE")
        d = conn.execute("SELECT * FROM drafts WHERE id=?", (a.id,)).fetchone()
        if not d:
            raise SystemExit(f"no draft #{a.id}")
        if d["status"] != "pending":
            raise SystemExit(f"draft #{a.id} is already {d['status']}")
        conn.execute("UPDATE drafts SET status=?, decided_by=?, decided_at=?, note=? WHERE id=?",
                     (a.decision, by, time.time(), note, a.id))
        record_event("decision", f"{by} {a.decision} draft #{a.id}",
                     {"draft": a.id, "decision": a.decision, "by": by, "note": note}, conn)
        conn.execute("COMMIT")
    return {"draft": a.id, "status": a.decision, "by": by}


def draft_posted(a) -> dict:
    url = bounded("destination", a.url)
    with closing(db()) as conn:
        conn.execute("BEGIN IMMEDIATE")
        d = conn.execute("SELECT * FROM drafts WHERE id=?", (a.id,)).fetchone()
        if not d or d["status"] != "approved":
            raise SystemExit("only an approved draft can be marked posted")
        conn.execute("UPDATE drafts SET status='posted', posted_url=? WHERE id=?", (url, a.id))
        record_event("posted", f"Draft #{a.id} posted to {d['channel']}", {"draft": a.id, "url": url}, conn)
        conn.execute("COMMIT")
    return {"draft": a.id, "status": "posted", "url": url}


def draft_list(a) -> list[dict]:
    with closing(db()) as conn:
        if a.status:
            rows = conn.execute("SELECT * FROM drafts WHERE status=? ORDER BY id DESC LIMIT ?", (a.status, a.limit))
        else:
            rows = conn.execute("SELECT * FROM drafts ORDER BY id DESC LIMIT ?", (a.limit,))
        return [row(r) for r in rows]


def watch(a):
    with closing(db()) as conn:
        if a.action == "add":
            q = bounded("title", a.query)
            conn.execute("INSERT OR REPLACE INTO watch(query,sources,reason,added) VALUES(?,?,?,?)",
                         (q, a.sources, bounded("reason", a.reason, required=False), time.time()))
            record_event("watch", f"Now watching \"{q}\"", {"query": q, "sources": a.sources}, conn)
        elif a.action == "remove":
            conn.execute("DELETE FROM watch WHERE query=?", (a.query,))
            record_event("watch", f"Stopped watching \"{a.query}\"", {"query": a.query}, conn)
        return [dict(r) for r in conn.execute("SELECT * FROM watch ORDER BY added")]


FEED_VERSION = 1  # bump only with a documented migration; see docs/FEED.md


def feed(a) -> dict:
    with closing(db()) as conn:
        events = [row(r) for r in conn.execute("SELECT * FROM events WHERE id>? ORDER BY id LIMIT ?", (a.since, a.limit))]
    return {"feed_version": FEED_VERSION, "events": events,
            "next_since": events[-1]["id"] if events else a.since}


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(prog="hire", description=__doc__)
    sub = p.add_subparsers(dest="cmd", required=True)

    d = sub.add_parser("draft").add_subparsers(dest="action", required=True)
    add = d.add_parser("add")
    for f in ("channel", "destination", "content", "rationale"):
        add.add_argument(f"--{f}")
    add.add_argument("--rules-url", dest="rules_url", help="community rules URL, or UNVERIFIED")
    add.add_argument("--revise", type=int, help="pending draft id this replaces")
    dec = d.add_parser("decide")
    dec.add_argument("--id", type=int, required=True)
    dec.add_argument("--decision", choices=["approved", "rejected"], required=True)
    dec.add_argument("--by", required=True, help="name of the person who decided")
    dec.add_argument("--note")
    posted = d.add_parser("posted")
    posted.add_argument("--id", type=int, required=True)
    posted.add_argument("--url", required=True)
    ls = d.add_parser("list")
    ls.add_argument("--status", choices=sorted(STATUSES))
    ls.add_argument("--limit", type=int, default=20)

    w = sub.add_parser("watch")
    w.add_argument("action", choices=["add", "remove", "list"])
    w.add_argument("--query")
    w.add_argument("--sources")
    w.add_argument("--reason")

    e = sub.add_parser("event")
    e.add_argument("--kind", required=True, choices=["note", "campaign", "checkpoint", "report"])
    e.add_argument("--title", required=True)
    e.add_argument("--data", default="{}", help="JSON object")

    f = sub.add_parser("feed")
    f.add_argument("--since", type=int, default=0)
    f.add_argument("--limit", type=int, default=200)

    a = p.parse_args(argv)
    if a.cmd == "draft":
        out = {"add": draft_add, "decide": draft_decide, "posted": draft_posted, "list": draft_list}[a.action](a)
    elif a.cmd == "watch":
        if a.action != "list" and not a.query:
            raise SystemExit("--query is required")
        out = watch(a)
    elif a.cmd == "event":
        data = json.loads(a.data)
        if not isinstance(data, dict):
            raise SystemExit("--data must be a JSON object")
        out = {"event": record_event(a.kind, bounded("title", a.title), data)}
    else:
        out = feed(a)
    json.dump(out, sys.stdout, ensure_ascii=False, indent=1)
    sys.stdout.write("\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
