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
import os
import re
import sqlite3
import sys
import time
import tempfile
import uuid
from contextlib import contextmanager
from pathlib import Path

from hire import db, record_event

KINDS = ("audience_note", "post_angles", "review_packet")
TITLES = ("Research one candidate audience and problem", "Draft three evidence-linked post angles", "Prepare owner review packet")
CRITERIA = (
    "One provisional audience, one problem, two exact source quotes, and explicit evidence limits.",
    "Exactly three distinct draft post angles; each cites a checked source and states a claim limit.",
    "Unsupported claims listed, five qualitative checks recorded, results summarized, one specific owner decision requested, and one bounded next-step proposal with a continue-or-stop reason.",
)
REVISION_CRITERION = "Exactly three revised, distinct evidence-linked post angles, with claim limits; materially change the rejected draft."
SCHEMA = """
CREATE TABLE IF NOT EXISTS runways(
 id TEXT PRIMARY KEY, request_id TEXT UNIQUE NOT NULL, goal TEXT NOT NULL,
 criteria TEXT NOT NULL, scope TEXT NOT NULL, scope_version INTEGER NOT NULL DEFAULT 1,
 profile_version INTEGER NOT NULL, deadline_at REAL, pilot_root_id TEXT,
 owner_actor TEXT NOT NULL, owner_input TEXT NOT NULL DEFAULT '',
 max_runs INTEGER NOT NULL, max_model_requests INTEGER NOT NULL DEFAULT 20,
 max_active_seconds INTEGER NOT NULL,
 token_limit INTEGER NOT NULL, reserve_per_run INTEGER NOT NULL,
 token_reserved INTEGER NOT NULL DEFAULT 0, token_used INTEGER NOT NULL DEFAULT 0,
 run_count INTEGER NOT NULL DEFAULT 0, status TEXT NOT NULL, version INTEGER NOT NULL,
 active_execution TEXT, next_due REAL, wait_reason TEXT,
 source_runway_id TEXT, source_review_id TEXT, source_artifact_id TEXT, source_artifact_digest TEXT,
 created_at REAL NOT NULL, updated_at REAL NOT NULL);
CREATE TABLE IF NOT EXISTS runway_steps(
 id TEXT PRIMARY KEY, runway_id TEXT NOT NULL, ordinal INTEGER NOT NULL,
 kind TEXT NOT NULL, task_id TEXT UNIQUE NOT NULL, task_version INTEGER NOT NULL,
 status TEXT NOT NULL, attempts INTEGER NOT NULL DEFAULT 0,
 artifact_id TEXT, UNIQUE(runway_id,ordinal));
CREATE TABLE IF NOT EXISTS runway_sources(
 runway_id TEXT NOT NULL, url TEXT NOT NULL, content TEXT NOT NULL,
 digest TEXT NOT NULL, captured_at REAL, PRIMARY KEY(runway_id,url));
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
 created_at REAL NOT NULL, source_input_id TEXT);
CREATE TABLE IF NOT EXISTS runway_reviews(
 id TEXT PRIMARY KEY, request_id TEXT UNIQUE NOT NULL, runway_id TEXT NOT NULL,
 artifact_id TEXT NOT NULL, artifact_digest TEXT NOT NULL, decision TEXT NOT NULL,
 instruction TEXT NOT NULL, actor_id TEXT NOT NULL, actor_name TEXT NOT NULL,
 step_id TEXT, created_at REAL NOT NULL);
CREATE TABLE IF NOT EXISTS runway_revision_grants(
 id TEXT PRIMARY KEY, request_id TEXT UNIQUE NOT NULL, payload_digest TEXT NOT NULL,
 pilot_root_id TEXT NOT NULL, source_runway_id TEXT NOT NULL,
 source_runway_version INTEGER NOT NULL, source_review_id TEXT NOT NULL,
 source_artifact_id TEXT NOT NULL, source_artifact_digest TEXT NOT NULL,
 profile_version INTEGER NOT NULL, owner_actor TEXT NOT NULL, scope TEXT NOT NULL,
 max_runs INTEGER NOT NULL, max_model_requests INTEGER NOT NULL,
 token_limit INTEGER NOT NULL, max_active_seconds INTEGER NOT NULL,
 deadline_at REAL NOT NULL, status TEXT NOT NULL, created_at REAL NOT NULL,
 budget_mode TEXT NOT NULL DEFAULT 'same_pilot', released_runway_id TEXT);
CREATE TABLE IF NOT EXISTS runway_chat_claims(
 request_id TEXT PRIMARY KEY, actor_id TEXT NOT NULL, session_key TEXT NOT NULL,
 content_digest TEXT NOT NULL, status TEXT NOT NULL,
 created_at REAL NOT NULL, ended_at REAL);
CREATE TABLE IF NOT EXISTS runway_model_requests(
 request_id TEXT PRIMARY KEY, execution_id TEXT NOT NULL, pilot_root_id TEXT NOT NULL,
 request_digest TEXT NOT NULL, reserved_tokens INTEGER NOT NULL, reported_tokens INTEGER,
 status TEXT NOT NULL, created_at REAL NOT NULL, ended_at REAL);
CREATE TABLE IF NOT EXISTS runway_terminal_receipts(
 execution_id TEXT PRIMARY KEY, audit_event_id TEXT UNIQUE NOT NULL,
 evidence_json TEXT NOT NULL, evidence_digest TEXT NOT NULL, recorded_at REAL NOT NULL);
CREATE TABLE IF NOT EXISTS runway_response_receipts(
 request_id TEXT PRIMARY KEY, request_digest TEXT NOT NULL,
 response_json TEXT NOT NULL, recorded_at REAL NOT NULL);
CREATE TABLE IF NOT EXISTS runway_pilot_checkpoints(
 request_id TEXT PRIMARY KEY, runway_id TEXT NOT NULL, owner_actor TEXT NOT NULL,
 source_version INTEGER NOT NULL, reported_tokens INTEGER NOT NULL, created_at REAL NOT NULL);
CREATE TABLE IF NOT EXISTS runway_campaigns(
 runway_id TEXT PRIMARY KEY, version INTEGER NOT NULL, stage TEXT NOT NULL,
 mode TEXT NOT NULL DEFAULT 'internal',
 owner_actor TEXT NOT NULL, source_artifact_id TEXT NOT NULL, source_artifact_digest TEXT NOT NULL,
 asset_artifact_id TEXT, asset_artifact_digest TEXT,
 brief_json TEXT NOT NULL, experiment_json TEXT NOT NULL,
 created_at REAL NOT NULL, updated_at REAL NOT NULL);
CREATE TABLE IF NOT EXISTS runway_campaign_revisions(
 id TEXT PRIMARY KEY, runway_id TEXT NOT NULL, version INTEGER NOT NULL,
 request_id TEXT UNIQUE NOT NULL, payload_digest TEXT NOT NULL,
 actor_id TEXT NOT NULL, source_artifact_id TEXT NOT NULL,
 source_artifact_digest TEXT NOT NULL, brief_json TEXT NOT NULL,
 experiment_json TEXT NOT NULL, created_at REAL NOT NULL,
 UNIQUE(runway_id,version));
CREATE TABLE IF NOT EXISTS runway_campaign_actions(
 id TEXT PRIMARY KEY, runway_id TEXT NOT NULL, version INTEGER NOT NULL,
 request_id TEXT UNIQUE NOT NULL, payload_digest TEXT NOT NULL,
 action TEXT NOT NULL, status TEXT NOT NULL, actor_id TEXT NOT NULL,
 payload_json TEXT NOT NULL, created_at REAL NOT NULL,
 UNIQUE(runway_id,version));
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
    for column in ("max_model_requests INTEGER NOT NULL DEFAULT 20", "source_runway_id TEXT", "source_review_id TEXT",
                   "source_artifact_id TEXT", "source_artifact_digest TEXT",
                   "accounting_mode TEXT NOT NULL DEFAULT 'hard_cap'", "request_allowance INTEGER NOT NULL DEFAULT 0"):
        if column.split()[0] not in existing:
            conn.execute("ALTER TABLE runways ADD COLUMN " + column)
    grant_columns = {row[1] for row in conn.execute("PRAGMA table_info(runway_revision_grants)")}
    # Expired authority stays in history; a later explicit grant needs its own
    # row. SQLite cannot drop the old inline UNIQUE constraint in place.
    grant_schema = conn.execute("SELECT sql FROM sqlite_master WHERE name='runway_revision_grants'").fetchone()[0]
    if "source_review_id TEXT UNIQUE NOT NULL" in grant_schema:
        conn.execute("BEGIN IMMEDIATE")
        try:
            grant_schema = conn.execute("SELECT sql FROM sqlite_master WHERE name='runway_revision_grants'").fetchone()[0]
            if "source_review_id TEXT UNIQUE NOT NULL" in grant_schema:
                replacement = re.sub(r'^CREATE TABLE(?: IF NOT EXISTS)? "?runway_revision_grants"?', "CREATE TABLE runway_revision_grants_migrating", grant_schema)
                conn.execute(replacement.replace("source_review_id TEXT UNIQUE NOT NULL", "source_review_id TEXT NOT NULL"))
                conn.execute("INSERT INTO runway_revision_grants_migrating SELECT * FROM runway_revision_grants")
                conn.execute("DROP TABLE runway_revision_grants")
                conn.execute("ALTER TABLE runway_revision_grants_migrating RENAME TO runway_revision_grants")
            conn.commit()
        except BaseException:
            conn.rollback()
            conn.close()
            raise
    for column in ("scope TEXT NOT NULL DEFAULT 'internal_revision_draft'", "budget_mode TEXT NOT NULL DEFAULT 'same_pilot'", "released_runway_id TEXT", "accounting_mode TEXT NOT NULL DEFAULT 'hard_cap'", "instruction_digest TEXT"):
        if column.split()[0] not in grant_columns:
            conn.execute("ALTER TABLE runway_revision_grants ADD COLUMN " + column)
    conn.execute("CREATE UNIQUE INDEX IF NOT EXISTS runway_revision_grants_current ON runway_revision_grants(source_review_id) WHERE status IN ('held_for_metering','released')")
    request_columns = {row[1] for row in conn.execute("PRAGMA table_info(runway_model_requests)")}
    if "request_digest" not in request_columns:
        # Older ledgers recorded request counts without a body digest. Mark
        # those receipts unverifiable so an old ID cannot admit another send.
        conn.execute("ALTER TABLE runway_model_requests ADD COLUMN request_digest TEXT NOT NULL DEFAULT 'legacy-unverified'")
    campaign_columns = {row[1] for row in conn.execute("PRAGMA table_info(runway_campaigns)")}
    if "mode" not in campaign_columns:
        conn.execute("ALTER TABLE runway_campaigns ADD COLUMN mode TEXT NOT NULL DEFAULT 'internal'")
    source_columns = {row[1] for row in conn.execute("PRAGMA table_info(runway_sources)")}
    if "captured_at" not in source_columns:
        conn.execute("ALTER TABLE runway_sources ADD COLUMN captured_at REAL")
    input_columns = {row[1] for row in conn.execute("PRAGMA table_info(runway_inputs)")}
    if "source_input_id" not in input_columns:
        conn.execute("ALTER TABLE runway_inputs ADD COLUMN source_input_id TEXT")
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


def require_fixture_ledger():
    root = Path(os.environ.get("HIRE_STATE", "/var/lib/plow/hire")).resolve()
    temp = Path(tempfile.gettempdir()).resolve()
    if os.environ.get("MARKETING_CAMPAIGN_FIXTURE") != "ISOLATED_TEST_ONLY" or root == temp or not root.is_relative_to(temp):
        raise ValueError("Fixture campaigns require an isolated disposable ledger")


def snapshot(conn, runway_id=None):
    project = conn.execute("SELECT * FROM runways WHERE id=?" if runway_id else
                           "SELECT * FROM runways WHERE scope!='employee_shift' ORDER BY created_at DESC LIMIT 1",
                           (runway_id,) if runway_id else ()).fetchone()
    if project is None:
        return None
    rid = project["id"]
    campaign = conn.execute("SELECT * FROM runway_campaigns WHERE runway_id=?", (rid,)).fetchone()
    return {"project": as_dict(project),
            "source_metadata": [as_dict(r) for r in conn.execute(
                "SELECT url,digest,captured_at FROM runway_sources WHERE runway_id=? ORDER BY url", (rid,))],
            "campaign": as_dict(campaign),
            "campaign_revisions": [as_dict(r) for r in conn.execute(
                "SELECT * FROM runway_campaign_revisions WHERE runway_id=? ORDER BY version", (rid,))],
            "campaign_actions": [as_dict(r) for r in conn.execute(
                "SELECT * FROM runway_campaign_actions WHERE runway_id=? ORDER BY version", (rid,))],
            "steps": [as_dict(r) for r in conn.execute("SELECT * FROM runway_steps WHERE runway_id=? ORDER BY ordinal", (rid,))],
            "artifacts": [as_dict(r) for r in conn.execute("SELECT * FROM runway_artifacts WHERE runway_id=? ORDER BY created_at", (rid,))],
            "inputs": [as_dict(r) for r in conn.execute("SELECT * FROM runway_inputs WHERE runway_id=? ORDER BY created_at", (rid,))],
            "reviews": [as_dict(r) for r in conn.execute("SELECT * FROM runway_reviews WHERE runway_id=? ORDER BY created_at", (rid,))],
            "revision_grants": [as_dict(r) for r in conn.execute("SELECT * FROM runway_revision_grants WHERE source_runway_id=? ORDER BY created_at", (rid,))],
            "executions": [as_dict(r) for r in conn.execute("SELECT * FROM runway_executions WHERE runway_id=? ORDER BY started_at", (rid,))],
            "terminal_receipts": [as_dict(r) for r in conn.execute("SELECT t.* FROM runway_terminal_receipts t JOIN runway_executions e ON e.id=t.execution_id WHERE e.runway_id=? ORDER BY t.recorded_at", (rid,))],
            "response_receipts": [as_dict(r) for r in conn.execute("SELECT t.* FROM runway_response_receipts t JOIN runway_model_requests m ON m.request_id=t.request_id JOIN runway_executions e ON e.id=m.execution_id WHERE e.runway_id=? ORDER BY t.recorded_at", (rid,))],
            "pilot_checkpoints": [as_dict(r) for r in conn.execute("SELECT * FROM runway_pilot_checkpoints WHERE runway_id=? ORDER BY created_at", (rid,))],
            "model_requests": [as_dict(r) for r in conn.execute("SELECT m.* FROM runway_model_requests m JOIN runway_executions e ON e.id=m.execution_id WHERE e.runway_id=? ORDER BY m.created_at,m.request_id", (rid,))]}


def list_projects():
    with connection() as conn:
        rows = conn.execute("""SELECT r.id,r.goal,r.status,r.created_at,r.updated_at,
                              (SELECT COUNT(*) FROM runway_artifacts a WHERE a.runway_id=r.id) AS artifact_count
                              FROM runways r WHERE r.scope!='employee_shift' ORDER BY r.created_at DESC,r.id DESC""").fetchall()
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


def save_campaign_brief(data):
    """Bind a marketing hypothesis to checked runway evidence without granting execution."""
    rid = require(data.get("id"), 32)
    artifact_id = require(data.get("source_artifact_id"), 32)
    digest = require(data.get("source_artifact_digest"), 64)
    request_id = require(data.get("request_id"), 120)
    actor = require(data.get("actor_id"), 100)
    if data.get("actor_owner") is not True:
        raise ValueError("Only the owner can set the campaign mandate")
    fixture = data.get("fixture") is True
    if fixture:
        require_fixture_ledger()
    if not all(re.fullmatch(r"[a-f0-9]{32}", item) for item in (rid, artifact_id)) or not re.fullmatch(r"[a-f0-9]{64}", digest):
        raise ValueError("Invalid campaign or evidence identity")
    project_version, version = data.get("project_version"), data.get("version")
    if type(project_version) is not int or project_version < 1 or type(version) is not int or version < 0:
        raise ValueError("Exact project and campaign versions are required")
    raw_brief, raw_experiment = data.get("brief"), data.get("experiment")
    if not isinstance(raw_brief, dict) or not isinstance(raw_experiment, dict):
        raise ValueError("Campaign brief and experiment rule are required")
    if "priority_rationale" not in raw_brief:
        # An exact retry of a pre-rationale brief keeps its original receipt.
        legacy_digest = hashlib.sha256(json.dumps({"id": rid, "source": artifact_id,
            "digest": digest, "mode": "fixture" if fixture else "internal",
            "brief": raw_brief, "experiment": raw_experiment},
            sort_keys=True, separators=(",", ":")).encode()).hexdigest()
        with connection() as conn:
            prior = conn.execute("SELECT * FROM runway_campaign_revisions WHERE request_id=?",
                                 (request_id,)).fetchone()
            campaign = conn.execute("SELECT mode FROM runway_campaigns WHERE runway_id=?",
                                    (rid,)).fetchone()
            if prior and campaign and prior["runway_id"] == rid and \
                    prior["version"] == version + 1 and prior["actor_id"] == actor and \
                    prior["source_artifact_id"] == artifact_id and \
                    prior["source_artifact_digest"] == digest and \
                    prior["payload_digest"] == legacy_digest and \
                    campaign["mode"] == ("fixture" if fixture else "internal") and \
                    json.loads(prior["brief_json"]) == raw_brief and \
                    json.loads(prior["experiment_json"]) == raw_experiment:
                return snapshot(conn, rid)
    brief_fields = ("audience", "problem", "hypothesis", "priority_rationale", "proposition", "desired_behavior",
                    "channel", "primary_metric", "metric_definition", "guardrail",
                    "review_timing", "non_goals")
    brief = {key: require(raw_brief.get(key), 600) for key in brief_fields}
    experiment_fields = ("intervention", "target_population", "observation_window", "metric_source")
    experiment = {key: require(raw_experiment.get(key), 600) for key in experiment_fields}
    rule = raw_experiment.get("decision_rule")
    if rule not in ("learning_only", "minimum_sample"):
        raise ValueError("Choose a learning-only or minimum-sample decision rule")
    sample = raw_experiment.get("minimum_sample")
    if type(sample) is not int or sample < (1 if rule == "minimum_sample" else 0) or sample > 1000000:
        raise ValueError("Invalid experiment sample requirement")
    if rule == "learning_only" and sample != 0:
        raise ValueError("Learning-only campaigns cannot imply a continuation threshold")
    experiment["decision_rule"] = rule
    experiment["minimum_sample"] = sample
    brief_json = json.dumps(brief, sort_keys=True, separators=(",", ":"))
    experiment_json = json.dumps(experiment, sort_keys=True, separators=(",", ":"))
    payload_digest = hashlib.sha256(json.dumps({"id": rid, "source": artifact_id, "digest": digest,
        "mode": "fixture" if fixture else "internal", "brief": brief, "experiment": experiment},
        sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        prior = conn.execute("SELECT * FROM runway_campaign_revisions WHERE request_id=?", (request_id,)).fetchone()
        if prior:
            if prior["runway_id"] != rid or prior["payload_digest"] != payload_digest or prior["actor_id"] != actor:
                raise ValueError("Request ID belongs to a different campaign edit")
            return snapshot(conn, rid)
        project = conn.execute("SELECT * FROM runways WHERE id=?", (rid,)).fetchone()
        if project is None or project["version"] != project_version or project["active_execution"]:
            raise ValueError("Project changed or has an unresolved execution")
        if project["status"] not in ("needs_review", "done"):
            raise ValueError("Wait for the bounded assignment to finish before changing campaign direction")
        source = conn.execute("SELECT * FROM runway_artifacts WHERE id=? AND runway_id=? AND kind='audience_note'",
                              (artifact_id, rid)).fetchone()
        if source is None or source["digest"] != digest:
            raise ValueError("Exact audience evidence is required")
        try:
            note = json.loads(source["content"])
            evidence = note["evidence"]
            if not isinstance(evidence, list) or len(evidence) < 2 or not all(
                isinstance(item, dict) and isinstance(item.get("sourceUrl"), str) and
                isinstance(item.get("quote"), str) and item["quote"].strip() for item in evidence):
                raise ValueError()
        except (ValueError, KeyError, TypeError):
            raise ValueError("Audience note lacks two inspectable source observations") from None
        saved_sources = {row[0]: row[1] for row in conn.execute(
            "SELECT url,content FROM runway_sources WHERE runway_id=?", (rid,))}
        if len({item["sourceUrl"] for item in evidence}) < 2 or any(
            item["sourceUrl"] not in saved_sources or item["quote"] not in saved_sources[item["sourceUrl"]]
            for item in evidence):
            raise ValueError("Audience observations must cite both checked project sources")
        current = conn.execute("SELECT * FROM runway_campaigns WHERE runway_id=?", (rid,)).fetchone()
        if (current["version"] if current else 0) != version:
            raise ValueError("Stale campaign version")
        if current and current["mode"] != ("fixture" if fixture else "internal"):
            raise ValueError("Fixture provenance cannot change")
        # A later brief edit keeps an explicitly adopted linked revision selected.
        asset = (conn.execute("SELECT * FROM runway_artifacts WHERE id=? AND digest=?",
            (current["asset_artifact_id"], current["asset_artifact_digest"])).fetchone()
            if current and current["asset_artifact_id"] else None)
        if asset is None:
            asset = conn.execute("SELECT * FROM runway_artifacts WHERE runway_id=? AND kind IN ('post_angles','revision_angles') ORDER BY created_at DESC LIMIT 1", (rid,)).fetchone()
        stage = "align" if asset else "create"
        next_version = version + 1
        conn.execute("INSERT INTO runway_campaigns(runway_id,version,stage,mode,owner_actor,source_artifact_id,source_artifact_digest,asset_artifact_id,asset_artifact_digest,brief_json,experiment_json,created_at,updated_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?) "
                     "ON CONFLICT(runway_id) DO UPDATE SET version=excluded.version,stage=excluded.stage,owner_actor=excluded.owner_actor,source_artifact_id=excluded.source_artifact_id,source_artifact_digest=excluded.source_artifact_digest,asset_artifact_id=excluded.asset_artifact_id,asset_artifact_digest=excluded.asset_artifact_digest,brief_json=excluded.brief_json,experiment_json=excluded.experiment_json,updated_at=excluded.updated_at",
                     (rid, next_version, stage, "fixture" if fixture else "internal", actor, artifact_id, digest, asset["id"] if asset else None,
                      asset["digest"] if asset else None, brief_json, experiment_json, now, now))
        conn.execute("INSERT INTO runway_campaign_revisions VALUES(?,?,?,?,?,?,?,?,?,?,?)",
                     (uuid.uuid4().hex, rid, next_version, request_id, payload_digest, actor,
                      artifact_id, digest, brief_json, experiment_json, now))
        record_event("checkpoint", "Campaign brief saved for owner review", {"runway_id": rid,
            "campaign_version": next_version, "stage": stage, "source_artifact_id": artifact_id}, conn)
        return snapshot(conn, rid)


def validated_observation(raw):
    """Validate counted observations without inferring missing history or causality."""
    if not isinstance(raw, dict):
        raise ValueError("Observation payload is required")
    payload = {key: require(raw.get(key), 500) for key in (
        "observation_id", "source", "timezone", "metric_definition", "attribution_limitations")}
    for key in ("captured_at", "period_start", "period_end"):
        value = raw.get(key)
        if type(value) not in (int, float) or not math.isfinite(value):
            raise ValueError("Finite observation timestamps are required")
        payload[key] = value
    if not payload["period_start"] < payload["period_end"] <= payload["captured_at"] <= time.time() + 300:
        raise ValueError("Invalid observation period or capture time")
    for key in ("numerator", "denominator"):
        value = raw.get(key)
        if type(value) is not int or value < 0 or value > 100000000:
            raise ValueError("Nonnegative integer observation counts are required")
        payload[key] = value
    if payload["numerator"] > payload["denominator"]:
        raise ValueError("Numerator cannot exceed denominator")
    if raw.get("value_type") not in ("actual", "estimated"):
        raise ValueError("Observation must say actual or estimated")
    payload["value_type"] = raw["value_type"]
    return payload


def campaign_observation(data):
    """Save owner-attested internal context without claiming a launch or outcome decision."""
    rid = require(data.get("id"), 32)
    request_id = require(data.get("request_id"), 120)
    actor = require(data.get("actor_id"), 100)
    if data.get("actor_owner") is not True or not re.fullmatch(r"[a-f0-9]{32}", rid):
        raise ValueError("An authenticated owner and valid campaign are required")
    expected, project_version = data.get("version"), data.get("project_version")
    if type(expected) is not int or expected < 1 or type(project_version) is not int or project_version < 1:
        raise ValueError("Exact project and campaign versions are required")
    raw = data.get("observation")
    payload = validated_observation(raw)
    payload["source_reference"] = require(raw.get("source_reference"), 500)
    payload["interpretation"] = require(raw.get("interpretation"), 1000)
    digest = hashlib.sha256(json.dumps({"id": rid, "observation": payload},
        sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        prior = conn.execute("SELECT * FROM runway_campaign_actions WHERE request_id=?", (request_id,)).fetchone()
        if prior:
            if (prior["runway_id"], prior["action"], prior["payload_digest"], prior["actor_id"]) != (
                    rid, "manual_observation", digest, actor):
                raise ValueError("Request ID belongs to a different campaign observation")
            return snapshot(conn, rid)
        project = conn.execute("SELECT * FROM runways WHERE id=?", (rid,)).fetchone()
        campaign = conn.execute("SELECT * FROM runway_campaigns WHERE runway_id=?", (rid,)).fetchone()
        if not project or project["version"] != project_version or project["active_execution"] or \
                project["status"] not in ("needs_review", "done"):
            raise ValueError("Project changed or is not at an owner review boundary")
        if not campaign or campaign["mode"] != "internal" or campaign["version"] != expected:
            raise ValueError("Internal campaign changed; refresh before recording")
        brief = json.loads(campaign["brief_json"])
        experiment = json.loads(campaign["experiment_json"])
        if payload["metric_definition"] != brief["metric_definition"] or payload["source"] != experiment["metric_source"]:
            raise ValueError("Observation does not match the preregistered metric and source")
        if conn.execute("SELECT 1 FROM runway_campaign_actions WHERE runway_id=? AND action IN ('manual_observation','measure') AND json_extract(payload_json,'$.observation_id')=?",
                        (rid, payload["observation_id"])).fetchone():
            raise ValueError("Observation ID was already imported")
        payload.update({"brief_revision": conn.execute("SELECT MAX(version) FROM runway_campaign_revisions WHERE runway_id=?",
                        (rid,)).fetchone()[0], "asset_id": campaign["asset_artifact_id"],
                        "asset_digest": campaign["asset_artifact_digest"],
                        "launch_receipt": None, "external_effect": False, "causality": "not_established"})
        next_version = expected + 1
        conn.execute("INSERT INTO runway_campaign_actions VALUES(?,?,?,?,?,?,?,?,?,?)",
                     (uuid.uuid4().hex, rid, next_version, request_id, digest, "manual_observation",
                      "owner_reported", actor, json.dumps(payload, sort_keys=True, separators=(",", ":")), now))
        # Fresh evidence reopens a past decision while retaining its history.
        conn.execute("UPDATE runway_campaigns SET version=?,stage='align' WHERE runway_id=?",
                     (next_version, rid))
        record_event("checkpoint", "Owner-reported observation saved without launch attribution",
                     {"runway_id": rid, "campaign_version": next_version}, conn)
        return snapshot(conn, rid)


def campaign_internal_action(data):
    """Record owner decisions, lessons, or capability requests without effects."""
    rid = require(data.get("id"), 32)
    request_id = require(data.get("request_id"), 120)
    actor = require(data.get("actor_id"), 100)
    action = data.get("action")
    if data.get("actor_owner") is not True or not re.fullmatch(r"[a-f0-9]{32}", rid) or \
            action not in ("internal_decision", "internal_lesson", "capability_request"):
        raise ValueError("Authenticated owner and internal action are required")
    project_version, campaign_version = data.get("project_version"), data.get("version")
    if type(project_version) is not int or project_version < 1 or \
            type(campaign_version) is not int or campaign_version < 1:
        raise ValueError("Exact project and campaign versions are required")
    raw = data.get("payload")
    if not isinstance(raw, dict):
        raise ValueError("An internal decision or lesson payload is required")
    if action == "internal_decision":
        decision = raw.get("decision")
        if decision not in ("continue", "revise", "pause", "stop", "collect_evidence"):
            raise ValueError("Choose a supported decision")
        ids = raw.get("observation_action_ids")
        if not isinstance(ids, list) or not ids or len(ids) > 100 or \
                any(not isinstance(item, str) or not re.fullmatch(r"[a-f0-9]{32}", item) for item in ids) or \
                len(set(ids)) != len(ids):
            raise ValueError("Exact owner observation action IDs are required")
        payload = {"decision": decision, "rationale": require(raw.get("rationale"), 1000),
                   "observation_action_ids": sorted(ids)}
    elif action == "internal_lesson":
        decision_id = require(raw.get("decision_id"), 32)
        if not re.fullmatch(r"[a-f0-9]{32}", decision_id):
            raise ValueError("An exact decision ID is required")
        payload = {key: require(raw.get(key), 1000) for key in (
            "lesson", "context", "uncertainty", "revisit_condition", "next_action")}
        payload["decision_id"] = decision_id
    else:
        if raw.get("cost_status") not in ("unknown", "zero", "estimated"):
            raise ValueError("Capability request cost status must be explicit")
        payload = {key: require(raw.get(key), 1000) for key in (
            "blocked_task", "required_scope", "expected_benefit", "cost_note")}
        payload["cost_status"] = raw["cost_status"]
    digest = hashlib.sha256(json.dumps({"id": rid, "action": action, "payload": payload},
        sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        prior = conn.execute("SELECT * FROM runway_campaign_actions WHERE request_id=?", (request_id,)).fetchone()
        if prior:
            if (prior["runway_id"], prior["action"], prior["payload_digest"], prior["actor_id"]) != (
                    rid, action, digest, actor):
                raise ValueError("Request ID belongs to a different internal action")
            return snapshot(conn, rid)
        project = conn.execute("SELECT * FROM runways WHERE id=?", (rid,)).fetchone()
        campaign = conn.execute("SELECT * FROM runway_campaigns WHERE runway_id=?", (rid,)).fetchone()
        if not project or project["version"] != project_version or project["active_execution"] or \
                project["status"] not in ("needs_review", "done"):
            raise ValueError("Project changed or has unresolved work")
        # owner_actor identifies the device session that wrote the brief. A later
        # authenticated owner device may decide; the host verifies the exact
        # brief receipt and supplies actor_owner from its session, not the body.
        if not campaign or campaign["version"] != campaign_version or campaign["mode"] != "internal":
            raise ValueError("An unchanged owner internal campaign is required")
        brief_revision = conn.execute("SELECT MAX(version) FROM runway_campaign_revisions WHERE runway_id=?",
                                      (rid,)).fetchone()[0]
        if action == "internal_decision":
            if campaign["stage"] != "align":
                raise ValueError("A new observation or brief is required before another decision")
            observations = []
            for action_id in payload["observation_action_ids"]:
                item = conn.execute("SELECT * FROM runway_campaign_actions WHERE id=? AND runway_id=? AND action='manual_observation'",
                                    (action_id, rid)).fetchone()
                if not item:
                    raise ValueError("Decision references an unavailable owner observation")
                value = json.loads(item["payload_json"])
                if value.get("brief_revision") != brief_revision or \
                        (value.get("asset_id"), value.get("asset_digest")) != (
                            campaign["asset_artifact_id"], campaign["asset_artifact_digest"]):
                    raise ValueError("Decision observation is stale for this brief or asset")
                observations.append((item, value))
            previous = conn.execute("SELECT * FROM runway_campaign_actions WHERE runway_id=? "
                "AND action='internal_decision' AND json_extract(payload_json,'$.brief_revision')=? "
                "ORDER BY version DESC LIMIT 1", (rid, brief_revision)).fetchone()
            if previous and json.loads(previous["payload_json"])["decision"] == "collect_evidence" and \
                    max(item["version"] for item, _ in observations) <= previous["version"]:
                raise ValueError("Wait for a new observation before deciding again")
            experiment = json.loads(campaign["experiment_json"])
            actual_sample = sum(value["denominator"] for _, value in observations
                                if value["value_type"] == "actual")
            insufficient = experiment["decision_rule"] == "minimum_sample" and \
                actual_sample < experiment["minimum_sample"]
            if insufficient and payload["decision"] != "collect_evidence":
                raise ValueError("Insufficient actual sample; collect evidence")
            if experiment["decision_rule"] == "learning_only" and payload["decision"] == "continue":
                raise ValueError("Learning-only brief has no continuation threshold")
            payload.update({"actual_sample": actual_sample,
                "required_sample": experiment["minimum_sample"], "inconclusive": insufficient,
                "brief_revision": brief_revision, "asset_id": campaign["asset_artifact_id"],
                "asset_digest": campaign["asset_artifact_digest"], "launch_receipt": None,
                "external_effect": False, "execution_granted": False,
                "causality": "not_established", "evidence_type": "owner_reported"})
            next_stage = "align" if payload["decision"] == "collect_evidence" else "learn"
        elif action == "internal_lesson":
            if campaign["stage"] != "learn":
                raise ValueError("A current internal decision must precede a proposed lesson")
            decision = conn.execute("SELECT * FROM runway_campaign_actions WHERE id=? AND runway_id=? AND action='internal_decision'",
                                    (payload["decision_id"], rid)).fetchone()
            latest = conn.execute("SELECT * FROM runway_campaign_actions WHERE runway_id=? AND action!='capability_request' ORDER BY version DESC LIMIT 1",
                                  (rid,)).fetchone()
            if not decision or not latest or latest["id"] != decision["id"] or \
                    json.loads(decision["payload_json"]).get("brief_revision") != brief_revision:
                raise ValueError("The exact current decision is required before learning")
            basis = json.loads(decision["payload_json"])
            payload.update({"brief_revision": brief_revision,
                "observation_action_ids": basis["observation_action_ids"],
                "asset_id": basis["asset_id"], "asset_digest": basis["asset_digest"],
                "evidence_type": "owner_reported", "causality": "not_established",
                "external_effect": False, "execution_granted": False})
            next_stage = "complete"
        else:
            payload.update({"brief_revision": brief_revision,
                "asset_id": campaign["asset_artifact_id"],
                "asset_digest": campaign["asset_artifact_digest"],
                "capability_granted": False, "purchase_authorized": False,
                "external_effect": False})
            next_stage = campaign["stage"]
        next_version = campaign_version + 1
        conn.execute("INSERT INTO runway_campaign_actions VALUES(?,?,?,?,?,?,?,?,?,?)",
                     (uuid.uuid4().hex, rid, next_version, request_id, digest, action,
                      "owner_reported_decision" if action == "internal_decision" else
                      "proposed_lesson" if action == "internal_lesson" else "request_only",
                      actor, json.dumps(payload, sort_keys=True, separators=(",", ":")), now))
        if action == "capability_request":
            conn.execute("UPDATE runway_campaigns SET version=? WHERE runway_id=?", (next_version, rid))
        else:
            conn.execute("UPDATE runway_campaigns SET version=?,stage=? WHERE runway_id=?",
                         (next_version, next_stage, rid))
        record_event("checkpoint", "Owner internal campaign " + action,
                     {"runway_id": rid, "campaign_version": next_version,
                      "external_effect": False}, conn)
        return snapshot(conn, rid)


def adopt_campaign_revision(data):
    """Select an approved linked draft for the internal campaign; no launch authority."""
    rid = require(data.get("id"), 32)
    request_id = require(data.get("request_id"), 120)
    actor = require(data.get("actor_id"), 100)
    revision_id = require(data.get("revision_runway_id"), 32)
    artifact_id = require(data.get("revision_artifact_id"), 32)
    digest = require(data.get("revision_artifact_digest"), 64)
    review_id = require(data.get("revision_review_id"), 32)
    if data.get("actor_owner") is not True or any(not re.fullmatch(r"[a-f0-9]{32}", value)
            for value in (rid, revision_id, artifact_id, review_id)) or not re.fullmatch(r"[a-f0-9]{64}", digest):
        raise ValueError("Authenticated owner and exact revision identities are required")
    project_version, campaign_version, revision_version = (data.get(key) for key in
        ("project_version", "version", "revision_project_version"))
    if any(type(value) is not int or value < 1 for value in
           (project_version, campaign_version, revision_version)):
        raise ValueError("Exact source, campaign, and revision versions are required")
    payload = {"revision_runway_id": revision_id, "revision_artifact_id": artifact_id,
               "revision_artifact_digest": digest, "revision_review_id": review_id}
    payload_digest = hashlib.sha256(json.dumps({"id": rid, **payload},
        sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        prior = conn.execute("SELECT * FROM runway_campaign_actions WHERE request_id=?", (request_id,)).fetchone()
        if prior:
            if (prior["runway_id"], prior["action"], prior["payload_digest"], prior["actor_id"]) != (
                    rid, "adopt_revision", payload_digest, actor):
                raise ValueError("Request ID belongs to a different campaign action")
            return snapshot(conn, rid)
        source = conn.execute("SELECT * FROM runways WHERE id=?", (rid,)).fetchone()
        campaign = conn.execute("SELECT * FROM runway_campaigns WHERE runway_id=?", (rid,)).fetchone()
        revision = conn.execute("SELECT * FROM runways WHERE id=?", (revision_id,)).fetchone()
        if not source or source["version"] != project_version or source["active_execution"] or \
                source["status"] not in ("needs_review", "done"):
            raise ValueError("Source assignment changed or has unresolved work")
        if campaign and campaign["mode"] == "fixture":
            require_fixture_ledger()
        if not campaign or campaign["version"] != campaign_version or campaign["mode"] not in ("internal", "fixture") or \
                campaign["stage"] != "align" or campaign["owner_actor"] != actor or not campaign["asset_artifact_id"]:
            raise ValueError("An unchanged owner internal campaign at alignment is required")
        if not revision or revision["version"] != revision_version or revision["status"] != "done" or \
                revision["active_execution"] or revision["scope"] != "internal_revision_draft" or \
                revision["source_runway_id"] != rid:
            raise ValueError("Revision is unfinished or belongs to another campaign")
        grant = conn.execute("SELECT * FROM runway_revision_grants WHERE released_runway_id=? AND source_runway_id=?",
                             (revision_id, rid)).fetchone()
        predecessor_review = conn.execute("SELECT * FROM runway_reviews WHERE id=? AND runway_id=?",
                                          (revision["source_review_id"], rid)).fetchone()
        if not grant or grant["status"] != "released" or \
                grant["source_review_id"] != revision["source_review_id"] or \
                not predecessor_review or predecessor_review["decision"] != "revision_requested" or \
                (predecessor_review["artifact_id"], predecessor_review["artifact_digest"]) != (
                    revision["source_artifact_id"], revision["source_artifact_digest"]):
            raise ValueError("Released grant and exact predecessor review are required")
        artifact = conn.execute("SELECT * FROM runway_artifacts WHERE id=? AND runway_id=? AND kind='revision_angles'",
                                (artifact_id, revision_id)).fetchone()
        approval = conn.execute("SELECT * FROM runway_reviews WHERE id=? AND runway_id=?",
                                (review_id, revision_id)).fetchone()
        if not artifact or artifact["digest"] != digest or not approval or approval["decision"] != "approved" or \
                (approval["artifact_id"], approval["artifact_digest"], approval["actor_id"]) != (
                    artifact_id, digest, actor):
            raise ValueError("Exact revised asset and owner approval are required")
        brief_version = conn.execute("SELECT MAX(version) FROM runway_campaign_revisions WHERE runway_id=?",
                                     (rid,)).fetchone()[0]
        selected = (campaign["asset_artifact_id"], campaign["asset_artifact_digest"])
        predecessor = (revision["source_artifact_id"], revision["source_artifact_digest"])
        if selected != predecessor:
            previous = conn.execute("SELECT payload_json FROM runway_campaign_actions "
                "WHERE runway_id=? AND action='adopt_revision' "
                "AND json_extract(payload_json,'$.revision_runway_id')=? "
                "AND json_extract(payload_json,'$.revision_artifact_id')=? "
                "ORDER BY version DESC LIMIT 1", (rid, revision_id, artifact_id)).fetchone()
            previous_brief = json.loads(previous["payload_json"]).get("brief_revision") if previous else None
            if selected != (artifact_id, digest) or type(previous_brief) is not int or \
                    previous_brief >= brief_version:
                raise ValueError("Revised asset is already selected for this brief or predecessor changed")
        payload.update({"predecessor_id": predecessor[0],
            "predecessor_digest": predecessor[1],
            "source_review_id": predecessor_review["id"], "brief_revision": brief_version,
            "external_effect": False, "launch_authorized": False})
        next_version = campaign_version + 1
        conn.execute("UPDATE runway_campaigns SET version=?,asset_artifact_id=?,asset_artifact_digest=?,updated_at=? WHERE runway_id=?",
                     (next_version, artifact_id, digest, now, rid))
        conn.execute("INSERT INTO runway_campaign_actions VALUES(?,?,?,?,?,?,?,?,?,?)",
                     (uuid.uuid4().hex, rid, next_version, request_id, payload_digest,
                      "adopt_revision", "owner_selected_internal", actor,
                      json.dumps(payload, sort_keys=True, separators=(",", ":")), now))
        record_event("checkpoint", "Owner selected approved linked revision for internal campaign",
                     {"runway_id": rid, "revision_runway_id": revision_id,
                      "revision_artifact_id": artifact_id, "campaign_version": next_version}, conn)
        return snapshot(conn, rid)


def campaign_action(data):
    """Advance a simulated campaign with exact versions and no external effect."""
    require_fixture_ledger()
    rid = require(data.get("id"), 32)
    request_id = require(data.get("request_id"), 120)
    actor = require(data.get("actor_id"), 100)
    if data.get("actor_owner") is not True or not re.fullmatch(r"[a-f0-9]{32}", rid):
        raise ValueError("Fixture owner and valid campaign ID required")
    expected = data.get("version")
    project_version = data.get("project_version")
    if type(expected) is not int or expected < 1 or type(project_version) is not int or project_version < 1:
        raise ValueError("Exact project and campaign versions are required")
    action = data.get("action")
    if action not in ("revise_asset", "align", "launch", "measure", "decide", "learn"):
        raise ValueError("Unknown fixture campaign action")
    raw = data.get("payload")
    if not isinstance(raw, dict):
        raise ValueError("Fixture action payload is required")
    payload = dict(raw)
    if action == "revise_asset":
        payload = {"review_id": require(raw.get("review_id"), 32),
                   "predecessor_id": require(raw.get("predecessor_id"), 32),
                   "predecessor_digest": require(raw.get("predecessor_digest"), 64),
                   "revision_note": require(raw.get("revision_note"), 1000)}
    elif action == "align":
        payload = {"review_id": require(raw.get("review_id"), 32),
                   "asset_id": require(raw.get("asset_id"), 32),
                   "asset_digest": require(raw.get("asset_digest"), 64)}
    elif action == "launch":
        if raw.get("destination") != "fixture://publisher" or raw.get("checklist") != {
            "asset": "checked", "link": "not_applicable", "tracking": "fixture_only",
            "destination": "fixture_only", "rollback": "fixture_reset"}:
            raise ValueError("Only the isolated fake publisher checklist is supported")
        payload = {"destination": "fixture://publisher", "checklist": raw["checklist"],
                   "receipt": "SIMULATED_ONLY", "external_effect": False}
    elif action == "measure":
        payload = validated_observation(raw)
    elif action == "decide":
        if raw.get("decision") not in ("continue", "revise", "pause", "stop", "collect_evidence"):
            raise ValueError("Unknown outcome decision")
        payload = {"decision": raw["decision"], "rationale": require(raw.get("rationale"), 1000)}
    elif action == "learn":
        payload = {key: require(raw.get(key), 1000) for key in (
            "lesson", "context", "uncertainty", "revisit_condition", "next_action")}
    digest = hashlib.sha256(json.dumps({"id": rid, "action": action, "payload": payload},
        sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        prior = conn.execute("SELECT * FROM runway_campaign_actions WHERE request_id=?", (request_id,)).fetchone()
        if prior:
            if (prior["runway_id"], prior["action"], prior["payload_digest"], prior["actor_id"]) != (rid, action, digest, actor):
                raise ValueError("Request ID belongs to a different campaign action")
            return snapshot(conn, rid)
        project = conn.execute("SELECT * FROM runways WHERE id=?", (rid,)).fetchone()
        campaign = conn.execute("SELECT * FROM runway_campaigns WHERE runway_id=?", (rid,)).fetchone()
        if not project or project["version"] != project_version or project["active_execution"]:
            raise ValueError("Project changed or has an unresolved execution")
        if not campaign or campaign["mode"] != "fixture" or campaign["version"] != expected:
            raise ValueError("Fixture campaign changed; refresh before acting")
        if project["status"] not in ("needs_review", "done"):
            raise ValueError("Campaign worker is not at a review boundary")
        brief_revision = conn.execute("SELECT MAX(version) FROM runway_campaign_revisions WHERE runway_id=?", (rid,)).fetchone()[0]
        prior_actions = [dict(row) for row in conn.execute(
            "SELECT * FROM runway_campaign_actions WHERE runway_id=? ORDER BY version", (rid,))
            if json.loads(row["payload_json"]).get("brief_revision") == brief_revision]
        last = prior_actions[-1] if prior_actions else None
        previous = json.loads(last["payload_json"]) if last else None
        stage = campaign["stage"]
        if action == "revise_asset":
            if stage != "align" or last:
                raise ValueError("Current brief is not waiting for its first fixture revision")
            if (payload["predecessor_id"], payload["predecessor_digest"]) != (
                    campaign["asset_artifact_id"], campaign["asset_artifact_digest"]):
                raise ValueError("Fixture predecessor changed")
            review = conn.execute("SELECT * FROM runway_reviews WHERE id=? AND runway_id=?",
                                  (payload["review_id"], rid)).fetchone()
            if not review or review["artifact_id"] != payload["predecessor_id"] or \
                    review["artifact_digest"] != payload["predecessor_digest"] or \
                    review["decision"] not in ("approved", "revision_requested"):
                raise ValueError("An exact predecessor review is required")
            if review["decision"] == "revision_requested" and review["created_at"] < campaign["updated_at"]:
                raise ValueError("Revision request predates this brief")
            if review["decision"] == "approved" and review["created_at"] >= campaign["updated_at"]:
                raise ValueError("The current approved draft needs no fixture revision")
            predecessor = conn.execute("SELECT * FROM runway_artifacts WHERE id=? AND runway_id=?",
                                       (payload["predecessor_id"], rid)).fetchone()
            if not predecessor or predecessor["digest"] != payload["predecessor_digest"]:
                raise ValueError("Fixture predecessor is unavailable")
            try:
                revised = json.loads(predecessor["content"])
                angles = revised["angles"]
                if not isinstance(angles, list) or len(angles) != 3 or not all(
                    isinstance(angle, dict) and all(isinstance(angle.get(key), str) and angle[key].strip()
                    for key in ("title", "hook", "sourceUrl", "why", "claimLimit")) for angle in angles):
                    raise ValueError()
                urls = json.loads(predecessor["source_urls"])
                saved_urls = {row[0] for row in conn.execute(
                    "SELECT url FROM runway_sources WHERE runway_id=?", (rid,))}
                if any(angle["sourceUrl"] not in urls or angle["sourceUrl"] not in saved_urls for angle in angles):
                    raise ValueError()
            except (ValueError, KeyError, TypeError):
                raise ValueError("Fixture predecessor does not contain three sourced angles") from None
            new_hook = "Ask what your marketing agent may do before work is approved"
            if angles[0]["hook"] == new_hook:
                raise ValueError("Fixture supports one deterministic asset revision per predecessor")
            angles[0]["hook"] = new_hook
            revised.update({"revisionOf": predecessor["id"], "revisionRequest": review["id"],
                            "fixtureOnly": True,
                            "qa": {"threeSourcedAngles": True,
                                   "outcomeClaimCheck": "not_assessed"},
                            "qualitativeReview": "Pending exact owner review; audience fit, clarity, product truth, channel, and action are not model-scored"})
            content = json.dumps(revised, sort_keys=True, separators=(",", ":"))
            asset_id = uuid.uuid4().hex
            asset_digest = hashlib.sha256(content.encode()).hexdigest()
            conn.execute("INSERT INTO runway_artifacts VALUES(?,?,?,?,?,?,?,?)",
                         (asset_id, rid, uuid.uuid4().hex, "revision_angles", content,
                          asset_digest, predecessor["source_urls"], now))
            payload.update({"asset_id": asset_id, "asset_digest": asset_digest,
                            "source_review_decision": review["decision"], "simulated": True})
            next_stage = "align"
        elif action == "align":
            if stage != "align" or (last and last["action"] != "revise_asset"):
                raise ValueError("Current brief is not waiting for alignment")
            if payload["asset_id"] != campaign["asset_artifact_id"] or payload["asset_digest"] != campaign["asset_artifact_digest"]:
                raise ValueError("Campaign asset changed")
            review = conn.execute("SELECT * FROM runway_reviews WHERE id=? AND runway_id=?", (payload["review_id"], rid)).fetchone()
            if not review or review["decision"] != "approved" or review["artifact_id"] != payload["asset_id"] or \
                    review["artifact_digest"] != payload["asset_digest"] or review["created_at"] < campaign["updated_at"]:
                raise ValueError("Current brief and exact asset need a fresh owner approval")
            next_stage = "launch"
            payload["review_actor"] = review["actor_id"]
        elif action == "launch":
            if stage != "launch" or not last or last["action"] != "align":
                raise ValueError("Fixture launch is not aligned")
            next_stage = "measure"
            payload["asset_id"] = campaign["asset_artifact_id"]
            payload["asset_digest"] = campaign["asset_artifact_digest"]
        elif action == "measure":
            if stage != "measure" or not any(item["action"] == "launch" for item in prior_actions):
                raise ValueError("Fixture has no simulated launch to measure")
            experiment = json.loads(campaign["experiment_json"])
            brief = json.loads(campaign["brief_json"])
            if payload["metric_definition"] != brief["metric_definition"] or payload["source"] != experiment["metric_source"]:
                raise ValueError("Observation does not match the preregistered metric")
            if conn.execute("SELECT 1 FROM runway_campaign_actions WHERE runway_id=? AND action='measure' AND json_extract(payload_json,'$.observation_id')=?",
                            (rid, payload["observation_id"])).fetchone():
                raise ValueError("Observation ID was already imported")
            next_stage = "measure"
        elif action == "decide":
            if stage != "measure" or not any(item["action"] == "measure" for item in prior_actions):
                raise ValueError("No measured outcome is ready for a decision")
            if last and last["action"] == "decide" and previous["decision"] == "collect_evidence":
                raise ValueError("Wait for a new observation before deciding again")
            experiment = json.loads(campaign["experiment_json"])
            observations = [json.loads(item["payload_json"]) for item in prior_actions if item["action"] == "measure"]
            actual_count = sum(item["denominator"] for item in observations if item["value_type"] == "actual")
            if experiment["decision_rule"] == "minimum_sample" and actual_count < experiment["minimum_sample"] and payload["decision"] != "collect_evidence":
                raise ValueError("Insufficient actual sample; collect evidence")
            if experiment["decision_rule"] == "learning_only" and payload["decision"] == "continue":
                raise ValueError("Learning-only brief has no continuation threshold")
            payload["actual_sample"] = actual_count
            payload["required_sample"] = experiment["minimum_sample"]
            payload["inconclusive"] = experiment["decision_rule"] == "minimum_sample" and actual_count < experiment["minimum_sample"]
            next_stage = "measure" if payload["decision"] == "collect_evidence" else "learn"
        else:
            if stage != "learn" or not last or last["action"] != "decide":
                raise ValueError("An outcome decision must precede a proposed lesson")
            next_stage = "complete"
            payload["decision_id"] = last["id"]
        payload["brief_revision"] = brief_revision
        next_version = expected + 1
        conn.execute("INSERT INTO runway_campaign_actions VALUES(?,?,?,?,?,?,?,?,?,?)",
                     (uuid.uuid4().hex, rid, next_version, request_id, digest, action,
                      "simulated" if action == "launch" else "recorded", actor,
                      json.dumps(payload, sort_keys=True, separators=(",", ":")), now))
        if action == "revise_asset":
            conn.execute("UPDATE runway_campaigns SET version=?,stage=?,asset_artifact_id=?,asset_artifact_digest=?,updated_at=? WHERE runway_id=?",
                         (next_version, next_stage, payload["asset_id"], payload["asset_digest"], now, rid))
        else:
            conn.execute("UPDATE runway_campaigns SET version=?,stage=?,updated_at=? WHERE runway_id=?",
                         (next_version, next_stage, now, rid))
        record_event("checkpoint", "Fixture campaign " + action, {"runway_id": rid,
            "campaign_version": next_version, "simulated": True}, conn)
        return snapshot(conn, rid)


def campaign_lessons(data):
    """Retrieve contextual fixture lessons without promoting them to policy."""
    require_fixture_ledger()
    audience = require(data.get("audience"), 600).casefold()
    excluded = data.get("exclude_campaign_id")
    if excluded is not None and (not isinstance(excluded, str) or
            not re.fullmatch(r"[a-f0-9]{32}", excluded)):
        raise ValueError("Invalid excluded campaign ID")
    with connection() as conn:
        rows = conn.execute("SELECT a.* FROM runway_campaign_actions a JOIN runway_campaigns c ON c.runway_id=a.runway_id WHERE a.action='learn' AND c.mode='fixture' ORDER BY a.created_at DESC").fetchall()
        lessons = []
        for row in rows:
            if row["runway_id"] == excluded:
                continue
            lesson = json.loads(row["payload_json"])
            revision = conn.execute("SELECT brief_json FROM runway_campaign_revisions WHERE runway_id=? AND version=?",
                                    (row["runway_id"], lesson["brief_revision"])).fetchone()
            if revision:
                brief = json.loads(revision["brief_json"])
                if audience in brief["audience"].casefold():
                    decision_row = conn.execute("SELECT * FROM runway_campaign_actions WHERE id=? AND runway_id=? AND action='decide' AND version<?",
                        (lesson.get("decision_id"), row["runway_id"], row["version"])).fetchone()
                    if not decision_row:
                        continue
                    decision = json.loads(decision_row["payload_json"])
                    if decision.get("brief_revision") != lesson["brief_revision"]:
                        continue
                    observations = []
                    for action in conn.execute("SELECT * FROM runway_campaign_actions WHERE runway_id=? AND action='measure' AND version<? ORDER BY version",
                            (row["runway_id"], decision_row["version"])):
                        value = json.loads(action["payload_json"])
                        if value.get("brief_revision") == lesson["brief_revision"]:
                            observations.append({"action_id": action["id"], "created_at": action["created_at"],
                                "source": value["source"], "captured_at": value["captured_at"],
                                "period_start": value["period_start"], "period_end": value["period_end"],
                                "timezone": value["timezone"], "metric_definition": value["metric_definition"],
                                "value_type": value["value_type"], "numerator": value["numerator"],
                                "denominator": value["denominator"],
                                "attribution_limitations": value["attribution_limitations"]})
                    if not observations:
                        continue
                    lessons.append({"campaign_id": row["runway_id"], "action_id": row["id"],
                                    "created_at": row["created_at"], "lesson": lesson,
                                    "brief": brief, "decision": decision, "observations": observations})
        return {"lessons": lessons}


def internal_campaign_lessons(data):
    """Find contextual owner-proposed learning without promoting it to policy."""
    audience = require(data.get("audience"), 600).casefold()
    excluded = data.get("exclude_campaign_id")
    if excluded is not None and (not isinstance(excluded, str) or
            not re.fullmatch(r"[a-f0-9]{32}", excluded)):
        raise ValueError("Invalid excluded campaign ID")
    with connection() as conn:
        rows = conn.execute("SELECT a.* FROM runway_campaign_actions a "
            "JOIN runway_campaigns c ON c.runway_id=a.runway_id "
            "WHERE a.action='internal_lesson' AND c.mode='internal' "
            "ORDER BY a.created_at DESC,a.id DESC").fetchall()
        lessons = []
        for row in rows:
            if row["runway_id"] == excluded:
                continue
            try:
                lesson = json.loads(row["payload_json"])
                if not isinstance(lesson, dict):
                    continue
                observation_ids = lesson["observation_action_ids"]
                if not isinstance(observation_ids, list) or not observation_ids or \
                        any(not isinstance(item, str) or not re.fullmatch(r"[a-f0-9]{32}", item)
                            for item in observation_ids):
                    continue
            except (ValueError, KeyError, TypeError):
                continue
            brief_row = conn.execute("SELECT * FROM runway_campaign_revisions "
                "WHERE runway_id=? AND version=?", (row["runway_id"],
                lesson.get("brief_revision"))).fetchone()
            if not brief_row:
                continue
            try:
                brief = json.loads(brief_row["brief_json"])
                if not isinstance(brief, dict):
                    continue
            except (ValueError, TypeError):
                continue
            if not isinstance(brief.get("audience"), str) or \
                    audience not in brief["audience"].casefold():
                continue
            decision_row = conn.execute("SELECT * FROM runway_campaign_actions "
                "WHERE id=? AND runway_id=? AND action='internal_decision'",
                (lesson.get("decision_id"), row["runway_id"])).fetchone()
            if not decision_row:
                continue
            try:
                decision = json.loads(decision_row["payload_json"])
                if not isinstance(decision, dict):
                    continue
            except (ValueError, TypeError):
                continue
            if decision.get("observation_action_ids") != observation_ids:
                continue
            observations = []
            for action_id in observation_ids:
                observation = conn.execute("SELECT * FROM runway_campaign_actions "
                    "WHERE id=? AND runway_id=? AND action='manual_observation'",
                    (action_id, row["runway_id"])).fetchone()
                if not observation:
                    break
                try:
                    value = json.loads(observation["payload_json"])
                    if not isinstance(value, dict):
                        break
                    observations.append({"action_id": action_id,
                        "request_id": observation["request_id"],
                        "actor_id": observation["actor_id"],
                        "payload_json": observation["payload_json"],
                        "source_reference": value["source_reference"],
                        "metric_definition": value["metric_definition"],
                        "period_start": value["period_start"], "period_end": value["period_end"],
                        "timezone": value["timezone"], "value_type": value["value_type"],
                        "numerator": value["numerator"], "denominator": value["denominator"],
                        "attribution_limitations": value["attribution_limitations"]})
                except (ValueError, KeyError, TypeError):
                    break
            if len(observations) != len(observation_ids):
                continue
            lessons.append({"campaign_id": row["runway_id"], "action_id": row["id"],
                "request_id": row["request_id"], "actor_id": row["actor_id"],
                "payload_json": row["payload_json"], "created_at": row["created_at"],
                "lesson": lesson, "brief": brief, "decision": decision,
                "brief_receipt": {"version": brief_row["version"],
                    "request_id": brief_row["request_id"],
                    "actor_id": brief_row["actor_id"],
                    "source_artifact_id": brief_row["source_artifact_id"],
                    "source_artifact_digest": brief_row["source_artifact_digest"],
                    "brief_json": brief_row["brief_json"],
                    "experiment_json": brief_row["experiment_json"]},
                "decision_receipt": {"action_id": decision_row["id"],
                    "request_id": decision_row["request_id"],
                    "actor_id": decision_row["actor_id"],
                    "payload_json": decision_row["payload_json"]},
                "observations": observations})
        return {"lessons": lessons}


def fixture_seed(data):
    """Save one deterministic packet without a model or network call."""
    require_fixture_ledger()
    owner = require(data.get("owner_actor"), 100)
    request_id = require(data.get("request_id"), 120)
    sources = [
        {"url": "fixture://source/founder-time", "content": "Fixture founder says marketing takes time."},
        {"url": "fixture://source/founder-controls", "content": "Fixture founder asks for clear controls."},
    ]
    urls = [item["url"] for item in sources]
    state = create({"request_id": request_id, "goal": "SIMULATED campaign: test an internal founder message",
        "owner_actor": owner, "profile_version": 1, "sources": sources, "fixture": True})
    if state["project"]["status"] != "ready" or state["artifacts"]:
        return state
    outputs = [
        {"audience": "Founder audience hypothesis", "problem": "Marketing time",
         "evidence": [{"sourceUrl": item["url"], "quote": item["content"],
                       "inference": "Fixture observation only"} for item in sources],
         "limitations": "Synthetic source; no market demand evidence"},
        {"angles": [
            {"title": "Clear controls", "hook": "Know what your marketing agent can do",
             "sourceUrl": urls[1], "why": "Control concern", "claimLimit": "No outcome claim"},
            {"title": "Save attention", "hook": "Review a bounded draft",
             "sourceUrl": urls[0], "why": "Time concern", "claimLimit": "No time saved claim"},
            {"title": "Inspect the work", "hook": "See evidence before acting",
             "sourceUrl": urls[1], "why": "Trust concern", "claimLimit": "No demand claim"}]},
        {"summary": "Three fixture draft angles are ready for owner review",
         "unsupportedClaims": ["Proven demand", "Guaranteed time saving"],
         "qualitativeReview": {"audienceFit": "Provisional founder fit from two comments only",
            "clarity": "Each angle has one concrete opening",
            "productTruth": "No outcome proof; hold back performance claims",
            "channelSuitability": "Internal draft only; no channel permission",
            "desiredAction": "Ask for owner review before requesting a demo"},
         "nextOwnerDecision": "Choose one internal angle", "recommendation": "Review the exact draft",
         "nextStepProposal": {"hypothesis": "Clear controls may fit founders", "evidenceGap": "No actual audience response",
                              "intendedAudience": "Founders (hypothesis)", "estimatedWork": "One owner review",
                              "continueOrStop": "continue", "reason": "Fixture learning exercise"}},
    ]
    for output in outputs:
        claimed = claim()
        if not claimed or claimed["project"]["id"] != state["project"]["id"]:
            raise ValueError("Fixture seed could not claim its isolated project")
        state = settle({"execution_id": claimed["execution_id"], "content": json.dumps(output),
            "source_urls": urls, "usage": {"totalTokens": 0}}, True)
    return state


def meter_active():
    """Return the one durable worker claim visible to the provider fetch guard."""
    with connection() as conn:
        rows = conn.execute("""SELECT p.active_execution,p.accounting_mode,p.deadline_at,
            e.started_at,p.max_active_seconds,
            (SELECT COALESCE(SUM(done.ended_at-done.started_at),0) FROM runway_executions done
             WHERE done.runway_id=p.id AND done.id!=p.active_execution AND done.ended_at IS NOT NULL) AS spent
            FROM runways p LEFT JOIN runway_executions e ON e.id=p.active_execution
            WHERE p.active_execution IS NOT NULL""").fetchall()
        if len(rows) > 1:
            raise ValueError("Concurrent runway executions need reconciliation")
        deadline = None
        if rows and rows[0][2] is not None and rows[0][3] is not None:
            deadline = min(rows[0][2], rows[0][3] + max(0, rows[0][4] - rows[0][5]))
        return {"execution_id": rows[0][0] if rows else None,
                "accounting_mode": rows[0][1] if rows else None, "deadline_at": deadline}


def create(data):
    request_id = require(data.get("request_id"), 120)
    goal = require(data.get("goal"), 1200)
    owner = require(data.get("owner_actor"), 100)
    mode = data.get("accounting_mode", "hard_cap")
    if mode not in ("hard_cap", "post_response"):
        raise ValueError("Unknown request accounting policy")
    if mode == "post_response" and (data.get("actor_owner") is not True or data.get("accept_post_response_accounting") is not True):
        raise ValueError("Post-response accounting requires an explicit owner grant")
    profile_version = data.get("profile_version")
    sources = data.get("sources")
    if not isinstance(profile_version, int) or profile_version < 1 or not isinstance(sources, list) or len(sources) != 2:
        raise ValueError("Profile version and exactly two checked sources are required")
    if len({s.get("url") for s in sources if isinstance(s, dict)}) != 2:
        raise ValueError("Sources must have distinct URLs")
    fixture = data.get("fixture") is True
    if fixture:
        require_fixture_ledger()
    for source in sources:
        url = source.get("url") if isinstance(source, dict) else None
        allowed = re.fullmatch(r"fixture://source/[a-z0-9-]+", url) if fixture and isinstance(url, str) else \
            re.fullmatch(r"https://news\.ycombinator\.com/item\?id=[0-9]{1,12}", url) if not fixture and isinstance(url, str) else None
        if not allowed:
            raise ValueError("Only restricted checked sources or isolated fixture sources are allowed")
        require(source.get("content"), 16000)
    now = time.time()
    rid = uuid.uuid4().hex
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        prior = conn.execute("SELECT id,goal,owner_actor,profile_version,accounting_mode FROM runways WHERE request_id=?", (request_id,)).fetchone()
        if prior:
            saved_urls = {r[0] for r in conn.execute("SELECT url FROM runway_sources WHERE runway_id=?", (prior["id"],))}
            if (prior["goal"] != goal or prior["owner_actor"] != owner or
                    prior["profile_version"] != profile_version or prior["accounting_mode"] != mode or saved_urls != {s["url"] for s in sources}):
                raise ValueError("Request ID belongs to a different project")
            return snapshot(conn, prior["id"])
        active = conn.execute("SELECT 1 FROM runways WHERE status IN ('running','ready','waiting','paused','unknown') LIMIT 1").fetchone()
        if active:
            raise ValueError("One active standing assignment is already enabled")
        conn.execute("INSERT INTO runways(id,request_id,goal,criteria,scope,scope_version,profile_version,deadline_at,owner_actor,max_runs,max_active_seconds,token_limit,reserve_per_run,status,version,created_at,updated_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                     (rid, request_id, goal, json.dumps(CRITERIA), "internal_research_draft", 1, profile_version,
                      now + 1800, owner, 6, 900, 150000, 25000, "ready", 1, now, now))
        conn.execute("UPDATE runways SET pilot_root_id=id WHERE id=?", (rid,))
        if mode == "post_response":
            conn.execute("""UPDATE runways SET accounting_mode='post_response',request_allowance=1,
                max_runs=3,max_model_requests=3,max_active_seconds=900,deadline_at=?,token_limit=75000 WHERE id=?""",
                         (now + 900, rid))
        for source in sources:
            content = source["content"].strip()
            conn.execute("INSERT INTO runway_sources(runway_id,url,content,digest,captured_at) VALUES(?,?,?,?,?)",
                         (rid, source["url"], content, hashlib.sha256(content.encode()).hexdigest(), now))
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


def claim(required_mode=None):
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        if conn.execute("SELECT 1 FROM runway_chat_claims WHERE status IN ('pending','unknown') LIMIT 1").fetchone():
            return None
        if conn.execute("SELECT 1 FROM runways WHERE active_execution IS NOT NULL LIMIT 1").fetchone():
            return None
        project = conn.execute("SELECT * FROM runways WHERE status IN ('ready','waiting') ORDER BY created_at LIMIT 1").fetchone()
        if project is None or project["active_execution"] or project["next_due"] and project["next_due"] > now:
            return None
        if required_mode is not None and project["accounting_mode"] != required_mode:
            return None
        rid = project["id"]
        if project["accounting_mode"] == "post_response":
            requests = conn.execute("SELECT status FROM runway_model_requests WHERE pilot_root_id=?", (project["pilot_root_id"],)).fetchall()
            if any(r[0] != "reported" for r in requests) or len(requests) >= project["request_allowance"]:
                conn.execute("UPDATE runways SET status='needs_review',wait_reason='Review observed model usage before more requests',version=version+1,updated_at=? WHERE id=?", (now, rid))
                return None
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
        profile = conn.execute("SELECT * FROM marketing_profile WHERE id='marketing'").fetchone()
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
        review = conn.execute("SELECT r.*,a.content AS target_content FROM runway_reviews r JOIN runway_artifacts a ON a.id=r.artifact_id WHERE r.step_id=? OR r.id=?",
                              (step["id"], project["source_review_id"])).fetchone()
        return {"execution_id": eid, "project": as_dict(project), "step": as_dict(step),
                "profile": as_dict(profile),
                "sources": [as_dict(r) for r in conn.execute("SELECT * FROM runway_sources WHERE runway_id=?", (rid,))],
                "artifacts": [as_dict(r) for r in conn.execute("SELECT * FROM runway_artifacts WHERE runway_id=? ORDER BY created_at", (rid,))],
                "inputs": [as_dict(r) for r in conn.execute("SELECT id,request_id,actor_id,actor_name,content,created_at,source_input_id FROM runway_inputs WHERE runway_id=? ORDER BY created_at DESC,id DESC LIMIT 8", (rid,))][::-1],
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
        if data.get("accounting_mode", "hard_cap") != project["accounting_mode"]:
            raise ValueError("Request accounting policy differs from the owner grant")
        root_id = project["pilot_root_id"] or project["id"]
        root = conn.execute("SELECT max_model_requests FROM runways WHERE id=?", (root_id,)).fetchone()
        if root is None:
            raise ValueError("Pilot root is missing")
        if conn.execute("SELECT 1 FROM runway_model_requests WHERE pilot_root_id=? AND status IN ('unknown','overrun') LIMIT 1", (root_id,)).fetchone():
            raise ValueError("Unresolved model request needs reconciliation")
        count, charged = conn.execute("SELECT COUNT(*),COALESCE(SUM(CASE WHEN reported_tokens IS NOT NULL THEN reported_tokens ELSE reserved_tokens END),0) FROM runway_model_requests WHERE pilot_root_id=?", (root_id,)).fetchone()
        project_count = conn.execute("SELECT COUNT(*) FROM runway_model_requests m JOIN runway_executions e ON e.id=m.execution_id WHERE e.runway_id=?", (project["id"],)).fetchone()[0]
        execution_charged = conn.execute("SELECT COALESCE(SUM(CASE WHEN reported_tokens IS NOT NULL THEN reported_tokens ELSE reserved_tokens END),0) FROM runway_model_requests WHERE execution_id=?", (execution_id,)).fetchone()[0]
        if count >= root["max_model_requests"] or project_count >= project["max_model_requests"]:
            raise ValueError("Pilot model request ceiling reached before dispatch")
        if project["accounting_mode"] == "post_response" and (count >= project["request_allowance"] or root_id != project["id"] or request_id != execution_id):
            raise ValueError("Post-response pilot requires its exact request and usage checkpoint")
        if charged + reserved > 250000:
            raise ValueError("Pilot token ceiling reached before dispatch")
        if project["accounting_mode"] == "post_response" and charged + reserved > project["token_limit"]:
            raise ValueError("Pilot observed-token admission allowance reached")
        if execution_charged + reserved > execution["reserved_tokens"]:
            raise ValueError("Execution token reservation reached before dispatch")
        conn.execute("INSERT INTO runway_model_requests(request_id,execution_id,pilot_root_id,request_digest,reserved_tokens,reported_tokens,status,created_at,ended_at) VALUES(?,?,?,?,?,NULL,'reserved',?,NULL)",
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
        response = data.get("response_receipt")
        if response is not None:
            if (not isinstance(response, dict) or data.get("request_digest") != row["request_digest"] or
                    not isinstance(response.get("terminal_type"), str) or len(response["terminal_type"]) > 60 or
                    not set(response) <= {"terminal_type", "provider_response_id", "input_tokens", "output_tokens", "evidence_digest", "http_status"}):
                raise ValueError("Response receipt does not match the reserved request")
            if status == "reported":
                counts = (response.get("input_tokens"), response.get("output_tokens"))
                if (response["terminal_type"] not in ("response.completed", "response.failed", "response.incomplete") or
                        any(type(n) is not int or n < 0 for n in counts) or sum(counts) != reported or
                        not re.fullmatch(r"[a-f0-9]{64}", str(response.get("evidence_digest", "")))):
                    raise ValueError("Provider response usage is incomplete or inconsistent")
            encoded = json.dumps(response, sort_keys=True, separators=(",", ":"))
            if len(encoded) > 1200:
                raise ValueError("Response receipt exceeds its metadata allowance")
            previous = conn.execute("SELECT response_json FROM runway_response_receipts WHERE request_id=?", (request_id,)).fetchone()
            if previous and previous[0] != encoded:
                raise ValueError("Provider response receipt cannot be replaced")
            if not previous:
                conn.execute("INSERT INTO runway_response_receipts VALUES(?,?,?,?)", (request_id, row["request_digest"], encoded, time.time()))
        if row["status"] == "reported" and status == "reported" and row["reported_tokens"] == reported:
            return {"request_id": request_id, "status": "reported"}
        if row["status"] == "unknown" and status == "unknown":
            return {"request_id": request_id, "status": "unknown"}
        # A host timeout can race the provider's terminal frame. Its first
        # digest-bound receipt may improve usage knowledge without releasing
        # the still-unknown execution or admitting another turn.
        late_receipt = row["status"] == "unknown" and status == "reported" and response is not None and not previous
        if row["status"] != "reserved" and not late_receipt:
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
            target = conn.execute("SELECT a.digest FROM runway_reviews r JOIN runway_artifacts a ON a.id=r.artifact_id WHERE r.step_id=? OR r.id=?",
                                  (step["id"], project["source_review_id"])).fetchone()
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
        elif project["accounting_mode"] == "post_response" and project["request_allowance"] == 1 and project["max_model_requests"] > 1 and conn.execute(
                "SELECT COUNT(*) FROM runway_model_requests WHERE pilot_root_id=?", (project["pilot_root_id"],)).fetchone()[0] >= project["request_allowance"]:
            new_status, reason = "needs_review", "First request finished. Review observed usage before releasing the remaining pilot requests."
        conn.execute("UPDATE runway_executions SET status=?,reported_tokens=?,usage_json=?,error=?,ended_at=?,artifact_id=? WHERE id=?",
                     ("succeeded" if success else "failed", usage["totalTokens"] if usage else None,
                      json.dumps(usage) if usage else None, None if success else data["error"], now, artifact_id, eid))
        conn.execute("UPDATE runways SET status=?,wait_reason=?,next_due=NULL WHERE id=?", (new_status, reason, project["id"]))
        if usage is None:
            conn.execute("UPDATE runways SET status='needs_review',wait_reason='Model usage missing; conservative reservation charged and review required' WHERE id=?", (project["id"],))
        return snapshot(conn, project["id"])


def continue_pilot(data):
    """A separate owner checkpoint opens the remaining two requests, never a new budget."""
    rid = require(data.get("id"), 32)
    request_id = require(data.get("request_id"), 120)
    owner = require(data.get("owner_actor"), 100)
    version = data.get("version")
    if data.get("actor_owner") is not True or type(version) is not int or data.get("usage_reviewed") is not True:
        raise ValueError("Owner review of actual usage and current version are required")
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        prior = conn.execute("SELECT * FROM runway_pilot_checkpoints WHERE request_id=?", (request_id,)).fetchone()
        if prior:
            if (prior["runway_id"], prior["owner_actor"], prior["source_version"]) != (rid, owner, version):
                raise ValueError("Request ID belongs to a different checkpoint")
            return snapshot(conn, rid)
        project = conn.execute("SELECT * FROM runways WHERE id=?", (rid,)).fetchone()
        if project is None or project["version"] != version:
            raise ValueError("Stale project version")
        if (project["accounting_mode"] != "post_response" or project["status"] != "needs_review" or
                project["active_execution"] or project["request_allowance"] != 1 or
                project["scope"] != "internal_research_draft" or project["max_model_requests"] != 3):
            raise ValueError("Pilot is not at its first-request checkpoint")
        if project["deadline_at"] is None or now >= project["deadline_at"]:
            raise ValueError("Pilot deadline has passed; this checkpoint cannot renew it")
        requests = conn.execute("SELECT * FROM runway_model_requests WHERE pilot_root_id=?", (rid,)).fetchall()
        if len(requests) != 1 or requests[0]["status"] != "reported" or requests[0]["reported_tokens"] is None:
            raise ValueError("One confirmed provider usage receipt is required")
        if not conn.execute("SELECT 1 FROM runway_response_receipts WHERE request_id=?", (requests[0]["request_id"],)).fetchone():
            raise ValueError("The provider's terminal usage receipt is required")
        if project["token_used"] + project["token_reserved"] + project["reserve_per_run"] > project["token_limit"]:
            raise ValueError("Observed usage leaves no further request allowance")
        conn.execute("INSERT INTO runway_pilot_checkpoints VALUES(?,?,?,?,?,?)", (request_id, rid, owner, version, requests[0]["reported_tokens"], now))
        conn.execute("UPDATE runways SET request_allowance=3,status='ready',wait_reason=NULL,version=version+1,updated_at=? WHERE id=?", (now, rid))
        record_event("checkpoint", "Owner reviewed first request usage; remaining pilot requests released", {"runway_id": rid, "request_id": request_id, "owner_actor": owner}, conn)
        return snapshot(conn, rid)


def inspect_model_request(data):
    request_id = require(data.get("request_id"), 120)
    with connection() as conn:
        request = conn.execute("SELECT * FROM runway_model_requests WHERE request_id=?", (request_id,)).fetchone()
        receipt = conn.execute("SELECT * FROM runway_response_receipts WHERE request_id=?", (request_id,)).fetchone()
        return {"request": as_dict(request), "response_receipt": as_dict(receipt)}


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
            conn.execute("UPDATE runways SET status='needs_review',wait_reason=?,next_due=NULL,version=version+1,updated_at=? WHERE id=?",
                         ("Revision request saved; execution awaits a metered model route and a fresh owner grant", now, rid))
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
    budget_mode = data.get("budget_mode", "same_pilot")
    if budget_mode not in ("same_pilot", "fresh_pilot"):
        raise ValueError("Revision budget mode must be same_pilot or fresh_pilot")
    accounting_mode = data.get("accounting_mode", "hard_cap")
    if accounting_mode not in ("hard_cap", "post_response"):
        raise ValueError("Unknown revision accounting policy")
    if accounting_mode == "post_response" and (data.get("accept_post_response_accounting") is not True or
            budget_mode != "fresh_pilot" or limits != {"max_runs": 1, "max_model_requests": 1, "token_limit": 25000, "max_active_seconds": 300}):
        raise ValueError("Post-response revision requires explicit acceptance of one request and its 25,000-token allowance")
    payload = {"id": rid, "version": version, "review_id": review_id, "artifact_id": artifact_id,
               "digest": digest, "request_id": request_id, "owner_actor": owner,
               "deadline_at": deadline, "scope": "internal_revision_draft", "budget_mode": budget_mode, **limits}
    if accounting_mode == "post_response": payload["accounting_mode"] = accounting_mode
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
        if accounting_mode == "post_response" and deadline > now + 900:
            raise ValueError("Post-response revision deadline must be within the next 15 minutes")
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
        conn.execute("UPDATE runway_revision_grants SET status='expired' WHERE source_review_id=? AND status='held_for_metering' AND deadline_at<=?", (review_id, now))
        if conn.execute("SELECT 1 FROM runway_revision_grants WHERE source_review_id=? AND status IN ('held_for_metering','released')", (review_id,)).fetchone():
            raise ValueError("This revision review already has a grant")
        old_root_id = project["pilot_root_id"] or rid
        root = conn.execute("SELECT * FROM runways WHERE id=?", (old_root_id,)).fetchone()
        if root is None:
            raise ValueError("Pilot root is missing")
        if budget_mode == "same_pilot":
            totals = conn.execute("SELECT COALESCE(SUM(run_count),0),COALESCE(SUM(token_used+token_reserved),0) FROM runways WHERE pilot_root_id=?", (old_root_id,)).fetchone()
            spent_seconds = conn.execute("SELECT COALESCE(SUM(e.ended_at-e.started_at),0) FROM runway_executions e JOIN runways r ON r.id=e.runway_id WHERE r.pilot_root_id=? AND e.ended_at IS NOT NULL", (old_root_id,)).fetchone()[0]
            if limits["max_runs"] > root["max_runs"] - totals[0] or limits["token_limit"] > root["token_limit"] - totals[1] or limits["max_active_seconds"] > root["max_active_seconds"] - spent_seconds:
                raise ValueError("Revision proposal exceeds the remaining recorded pilot allowance")
        elif limits["token_limit"] < 25000:
            raise ValueError("Fresh pilot needs at least one 25,000-token execution reservation")
        grant_id = uuid.uuid4().hex
        root_id = grant_id if budget_mode == "fresh_pilot" else old_root_id
        conn.execute("INSERT INTO runway_revision_grants(id,request_id,payload_digest,pilot_root_id,source_runway_id,source_runway_version,source_review_id,source_artifact_id,source_artifact_digest,profile_version,owner_actor,scope,max_runs,max_model_requests,token_limit,max_active_seconds,deadline_at,status,created_at,budget_mode) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                     (grant_id, request_id, payload_digest, root_id, rid, version, review_id,
                      artifact_id, digest, project["profile_version"], owner, "internal_revision_draft", limits["max_runs"],
                      limits["max_model_requests"], limits["token_limit"], limits["max_active_seconds"],
                      deadline, "held_for_metering", now, budget_mode))
        conn.execute("UPDATE runway_revision_grants SET accounting_mode=?,instruction_digest=? WHERE id=?",
                     (accounting_mode, hashlib.sha256(review_row["instruction"].encode()).hexdigest(), grant_id))
        record_event("checkpoint", "Bounded revision grant held for metering", {"runway_id": rid, "grant_id": grant_id, "review_id": review_id}, conn)
        return snapshot(conn, rid)


def release_revision_grant(data):
    """Create one linked assignment after a separately verified transport gate."""
    grant_id = require(data.get("grant_id"), 32)
    owner = require(data.get("owner_actor"), 100)
    if not re.fullmatch(r"[a-f0-9]{32}", grant_id) or data.get("actor_owner") is not True:
        raise ValueError("Exact owner authorization is required")
    if data.get("transport_ready") is not True:
        raise ValueError("A verified per-request transport is required")
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        grant = conn.execute("SELECT * FROM runway_revision_grants WHERE id=?", (grant_id,)).fetchone()
        if grant is None or grant["owner_actor"] != owner:
            raise ValueError("Revision grant is missing or belongs to another owner")
        if data.get("accounting_mode", "hard_cap") != grant["accounting_mode"]:
            raise ValueError("Transport accounting policy differs from the exact revision grant")
        if grant["status"] == "released" and grant["released_runway_id"]:
            return snapshot(conn, grant["released_runway_id"])
        if grant["status"] != "held_for_metering" or now >= grant["deadline_at"]:
            raise ValueError("Revision grant is unavailable or expired")
        source = conn.execute("SELECT * FROM runways WHERE id=?", (grant["source_runway_id"],)).fetchone()
        if source is None or source["version"] != grant["source_runway_version"] or source["status"] != "needs_review" or source["active_execution"]:
            raise ValueError("Source assignment changed since the owner grant")
        profile = conn.execute("SELECT version FROM marketing_profile WHERE id='marketing'").fetchone()
        if profile is None or profile["version"] != grant["profile_version"]:
            raise ValueError("Owner brief changed since the grant")
        review = conn.execute("SELECT * FROM runway_reviews WHERE id=? AND runway_id=?", (grant["source_review_id"], source["id"])).fetchone()
        artifact = conn.execute("SELECT * FROM runway_artifacts WHERE id=? AND runway_id=?", (grant["source_artifact_id"], source["id"])).fetchone()
        latest = conn.execute("SELECT id FROM runway_reviews WHERE runway_id=? ORDER BY created_at DESC,id DESC LIMIT 1", (source["id"],)).fetchone()
        if review is None or review["decision"] != "revision_requested" or review["step_id"] is not None or latest is None or latest["id"] != review["id"] or artifact is None or review["artifact_id"] != artifact["id"] or artifact["digest"] != grant["source_artifact_digest"] or review["artifact_digest"] != artifact["digest"]:
            raise ValueError("Exact saved revision instruction or artifact changed")
        if grant["instruction_digest"] is not None and hashlib.sha256(review["instruction"].encode()).hexdigest() != grant["instruction_digest"]:
            raise ValueError("Exact saved revision instruction changed")
        if conn.execute("SELECT 1 FROM runways WHERE status IN ('running','ready','waiting','paused','unknown') LIMIT 1").fetchone():
            raise ValueError("Another assignment is active or unresolved")
        if grant["budget_mode"] == "same_pilot":
            missing = conn.execute("SELECT 1 FROM runway_executions e JOIN runways r ON r.id=e.runway_id WHERE r.pilot_root_id=? AND e.status!='rejected' AND NOT EXISTS(SELECT 1 FROM runway_model_requests m WHERE m.execution_id=e.id) LIMIT 1", (grant["pilot_root_id"],)).fetchone()
            if missing:
                raise ValueError("Historical provider request count is unknown; authorize a fresh pilot")
            if conn.execute("SELECT 1 FROM runway_model_requests WHERE pilot_root_id=? AND status IN ('reserved','unknown','overrun') LIMIT 1", (grant["pilot_root_id"],)).fetchone():
                raise ValueError("Pilot has an unresolved provider request")
            root = conn.execute("SELECT * FROM runways WHERE id=?", (grant["pilot_root_id"],)).fetchone()
            totals = conn.execute("SELECT COALESCE(SUM(run_count),0),COALESCE(SUM(token_used+token_reserved),0) FROM runways WHERE pilot_root_id=?", (grant["pilot_root_id"],)).fetchone()
            spent_seconds = conn.execute("SELECT COALESCE(SUM(e.ended_at-e.started_at),0) FROM runway_executions e JOIN runways r ON r.id=e.runway_id WHERE r.pilot_root_id=? AND e.ended_at IS NOT NULL", (grant["pilot_root_id"],)).fetchone()[0]
            request_count = conn.execute("SELECT COUNT(*) FROM runway_model_requests WHERE pilot_root_id=?", (grant["pilot_root_id"],)).fetchone()[0]
            if root is None or totals[0] + grant["max_runs"] > root["max_runs"] or totals[1] + grant["token_limit"] > root["token_limit"] or request_count + grant["max_model_requests"] > root["max_model_requests"] or spent_seconds + grant["max_active_seconds"] > root["max_active_seconds"]:
                raise ValueError("Pilot allowance changed since the owner grant")
        elif grant["budget_mode"] != "fresh_pilot":
            raise ValueError("Unknown revision budget mode")
        rid = grant_id if grant["budget_mode"] == "fresh_pilot" else uuid.uuid4().hex
        title = "Revise saved marketing draft angles"
        task_id, step_id = uuid.uuid4().hex, uuid.uuid4().hex
        conn.execute("INSERT INTO runways(id,request_id,goal,criteria,scope,scope_version,profile_version,deadline_at,pilot_root_id,owner_actor,owner_input,max_runs,max_model_requests,max_active_seconds,token_limit,reserve_per_run,status,version,source_runway_id,source_review_id,source_artifact_id,source_artifact_digest,created_at,updated_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                     (rid, "revision:" + grant_id, title, json.dumps([REVISION_CRITERION]), "internal_revision_draft", 1,
                      grant["profile_version"], grant["deadline_at"], grant["pilot_root_id"], owner, review["instruction"],
                      grant["max_runs"], grant["max_model_requests"], grant["max_active_seconds"], grant["token_limit"],
                      25000, "ready", 1, source["id"], review["id"], artifact["id"], artifact["digest"], now, now))
        conn.execute("UPDATE runways SET accounting_mode=?,request_allowance=? WHERE id=?",
                     (grant["accounting_mode"], 1 if grant["accounting_mode"] == "post_response" else 0, rid))
        for row in conn.execute("SELECT url,content,digest,captured_at FROM runway_sources WHERE runway_id=?", (source["id"],)):
            conn.execute("INSERT INTO runway_sources(runway_id,url,content,digest,captured_at) VALUES(?,?,?,?,?)",
                         (rid, row["url"], row["content"], row["digest"], row["captured_at"]))
        source_inputs = conn.execute("SELECT id,actor_id,actor_name,content,created_at FROM runway_inputs "
            "WHERE runway_id=? AND created_at>=? ORDER BY created_at DESC,id DESC LIMIT 8",
            (source["id"], artifact["created_at"])).fetchall()
        for item in reversed(source_inputs):
            conn.execute("INSERT INTO runway_inputs(id,request_id,runway_id,actor_id,actor_name,content,created_at,source_input_id) VALUES(?,?,?,?,?,?,?,?)",
                         (uuid.uuid4().hex, "revision:" + grant_id + ":" + item["id"], rid,
                          item["actor_id"], item["actor_name"], item["content"], item["created_at"], item["id"]))
        conn.execute("INSERT INTO tasks VALUES(?,?,?,?,?,?,?,?,?,?)",
                     (task_id, title, "ready", "normal", REVISION_CRITERION, "agent_ready", None,
                      "agent:main:marketing-task-" + task_id, 1, int(now)))
        conn.execute("INSERT INTO runway_steps VALUES(?,?,?,?,?,?,?,?,?)",
                     (step_id, rid, 0, "revision_angles", task_id, 1, "ready", 0, None))
        conn.execute("UPDATE runway_revision_grants SET status='released',released_runway_id=? WHERE id=?", (rid, grant_id))
        record_event("checkpoint", "Linked marketing revision assignment released", {"runway_id": rid, "grant_id": grant_id, "source_artifact_id": artifact["id"]}, conn)
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
        conn.execute("INSERT INTO runway_inputs(id,request_id,runway_id,actor_id,actor_name,content,created_at,source_input_id) VALUES(?,?,?,?,?,?,?,NULL)",
                     (uuid.uuid4().hex, request_id, rid, actor_id, actor_name, content, now))
        conn.execute("UPDATE runways SET version=version+1,updated_at=?,"
                     "status=CASE WHEN status='waiting' AND ?=1 THEN 'ready' ELSE status END,"
                     "next_due=CASE WHEN status='waiting' AND ?=1 THEN NULL ELSE next_due END,"
                     "wait_reason=CASE WHEN status='waiting' AND ?=1 THEN NULL ELSE wait_reason END WHERE id=?",
                     (now, int(activate), int(activate), int(activate), rid))
        record_event("checkpoint", "Project input recorded", {"runway_id": rid, "actor_id": actor_id}, conn)
        return snapshot(conn, rid)


def hold_unknown_task(conn, execution, now):
    """Show a held execution as blocked work without overwriting a newer edit."""
    step = conn.execute("SELECT id,task_id,task_version FROM runway_steps WHERE id=?",
                        (execution["step_id"],)).fetchone()
    if step is None:
        return
    conn.execute("UPDATE runway_steps SET status='unknown' WHERE id=?", (step["id"],))
    task = conn.execute("SELECT version,status,action_state,blocker FROM tasks WHERE id=?",
                        (step["task_id"],)).fetchone()
    blocker = "Execution outcome unknown; reconcile before any retry."
    if task is None or task["version"] != step["task_version"]:
        return
    if (task["status"], task["action_state"], task["blocker"]) == ("paused", "blocked", blocker):
        return
    conn.execute("UPDATE tasks SET status='paused',action_state='blocked',blocker=?,version=version+1,updated_at=? WHERE id=?",
                 (blocker, int(now), step["task_id"]))
    conn.execute("UPDATE runway_steps SET task_version=task_version+1 WHERE id=?", (step["id"],))


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
        hold_unknown_task(conn, execution, now)
        record_event("checkpoint", "Marketing execution outcome unknown", {"runway_id": execution["runway_id"], "execution_id": eid}, conn)
        return snapshot(conn, execution["runway_id"])


def reconcile_terminal(data):
    """Release execution ownership from a persisted end, retaining unknown usage.

    Read the pinned Gateway audit store ourselves: caller-supplied status text,
    a timeout, or a missing session cannot manufacture a terminal receipt.
    """
    eid = require(data.get("execution_id"), 32)
    if not re.fullmatch(r"[a-f0-9]{32}", eid):
        raise ValueError("Invalid execution ID")
    digest = hashlib.sha256(eid.encode()).hexdigest()[:16]
    session_id = f"internal-session-effects-{eid}-{digest}"
    session_key = f"agent:runway-worker:internal-session-effects:{eid}-{digest}"
    audit_path = Path(os.environ.get("OPENCLAW_STATE_DIR", "/var/lib/plow")) / "state" / "openclaw.sqlite"
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        execution = conn.execute("SELECT * FROM runway_executions WHERE id=?", (eid,)).fetchone()
        if execution is None:
            raise ValueError("Unknown execution")
        prior = conn.execute("SELECT 1 FROM runway_terminal_receipts WHERE execution_id=?", (eid,)).fetchone()
        if prior:
            return snapshot(conn, execution["runway_id"])
        project = conn.execute("SELECT * FROM runways WHERE id=?", (execution["runway_id"],)).fetchone()
        if execution["status"] != "unknown" or project["active_execution"] != eid or project["status"] != "unknown":
            raise ValueError("Only the matching held execution can be reconciled")
        try:
            audit = sqlite3.connect(audit_path.resolve().as_uri() + "?mode=ro", uri=True)
            audit.row_factory = sqlite3.Row
            try:
                rows = audit.execute("""SELECT sequence,event_id,occurred_at,kind,action,status,
                    actor_type,actor_id,agent_id,session_key,session_id,run_id
                    FROM audit_events WHERE run_id=? ORDER BY sequence""", (eid,)).fetchall()
            finally:
                audit.close()
        except sqlite3.Error as error:
            raise ValueError("Gateway terminal evidence is unavailable") from error
        expected = ("agent", "runway-worker", "runway-worker", session_key, session_id, eid)
        valid = lambda row: tuple(row[k] for k in ("actor_type", "actor_id", "agent_id", "session_key", "session_id", "run_id")) == expected
        starts = [r for r in rows if r["action"] == "agent.run.started"]
        terminal = rows[-1] if rows else None
        if (len(starts) != 1 or not valid(starts[0]) or starts[0]["kind"] != "agent_run" or terminal is None or not valid(terminal) or
                terminal["kind"] != "agent_run" or terminal["action"] != "agent.run.finished" or terminal["status"] not in ("failed", "succeeded") or
                terminal["sequence"] <= starts[0]["sequence"] or
                not execution["started_at"] * 1000 <= starts[0]["occurred_at"] <= terminal["occurred_at"] <= now * 1000):
            raise ValueError("No unambiguous matching Gateway terminal receipt")
        evidence = json.dumps({"source": "openclaw.audit_events", "started": dict(starts[0]),
                               "terminal": dict(terminal)}, sort_keys=True, separators=(",", ":"))
        conn.execute("INSERT INTO runway_terminal_receipts VALUES(?,?,?,?,?)",
                     (eid, terminal["event_id"], evidence, hashlib.sha256(evidence.encode()).hexdigest(), now))
        # The butler may unlock the room after departure, but never erase the bill.
        reason = ("Gateway confirmed this run failed." if terminal["status"] == "failed" else
                  "Gateway completed this run, but the app could not verify its usage and deliverable.")
        reason += " Actual usage remains unknown; its reservation is retained. No automatic retry."
        conn.execute("UPDATE runway_executions SET status='failed',ended_at=? WHERE id=?",
                     (terminal["occurred_at"] / 1000, eid))
        conn.execute("UPDATE runway_model_requests SET status='unknown',ended_at=COALESCE(ended_at,?) WHERE execution_id=? AND status='reserved'",
                     (now, eid))
        conn.execute("UPDATE runways SET active_execution=NULL,status='needs_review',wait_reason=?,next_due=NULL,version=version+1,updated_at=? WHERE id=?",
                     (reason, now, project["id"]))
        step = conn.execute("SELECT * FROM runway_steps WHERE id=?", (execution["step_id"],)).fetchone()
        if step:
            task = conn.execute("SELECT version FROM tasks WHERE id=?", (step["task_id"],)).fetchone()
            if task and task["version"] == step["task_version"]:
                conn.execute("UPDATE runway_steps SET status='blocked',task_version=task_version+1 WHERE id=?", (step["id"],))
                conn.execute("UPDATE tasks SET status='needs_you',action_state='user_waiting',blocker=?,version=version+1,updated_at=? WHERE id=?",
                             (reason, int(now), step["task_id"]))
        record_event("checkpoint", "Gateway run ended; usage reservation retained",
                     {"runway_id": project["id"], "execution_id": eid, "audit_event_id": terminal["event_id"]}, conn)
        return snapshot(conn, project["id"])


def rejected(data):
    """Reopen a proven pre-dispatch rejection with no model request receipt."""
    eid = require(data.get("execution_id"), 32)
    code = data.get("gateway_code")
    message = require(data.get("gateway_message"), 500)
    meter_schema_failure = (code == "METER_PRE_DISPATCH_SCHEMA" and
        message == "table runway_model_requests has 8 columns but 9 values were supplied")
    if code != "INVALID_REQUEST" and not meter_schema_failure:
        raise ValueError("Only a proven pre-dispatch rejection can be reopened")
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
        if meter_schema_failure and (execution["status"] != "unknown" or
                                     execution["error"] != "OpenClaw returned no confirmed model usage."):
            raise ValueError("Meter schema reconciliation requires the matching held execution")
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
        conn.execute("UPDATE tasks SET status='ready',action_state='agent_ready',blocker=NULL,version=version+1,updated_at=? WHERE id=?", (int(now), step["task_id"]))
        record_event("checkpoint", "Marketing admission failed before inference", {"runway_id": project["id"], "execution_id": eid, "code": code}, conn)
        return snapshot(conn, project["id"])


def recover():
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        projects = conn.execute("SELECT id,status,active_execution FROM runways WHERE active_execution IS NOT NULL").fetchall()
        ids = [project["id"] for project in projects]
        for project in projects:
            now = time.time()
            execution = conn.execute("SELECT * FROM runway_executions WHERE id=?", (project["active_execution"],)).fetchone()
            if execution is not None:
                conn.execute("UPDATE runway_executions SET status='unknown',error=COALESCE(error,'Host restarted during an OpenClaw execution'),ended_at=COALESCE(ended_at,?) WHERE id=? AND status='running'",
                             (now, execution["id"]))
                hold_unknown_task(conn, execution, now)
            if project["status"] != "unknown":
                conn.execute("UPDATE runways SET status='unknown',wait_reason='Host restarted during an OpenClaw execution; reconcile original outcome before resuming',version=version+1,updated_at=? WHERE id=?", (now, project["id"]))
                record_event("checkpoint", "Marketing execution outcome unknown after restart", {"runway_id": project["id"]}, conn)
        chats = [r[0] for r in conn.execute("SELECT request_id FROM runway_chat_claims WHERE status='pending'")]
        for request_id in chats:
            conn.execute("UPDATE runway_chat_claims SET status='unknown',ended_at=? WHERE request_id=?",
                         (time.time(), request_id))
        requests = [r[0] for r in conn.execute("SELECT request_id FROM runway_model_requests WHERE status='reserved'")]
        for request_id in requests:
            conn.execute("UPDATE runway_model_requests SET status='unknown',ended_at=? WHERE request_id=?",
                         (time.time(), request_id))
        return {"unknown_runways": ids, "unknown_chats": chats, "unknown_model_requests": requests}


def usage_history():
    """One row per physical request, falling back only for older unmetered turns."""
    with connection() as conn:
        rows = conn.execute("""SELECT e.*,p.token_reserved AS held_tokens,
            m.request_id,m.reported_tokens AS request_tokens,m.status AS request_status,
            m.created_at AS requested_at,r.response_json
            FROM runway_executions e JOIN runways p ON p.id=e.runway_id
            LEFT JOIN runway_model_requests m ON m.execution_id=e.id
            LEFT JOIN runway_response_receipts r ON r.request_id=m.request_id
            WHERE e.status!='rejected' AND NOT EXISTS (
              SELECT 1 FROM runway_sources s WHERE (s.runway_id=p.id OR s.runway_id=p.pilot_root_id)
              AND s.url LIKE 'fixture://%')
            ORDER BY e.started_at""").fetchall()
        events = []
        for row in rows:
            physical = row["request_id"] is not None
            total = row["request_tokens"] if physical else row["reported_tokens"]
            response = json.loads(row["response_json"]) if row["response_json"] else {}
            events.append({"id": "worker:" + (row["request_id"] or row["id"]),
                "kind": "autonomous", "source": "provider_receipt" if physical else "legacy_turn",
                "createdAt": row["requested_at"] if physical else row["started_at"],
                "totalTokens": total, "inputTokens": response.get("input_tokens"),
                "outputTokens": response.get("output_tokens"),
                "status": "reported" if total is not None else "unknown"})
        # A retained execution reservation is an allowance hold, never consumption.
        held = sum({row["runway_id"]: row["held_tokens"] for row in rows}.values())
        return {"events": events, "reservedTokens": held}


SHIFT_SCOPE = "employee_shift"
SHIFT_RESERVE = 25000


def shift_open(data):
    """An owner-granted shift: a bounded number of metered worker turns before a deadline."""
    request_id = require(data.get("request_id"), 120)
    owner = require(data.get("owner_actor"), 100)
    turns, tokens, deadline = data.get("turn_limit"), data.get("token_limit"), data.get("deadline_at")
    if data.get("actor_owner") is not True or data.get("accept_post_response_accounting") is not True:
        raise ValueError("A shift needs an explicit owner grant for post-response accounting")
    # The owner sets the real cap (10M tokens a day by default); this is the outer bound a grant may carry.
    if type(turns) is not int or not 1 <= turns <= 2000 or type(tokens) is not int or not SHIFT_RESERVE <= tokens <= 20_000_000:
        raise ValueError("A shift needs 1-2000 turns and a token limit of 25,000-20,000,000")
    now = time.time()
    if not isinstance(deadline, (int, float)) or not now < deadline <= now + 86400:
        raise ValueError("A shift deadline must fall within the next 24 hours")
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        prior = conn.execute("SELECT * FROM runways WHERE request_id=?", (request_id,)).fetchone()
        if prior:
            if prior["scope"] != SHIFT_SCOPE:
                raise ValueError("Request ID belongs to a different grant")
            return as_dict(prior)
        if conn.execute("SELECT 1 FROM runways WHERE status IN ('running','ready','waiting','paused','unknown','shift_idle') LIMIT 1").fetchone():
            raise ValueError("Another assignment or shift is active; finish it first")
        profile = conn.execute("SELECT version FROM marketing_profile WHERE id='marketing'").fetchone()
        rid = uuid.uuid4().hex
        conn.execute("INSERT INTO runways(id,request_id,goal,criteria,scope,scope_version,profile_version,deadline_at,owner_actor,max_runs,max_model_requests,max_active_seconds,token_limit,reserve_per_run,status,version,created_at,updated_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                     (rid, request_id, "Employee shift", "[]", SHIFT_SCOPE, 1, profile[0] if profile else 1, deadline, owner,
                      turns, turns, int(deadline - now), tokens, SHIFT_RESERVE, "shift_idle", 1, now, now))
        conn.execute("UPDATE runways SET pilot_root_id=id,accounting_mode='post_response',request_allowance=? WHERE id=?", (turns, rid))
        record_event("checkpoint", "Employee shift granted", {"runway_id": rid, "turns": turns, "token_limit": tokens}, conn)
        return as_dict(conn.execute("SELECT * FROM runways WHERE id=?", (rid,)).fetchone())


def shift_claim(data):
    """Claim one metered worker turn for a shift, or explain why not."""
    rid = require(data.get("runway_id"), 32)
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        project = conn.execute("SELECT * FROM runways WHERE id=?", (rid,)).fetchone()
        if project is None or project["scope"] != SHIFT_SCOPE:
            raise ValueError("That shift grant does not exist")
        if project["status"] != "shift_idle":
            raise ValueError("The shift grant is " + project["status"] + "; reconcile before another turn")
        if conn.execute("SELECT 1 FROM runway_chat_claims WHERE status IN ('pending','unknown') LIMIT 1").fetchone():
            raise ValueError("A chat turn is unresolved; the shift waits")
        if conn.execute("SELECT 1 FROM runways WHERE active_execution IS NOT NULL LIMIT 1").fetchone():
            raise ValueError("Another metered turn is running; the shift waits")
        if now >= project["deadline_at"]:
            conn.execute("UPDATE runways SET status='completed',wait_reason='Shift deadline reached',version=version+1,updated_at=? WHERE id=?", (now, rid))
            raise ValueError("The shift deadline has passed")
        requests = conn.execute("SELECT status FROM runway_model_requests WHERE pilot_root_id=?", (rid,)).fetchall()
        if any(r[0] not in ("reported",) for r in requests):
            raise ValueError("A model request is unresolved; reconcile before another turn")
        if len(requests) >= project["request_allowance"] or project["run_count"] >= project["max_runs"]:
            raise ValueError("The shift's turn allowance is used")
        if project["token_used"] + project["token_reserved"] + SHIFT_RESERVE > project["token_limit"]:
            raise ValueError("The shift's token allowance is used")
        eid, step_id = uuid.uuid4().hex, uuid.uuid4().hex
        conn.execute("INSERT INTO runway_steps VALUES(?,?,?,?,?,?,?,?,?)", (step_id, rid, project["run_count"], "shift_turn", "shift-" + eid, 1, "running", 1, None))
        conn.execute("INSERT INTO runway_executions(id,runway_id,step_id,status,reserved_tokens,started_at) VALUES(?,?,?,'running',?,?)", (eid, rid, step_id, SHIFT_RESERVE, now))
        conn.execute("UPDATE runways SET status='running',active_execution=?,token_reserved=token_reserved+?,run_count=run_count+1,version=version+1,updated_at=? WHERE id=?",
                     (eid, SHIFT_RESERVE, now, rid))
        return {"execution_id": eid, "runway_id": rid, "deadline_at": project["deadline_at"], "turn": project["run_count"] + 1,
                "turn_limit": project["request_allowance"], "token_used": project["token_used"], "token_limit": project["token_limit"]}


def shift_settle(data):
    """Close one shift turn. Unknown outcomes stop the shift until reconciled."""
    eid = require(data.get("execution_id"), 32)
    status = data.get("status")
    if status not in ("succeeded", "failed", "unknown"):
        raise ValueError("A shift turn settles as succeeded, failed or unknown")
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        execution = conn.execute("SELECT * FROM runway_executions WHERE id=?", (eid,)).fetchone()
        if execution is None:
            raise ValueError("Unknown shift turn")
        project = conn.execute("SELECT * FROM runways WHERE id=?", (execution["runway_id"],)).fetchone()
        if project is None or project["scope"] != SHIFT_SCOPE:
            raise ValueError("That turn is not part of a shift")
        if execution["status"] != "running":
            return {"execution_id": eid, "status": execution["status"], "token_used": project["token_used"]}
        pending = conn.execute("SELECT 1 FROM runway_model_requests WHERE execution_id=? AND status IN ('reserved','unknown','overrun') LIMIT 1", (eid,)).fetchone()
        if pending and status != "unknown":
            status = "unknown"
        reported = conn.execute("SELECT COALESCE(SUM(reported_tokens),0),COUNT(*) FROM runway_model_requests WHERE execution_id=?", (eid,)).fetchone()
        billed = reported[0] if status != "unknown" else execution["reserved_tokens"]
        error = data.get("error")
        conn.execute("UPDATE runway_executions SET status=?,reported_tokens=?,error=?,ended_at=? WHERE id=?",
                     (status, reported[0] if reported[1] else None, str(error)[:500] if error else None, now, eid))
        conn.execute("UPDATE runway_steps SET status=? WHERE id=?", ("done" if status == "succeeded" else status, execution["step_id"]))
        conn.execute("UPDATE runways SET status=?,active_execution=NULL,token_reserved=token_reserved-?,token_used=token_used+?,wait_reason=?,version=version+1,updated_at=? WHERE id=?",
                     ("unknown" if status == "unknown" else "shift_idle", execution["reserved_tokens"], billed,
                      "A shift turn's outcome is unknown; reconcile before resuming" if status == "unknown" else None, now, project["id"]))
        return {"execution_id": eid, "status": status, "tokens": billed, "token_used": project["token_used"] + billed}


def shift_close(data):
    """Close a shift grant by its request ID. A shift that never went live has no grant."""
    request_id = require(data.get("request_id"), 120)
    now = time.time()
    with connection() as conn:
        conn.execute("BEGIN IMMEDIATE")
        project = conn.execute("SELECT * FROM runways WHERE request_id=?", (request_id,)).fetchone()
        if project is None:
            return {"status": "none"}
        rid = project["id"]
        if project["scope"] != SHIFT_SCOPE:
            raise ValueError("That request ID is not a shift grant")
        if project["active_execution"]:
            raise ValueError("A shift turn is still running")
        if project["status"] == "shift_idle":
            conn.execute("UPDATE runways SET status='completed',wait_reason='Shift ended',version=version+1,updated_at=? WHERE id=?", (now, rid))
        record_event("checkpoint", "Employee shift closed", {"runway_id": rid, "token_used": project["token_used"], "turns": project["run_count"]}, conn)
        return as_dict(conn.execute("SELECT * FROM runways WHERE id=?", (rid,)).fetchone())


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=("create", "status", "list", "inspect", "campaign-brief", "campaign-observation", "campaign-internal-action", "campaign-internal-lessons", "campaign-adopt-revision", "campaign-action", "campaign-lessons", "fixture-seed", "meter-active", "usage-history", "claim", "claim-post-response", "chat-claim", "chat-finish", "chat-reconcile", "model-reserve", "model-finish", "model-inspect", "continue-pilot", "finish", "fail", "unknown", "rejected", "terminal-reconcile", "input", "review", "prepare-revision-grant", "release-revision-grant", "pause", "resume", "recover", "shift-open", "shift-claim", "shift-settle", "shift-close"))
    args = parser.parse_args()
    data = read_input() if args.action in ("create", "inspect", "campaign-brief", "campaign-observation", "campaign-internal-action", "campaign-internal-lessons", "campaign-adopt-revision", "campaign-action", "campaign-lessons", "fixture-seed", "chat-claim", "chat-finish", "chat-reconcile", "model-reserve", "model-finish", "model-inspect", "continue-pilot", "finish", "fail", "unknown", "rejected", "terminal-reconcile", "input", "review", "prepare-revision-grant", "release-revision-grant", "pause", "resume", "shift-open", "shift-claim", "shift-settle", "shift-close") else {}
    if args.action == "create": result = create(data)
    elif args.action == "status":
        with connection() as conn: result = snapshot(conn)
    elif args.action == "list": result = list_projects()
    elif args.action == "inspect": result = inspect(data)
    elif args.action == "campaign-brief": result = save_campaign_brief(data)
    elif args.action == "campaign-observation": result = campaign_observation(data)
    elif args.action == "campaign-internal-action": result = campaign_internal_action(data)
    elif args.action == "campaign-internal-lessons": result = internal_campaign_lessons(data)
    elif args.action == "campaign-adopt-revision": result = adopt_campaign_revision(data)
    elif args.action == "campaign-action": result = campaign_action(data)
    elif args.action == "campaign-lessons": result = campaign_lessons(data)
    elif args.action == "fixture-seed": result = fixture_seed(data)
    elif args.action == "meter-active": result = meter_active()
    elif args.action == "usage-history": result = usage_history()
    elif args.action == "claim": result = claim()
    elif args.action == "claim-post-response": result = claim("post_response")
    elif args.action == "chat-claim": result = claim_chat(data)
    elif args.action == "chat-finish": result = finish_chat(data)
    elif args.action == "chat-reconcile": result = reconcile_chat(data)
    elif args.action == "model-reserve": result = reserve_model_request(data)
    elif args.action == "model-finish": result = finish_model_request(data)
    elif args.action == "model-inspect": result = inspect_model_request(data)
    elif args.action == "continue-pilot": result = continue_pilot(data)
    elif args.action == "finish": result = settle(data, True)
    elif args.action == "fail": result = settle(data, False)
    elif args.action == "unknown": result = unknown(data)
    elif args.action == "rejected": result = rejected(data)
    elif args.action == "terminal-reconcile": result = reconcile_terminal(data)
    elif args.action == "input": result = add_input(data)
    elif args.action == "review": result = review(data)
    elif args.action == "prepare-revision-grant": result = prepare_revision_grant(data)
    elif args.action == "release-revision-grant": result = release_revision_grant(data)
    elif args.action in ("pause", "resume"): result = change(data, args.action)
    elif args.action == "shift-open": result = shift_open(data)
    elif args.action == "shift-claim": result = shift_claim(data)
    elif args.action == "shift-settle": result = shift_settle(data)
    elif args.action == "shift-close": result = shift_close(data)
    else: result = recover()
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    try: main()
    except (ValueError, sqlite3.Error, KeyError, json.JSONDecodeError) as exc:
        print(str(exc), file=sys.stderr)
        sys.exit(2)
