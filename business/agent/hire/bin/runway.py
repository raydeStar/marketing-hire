#!/usr/bin/env python3
"""Durable, bounded project claims in the existing hire SQLite ledger.

The host owns inference and validates deliverables. This module only admits
authorized steps, fences stale results, and records exact receipts.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import sqlite3
import sys
import time
import uuid
from contextlib import contextmanager

from hire import db, record_event

KINDS = ("audience_note", "post_angles", "review_packet")
TITLES = ("Research one candidate audience and problem", "Draft three evidence-linked post angles", "Prepare owner review packet")
CRITERIA = (
    "One provisional audience, one problem, two exact source quotes, and explicit evidence limits.",
    "Exactly three distinct draft post angles; each cites a checked source and states a claim limit.",
    "Unsupported claims listed, results summarized, one specific owner decision requested, and one bounded next-step proposal with a continue-or-stop reason.",
)
REVISION_CRITERION = "Exactly three revised, distinct evidence-linked post angles, with claim limits; materially change the rejected draft."
SCHEMA = """
CREATE TABLE IF NOT EXISTS runways(
 id TEXT PRIMARY KEY, request_id TEXT UNIQUE NOT NULL, goal TEXT NOT NULL,
 criteria TEXT NOT NULL, scope TEXT NOT NULL, scope_version INTEGER NOT NULL DEFAULT 1,
 profile_version INTEGER NOT NULL, deadline_at REAL, pilot_root_id TEXT,
 owner_actor TEXT NOT NULL, owner_input TEXT NOT NULL DEFAULT '',
 max_runs INTEGER NOT NULL, max_active_seconds INTEGER NOT NULL,
 token_limit INTEGER NOT NULL, reserve_per_run INTEGER NOT NULL,
 token_reserved INTEGER NOT NULL DEFAULT 0, token_used INTEGER NOT NULL DEFAULT 0,
 run_count INTEGER NOT NULL DEFAULT 0, status TEXT NOT NULL, version INTEGER NOT NULL,
 active_execution TEXT, next_due REAL, wait_reason TEXT,
 created_at REAL NOT NULL, updated_at REAL NOT NULL);
CREATE TABLE IF NOT EXISTS runway_steps(
 id TEXT PRIMARY KEY, runway_id TEXT NOT NULL, ordinal INTEGER NOT NULL,
 kind TEXT NOT NULL, task_id TEXT UNIQUE NOT NULL, task_version INTEGER NOT NULL,
 status TEXT NOT NULL, attempts INTEGER NOT NULL DEFAULT 0,
 artifact_id TEXT, UNIQUE(runway_id,ordinal));
CREATE TABLE IF NOT EXISTS runway_sources(
 runway_id TEXT NOT NULL, url TEXT NOT NULL, content TEXT NOT NULL,
 digest TEXT NOT NULL, PRIMARY KEY(runway_id,url));
CREATE TABLE IF NOT EXISTS runway_executions(
 id TEXT PRIMARY KEY, runway_id TEXT NOT NULL, step_id TEXT NOT NULL,
 status TEXT NOT NULL, reserved_tokens INTEGER NOT NULL, reported_tokens INTEGER,
 usage_json TEXT, error TEXT, started_at REAL NOT NULL, ended_at REAL,
 artifact_id TEXT);
CREATE TABLE IF NOT EXISTS runway_artifacts(
 id TEXT PRIMARY KEY, runway_id TEXT NOT NULL, step_id TEXT UNIQUE NOT NULL,
 kind TEXT NOT NULL, content TEXT NOT NULL, digest TEXT NOT NULL,
 source_urls TEXT NOT NULL, created_at REAL NOT NULL);
CREATE TABLE IF NOT EXISTS runway_inputs(
 id TEXT PRIMARY KEY, request_id TEXT UNIQUE NOT NULL, runway_id TEXT NOT NULL,
 actor_id TEXT NOT NULL, actor_name TEXT NOT NULL, content TEXT NOT NULL,
 created_at REAL NOT NULL);
CREATE TABLE IF NOT EXISTS runway_reviews(
 id TEXT PRIMARY KEY, request_id TEXT UNIQUE NOT NULL, runway_id TEXT NOT NULL,
 artifact_id TEXT NOT NULL, artifact_digest TEXT NOT NULL, decision TEXT NOT NULL,
 instruction TEXT NOT NULL, actor_id TEXT NOT NULL, actor_name TEXT NOT NULL,
 step_id TEXT, created_at REAL NOT NULL);
CREATE TABLE IF NOT EXISTS runway_revision_grants(
 id TEXT PRIMARY KEY, request_id TEXT UNIQUE NOT NULL, payload_digest TEXT NOT NULL,
 pilot_root_id TEXT NOT NULL, source_runway_id TEXT NOT NULL,
 source_runway_version INTEGER NOT NULL, source_review_id TEXT UNIQUE NOT NULL,
 source_artifact_id TEXT NOT NULL, source_artifact_digest TEXT NOT NULL,
 profile_version INTEGER NOT NULL, owner_actor TEXT NOT NULL, scope TEXT NOT NULL,
 max_runs INTEGER NOT NULL, max_model_requests INTEGER NOT NULL,
 token_limit INTEGER NOT NULL, max_active_seconds INTEGER NOT NULL,
 deadline_at REAL NOT NULL, status TEXT NOT NULL, created_at REAL NOT NULL);
CREATE TABLE IF NOT EXISTS runway_chat_claims(
 request_id TEXT PRIMARY KEY, actor_id TEXT NOT NULL, session_key TEXT NOT NULL,
 content_digest TEXT NOT NULL, status TEXT NOT NULL,
 created_at REAL NOT NULL, ended_at REAL);
CREATE TABLE IF NOT EXISTS runway_model_requests(
 request_id TEXT PRIMARY KEY, execution_id TEXT NOT NULL, pilot_root_id TEXT NOT NULL,
 request_digest TEXT NOT NULL, reserved_tokens INTEGER NOT NULL, reported_tokens INTEGER,
 status TEXT NOT NULL, created_at REAL NOT NULL, ended_at REAL);
