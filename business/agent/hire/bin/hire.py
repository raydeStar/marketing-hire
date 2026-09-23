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
import uuid
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
CREATE TABLE IF NOT EXISTS tasks(
  id TEXT PRIMARY KEY, title TEXT NOT NULL, status TEXT NOT NULL,
  priority TEXT NOT NULL, next_action TEXT NOT NULL, action_state TEXT NOT NULL,
  blocker TEXT, conversation_key TEXT NOT NULL, version INTEGER NOT NULL,
  updated_at INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS task_requests(
  request_id TEXT PRIMARY KEY, operation TEXT NOT NULL, target_id TEXT,
  result TEXT NOT NULL, created_at INTEGER NOT NULL);
"""
STATUSES = {"pending", "approved", "rejected", "posted", "withdrawn"}
LIMITS = {"channel": 60, "destination": 500, "content": 4000, "rationale": 1000,
          "rules_url": 500, "by": 80, "note": 500, "title": 200, "reason": 300,
          "next_action": 1000, "blocker": 1000, "request_id": 120}
TASK_STATUSES = {"ready", "working", "needs_you", "done"}
TASK_PRIORITIES = {"high", "normal", "low"}
ACTION_STATES = {"agent_ready", "user_waiting", "blocked", "none"}


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


def task_payload(a) -> dict:
    """Accept a JSON object on stdin for the host or explicit flags for the agent."""
    if a.input_json:
        raw = sys.stdin.read() if a.input_json == "-" else a.input_json
        try:
            payload = json.loads(raw)
        except json.JSONDecodeError as exc:
            raise SystemExit(f"invalid task JSON: {exc}") from exc
        if not isinstance(payload, dict):
            raise SystemExit("task input must be a JSON object")
        permitted = {"request_id", "title", "status", "priority", "next_action", "action_state", "blocker", "version"}
        if set(payload) - permitted:
            raise SystemExit("unknown task fields: " + ", ".join(sorted(set(payload) - permitted)))
        return payload
    return {name: getattr(a, name) for name in
            ("request_id", "title", "status", "priority", "next_action", "action_state", "blocker", "version")
            if getattr(a, name, None) is not None}


def task_result(r: sqlite3.Row) -> dict:
    return dict(r)


def task_validate(data: dict, create: bool) -> dict:
    if not isinstance(data.get("request_id"), str):
        raise SystemExit("request_id must be a string")
    request_id = bounded("request_id", data["request_id"])
    result = {"request_id": request_id}
    for name, choices in (("status", TASK_STATUSES), ("priority", TASK_PRIORITIES),
                          ("action_state", ACTION_STATES)):
        value = data.get(name)
        if value is not None:
            if value not in choices:
                raise SystemExit(f"invalid {name}: {value}")
            result[name] = value
    for name in ("title", "next_action", "blocker"):
        if name in data:
            value = data[name]
            if value is not None and not isinstance(value, str):
                raise SystemExit(f"{name} must be a string")
            result[name] = bounded(name, value, required=name == "title") if value is not None else None
            if name == "next_action" and result[name] is None:
                result[name] = ""
    if create:
        result.setdefault("status", "ready")
        result.setdefault("priority", "normal")
        result.setdefault("action_state", "none")
        result.setdefault("next_action", "")
        if not result.get("title"):
            raise SystemExit("--title is required")
        if "version" in data:
            raise SystemExit("version is only valid for update")
    else:
        version = data.get("version")
        if type(version) is not int or version < 1:
            raise SystemExit("positive integer version is required")
        result["version"] = version
        if not any(name in result for name in ("title", "status", "priority", "next_action", "action_state", "blocker")):
            raise SystemExit("task update has no changed fields")
    if result.get("action_state") == "blocked" and not result.get("blocker") and create:
        raise SystemExit("blocked tasks require a blocker")
    return result


def task_write(a) -> dict:
    data = task_validate(task_payload(a), a.action == "create")
    request_id = data.pop("request_id")
    with closing(db()) as conn:
        conn.execute("BEGIN IMMEDIATE")
        previous = conn.execute("SELECT * FROM task_requests WHERE request_id=?", (request_id,)).fetchone()
        if previous:
            if previous["operation"] != a.action or previous["target_id"] != getattr(a, "id", None):
                raise SystemExit("request_id already used for another task operation")
            return json.loads(previous["result"])
        if a.action == "create":
            task_id = uuid.uuid4().hex
            values = {"id": task_id, "title": data["title"], "status": data["status"],
                      "priority": data["priority"], "next_action": data["next_action"],
                      "action_state": data["action_state"], "blocker": data.get("blocker"),
                      "conversation_key": f"agent:main:marketing-task-{task_id}",
                      "version": 1, "updated_at": int(time.time())}
            conn.execute("INSERT INTO tasks(id,title,status,priority,next_action,action_state,blocker,conversation_key,version,updated_at) "
                         "VALUES(:id,:title,:status,:priority,:next_action,:action_state,:blocker,:conversation_key,:version,:updated_at)", values)
        else:
            task_id = a.id
            old = conn.execute("SELECT * FROM tasks WHERE id=?", (task_id,)).fetchone()
            if not old:
                raise SystemExit("task not found")
            if old["version"] != data["version"]:
                raise SystemExit("stale task version")
            values = task_result(old)
            values.update({k: v for k, v in data.items() if k != "version"})
            if "action_state" in data and data["action_state"] != "blocked" and "blocker" not in data:
                values["blocker"] = None
            if values["action_state"] == "blocked" and not values["blocker"]:
                raise SystemExit("blocked tasks require a blocker")
            values["version"] += 1
            values["updated_at"] = int(time.time())
            conn.execute("UPDATE tasks SET title=:title,status=:status,priority=:priority,next_action=:next_action,"
                         "action_state=:action_state,blocker=:blocker,version=:version,updated_at=:updated_at WHERE id=:id", values)
        result = task_result(conn.execute("SELECT * FROM tasks WHERE id=?", (task_id,)).fetchone())
        record_event("task", f"Task {result['title']} {'created' if a.action == 'create' else 'updated'}",
                     {"task_id": task_id, "operation": a.action, "version": result["version"],
                      "status": result["status"], "priority": result["priority"]}, conn)
        conn.execute("INSERT INTO task_requests(request_id,operation,target_id,result,created_at) VALUES(?,?,?,?,?)",
                     (request_id, a.action, getattr(a, "id", None), json.dumps(result, ensure_ascii=False), int(time.time())))
        conn.execute("COMMIT")
        return result


def task_read(a):
    with closing(db()) as conn:
        if a.action == "get":
            result = conn.execute("SELECT * FROM tasks WHERE id=?", (a.id,)).fetchone()
            if not result:
                raise SystemExit("task not found")
            return task_result(result)
        return [task_result(r) for r in conn.execute("SELECT * FROM tasks ORDER BY updated_at DESC, id DESC LIMIT ?", (a.limit,))]


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

    t = sub.add_parser("task").add_subparsers(dest="action", required=True)
    for operation in ("create", "update"):
        command = t.add_parser(operation)
        if operation == "update":
            command.add_argument("--id", required=True)
            command.add_argument("--version", type=int)
        command.add_argument("--request-id", dest="request_id")
        command.add_argument("--title")
        command.add_argument("--status", choices=sorted(TASK_STATUSES))
        command.add_argument("--priority", choices=sorted(TASK_PRIORITIES))
        command.add_argument("--next-action", dest="next_action")
        command.add_argument("--action-state", dest="action_state", choices=sorted(ACTION_STATES))
        command.add_argument("--blocker")
        command.add_argument("--input-json", help="JSON object, or - to read stdin")
    task_get = t.add_parser("get")
    task_get.add_argument("--id", required=True)
    task_list = t.add_parser("list")
    task_list.add_argument("--limit", type=int, default=200)

    a = p.parse_args(argv)
    if a.cmd == "task":
        if a.action == "list" and (a.limit < 1 or a.limit > 1000):
            raise SystemExit("task list limit must be 1..1000")
        out = task_write(a) if a.action in ("create", "update") else task_read(a)
    elif a.cmd == "draft":
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