"""


@contextmanager
def connection():
    conn = db()
    conn.executescript(SCHEMA)
    existing = {row[1] for row in conn.execute("PRAGMA table_info(runways)")}
    if "scope_version" not in existing:
        conn.execute("ALTER TABLE runways ADD COLUMN scope_version INTEGER NOT NULL DEFAULT 1")
    if "deadline_at" not in existing:
        conn.execute("ALTER TABLE runways ADD COLUMN deadline_at REAL")
    if "pilot_root_id" not in existing:
        conn.execute("ALTER TABLE runways ADD COLUMN pilot_root_id TEXT")
        conn.execute("UPDATE runways SET pilot_root_id=id WHERE pilot_root_id IS NULL")
    try:
        with conn:
            yield conn
    finally:
        conn.close()


def as_dict(row):
    return dict(row) if row is not None else None


def read_input():
    value = json.load(sys.stdin)
    if not isinstance(value, dict):
        raise ValueError("Expected one JSON object")
    return value


def require(value, limit):
    if not isinstance(value, str) or not value.strip() or len(value) > limit:
        raise ValueError(f"Expected nonempty text up to {limit} characters")
    return value.strip()


def snapshot(conn, runway_id=None):
    project = conn.execute("SELECT * FROM runways WHERE id=?" if runway_id else
                           "SELECT * FROM runways ORDER BY created_at DESC LIMIT 1",
                           (runway_id,) if runway_id else ()).fetchone()
    if project is None:
        return None
    rid = project["id"]
    return {"project": as_dict(project),
            "steps": [as_dict(r) for r in conn.execute("SELECT * FROM runway_steps WHERE runway_id=? ORDER BY ordinal", (rid,))],
            "artifacts": [as_dict(r) for r in conn.execute("SELECT * FROM runway_artifacts WHERE runway_id=? ORDER BY created_at", (rid,))],
            "inputs": [as_dict(r) for r in conn.execute("SELECT * FROM runway_inputs WHERE runway_id=? ORDER BY created_at", (rid,))],
            "reviews": [as_dict(r) for r in conn.execute("SELECT * FROM runway_reviews WHERE runway_id=? ORDER BY created_at", (rid,))],
            "revision_grants": [as_dict(r) for r in conn.execute("SELECT * FROM runway_revision_grants WHERE source_runway_id=? ORDER BY created_at", (rid,))],
            "executions": [as_dict(r) for r in conn.execute("SELECT * FROM runway_executions WHERE runway_id=? ORDER BY started_at", (rid,))],
            "model_requests": [as_dict(r) for r in conn.execute("SELECT m.* FROM runway_model_requests m JOIN runway_executions e ON e.id=m.execution_id WHERE e.runway_id=? ORDER BY m.created_at,m.request_id", (rid,))]}


def list_projects():
    with connection() as conn:
        rows = conn.execute("""SELECT r.id,r.goal,r.status,r.created_at,r.updated_at,
                              (SELECT COUNT(*) FROM runway_artifacts a WHERE a.runway_id=r.id) AS artifact_count
                              FROM runways r ORDER BY r.created_at DESC,r.id DESC""").fetchall()
        return {"projects": [as_dict(row) for row in rows]}


def inspect(data):
    rid = require(data.get("id"), 32)
    if not re.fullmatch(r"[a-f0-9]{32}", rid):
        raise ValueError("Invalid project ID")
    with connection() as conn:
        result = snapshot(conn, rid)
        if result is None:
            raise ValueError("Project not found")
        return result


def create(data):
    request_id = require(data.get("request_id"), 120)
    goal = require(data.get("goal"), 1200)
    owner = require(data.get("owner_actor"), 100)
    profile_version = data.get("profile_version")
    sources = data.get("sources")
    if not isinstance(profile_version, int) or profile_version < 1 or not isinstance(sources, list) or len(sources) != 2:
        raise ValueError("Profile version and exactly two checked sources are required")
    if len({s.get("url") for s in sources if isinstance(s, dict)}) != 2:
        raise ValueError("Sources must have distinct URLs")
    for source in sources:
        if not isinstance(source, dict) or not isinstance(source.get("url"), str) or not re.fullmatch(r"https://news\.ycombinator\.com/item\?id=[0-9]{1,12}", source["url"]):
            raise ValueError("Only the restricted checked HN sources are allowed")
        require(source.get("content"), 16000)
    now = time.time()
    rid = uuid.uuid4().hex
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        prior = conn.execute("SELECT id,goal,owner_actor FROM runways WHERE request_id=?", (request_id,)).fetchone()
        if prior:
            if prior["goal"] != goal or prior["owner_actor"] != owner:
                raise ValueError("Request ID belongs to a different project")
            return snapshot(conn, prior["id"])
        active = conn.execute("SELECT 1 FROM runways WHERE status IN ('running','ready','waiting','paused','unknown') LIMIT 1").fetchone()
        if active:
            raise ValueError("One active standing assignment is already enabled")
        conn.execute("INSERT INTO runways(id,request_id,goal,criteria,scope,scope_version,profile_version,deadline_at,owner_actor,max_runs,max_active_seconds,token_limit,reserve_per_run,status,version,created_at,updated_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                     (rid, request_id, goal, json.dumps(CRITERIA), "internal_research_draft", 1, profile_version,
                      now + 1800, owner, 6, 900, 150000, 25000, "ready", 1, now, now))
        conn.execute("UPDATE runways SET pilot_root_id=id WHERE id=?", (rid,))
        for source in sources:
            content = source["content"].strip()
            conn.execute("INSERT INTO runway_sources VALUES(?,?,?,?)", (rid, source["url"], content,
                         hashlib.sha256(content.encode()).hexdigest()))
        for ordinal, (kind, title) in enumerate(zip(KINDS, TITLES)):
            task_id, step_id = uuid.uuid4().hex, uuid.uuid4().hex
            conn.execute("INSERT INTO tasks VALUES(?,?,?,?,?,?,?,?,?,?)",
                         (task_id, title, "ready", "high" if ordinal == 0 else "normal", CRITERIA[ordinal],
                          "agent_ready", None, "agent:main:marketing-task-" + task_id, 1, int(now)))
            conn.execute("INSERT INTO runway_steps VALUES(?,?,?,?,?,?,?,?,?)",
                         (step_id, rid, ordinal, kind, task_id, 1, "ready", 0, None))
            record_event("task", "Task " + title + " created", {"task_id": task_id, "runway_id": rid}, conn)
        record_event("checkpoint", "Standing marketing assignment enabled", {"runway_id": rid, "scope": "internal_research_draft"}, conn)
        return snapshot(conn, rid)


def claim():
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        if conn.execute("SELECT 1 FROM runway_chat_claims WHERE status IN ('pending','unknown') LIMIT 1").fetchone():
            return None
        project = conn.execute("SELECT * FROM runways WHERE status IN ('ready','waiting') ORDER BY created_at LIMIT 1").fetchone()
        if project is None or project["active_execution"] or project["next_due"] and project["next_due"] > now:
            return None
        rid = project["id"]
        if project["deadline_at"] is None:
            conn.execute("UPDATE runways SET status='needs_review',wait_reason='Legacy assignment has no bounded deadline; new owner grant required',version=version+1,updated_at=? WHERE id=?", (now, rid))
            return None
        if now >= project["deadline_at"]:
            conn.execute("UPDATE runways SET status='needs_review',wait_reason='Assignment deadline reached; owner review needed',version=version+1,updated_at=? WHERE id=?", (now, rid))
            return None
        if project["run_count"] >= project["max_runs"] or project["token_used"] + project["token_reserved"] + project["reserve_per_run"] > project["token_limit"]:
            conn.execute("UPDATE runways SET status='budget_exhausted',wait_reason='Run or token allowance exhausted',version=version+1,updated_at=? WHERE id=?", (now, rid))
            return None
        active_seconds = conn.execute("SELECT COALESCE(SUM(ended_at-started_at),0) FROM runway_executions WHERE runway_id=? AND ended_at IS NOT NULL", (rid,)).fetchone()[0]
        if active_seconds >= project["max_active_seconds"]:
            conn.execute("UPDATE runways SET status='budget_exhausted',wait_reason='Active time allowance exhausted',version=version+1,updated_at=? WHERE id=?", (now, rid))
            return None
        step = conn.execute("SELECT * FROM runway_steps WHERE runway_id=? AND status='ready' ORDER BY ordinal LIMIT 1", (rid,)).fetchone()
        if step is None:
            conn.execute("UPDATE runways SET status='needs_review',wait_reason='All deliverables saved; owner review needed',version=version+1,updated_at=? WHERE id=?", (now, rid))
            return None
        if step["ordinal"] and conn.execute("SELECT status FROM runway_steps WHERE runway_id=? AND ordinal=?", (rid, step["ordinal"] - 1)).fetchone()[0] != "done":
            return None
        task = conn.execute("SELECT version,status FROM tasks WHERE id=?", (step["task_id"],)).fetchone()
        profile = conn.execute("SELECT version FROM marketing_profile WHERE id='marketing'").fetchone()
        if task is None or task["version"] != step["task_version"] or task["status"] != "ready" or profile["version"] != project["profile_version"]:
            conn.execute("UPDATE runways SET status='needs_review',wait_reason='Task or owner brief changed; review before continuing',version=version+1,updated_at=? WHERE id=?", (now, rid))
            return None
        eid = uuid.uuid4().hex
        conn.execute("UPDATE tasks SET status='working',version=version+1,updated_at=? WHERE id=? AND version=?", (int(now), step["task_id"], task["version"]))
        conn.execute("UPDATE runway_steps SET status='running',task_version=task_version+1,attempts=attempts+1 WHERE id=?", (step["id"],))
        conn.execute("INSERT INTO runway_executions(id,runway_id,step_id,status,reserved_tokens,started_at) VALUES(?,?,?,'running',?,?)",
                     (eid, rid, step["id"], project["reserve_per_run"], now))
        conn.execute("UPDATE runways SET status='running',active_execution=?,token_reserved=token_reserved+?,run_count=run_count+1,wait_reason=NULL,version=version+1,updated_at=? WHERE id=?",
                     (eid, project["reserve_per_run"], now, rid))
        record_event("checkpoint", "Marketing step claimed", {"runway_id": rid, "execution_id": eid, "kind": step["kind"]}, conn)
        previous_error = conn.execute("SELECT error FROM runway_executions WHERE step_id=? AND status='failed' ORDER BY started_at DESC LIMIT 1", (step["id"],)).fetchone()
        review = conn.execute("SELECT r.*,a.content AS target_content FROM runway_reviews r JOIN runway_artifacts a ON a.id=r.artifact_id WHERE r.step_id=?", (step["id"],)).fetchone()
        return {"execution_id": eid, "project": as_dict(project), "step": as_dict(step),
                "sources": [as_dict(r) for r in conn.execute("SELECT * FROM runway_sources WHERE runway_id=?", (rid,))],
                "artifacts": [as_dict(r) for r in conn.execute("SELECT * FROM runway_artifacts WHERE runway_id=? ORDER BY created_at", (rid,))],
                "inputs": [as_dict(r) for r in conn.execute("SELECT actor_name,content,created_at FROM runway_inputs WHERE runway_id=? ORDER BY created_at DESC LIMIT 8", (rid,))][::-1],
                "review": as_dict(review),
                "last_error": previous_error[0] if previous_error else None}


def claim_chat(data):
    """Use the runway ledger as the cross-process turn claim before direct Chat inference."""
    request_id = require(data.get("request_id"), 120)
    actor_id = require(data.get("actor_id"), 100)
    session_key = require(data.get("session_key"), 200)
    digest = require(data.get("content_digest"), 64)
    if not re.fullmatch(r"[a-f0-9]{64}", digest):
        raise ValueError("Invalid chat content digest")
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        prior = conn.execute("SELECT * FROM runway_chat_claims WHERE request_id=?", (request_id,)).fetchone()
        if prior:
            if (prior["actor_id"], prior["session_key"], prior["content_digest"]) != (actor_id, session_key, digest):
                raise ValueError("Chat request ID belongs to different content or authority")
            return {"request_id": request_id, "status": prior["status"], "admitted": False}
        if conn.execute("SELECT 1 FROM runway_chat_claims WHERE status IN ('pending','unknown') LIMIT 1").fetchone():
            raise ValueError("Another direct chat turn is active or unresolved")
        if conn.execute("SELECT 1 FROM runways WHERE active_execution IS NOT NULL OR status='unknown' LIMIT 1").fetchone():
            raise ValueError("A runway execution is active or unresolved")
        conn.execute("INSERT INTO runway_chat_claims VALUES(?,?,?,?,?,?,NULL)",
                     (request_id, actor_id, session_key, digest, "pending", time.time()))
        return {"request_id": request_id, "status": "pending", "admitted": True}


def finish_chat(data):
    request_id = require(data.get("request_id"), 120)
    status = data.get("status")
    if status not in ("succeeded", "failed", "unknown"):
        raise ValueError("Invalid direct chat outcome")
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        row = conn.execute("SELECT status FROM runway_chat_claims WHERE request_id=?", (request_id,)).fetchone()
        if row is None:
            raise ValueError("Direct chat claim is missing")
        if row["status"] == status:
            return {"request_id": request_id, "status": status}
        if row["status"] != "pending":
            raise ValueError("Unresolved direct chat outcome needs reconciliation")
        conn.execute("UPDATE runway_chat_claims SET status=?,ended_at=? WHERE request_id=?",
                     (status, time.time(), request_id))
        return {"request_id": request_id, "status": status}


def reconcile_chat(data):
    """Release an unknown hold only for a reply already saved by the host."""
    request_id = require(data.get("request_id"), 120)
    actor_id = require(data.get("actor_id"), 100)
    session_key = require(data.get("session_key"), 200)
    digest = require(data.get("content_digest"), 64)
    if not re.fullmatch(r"[a-f0-9]{64}", digest):
        raise ValueError("Invalid chat content digest")
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        row = conn.execute("SELECT * FROM runway_chat_claims WHERE request_id=?", (request_id,)).fetchone()
        if row is None or (row["actor_id"], row["session_key"], row["content_digest"]) != (actor_id, session_key, digest):
            raise ValueError("Saved chat receipt does not match the execution claim")
        if row["status"] not in ("unknown", "succeeded"):
            raise ValueError("Only a confirmed saved reply can reconcile an unknown claim")
        if row["status"] == "unknown":
            conn.execute("UPDATE runway_chat_claims SET status='succeeded',ended_at=? WHERE request_id=?",
                         (time.time(), request_id))
        return {"request_id": request_id, "status": "succeeded"}


def reserve_model_request(data):
    """Reserve one network request; a duplicate ID never authorizes another send."""
    request_id = require(data.get("request_id"), 120)
    execution_id = require(data.get("execution_id"), 32)
    digest = require(data.get("request_digest"), 64)
    reserved = data.get("reserved_tokens")
    if not re.fullmatch(r"[a-f0-9]{64}", digest) or type(reserved) is not int or not 0 < reserved <= 250000:
        raise ValueError("A request digest and positive bounded token reservation are required")
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        prior = conn.execute("SELECT * FROM runway_model_requests WHERE request_id=?", (request_id,)).fetchone()
        if prior:
            if (prior["execution_id"], prior["request_digest"], prior["reserved_tokens"]) != (execution_id, digest, reserved):
                raise ValueError("Model request ID belongs to a different request")
            return {"request_id": request_id, "status": prior["status"], "admitted": False}
        execution = conn.execute("SELECT * FROM runway_executions WHERE id=?", (execution_id,)).fetchone()
        if execution is None or execution["status"] != "running":
            raise ValueError("A running execution is required before model dispatch")
        project = conn.execute("SELECT * FROM runways WHERE id=?", (execution["runway_id"],)).fetchone()
        if project is None or project["active_execution"] != execution_id or project["status"] != "running" or project["deadline_at"] is None or now >= project["deadline_at"]:
            raise ValueError("Runway is paused, expired, or no longer owns this execution")
        root_id = project["pilot_root_id"] or project["id"]
        if conn.execute("SELECT 1 FROM runway_model_requests WHERE pilot_root_id=? AND status IN ('unknown','overrun') LIMIT 1", (root_id,)).fetchone():
            raise ValueError("Unresolved model request needs reconciliation")
        count, charged = conn.execute("SELECT COUNT(*),COALESCE(SUM(CASE WHEN reported_tokens IS NOT NULL THEN reported_tokens ELSE reserved_tokens END),0) FROM runway_model_requests WHERE pilot_root_id=?", (root_id,)).fetchone()
        execution_charged = conn.execute("SELECT COALESCE(SUM(CASE WHEN reported_tokens IS NOT NULL THEN reported_tokens ELSE reserved_tokens END),0) FROM runway_model_requests WHERE execution_id=?", (execution_id,)).fetchone()[0]
        if count >= 20:
            raise ValueError("Pilot model request ceiling reached before dispatch")
        if charged + reserved > 250000:
            raise ValueError("Pilot token ceiling reached before dispatch")
        if execution_charged + reserved > execution["reserved_tokens"]:
            raise ValueError("Execution token reservation reached before dispatch")
        conn.execute("INSERT INTO runway_model_requests VALUES(?,?,?,?,?,NULL,'reserved',?,NULL)",
                     (request_id, execution_id, root_id, digest, reserved, now))
        return {"request_id": request_id, "status": "reserved", "admitted": True,
                "pilot_requests": count + 1, "pilot_tokens_reserved_or_reported": charged + reserved}


def finish_model_request(data):
    request_id = require(data.get("request_id"), 120)
    status = data.get("status")
    reported = data.get("reported_tokens")
    if status not in ("reported", "unknown") or (status == "reported" and (type(reported) is not int or reported < 0)):
        raise ValueError("Model request needs reported or unknown outcome")
    if status == "unknown" and reported is not None:
        raise ValueError("Unknown request cannot claim confirmed usage")
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        row = conn.execute("SELECT * FROM runway_model_requests WHERE request_id=?", (request_id,)).fetchone()
        if row is None:
            raise ValueError("Model request reservation is missing")
        if row["status"] == "reported" and status == "reported" and row["reported_tokens"] == reported:
            return {"request_id": request_id, "status": "reported"}
        if row["status"] == "unknown" and status == "unknown":
            return {"request_id": request_id, "status": "unknown"}
        if row["status"] != "reserved":
            raise ValueError("Model request outcome is already settled or unresolved")
        settled = "overrun" if status == "reported" and reported > row["reserved_tokens"] else status
        conn.execute("UPDATE runway_model_requests SET status=?,reported_tokens=?,ended_at=? WHERE request_id=?",
                     (settled, reported, time.time(), request_id))
        return {"request_id": request_id, "status": settled}


def settle(data, success):
    eid = require(data.get("execution_id"), 32)
    usage = data.get("usage")
    if usage is not None and (not isinstance(usage, dict) or not isinstance(usage.get("totalTokens"), int) or usage["totalTokens"] < 0):
        raise ValueError("Usage must contain a nonnegative totalTokens count")
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        execution = conn.execute("SELECT * FROM runway_executions WHERE id=?", (eid,)).fetchone()
        if execution is None:
            raise ValueError("Unknown execution")
        if execution["status"] != "running":
            return snapshot(conn, execution["runway_id"])
        if conn.execute("SELECT 1 FROM runway_model_requests WHERE execution_id=? AND status IN ('reserved','unknown','overrun') LIMIT 1", (eid,)).fetchone():
            raise ValueError("Model request is unresolved; do not settle the execution")
        project = conn.execute("SELECT * FROM runways WHERE id=?", (execution["runway_id"],)).fetchone()
        step = conn.execute("SELECT * FROM runway_steps WHERE id=?", (execution["step_id"],)).fetchone()
        task = conn.execute("SELECT version FROM tasks WHERE id=?", (step["task_id"],)).fetchone()
        request_tokens = conn.execute("SELECT COALESCE(SUM(reported_tokens),0) FROM runway_model_requests WHERE execution_id=?", (eid,)).fetchone()[0]
        billed = max(usage["totalTokens"], request_tokens) if usage else execution["reserved_tokens"]
        conn.execute("UPDATE runways SET token_reserved=token_reserved-?,token_used=token_used+?,active_execution=NULL,version=version+1,updated_at=? WHERE id=?",
                     (execution["reserved_tokens"], billed, now, project["id"]))
        if project["active_execution"] != eid or task is None or task["version"] != step["task_version"] or project["status"] not in ("running", "paused"):
            conn.execute("UPDATE runway_executions SET status='stale',reported_tokens=?,usage_json=?,error='Project or task changed during execution',ended_at=? WHERE id=?",
                         (usage["totalTokens"] if usage else None, json.dumps(usage) if usage else None, now, eid))
            if project["status"] != "paused":
                conn.execute("UPDATE runways SET status='needs_review',wait_reason='Stale result held for review; no task state overwritten' WHERE id=?", (project["id"],))
            return snapshot(conn, project["id"])
        artifact_id = None
        if success and step["kind"] == "revision_angles":
            target = conn.execute("SELECT a.digest FROM runway_reviews r JOIN runway_artifacts a ON a.id=r.artifact_id WHERE r.step_id=?", (step["id"],)).fetchone()
            if target is None or hashlib.sha256(require(data.get("content"), 12000).encode()).hexdigest() == target["digest"]:
                success = False
                data = {**data, "error": "Revision repeated the source artifact or lost its review link"}
        if success:
            content = require(data.get("content"), 12000)
            source_urls = data.get("source_urls")
            if not isinstance(source_urls, list) or not source_urls or not all(isinstance(u, str) for u in source_urls):
                raise ValueError("A verified source list is required")
            allowed = {r[0] for r in conn.execute("SELECT url FROM runway_sources WHERE runway_id=?", (project["id"],))}
            if not set(source_urls) <= allowed:
                raise ValueError("Artifact cited a source outside the assignment")
            artifact_id = uuid.uuid4().hex
            conn.execute("INSERT INTO runway_artifacts VALUES(?,?,?,?,?,?,?,?)",
                         (artifact_id, project["id"], step["id"], step["kind"], content,
                          hashlib.sha256(content.encode()).hexdigest(), json.dumps(source_urls), now))
            conn.execute("UPDATE runway_steps SET status='done',artifact_id=? WHERE id=?", (artifact_id, step["id"]))
            conn.execute("UPDATE tasks SET status='done',action_state='none',next_action=?,version=version+1,updated_at=? WHERE id=? AND version=?",
                         ("Saved artifact " + artifact_id + "; next project step is tracked by the runway.", int(now), step["task_id"], task["version"]))
            remaining = conn.execute("SELECT COUNT(*) FROM runway_steps WHERE runway_id=? AND status!='done'", (project["id"],)).fetchone()[0]
            new_status = "ready" if remaining else "needs_review"
            reason = None if remaining else "All deliverables saved; owner review needed"
            record_event("checkpoint", "Marketing deliverable saved", {"runway_id": project["id"], "artifact_id": artifact_id, "kind": step["kind"]}, conn)
        else:
            error = require(data.get("error"), 500)
            retry = step["attempts"] < 3 and project["run_count"] < project["max_runs"] and billed + project["token_used"] + project["reserve_per_run"] <= project["token_limit"]
            new_status = "ready" if retry else "needs_review"
            reason = "Repair attempt available after failed deliverable" if retry else "Deliverable failed after bounded attempts or allowance"
            conn.execute("UPDATE runway_steps SET status=?,task_version=task_version+1 WHERE id=?", ("ready" if retry else "blocked", step["id"]))
            conn.execute("UPDATE tasks SET status=?,action_state=?,next_action=?,version=version+1,updated_at=? WHERE id=? AND version=?",
                         ("ready" if retry else "needs_you", "agent_ready" if retry else "user_waiting", reason,
                          int(now), step["task_id"], task["version"]))
            record_event("checkpoint", "Marketing deliverable rejected by validator", {"runway_id": project["id"], "execution_id": eid, "reason": error}, conn)
        if project["status"] == "paused":
            new_status, reason = "paused", "Paused by owner; the active turn has settled"
        conn.execute("UPDATE runway_executions SET status=?,reported_tokens=?,usage_json=?,error=?,ended_at=?,artifact_id=? WHERE id=?",
                     ("succeeded" if success else "failed", usage["totalTokens"] if usage else None,
                      json.dumps(usage) if usage else None, None if success else data["error"], now, artifact_id, eid))
        conn.execute("UPDATE runways SET status=?,wait_reason=?,next_due=NULL WHERE id=?", (new_status, reason, project["id"]))
        if usage is None:
            conn.execute("UPDATE runways SET status='needs_review',wait_reason='Model usage missing; conservative reservation charged and review required' WHERE id=?", (project["id"],))
        return snapshot(conn, project["id"])


def change(data, action):
    rid = require(data.get("id"), 32)
    version = data.get("version")
    if not isinstance(version, int):
        raise ValueError("Current project version is required")
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        project = conn.execute("SELECT * FROM runways WHERE id=?", (rid,)).fetchone()
        if project is None or project["version"] != version:
            raise ValueError("Stale project version")
        if action == "pause" and project["status"] in ("ready", "running", "waiting"):
            status, reason = "paused", "Paused by owner; an active turn may still finish"
        elif action == "resume" and project["status"] == "paused" and not project["active_execution"]:
            if project["deadline_at"] is None or now >= project["deadline_at"]:
                raise ValueError("A bounded, unexpired assignment is required to resume")
            another = conn.execute("SELECT 1 FROM runways WHERE id!=? AND status IN ('running','ready','waiting','unknown') LIMIT 1", (rid,)).fetchone()
            if another:
                raise ValueError("Another standing assignment is active or unresolved")
            status, reason = "ready", None
        else:
            raise ValueError("Project cannot make that transition")
        conn.execute("UPDATE runways SET status=?,wait_reason=?,version=version+1,updated_at=? WHERE id=?", (status, reason, now, rid))
        record_event("checkpoint", "Marketing runway " + status, {"runway_id": rid}, conn)
        return snapshot(conn, rid)


def review(data):
    rid = require(data.get("id"), 32)
    request_id = require(data.get("request_id"), 120)
    artifact_id = require(data.get("artifact_id"), 32)
    digest = require(data.get("digest"), 64)
    actor_id = require(data.get("actor_id"), 100)
    actor_name = require(data.get("actor_name"), 60)
    if data.get("actor_owner") is not True:
        raise ValueError("Owner authorization is required")
    decision = data.get("decision")
    if decision not in ("approved", "rejected", "revision_requested"):
        raise ValueError("Unknown review decision")
    instruction = require(data.get("instruction"), 1000) if decision == "revision_requested" else ""
    defer = data.get("defer") is True and decision == "revision_requested"
    version = data.get("version")
    if not isinstance(version, int):
        raise ValueError("Current project version is required")
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        prior = conn.execute("SELECT * FROM runway_reviews WHERE request_id=?", (request_id,)).fetchone()
        if prior:
            if (prior["runway_id"], prior["artifact_id"], prior["artifact_digest"], prior["decision"], prior["instruction"], prior["actor_id"]) != (rid, artifact_id, digest, decision, instruction, actor_id):
                raise ValueError("Request ID belongs to a different review")
            return snapshot(conn, rid)
        project = conn.execute("SELECT * FROM runways WHERE id=?", (rid,)).fetchone()
        if project is None or project["version"] != version:
            raise ValueError("Stale project version")
        if project["status"] not in ("needs_review", "done") or project["active_execution"]:
            raise ValueError("Wait for the current work to finish before reviewing")
        artifact = conn.execute("SELECT * FROM runway_artifacts WHERE id=? AND runway_id=?", (artifact_id, rid)).fetchone()
        if artifact is None or artifact["digest"] != digest:
            raise ValueError("Artifact version changed; refresh before reviewing")
        latest = conn.execute("SELECT decision FROM runway_reviews WHERE artifact_id=? ORDER BY created_at DESC LIMIT 1", (artifact_id,)).fetchone()
        if latest:
            raise ValueError("This exact artifact has already been reviewed")
        step_id = None
        if decision == "revision_requested":
            if artifact["kind"] not in ("post_angles", "revision_angles"):
                raise ValueError("Only a post-angle set can be revised here")
            if defer:
                conn.execute("UPDATE runways SET status='needs_review',wait_reason=?,next_due=NULL,version=version+1,updated_at=? WHERE id=?",
                             ("Revision request saved; execution awaits a metered model route and a fresh owner grant", now, rid))
            else:
                if project["run_count"] >= project["max_runs"] or project["token_used"] + project["reserve_per_run"] > project["token_limit"]:
                    raise ValueError("The assignment budget cannot admit a revision")
                if project["deadline_at"] is None or now >= project["deadline_at"]:
                    raise ValueError("A bounded, unexpired assignment is required to execute a revision")
                ordinal = conn.execute("SELECT COALESCE(MAX(ordinal),-1)+1 FROM runway_steps WHERE runway_id=?", (rid,)).fetchone()[0]
                task_id, step_id = uuid.uuid4().hex, uuid.uuid4().hex
                conn.execute("INSERT INTO tasks VALUES(?,?,?,?,?,?,?,?,?,?)",
                             (task_id, "Revise evidence-linked post angles", "ready", "normal", REVISION_CRITERION,
                              "agent_ready", None, "agent:main:marketing-task-" + task_id, 1, int(now)))
                conn.execute("INSERT INTO runway_steps VALUES(?,?,?,?,?,?,?,?,?)",
                             (step_id, rid, ordinal, "revision_angles", task_id, 1, "ready", 0, None))
                conn.execute("UPDATE runways SET status='ready',wait_reason=NULL,next_due=NULL,version=version+1,updated_at=? WHERE id=?", (now, rid))
                record_event("task", "Owner-requested revision ready", {"task_id": task_id, "runway_id": rid, "artifact_id": artifact_id}, conn)
        elif decision == "approved":
            conn.execute("UPDATE runways SET status='done',wait_reason='Exact draft approved for internal use; nothing was published',version=version+1,updated_at=? WHERE id=?", (now, rid))
        else:
            conn.execute("UPDATE runways SET status='needs_review',wait_reason='Idea rejected; revise it or start a new assignment',version=version+1,updated_at=? WHERE id=?", (now, rid))
        conn.execute("INSERT INTO runway_reviews VALUES(?,?,?,?,?,?,?,?,?,?,?)",
                     (uuid.uuid4().hex, request_id, rid, artifact_id, digest, decision, instruction, actor_id, actor_name, step_id, now))
        record_event("checkpoint", "Owner reviewed marketing artifact", {"runway_id": rid, "artifact_id": artifact_id, "decision": decision}, conn)
        return snapshot(conn, rid)


def prepare_revision_grant(data):
    """Record exact owner authority, but never create an executable step here."""
    rid = require(data.get("id"), 32)
    review_id = require(data.get("review_id"), 32)
    artifact_id = require(data.get("artifact_id"), 32)
    digest = require(data.get("digest"), 64)
    request_id = require(data.get("request_id"), 120)
    owner = require(data.get("owner_actor"), 100)
    if data.get("actor_owner") is not True:
        raise ValueError("Owner authorization is required")
    if any(not re.fullmatch(r"[a-f0-9]{32}", value) for value in (rid, review_id, artifact_id)) or not re.fullmatch(r"[a-f0-9]{64}", digest):
        raise ValueError("Invalid review or artifact identity")
    version = data.get("version")
    limits = {name: data.get(name) for name in ("max_runs", "max_model_requests", "token_limit", "max_active_seconds")}
    if type(version) is not int or version < 1 or any(type(value) is not int or value < 1 for value in limits.values()):
        raise ValueError("Current version and positive integer limits are required")
    if limits["max_runs"] > 6 or limits["max_model_requests"] > 20 or limits["token_limit"] > 250000 or limits["max_active_seconds"] > 900:
        raise ValueError("Revision limits exceed the pilot envelope")
    deadline = data.get("deadline_at")
    if type(deadline) not in (int, float) or not math.isfinite(deadline):
        raise ValueError("An explicit finite revision deadline is required")
    payload = {"id": rid, "version": version, "review_id": review_id, "artifact_id": artifact_id,
               "digest": digest, "request_id": request_id, "owner_actor": owner,
               "deadline_at": deadline, "scope": "internal_revision_draft", **limits}
    payload_digest = hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        prior = conn.execute("SELECT * FROM runway_revision_grants WHERE request_id=?", (request_id,)).fetchone()
        if prior:
            if prior["payload_digest"] != payload_digest:
                raise ValueError("Request ID belongs to a different revision grant")
            return snapshot(conn, prior["source_runway_id"])
        if deadline <= now or deadline > now + 1800:
            raise ValueError("Revision deadline must be within the next 30 minutes")
        project = conn.execute("SELECT * FROM runways WHERE id=?", (rid,)).fetchone()
        if project is None or project["version"] != version:
            raise ValueError("Stale project version")
        if project["status"] != "needs_review" or project["active_execution"]:
            raise ValueError("Project must be idle and awaiting review")
        profile = conn.execute("SELECT version FROM marketing_profile WHERE id='marketing'").fetchone()
        if profile is None or profile["version"] != project["profile_version"]:
            raise ValueError("Owner brief changed; create a new assignment")
        review_row = conn.execute("SELECT * FROM runway_reviews WHERE id=? AND runway_id=?", (review_id, rid)).fetchone()
        if review_row is None or review_row["decision"] != "revision_requested" or review_row["step_id"] is not None or review_row["artifact_id"] != artifact_id or review_row["artifact_digest"] != digest:
            raise ValueError("The exact deferred revision review is required")
        latest = conn.execute("SELECT id FROM runway_reviews WHERE runway_id=? ORDER BY created_at DESC,id DESC LIMIT 1", (rid,)).fetchone()
        if latest is None or latest["id"] != review_id:
            raise ValueError("A newer owner decision superseded this review")
        artifact = conn.execute("SELECT kind,digest FROM runway_artifacts WHERE id=? AND runway_id=?", (artifact_id, rid)).fetchone()
        if artifact is None or artifact["digest"] != digest or artifact["kind"] not in ("post_angles", "revision_angles"):
            raise ValueError("Artifact version changed; refresh before granting")
        if conn.execute("SELECT 1 FROM runway_revision_grants WHERE source_review_id=?", (review_id,)).fetchone():
            raise ValueError("This revision review already has a grant")
        root_id = project["pilot_root_id"] or rid
        root = conn.execute("SELECT * FROM runways WHERE id=?", (root_id,)).fetchone()
        if root is None:
            raise ValueError("Pilot root is missing")
        totals = conn.execute("SELECT COALESCE(SUM(run_count),0),COALESCE(SUM(token_used+token_reserved),0) FROM runways WHERE pilot_root_id=?", (root_id,)).fetchone()
        spent_seconds = conn.execute("SELECT COALESCE(SUM(e.ended_at-e.started_at),0) FROM runway_executions e JOIN runways r ON r.id=e.runway_id WHERE r.pilot_root_id=? AND e.ended_at IS NOT NULL", (root_id,)).fetchone()[0]
        if limits["max_runs"] > root["max_runs"] - totals[0] or limits["token_limit"] > root["token_limit"] - totals[1] or limits["max_active_seconds"] > root["max_active_seconds"] - spent_seconds:
            raise ValueError("Revision proposal exceeds the remaining recorded pilot allowance")
        grant_id = uuid.uuid4().hex
        conn.execute("INSERT INTO runway_revision_grants VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                     (grant_id, request_id, payload_digest, root_id, rid, version, review_id,
                      artifact_id, digest, project["profile_version"], owner, "internal_revision_draft", limits["max_runs"],
                      limits["max_model_requests"], limits["token_limit"], limits["max_active_seconds"],
                      deadline, "held_for_metering", now))
        record_event("checkpoint", "Bounded revision grant held for metering", {"runway_id": rid, "grant_id": grant_id, "review_id": review_id}, conn)
        return snapshot(conn, rid)


def add_input(data):
    rid = require(data.get("id"), 32)
    request_id = require(data.get("request_id"), 120)
    actor_id = require(data.get("actor_id"), 100)
    actor_name = require(data.get("actor_name"), 60)
    content = require(data.get("content"), 1000)
    version = data.get("version")
    if not isinstance(version, int): raise ValueError("Current project version is required")
    activate = data.get("activate") is not False
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        prior = conn.execute("SELECT * FROM runway_inputs WHERE request_id=?", (request_id,)).fetchone()
        if prior:
            if prior["runway_id"] != rid or prior["actor_id"] != actor_id or prior["content"] != content:
                raise ValueError("Request ID belongs to a different project input")
            return snapshot(conn, rid)
        project = conn.execute("SELECT * FROM runways WHERE id=?", (rid,)).fetchone()
        if project is None or project["version"] != version:
            raise ValueError("Stale project version")
        if project["status"] not in ("ready", "running", "waiting", "paused", "needs_review"):
            raise ValueError("Project cannot receive input in its current state")
        conn.execute("INSERT INTO runway_inputs VALUES(?,?,?,?,?,?,?)", (uuid.uuid4().hex, request_id, rid, actor_id, actor_name, content, now))
        conn.execute("UPDATE runways SET version=version+1,updated_at=?,"
                     "status=CASE WHEN status='waiting' AND ?=1 THEN 'ready' ELSE status END,"
                     "next_due=CASE WHEN status='waiting' AND ?=1 THEN NULL ELSE next_due END,"
                     "wait_reason=CASE WHEN status='waiting' AND ?=1 THEN NULL ELSE wait_reason END WHERE id=?",
                     (now, int(activate), int(activate), int(activate), rid))
        record_event("checkpoint", "Project input recorded", {"runway_id": rid, "actor_id": actor_id}, conn)
        return snapshot(conn, rid)


def unknown(data):
    eid = require(data.get("execution_id"), 32)
    reason = require(data.get("error"), 500)
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        execution = conn.execute("SELECT * FROM runway_executions WHERE id=?", (eid,)).fetchone()
        if execution is None:
            raise ValueError("Unknown execution")
        if execution["status"] != "running":
            return snapshot(conn, execution["runway_id"])
        conn.execute("UPDATE runway_model_requests SET status='unknown',ended_at=? WHERE execution_id=? AND status='reserved'",
                     (now, eid))
        conn.execute("UPDATE runway_executions SET status='unknown',error=?,ended_at=? WHERE id=?", (reason, now, eid))
        conn.execute("UPDATE runways SET status='unknown',wait_reason=?,version=version+1,updated_at=? WHERE id=?",
                     ("Execution outcome unknown; reconcile before any retry. " + reason, now, execution["runway_id"]))
        record_event("checkpoint", "Marketing execution outcome unknown", {"runway_id": execution["runway_id"], "execution_id": eid}, conn)
        return snapshot(conn, execution["runway_id"])


def rejected(data):
    """Reopen only an authoritative Gateway INVALID_REQUEST admission rejection."""
    eid = require(data.get("execution_id"), 32)
    code = data.get("gateway_code")
    message = require(data.get("gateway_message"), 500)
    if code != "INVALID_REQUEST":
        raise ValueError("Only a pre-run Gateway validation rejection can be reopened")
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        execution = conn.execute("SELECT * FROM runway_executions WHERE id=?", (eid,)).fetchone()
        if execution is None:
            raise ValueError("Unknown execution")
        project = conn.execute("SELECT * FROM runways WHERE id=?", (execution["runway_id"],)).fetchone()
        if execution["status"] == "rejected":
            return snapshot(conn, execution["runway_id"])
        if execution["status"] not in ("running", "unknown") or project["status"] not in ("running", "unknown") or project["active_execution"] != eid:
            raise ValueError("Execution cannot be reconciled as a rejected admission")
        if conn.execute("SELECT 1 FROM runway_model_requests WHERE execution_id=? LIMIT 1", (eid,)).fetchone():
            raise ValueError("A model request receipt prevents pre-inference rejection")
        step = conn.execute("SELECT * FROM runway_steps WHERE id=?", (execution["step_id"],)).fetchone()
        task = conn.execute("SELECT version FROM tasks WHERE id=?", (step["task_id"],)).fetchone()
        if task is None or task["version"] != step["task_version"]:
            raise ValueError("Task changed; do not reopen an uncertain step")
        conn.execute("UPDATE runway_executions SET status='rejected',error=?,ended_at=? WHERE id=?", (message, now, eid))
        conn.execute("UPDATE runways SET status='ready',active_execution=NULL,token_reserved=token_reserved-?,wait_reason=NULL,version=version+1,updated_at=? WHERE id=?",
                     (execution["reserved_tokens"], now, project["id"]))
        conn.execute("UPDATE runway_steps SET status='ready',task_version=task_version+1 WHERE id=?", (step["id"],))
        conn.execute("UPDATE tasks SET status='ready',version=version+1,updated_at=? WHERE id=?", (int(now), step["task_id"]))
        record_event("checkpoint", "Gateway rejected marketing admission before inference", {"runway_id": project["id"], "execution_id": eid, "code": code}, conn)
        return snapshot(conn, project["id"])


def recover():
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        ids = [r[0] for r in conn.execute("SELECT id FROM runways WHERE active_execution IS NOT NULL")]
        for rid in ids:
            conn.execute("UPDATE runways SET status='unknown',wait_reason='Host restarted during an OpenClaw execution; reconcile original outcome before resuming',version=version+1,updated_at=? WHERE id=?", (time.time(), rid))
            record_event("checkpoint", "Marketing execution outcome unknown after restart", {"runway_id": rid}, conn)
        chats = [r[0] for r in conn.execute("SELECT request_id FROM runway_chat_claims WHERE status='pending'")]
        for request_id in chats:
            conn.execute("UPDATE runway_chat_claims SET status='unknown',ended_at=? WHERE request_id=?",
                         (time.time(), request_id))
        requests = [r[0] for r in conn.execute("SELECT request_id FROM runway_model_requests WHERE status='reserved'")]
        for request_id in requests:
            conn.execute("UPDATE runway_model_requests SET status='unknown',ended_at=? WHERE request_id=?",
                         (time.time(), request_id))
        return {"unknown_runways": ids, "unknown_chats": chats, "unknown_model_requests": requests}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=("create", "status", "list", "inspect", "claim", "chat-claim", "chat-finish", "chat-reconcile", "model-reserve", "model-finish", "finish", "fail", "unknown", "rejected", "input", "review", "prepare-revision-grant", "pause", "resume", "recover"))
    args = parser.parse_args()
    data = read_input() if args.action in ("create", "inspect", "chat-claim", "chat-finish", "chat-reconcile", "model-reserve", "model-finish", "finish", "fail", "unknown", "rejected", "input", "review", "prepare-revision-grant", "pause", "resume") else {}
    if args.action == "create": result = create(data)
    elif args.action == "status":
        with connection() as conn: result = snapshot(conn)
    elif args.action == "list": result = list_projects()
    elif args.action == "inspect": result = inspect(data)
    elif args.action == "claim": result = claim()
    elif args.action == "chat-claim": result = claim_chat(data)
    elif args.action == "chat-finish": result = finish_chat(data)
    elif args.action == "chat-reconcile": result = reconcile_chat(data)
    elif args.action == "model-reserve": result = reserve_model_request(data)
    elif args.action == "model-finish": result = finish_model_request(data)
    elif args.action == "finish": result = settle(data, True)
    elif args.action == "fail": result = settle(data, False)
    elif args.action == "unknown": result = unknown(data)
    elif args.action == "rejected": result = rejected(data)
    elif args.action == "input": result = add_input(data)
    elif args.action == "review": result = review(data)
    elif args.action == "prepare-revision-grant": result = prepare_revision_grant(data)
    elif args.action in ("pause", "resume"): result = change(data, args.action)
    else: result = recover()
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    try: main()
    except (ValueError, sqlite3.Error, KeyError, json.JSONDecodeError) as exc:
        print(str(exc), file=sys.stderr)
        sys.exit(2)
