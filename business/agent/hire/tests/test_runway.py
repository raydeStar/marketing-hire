"""Synthetic ledger fixtures: no model, network, or real owner data."""
import os
import json
import subprocess
import tempfile
import unittest
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import sys
from threading import Barrier
import time
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "bin"))
import runway


class RunwayLedgerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.prior = os.environ.get("HIRE_STATE")
        os.environ["HIRE_STATE"] = self.temp.name
        self.data = {"request_id": "fixture-create", "goal": "Synthetic bounded campaign packet",
                     "owner_actor": "owner-fixture", "profile_version": 1,
                     "sources": [{"url": "https://news.ycombinator.com/item?id=111", "content": "First checked fixture source."},
                                 {"url": "https://news.ycombinator.com/item?id=222", "content": "Second checked fixture source."}]}

    def tearDown(self):
        if hasattr(self, "fixture_prior"):
            if self.fixture_prior is None: os.environ.pop("MARKETING_CAMPAIGN_FIXTURE", None)
            else: os.environ["MARKETING_CAMPAIGN_FIXTURE"] = self.fixture_prior
        if self.prior is None: os.environ.pop("HIRE_STATE", None)
        else: os.environ["HIRE_STATE"] = self.prior
        self.temp.cleanup()

    def finish(self, claim, usage=100):
        return runway.settle({"execution_id": claim["execution_id"], "content": "Fixture validated deliverable",
                              "source_urls": [self.data["sources"][0]["url"]],
                              "usage": {"totalTokens": usage}}, True)

    def fixture_campaign(self, minimum_sample=3):
        self.fixture_prior = os.environ.get("MARKETING_CAMPAIGN_FIXTURE")
        os.environ["MARKETING_CAMPAIGN_FIXTURE"] = "ISOLATED_TEST_ONLY"
        created = runway.create(self.data)
        urls = [source["url"] for source in self.data["sources"]]
        note = {"audience": "Founders", "problem": "Marketing time",
                "evidence": [{"sourceUrl": url, "quote": source["content"]} for url, source in zip(urls, self.data["sources"])],
                "limitations": "Anecdotes only"}
        claim = runway.claim()
        saved = runway.settle({"execution_id": claim["execution_id"], "content": json.dumps(note),
            "source_urls": urls, "usage": {"totalTokens": 100}}, True)
        for _ in range(2): saved = self.finish(runway.claim())
        brief = {key: "Fixture statement" for key in ("audience", "problem", "hypothesis",
            "proposition", "desired_behavior", "channel", "primary_metric", "metric_definition", "guardrail",
            "review_timing", "non_goals")}
        brief["audience"] = "Founders"
        experiment = {"intervention": "Fixture draft", "target_population": "Founders",
            "observation_window": "Seven days", "metric_source": "Fixture observation",
            "decision_rule": "minimum_sample", "minimum_sample": minimum_sample}
        source = saved["artifacts"][0]
        payload = {"id": created["project"]["id"], "project_version": saved["project"]["version"],
            "version": 0, "request_id": "fixture-brief", "actor_id": "owner-fixture", "actor_owner": True,
            "fixture": True, "source_artifact_id": source["id"], "source_artifact_digest": source["digest"],
            "brief": brief, "experiment": experiment}
        return runway.save_campaign_brief(payload), payload

    def test_fixture_revision_keeps_predecessor_and_requires_fresh_review(self):
        self.fixture_prior = os.environ.get("MARKETING_CAMPAIGN_FIXTURE")
        os.environ["MARKETING_CAMPAIGN_FIXTURE"] = "ISOLATED_TEST_ONLY"
        state = runway.fixture_seed({"request_id": "revision-seed", "owner_actor": "owner-fixture"})
        source, asset = state["artifacts"][:2]
        rid = state["project"]["id"]
        brief = {key: "Fixture statement" for key in ("audience", "problem", "hypothesis",
            "proposition", "desired_behavior", "channel", "primary_metric", "metric_definition",
            "guardrail", "review_timing", "non_goals")}
        brief["audience"] = "Founders"
        state = runway.save_campaign_brief({"id": rid, "project_version": state["project"]["version"],
            "version": 0, "request_id": "revision-brief", "actor_id": "owner-fixture", "actor_owner": True,
            "fixture": True, "source_artifact_id": source["id"], "source_artifact_digest": source["digest"],
            "brief": brief, "experiment": {"intervention": "Fixture draft", "target_population": "Founders",
                "observation_window": "Seven days", "metric_source": "Fixture observation",
                "decision_rule": "learning_only", "minimum_sample": 0}})
        state = runway.review({"id": rid, "version": state["project"]["version"],
            "request_id": "revision-request", "artifact_id": asset["id"], "digest": asset["digest"],
            "decision": "revision_requested", "instruction": "Make the first hook more specific.",
            "actor_id": "owner-fixture", "actor_name": "Fixture owner", "actor_owner": True})
        revision = {"id": rid, "project_version": state["project"]["version"],
            "version": state["campaign"]["version"], "request_id": "fixture-asset-revision",
            "actor_id": "owner-fixture", "actor_owner": True, "action": "revise_asset",
            "payload": {"review_id": state["reviews"][-1]["id"], "predecessor_id": asset["id"],
                        "predecessor_digest": asset["digest"], "revision_note": "Specific first hook"}}
        with self.assertRaisesRegex(ValueError, "Fixture owner"):
            runway.campaign_action({**revision, "actor_owner": False})
        with self.assertRaisesRegex(ValueError, "predecessor changed"):
            runway.campaign_action({**revision, "payload": {**revision["payload"],
                "predecessor_digest": "0" * 64}})
        state = runway.campaign_action(revision)
        revised = state["artifacts"][-1]
        self.assertEqual(state["campaign"]["asset_artifact_id"], revised["id"])
        self.assertEqual(json.loads(revised["content"])["revisionOf"], asset["id"])
        self.assertEqual(len(runway.campaign_action(revision)["artifacts"]), 4)
        self.assertEqual(state["project"]["token_used"], 0)
        with self.assertRaisesRegex(ValueError, "Campaign asset changed"):
            runway.campaign_action({**revision, "request_id": "old-align", "version": state["campaign"]["version"],
                "action": "align", "payload": {"review_id": revision["payload"]["review_id"],
                    "asset_id": asset["id"], "asset_digest": asset["digest"]}})
        state = runway.review({"id": rid, "version": state["project"]["version"],
            "request_id": "revised-approval", "artifact_id": revised["id"], "digest": revised["digest"],
            "decision": "approved", "actor_id": "owner-fixture", "actor_name": "Fixture owner",
            "actor_owner": True})
        state = runway.campaign_action({"id": rid, "project_version": state["project"]["version"],
            "version": state["campaign"]["version"], "request_id": "revised-align",
            "actor_id": "owner-fixture", "actor_owner": True, "action": "align",
            "payload": {"review_id": state["reviews"][-1]["id"], "asset_id": revised["id"],
                        "asset_digest": revised["digest"]}})
        self.assertEqual(state["campaign"]["stage"], "launch")
        self.assertEqual(state["artifacts"][1]["id"], asset["id"])

    def test_fixture_campaign_launch_measure_decide_and_learn(self):
        state, brief_payload = self.fixture_campaign()
        with self.assertRaisesRegex(ValueError, "isolated disposable ledger"):
            with patch.dict(os.environ, {"MARKETING_CAMPAIGN_FIXTURE": ""}):
                runway.campaign_action({"id": state["project"]["id"]})
        asset = state["artifacts"][1]
        base = {"id": state["project"]["id"], "actor_id": "owner-fixture", "actor_owner": True}
        def act(action, payload, request_id):
            nonlocal state
            state = runway.campaign_action({**base, "project_version": state["project"]["version"],
                "version": state["campaign"]["version"], "request_id": request_id,
                "action": action, "payload": payload})
            return state
        with self.assertRaisesRegex(ValueError, "fresh owner approval"):
            act("align", {"review_id": "0"*32, "asset_id": asset["id"], "asset_digest": asset["digest"]}, "early-align")
        state = runway.review({"id": base["id"], "version": state["project"]["version"],
            "request_id": "fixture-asset-review", "artifact_id": asset["id"], "digest": asset["digest"],
            "decision": "approved", "actor_id": "owner-fixture", "actor_name": "Fixture owner", "actor_owner": True})
        aligned = act("align", {"review_id": state["reviews"][-1]["id"],
            "asset_id": asset["id"], "asset_digest": asset["digest"]}, "align-1")
        self.assertEqual(aligned["campaign"]["stage"], "launch")
        checklist = {"asset": "checked", "link": "not_applicable", "tracking": "fixture_only",
            "destination": "fixture_only", "rollback": "fixture_reset"}
        with self.assertRaisesRegex(ValueError, "fake publisher"):
            act("launch", {"destination": "https://example.com", "checklist": checklist}, "live-denied")
        launched = act("launch", {"destination": "fixture://publisher", "checklist": checklist}, "launch-1")
        self.assertEqual(launched["campaign"]["stage"], "measure")
        self.assertEqual(json.loads(launched["campaign_actions"][-1]["payload_json"])["receipt"], "SIMULATED_ONLY")
        now = time.time()
        observation = {"observation_id": "obs-1", "source": "Fixture observation",
            "captured_at": now, "period_start": now-3600, "period_end": now-30,
            "timezone": "America/Denver", "metric_definition": "Fixture statement",
            "attribution_limitations": "No causal inference", "numerator": 1, "denominator": 2,
            "value_type": "actual"}
        measured = act("measure", observation, "measure-1")
        self.assertEqual(measured["campaign"]["stage"], "measure")
        with self.assertRaisesRegex(ValueError, "already imported"):
            act("measure", {**observation, "numerator": 2}, "duplicate-observation")
        with self.assertRaisesRegex(ValueError, "Insufficient actual sample"):
            act("decide", {"decision": "continue", "rationale": "Too early"}, "early-decision")
        waiting = act("decide", {"decision": "collect_evidence", "rationale": "Two actual observations"}, "wait-decision")
        self.assertEqual(waiting["campaign"]["stage"], "measure")
        self.assertTrue(json.loads(waiting["campaign_actions"][-1]["payload_json"])["inconclusive"])
        with self.assertRaisesRegex(ValueError, "Wait for a new observation"):
            act("decide", {"decision": "collect_evidence", "rationale": "Nothing changed"}, "duplicate-wait")
        act("measure", {**observation, "observation_id": "obs-2", "numerator": 0, "denominator": 1}, "measure-2")
        decided = act("decide", {"decision": "pause", "rationale": "Enough observations to review; no causal claim"}, "decision-2")
        self.assertEqual(decided["campaign"]["stage"], "learn")
        learned = act("learn", {"lesson": "Specific framing may be clearer", "context": "Founders; one fixture channel",
            "uncertainty": "Small synthetic sample", "revisit_condition": "New real evidence arrives",
            "next_action": "Keep paused"}, "lesson-1")
        self.assertEqual(learned["campaign"]["stage"], "complete")
        self.assertEqual(learned["project"]["run_count"], 3)
        self.assertEqual(len(runway.campaign_lessons({"audience": "founders"})["lessons"]), 1)
        self.assertEqual(runway.campaign_lessons({"audience": "different audience"})["lessons"], [])
        edited = runway.save_campaign_brief({**brief_payload, "request_id": "new-brief", "version": learned["campaign"]["version"],
            "project_version": learned["project"]["version"],
            "brief": {**brief_payload["brief"], "audience": "New founder segment"}})
        self.assertEqual(edited["campaign"]["stage"], "align")
        self.assertEqual(runway.campaign_lessons({"audience": "founders"})["lessons"][0]["brief"]["audience"], "Founders")
        state = edited
        with self.assertRaisesRegex(ValueError, "fresh owner approval"):
            act("align", {"review_id": state["reviews"][-1]["id"],
                "asset_id": asset["id"], "asset_digest": asset["digest"]}, "stale-align")

    def test_three_dependent_steps_and_quiet_review(self):
        created = runway.create(self.data)
        self.assertEqual(created["project"]["status"], "ready")
        self.assertEqual(runway.create(self.data)["project"]["id"], created["project"]["id"])
        for ordinal in range(3):
            claim = runway.claim()
            self.assertEqual(claim["step"]["ordinal"], ordinal)
            self.assertIsNone(runway.claim())
            settled = self.finish(claim)
            self.assertEqual(settled["steps"][ordinal]["status"], "done")
        self.assertEqual(settled["project"]["status"], "needs_review")
        self.assertEqual(settled["project"]["token_used"], 300)
        self.assertEqual(len(settled["artifacts"]), 3)
        self.assertIsNone(runway.claim())

    def test_existing_source_and_campaign_rows_survive_additive_migration(self):
        legacy = runway.db()
        legacy.execute("CREATE TABLE runway_sources(runway_id TEXT NOT NULL,url TEXT NOT NULL,content TEXT NOT NULL,digest TEXT NOT NULL,PRIMARY KEY(runway_id,url))")
        legacy.execute("INSERT INTO runway_sources VALUES(?,?,?,?)", ("f"*32,"https://news.ycombinator.com/item?id=111","Old source","d"*64))
        legacy.execute("CREATE TABLE runway_campaigns(runway_id TEXT PRIMARY KEY,version INTEGER NOT NULL,stage TEXT NOT NULL,owner_actor TEXT NOT NULL,source_artifact_id TEXT NOT NULL,source_artifact_digest TEXT NOT NULL,asset_artifact_id TEXT,asset_artifact_digest TEXT,brief_json TEXT NOT NULL,experiment_json TEXT NOT NULL,created_at REAL NOT NULL,updated_at REAL NOT NULL)")
        legacy.execute("INSERT INTO runway_campaigns VALUES(?,?,?,?,?,?,?,?,?,?,?,?)",
            ("f"*32,1,"align","owner-fixture","a"*32,"d"*64,None,None,"{}","{}",1.0,1.0))
        legacy.execute("CREATE TABLE runway_inputs(id TEXT PRIMARY KEY,request_id TEXT UNIQUE NOT NULL,runway_id TEXT NOT NULL,actor_id TEXT NOT NULL,actor_name TEXT NOT NULL,content TEXT NOT NULL,created_at REAL NOT NULL)")
        legacy.execute("INSERT INTO runway_inputs VALUES(?,?,?,?,?,?,?)",
            ("b"*32,"old-input","f"*32,"old-actor","Earlier collaborator","Keep the claim narrow",1.0))
        legacy.close()
        with runway.connection() as migrated:
            source = migrated.execute("SELECT content,captured_at FROM runway_sources WHERE runway_id=?", ("f"*32,)).fetchone()
            campaign = migrated.execute("SELECT version,mode FROM runway_campaigns WHERE runway_id=?", ("f"*32,)).fetchone()
            old_input = migrated.execute("SELECT actor_id,content,source_input_id FROM runway_inputs WHERE id=?", ("b"*32,)).fetchone()
            self.assertEqual(source["content"], "Old source")
            self.assertIsNone(source["captured_at"])
            self.assertEqual((campaign["version"],campaign["mode"]), (1,"internal"))
            self.assertEqual((old_input["actor_id"],old_input["content"],old_input["source_input_id"]),
                             ("old-actor","Keep the claim narrow",None))

    def test_previous_assignment_and_artifacts_remain_inspectable(self):
        first = runway.create(self.data)
        for _ in range(3):
            first = self.finish(runway.claim())
        second = runway.create({**self.data, "request_id": "fixture-second-assignment",
                                "goal": "A second internal learning packet"})
        projects = runway.list_projects()["projects"]
        self.assertEqual([row["id"] for row in projects],
                         [second["project"]["id"], first["project"]["id"]])
        self.assertEqual(projects[1]["artifact_count"], 3)
        inspected = runway.inspect({"id": first["project"]["id"]})
        self.assertEqual([item["id"] for item in inspected["artifacts"]],
                         [item["id"] for item in first["artifacts"]])
        self.assertEqual(runway.inspect({"id": second["project"]["id"]})["artifacts"], [])
        with self.assertRaisesRegex(ValueError, "Invalid project ID"):
            runway.inspect({"id": "../owner-private"})

    def test_campaign_brief_binds_checked_evidence_without_granting_work(self):
        created = runway.create(self.data)
        urls = [source["url"] for source in self.data["sources"]]
        note = {"audience": "Founders (hypothesis)", "problem": "Marketing attention",
                "evidence": [{"sourceUrl": urls[0], "quote": "First checked fixture source.", "inference": "One comment"},
                             {"sourceUrl": urls[1], "quote": "Second checked fixture source.", "inference": "Another comment"}],
                "limitations": "Not validated demand"}
        first = runway.claim()
        saved = runway.settle({"execution_id": first["execution_id"], "content": json.dumps(note),
            "source_urls": urls, "usage": {"totalTokens": 100}}, True)
        with self.assertRaisesRegex(ValueError, "Wait for the bounded assignment"):
            runway.save_campaign_brief({"id": created["project"]["id"], "project_version": saved["project"]["version"],
                "version": 0, "request_id": "early-brief", "actor_id": "owner-fixture", "actor_owner": True,
                "source_artifact_id": saved["artifacts"][0]["id"],
                "source_artifact_digest": saved["artifacts"][0]["digest"],
                "brief": {key: "Fixture statement" for key in ("audience", "problem", "hypothesis",
                    "proposition", "desired_behavior", "channel", "primary_metric", "metric_definition", "guardrail",
                    "review_timing", "non_goals")},
                "experiment": {"intervention": "Fixture draft", "target_population": "Fixture audience",
                    "observation_window": "One week", "metric_source": "Manual fixture",
                    "decision_rule": "learning_only", "minimum_sample": 0}})
        for _ in range(2):
            saved = self.finish(runway.claim())
        source = saved["artifacts"][0]
        brief = {"audience": "Founders (provisional)", "problem": "Marketing attention",
                 "hypothesis": "A small reviewed draft may clarify positioning", "proposition": "Configurable marketing agent",
                 "desired_behavior": "Request an explanation", "channel": "Owner-reviewed social draft",
                 "primary_metric": "Qualified replies", "metric_definition": "Count distinct relevant replies",
                 "guardrail": "No product performance claim",
                 "review_timing": "At owner review; no calendar date set",
                 "non_goals": "No new channels or unverified product claims"}
        experiment = {"intervention": "One approved draft", "target_population": "Founder audience hypothesis",
                      "observation_window": "Seven days, America/Denver", "metric_source": "Manual owner observation",
                      "decision_rule": "learning_only", "minimum_sample": 0}
        payload = {"id": saved["project"]["id"], "project_version": saved["project"]["version"],
                   "version": 0, "request_id": "campaign-brief-fixture", "actor_id": "owner-fixture", "actor_owner": True,
                   "source_artifact_id": source["id"], "source_artifact_digest": source["digest"],
                   "brief": brief, "experiment": experiment}
        for missing in ("review_timing", "non_goals"):
            with self.assertRaisesRegex(ValueError, "Expected nonempty"):
                runway.save_campaign_brief({**payload, "brief": {key: value for key, value in brief.items() if key != missing}})
        with self.assertRaisesRegex(ValueError, "Only the owner"):
            runway.save_campaign_brief({**payload, "actor_owner": False})
        with self.assertRaisesRegex(ValueError, "Project changed"):
            runway.save_campaign_brief({**payload, "project_version": payload["project_version"] - 1})
        campaign = runway.save_campaign_brief(payload)
        self.assertEqual(campaign["campaign"]["stage"], "align")
        self.assertEqual(campaign["campaign"]["version"], 1)
        self.assertEqual(campaign["campaign"]["asset_artifact_id"], saved["artifacts"][1]["id"])
        self.assertEqual(campaign["project"]["run_count"], 3)
        self.assertEqual(runway.save_campaign_brief(payload)["campaign"]["version"], 1)
        with self.assertRaisesRegex(ValueError, "Stale campaign version"):
            runway.save_campaign_brief({**payload, "request_id": "stale-campaign"})
        revised = runway.save_campaign_brief({**payload, "version": 1, "request_id": "campaign-edit-fixture",
            "brief": {**brief, "hypothesis": "A more specific message may clarify positioning"}})
        self.assertEqual(revised["campaign"]["version"], 2)
        self.assertEqual(len(revised["campaign_revisions"]), 2)
        self.assertIsNone(runway.claim())
        now = time.time()
        observation = {"observation_id": "owner-observation-1", "source": experiment["metric_source"],
            "source_reference": "Owner notebook entry 1", "interpretation": "A contextual signal, not a campaign result",
            "captured_at": now, "period_start": now - 7200, "period_end": now - 3600,
            "timezone": "America/Denver", "metric_definition": brief["metric_definition"],
            "attribution_limitations": "No campaign launch or control group", "numerator": 1,
            "denominator": 2, "value_type": "actual"}
        observation_request = {"id": revised["project"]["id"], "project_version": revised["project"]["version"],
            "version": revised["campaign"]["version"], "request_id": "owner-observation-save",
            "actor_id": "owner-fixture", "actor_owner": True, "observation": observation}
        with self.assertRaisesRegex(ValueError, "authenticated owner"):
            runway.campaign_observation({**observation_request, "actor_owner": False})
        with self.assertRaisesRegex(ValueError, "Numerator"):
            runway.campaign_observation({**observation_request, "observation": {**observation,
                "numerator": 3}})
        observed = runway.campaign_observation(observation_request)
        self.assertEqual(observed["campaign"]["stage"], "align")
        self.assertEqual(observed["campaign"]["updated_at"], revised["campaign"]["updated_at"])
        self.assertEqual(observed["project"]["run_count"], 3)
        receipt = json.loads(observed["campaign_actions"][-1]["payload_json"])
        self.assertEqual(receipt["source_reference"], "Owner notebook entry 1")
        self.assertIsNone(receipt["launch_receipt"])
        self.assertEqual(receipt["causality"], "not_established")
        self.assertEqual(len(runway.campaign_observation(observation_request)["campaign_actions"]), 1)
        with self.assertRaisesRegex(ValueError, "already imported"):
            runway.campaign_observation({**observation_request, "request_id": "duplicate-observation",
                "version": observed["campaign"]["version"]})
        with self.assertRaisesRegex(ValueError, "campaign changed"):
            runway.campaign_observation({**observation_request, "request_id": "stale-observation"})
        decision_request = {"id": observed["project"]["id"],
            "project_version": observed["project"]["version"],
            "version": observed["campaign"]["version"],
            "request_id": "owner-internal-decision", "actor_id": "owner-fixture",
            "actor_owner": True, "action": "internal_decision",
            "payload": {"decision": "pause", "rationale": "One owner note is not campaign impact",
                "observation_action_ids": [observed["campaign_actions"][-1]["id"]]}}
        with self.assertRaisesRegex(ValueError, "Learning-only"):
            runway.campaign_internal_action({**decision_request, "payload": {
                **decision_request["payload"], "decision": "continue"}})
        with self.assertRaisesRegex(ValueError, "Authenticated owner"):
            runway.campaign_internal_action({**decision_request, "actor_owner": False})
        decided = runway.campaign_internal_action(decision_request)
        self.assertEqual(decided["campaign"]["stage"], "learn")
        decision_receipt = json.loads(decided["campaign_actions"][-1]["payload_json"])
        self.assertEqual(decision_receipt["observation_action_ids"], [observed["campaign_actions"][-1]["id"]])
        self.assertFalse(decision_receipt["execution_granted"])
        self.assertEqual(runway.campaign_internal_action(decision_request)["campaign"]["version"],
                         decided["campaign"]["version"])
        capability_request = {"id": observed["project"]["id"],
            "project_version": observed["project"]["version"],
            "version": decided["campaign"]["version"], "request_id": "missing-publisher-request",
            "actor_id": "owner-fixture", "actor_owner": True, "action": "capability_request",
            "payload": {"blocked_task": "Publish an approved post", "required_scope": "One named channel",
                "expected_benefit": "Learn from a bounded release", "cost_status": "unknown",
                "cost_note": "No account or price verified"}}
        with self.assertRaisesRegex(ValueError, "cost status"):
            runway.campaign_internal_action({**capability_request, "payload": {
                **capability_request["payload"], "cost_status": "free-ish"}})
        requested = runway.campaign_internal_action(capability_request)
        self.assertEqual(requested["campaign"]["stage"], "learn")
        self.assertEqual(requested["campaign"]["updated_at"], decided["campaign"]["updated_at"])
        self.assertFalse(json.loads(requested["campaign_actions"][-1]["payload_json"])["capability_granted"])
        self.assertEqual(runway.campaign_internal_action(capability_request)["campaign"]["version"],
                         requested["campaign"]["version"])
        lesson_request = {"id": observed["project"]["id"],
            "project_version": observed["project"]["version"],
            "version": requested["campaign"]["version"], "request_id": "owner-internal-lesson",
            "actor_id": "owner-fixture", "actor_owner": True, "action": "internal_lesson",
            "payload": {"decision_id": decided["campaign_actions"][-1]["id"],
                "lesson": "Ask founders about control before claiming outcomes",
                "context": "One owner notebook entry; no campaign launched",
                "uncertainty": "No control group or representative sample",
                "revisit_condition": "A separately authorized test yields observations",
                "next_action": "Keep the draft internal"}}
        with self.assertRaisesRegex(ValueError, "exact current decision"):
            runway.campaign_internal_action({**lesson_request,
                "payload": {**lesson_request["payload"], "decision_id": "0" * 32}})
        learned = runway.campaign_internal_action(lesson_request)
        self.assertEqual(learned["campaign"]["stage"], "complete")
        self.assertEqual(json.loads(learned["campaign_actions"][-1]["payload_json"])["causality"],
                         "not_established")
        reopened = runway.campaign_observation({**observation_request,
            "request_id": "owner-observation-2", "version": learned["campaign"]["version"],
            "observation": {**observation, "observation_id": "owner-observation-2"}})
        self.assertEqual(reopened["campaign"]["stage"], "align")
        self.assertEqual(len(reopened["campaign_actions"]), 5)
        limited = runway.save_campaign_brief({**payload,
            "request_id": "minimum-sample-brief", "version": reopened["campaign"]["version"],
            "experiment": {**experiment, "decision_rule": "minimum_sample", "minimum_sample": 3}})
        fresh = runway.campaign_observation({**observation_request,
            "request_id": "minimum-observation-1", "version": limited["campaign"]["version"],
            "observation": {**observation, "observation_id": "minimum-observation-1"}})
        minimum_decision = {**decision_request, "request_id": "minimum-decision",
            "version": fresh["campaign"]["version"], "payload": {
                "decision": "pause", "rationale": "Review the owner evidence",
                "observation_action_ids": [fresh["campaign_actions"][-1]["id"]]}}
        with self.assertRaisesRegex(ValueError, "stale for this brief"):
            runway.campaign_internal_action({**minimum_decision, "payload": {
                **minimum_decision["payload"], "observation_action_ids": [observed["campaign_actions"][-1]["id"]]}})
        with self.assertRaisesRegex(ValueError, "Insufficient actual sample"):
            runway.campaign_internal_action(minimum_decision)
        waiting = runway.campaign_internal_action({**minimum_decision,
            "payload": {**minimum_decision["payload"], "decision": "collect_evidence"}})
        self.assertTrue(json.loads(waiting["campaign_actions"][-1]["payload_json"])["inconclusive"])
        with self.assertRaisesRegex(ValueError, "Wait for a new observation"):
            runway.campaign_internal_action({**minimum_decision,
                "request_id": "premature-second-decision", "version": waiting["campaign"]["version"],
                "payload": {**minimum_decision["payload"], "decision": "collect_evidence"}})
        second = runway.campaign_observation({**observation_request,
            "request_id": "minimum-observation-2", "version": waiting["campaign"]["version"],
            "observation": {**observation, "observation_id": "minimum-observation-2",
                "numerator": 0, "denominator": 1}})
        eligible = runway.campaign_internal_action({**minimum_decision,
            "request_id": "eligible-minimum-decision", "version": second["campaign"]["version"],
            "payload": {**minimum_decision["payload"], "observation_action_ids": [
                fresh["campaign_actions"][-1]["id"], second["campaign_actions"][-1]["id"]]}})
        self.assertEqual(eligible["campaign"]["stage"], "learn")
        self.assertEqual(json.loads(eligible["campaign_actions"][-1]["payload_json"])["actual_sample"], 3)

    def test_owner_can_review_prior_saved_draft_while_newer_execution_is_held(self):
        first = runway.create(self.data)
        for _ in range(3):
            first = self.finish(runway.claim())
        self.assertIsNone(runway.claim())
        original = first["artifacts"][1]
        second = runway.create({**self.data, "request_id": "newer-held-assignment"})
        claim = runway.claim()
        runway.unknown({"execution_id": claim["execution_id"],
                        "error": "The newer provider request is unresolved."})
        reviewed = runway.review({"id": first["project"]["id"],
            "version": first["project"]["version"], "request_id": "review-prior-draft",
            "artifact_id": original["id"], "digest": original["digest"],
            "decision": "revision_requested", "instruction": "Clarify the claim limit.",
            "actor_id": "owner-fixture", "actor_name": "Fixture owner", "actor_owner": True})
        self.assertEqual(reviewed["reviews"][-1]["artifact_id"], original["id"])
        self.assertEqual(reviewed["project"]["status"], "needs_review")
        self.assertEqual(runway.inspect({"id": second["project"]["id"]})["project"]["status"], "unknown")
        self.assertIsNone(runway.claim())

    def test_pause_and_stale_task_do_not_admit_or_overwrite(self):
        created = runway.create(self.data)
        paused = runway.change({"id": created["project"]["id"], "version": 1}, "pause")
        self.assertIsNone(runway.claim())
        resumed = runway.change({"id": created["project"]["id"], "version": paused["project"]["version"]}, "resume")
        self.assertEqual(resumed["project"]["status"], "ready")
        claim = runway.claim()
        with runway.connection() as conn:
            conn.execute("UPDATE tasks SET version=version+1 WHERE id=?", (claim["step"]["task_id"],))
        settled = self.finish(claim)
        self.assertEqual(settled["project"]["status"], "needs_review")
        self.assertEqual(settled["executions"][0]["status"], "stale")
        self.assertEqual(len(settled["artifacts"]), 0)
        self.assertIsNone(runway.claim())

    def test_restart_unknown_never_replays(self):
        runway.create(self.data)
        claim = runway.claim()
        result = runway.recover()
        self.assertEqual(result["unknown_runways"], [claim["project"]["id"]])
        self.assertIsNone(runway.claim())
        with runway.connection() as conn:
            state = runway.snapshot(conn)
            task = dict(conn.execute("SELECT status,action_state,blocker FROM tasks WHERE id=?",
                                     (claim["step"]["task_id"],)).fetchone())
        self.assertEqual(state["project"]["status"], "unknown")
        self.assertEqual(state["project"]["token_reserved"], 25000)
        self.assertEqual(state["executions"][0]["status"], "unknown")
        self.assertEqual(state["steps"][0]["status"], "unknown")
        self.assertEqual(task["status"], "paused")
        self.assertEqual(task["action_state"], "blocked")
        self.assertIn("reconcile", task["blocker"])
        version = state["project"]["version"]
        runway.recover()
        with runway.connection() as conn:
            self.assertEqual(runway.snapshot(conn)["project"]["version"], version)

    def test_direct_chat_claim_serializes_with_runway_and_is_idempotent(self):
        runway.create(self.data)
        chat = {"request_id": "chat-fixture", "actor_id": "owner-fixture",
                "session_key": "agent:main:marketing-business-main", "content_digest": "a" * 64}
        self.assertTrue(runway.claim_chat(chat)["admitted"])
        self.assertFalse(runway.claim_chat(chat)["admitted"])
        self.assertIsNone(runway.claim())
        with self.assertRaisesRegex(ValueError, "different content"):
            runway.claim_chat({**chat, "content_digest": "b" * 64})
        with self.assertRaisesRegex(ValueError, "Another direct chat"):
            runway.claim_chat({**chat, "request_id": "other-chat"})
        self.assertEqual(runway.finish_chat({"request_id": chat["request_id"], "status": "succeeded"})["status"], "succeeded")
        self.assertIsNotNone(runway.claim())
        with self.assertRaisesRegex(ValueError, "runway execution"):
            runway.claim_chat({**chat, "request_id": "chat-during-work"})

    def test_unconfirmed_chat_claim_stays_held_after_restart(self):
        runway.create(self.data)
        chat = {"request_id": "chat-restart", "actor_id": "owner-fixture",
                "session_key": "agent:main:marketing-business-main", "content_digest": "c" * 64}
        runway.claim_chat(chat)
        recovered = runway.recover()
        self.assertEqual(recovered["unknown_chats"], [chat["request_id"]])
        self.assertIsNone(runway.claim())
        with self.assertRaisesRegex(ValueError, "Another direct chat"):
            runway.claim_chat({**chat, "request_id": "new-chat"})
        with self.assertRaisesRegex(ValueError, "needs reconciliation"):
            runway.finish_chat({"request_id": chat["request_id"], "status": "succeeded"})

    def test_saved_chat_receipt_reconciles_only_matching_unknown_claim(self):
        runway.create(self.data)
        chat = {"request_id": "chat-saved", "actor_id": "owner-fixture",
                "session_key": "agent:main:marketing-business-main", "content_digest": "d" * 64}
        runway.claim_chat(chat)
        self.assertEqual(runway.recover()["unknown_chats"], [chat["request_id"]])
        with self.assertRaisesRegex(ValueError, "does not match"):
            runway.reconcile_chat({**chat, "content_digest": "e" * 64})
        self.assertIsNone(runway.claim())
        self.assertEqual(runway.reconcile_chat(chat)["status"], "succeeded")
        self.assertEqual(runway.reconcile_chat(chat)["status"], "succeeded")
        self.assertIsNotNone(runway.claim())

    def test_pending_chat_cannot_be_reconciled_before_saved_outcome(self):
        runway.create(self.data)
        chat = {"request_id": "chat-still-running", "actor_id": "owner-fixture",
                "session_key": "agent:main:marketing-business-main", "content_digest": "f" * 64}
        runway.claim_chat(chat)
        with self.assertRaisesRegex(ValueError, "confirmed saved reply"):
            runway.reconcile_chat(chat)
        self.assertIsNone(runway.claim())

    def test_two_direct_chats_racing_admit_only_one(self):
        runway.create(self.data)
        barrier = Barrier(2)
        def attempt(number):
            barrier.wait()
            try:
                return runway.claim_chat({"request_id": f"chat-{number}", "actor_id": "owner-fixture",
                                          "session_key": f"agent:main:chat-{number}",
                                          "content_digest": str(number) * 64})["admitted"]
            except ValueError as error:
                self.assertIn("Another direct chat", str(error))
                return False
        with ThreadPoolExecutor(max_workers=2) as workers:
            results = list(workers.map(attempt, (1, 2)))
        self.assertEqual(sorted(results), [False, True])
        self.assertIsNone(runway.claim())

    def test_direct_chat_claim_is_shared_between_cli_processes(self):
        runway.create(self.data)
        script = str(Path(runway.__file__))
        processes = [subprocess.Popen([sys.executable, script, "chat-claim"],
                                      stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                      stderr=subprocess.PIPE, text=True,
                                      env=os.environ.copy()) for _ in range(2)]
        outcomes = []
        for number, process in enumerate(processes):
            stdout, stderr = process.communicate(json.dumps({"request_id": f"process-chat-{number}",
                "actor_id": "owner-fixture", "session_key": "agent:main:marketing-business-main",
                "content_digest": str(number + 1) * 64}), timeout=10)
            outcomes.append((process.returncode, stdout, stderr))
        self.assertEqual(sorted(code for code, _, _ in outcomes), [0, 2])
        self.assertTrue(any(json.loads(output)["admitted"] for code, output, _ in outcomes if code == 0))
        self.assertTrue(any("Another direct chat" in error for code, _, error in outcomes if code == 2))

    def test_model_request_admission_refuses_twenty_first_network_call(self):
        runway.create(self.data)
        execution = runway.claim()
        base = {"execution_id": execution["execution_id"], "request_digest": "a" * 64,
                "reserved_tokens": 100}
        for number in range(20):
            request = {**base, "request_id": f"network-{number}"}
            self.assertTrue(runway.reserve_model_request(request)["admitted"])
            self.assertFalse(runway.reserve_model_request(request)["admitted"])
            self.assertEqual(runway.finish_model_request({"request_id": request["request_id"],
                                                          "status": "reported", "reported_tokens": 10})["status"], "reported")
        with self.assertRaisesRegex(ValueError, "request ceiling"):
            runway.reserve_model_request({**base, "request_id": "network-20"})
        with self.assertRaisesRegex(ValueError, "different request"):
            runway.reserve_model_request({**base, "request_id": "network-0", "request_digest": "b" * 64})

    def test_existing_meter_table_gains_digest_before_first_live_reservation(self):
        conn = runway.db()
        try:
            conn.execute("CREATE TABLE runway_model_requests(request_id TEXT PRIMARY KEY, execution_id TEXT NOT NULL, pilot_root_id TEXT NOT NULL, reserved_tokens INTEGER NOT NULL, reported_tokens INTEGER, status TEXT NOT NULL, created_at REAL NOT NULL, ended_at REAL)")
            conn.execute("INSERT INTO runway_model_requests VALUES(?,?,?,?,?,?,?,?)",
                         ("legacy-network", "legacy-execution", "legacy-root", 100, 10, "reported", time.time(), time.time()))
        finally:
            conn.close()
        runway.create(self.data)
        execution = runway.claim()
        request = {"request_id": execution["execution_id"], "execution_id": execution["execution_id"],
                   "request_digest": "a" * 64, "reserved_tokens": 25000}
        self.assertTrue(runway.reserve_model_request(request)["admitted"])
        with runway.connection() as conn:
            saved = dict(conn.execute("SELECT request_id,request_digest FROM runway_model_requests WHERE request_id=?",
                                      (request["request_id"],)).fetchone())
            legacy = conn.execute("SELECT request_digest FROM runway_model_requests WHERE request_id='legacy-network'").fetchone()[0]
        self.assertEqual(saved, {"request_id": request["request_id"], "request_digest": "a" * 64})
        self.assertEqual(legacy, "legacy-unverified")

    def test_model_request_unknown_and_overrun_hold_execution(self):
        runway.create(self.data)
        execution = runway.claim()
        request = {"request_id": "uncertain-network", "execution_id": execution["execution_id"],
                   "request_digest": "c" * 64, "reserved_tokens": 100}
        runway.reserve_model_request(request)
        self.assertEqual(runway.recover()["unknown_model_requests"], [request["request_id"]])
        with self.assertRaisesRegex(ValueError, "already settled or unresolved"):
            runway.finish_model_request({"request_id": request["request_id"],
                                         "status": "reported", "reported_tokens": 50})
        late = self.finish(execution)
        self.assertEqual(late["project"]["status"], "unknown")
        self.assertEqual(late["executions"][0]["status"], "unknown")
        with self.assertRaisesRegex(ValueError, "running execution"):
            runway.reserve_model_request({**request, "request_id": "after-restart"})

    def test_reserved_request_stays_held_across_a_new_ledger_process(self):
        runway.create(self.data)
        execution = runway.claim()
        request = {"request_id": execution["execution_id"],
                   "execution_id": execution["execution_id"],
                   "request_digest": "c" * 64, "reserved_tokens": 25000}
        self.assertTrue(runway.reserve_model_request(request)["admitted"])
        command = [sys.executable, str(Path(runway.__file__)), "recover"]
        recovered = subprocess.run(command, capture_output=True, text=True,
                                   env=os.environ.copy(), timeout=10, check=True)
        receipt = json.loads(recovered.stdout)
        self.assertEqual(receipt["unknown_model_requests"], [request["request_id"]])
        self.assertEqual(receipt["unknown_runways"], [execution["project"]["id"]])
        claimed = subprocess.run([sys.executable, str(Path(runway.__file__)), "claim"],
                                 capture_output=True, text=True,
                                 env=os.environ.copy(), timeout=10, check=True)
        self.assertIsNone(json.loads(claimed.stdout))
        with self.assertRaisesRegex(ValueError, "already settled or unresolved"):
            runway.finish_model_request({"request_id": request["request_id"],
                                         "status": "reported", "reported_tokens": 8})

    def test_model_request_token_ceiling_and_overrun_fail_closed(self):
        runway.create(self.data)
        execution = runway.claim()
        request = {"request_id": "bounded-network", "execution_id": execution["execution_id"],
                   "request_digest": "d" * 64, "reserved_tokens": 100}
        runway.reserve_model_request(request)
        self.assertEqual(runway.finish_model_request({"request_id": request["request_id"],
                                                      "status": "reported", "reported_tokens": 101})["status"], "overrun")
        with self.assertRaisesRegex(ValueError, "Unresolved model request"):
            runway.reserve_model_request({**request, "request_id": "another-network"})
        with self.assertRaisesRegex(ValueError, "unresolved"):
            self.finish(execution)

    def test_model_request_aggregate_token_boundary_refuses_before_dispatch(self):
        runway.create(self.data)
        execution = runway.claim()
        with runway.connection() as conn:
            conn.execute("INSERT INTO runway_model_requests VALUES(?,?,?,?,?,?,?, ?,?)",
                         ("prior-network", "older-execution", execution["project"]["id"],
                          "e" * 64, 249999, 249999, "reported", time.time(), time.time()))
        with self.assertRaisesRegex(ValueError, "Pilot token ceiling"):
            runway.reserve_model_request({"request_id": "ceiling-network", "execution_id": execution["execution_id"],
                                          "request_digest": "f" * 64, "reserved_tokens": 2})
        with runway.connection() as conn:
            self.assertIsNone(conn.execute("SELECT 1 FROM runway_model_requests WHERE request_id='ceiling-network'").fetchone())

    def test_validation_failures_stop_after_two_repairs(self):
        runway.create(self.data)
        for attempt in range(3):
            claim = runway.claim()
            self.assertIsNotNone(claim)
            settled = runway.settle({"execution_id": claim["execution_id"], "error": "Fixture missing deliverable",
                                     "usage": {"totalTokens": 100}}, False)
            self.assertEqual(settled["steps"][0]["attempts"], attempt + 1)
        self.assertEqual(settled["project"]["status"], "needs_review")
        self.assertIsNone(runway.claim())

    def test_missing_usage_charges_reservation_and_stops(self):
        runway.create(self.data)
        claim = runway.claim()
        settled = runway.settle({"execution_id": claim["execution_id"], "content": "Fixture output",
                                 "source_urls": [self.data["sources"][0]["url"]]}, True)
        self.assertEqual(settled["project"]["token_used"], 25000)
        self.assertEqual(settled["project"]["status"], "needs_review")
        self.assertIsNone(runway.claim())

    def test_reported_budget_blocks_further_model_admission(self):
        runway.create(self.data)
        claim = runway.claim()
        self.finish(claim, usage=149000)
        self.assertIsNone(runway.claim())
        with runway.connection() as conn:
            state = runway.snapshot(conn)
        self.assertEqual(state["project"]["status"], "budget_exhausted")
        self.assertEqual(len(state["executions"]), 1)

    def test_confirmed_gateway_admission_rejection_releases_reservation(self):
        runway.create(self.data)
        first = runway.claim()
        settled = runway.rejected({"execution_id": first["execution_id"],
                                   "gateway_code": "INVALID_REQUEST", "gateway_message": "Fixture validation rejection"})
        self.assertEqual(settled["executions"][0]["status"], "rejected")
        self.assertEqual(settled["project"]["token_reserved"], 0)
        second = runway.claim()
        self.assertNotEqual(first["execution_id"], second["execution_id"])
        self.assertEqual(second["step"]["ordinal"], 0)

    def test_proven_meter_schema_failure_reconciles_only_without_network_receipt(self):
        runway.create(self.data)
        first = runway.claim()
        runway.unknown({"execution_id": first["execution_id"],
                        "error": "OpenClaw returned no confirmed model usage."})
        with runway.connection() as conn:
            self.assertEqual(conn.execute("SELECT status FROM tasks WHERE id=?",
                            (first["step"]["task_id"],)).fetchone()[0], "paused")
        proof = {"execution_id": first["execution_id"], "gateway_code": "METER_PRE_DISPATCH_SCHEMA",
                 "gateway_message": "table runway_model_requests has 8 columns but 9 values were supplied"}
        with self.assertRaisesRegex(ValueError, "proven pre-dispatch"):
            runway.rejected({**proof, "gateway_message": "unverified error"})
        settled = runway.rejected(proof)
        self.assertEqual(settled["project"]["status"], "ready")
        self.assertEqual(settled["project"]["token_reserved"], 0)
        self.assertEqual(settled["executions"][0]["status"], "rejected")
        with runway.connection() as conn:
            task = conn.execute("SELECT status,action_state,blocker FROM tasks WHERE id=?",
                                (first["step"]["task_id"],)).fetchone()
            self.assertEqual(tuple(task), ("ready", "agent_ready", None))
        second_claim = runway.claim()
        self.assertNotEqual(second_claim["execution_id"], first["execution_id"])
        runway.reserve_model_request({"request_id": second_claim["execution_id"],
            "execution_id": second_claim["execution_id"], "request_digest": "a" * 64,
            "reserved_tokens": 25000})
        runway.unknown({"execution_id": second_claim["execution_id"],
                        "error": "OpenClaw returned no confirmed model usage."})
        with self.assertRaisesRegex(ValueError, "receipt prevents"):
            runway.rejected({**proof, "execution_id": second_claim["execution_id"]})

    def test_attributed_input_is_idempotent_and_reaches_next_step(self):
        created = runway.create(self.data)
        first = runway.claim()
        project = self.finish(first)["project"]
        with runway.connection() as conn:
            conn.execute("UPDATE runways SET status='waiting',next_due=? WHERE id=?", (time.time() + 3600, project["id"]))
        self.assertIsNone(runway.claim())
        note = {"id": project["id"], "version": project["version"],
                "request_id": "fixture-collaborator-note", "actor_id": "collaborator-fixture",
                "actor_name": "Fixture collaborator", "content": "Keep the draft suitable for founders."}
        saved = runway.add_input(note)
        self.assertEqual(saved["project"]["status"], "ready")
        self.assertEqual(len(saved["inputs"]), 1)
        self.assertEqual(saved["inputs"][0]["actor_id"], "collaborator-fixture")
        self.assertEqual(len(runway.add_input(note)["inputs"]), 1)
        with self.assertRaises(ValueError):
            runway.add_input({**note, "actor_id": "owner-fixture"})
        next_step = runway.claim()
        self.assertEqual(next_step["inputs"][0]["content"], note["content"])
        self.assertIsNone(runway.claim())

    def test_input_is_saved_without_waking_work_when_model_gate_is_closed(self):
        created = runway.create(self.data)
        first = runway.claim()
        project = self.finish(first)["project"]
        with runway.connection() as conn:
            conn.execute("UPDATE runways SET status='waiting',next_due=?,wait_reason=? WHERE id=?",
                         (time.time() + 3600, "Waiting for an authorized event", project["id"]))
        saved = runway.add_input({"id": created["project"]["id"], "version": project["version"],
                                  "request_id": "closed-gate-note", "actor_id": "owner-fixture",
                                  "actor_name": "Fixture owner", "content": "Retain this constraint.",
                                  "activate": False})
        self.assertEqual(saved["project"]["status"], "waiting")
        self.assertEqual(saved["project"]["wait_reason"], "Waiting for an authorized event")
        self.assertEqual(len(saved["inputs"]), 1)
        self.assertIsNone(runway.claim())

    def test_source_url_must_be_exactly_allowlisted_shape(self):
        self.data["sources"][0]["url"] += "&redirect=https://example.com"
        with self.assertRaises(ValueError):
            runway.create(self.data)

    def test_meter_sees_only_the_durable_active_execution(self):
        runway.create(self.data)
        self.assertIsNone(runway.meter_active()["execution_id"])
        claim = runway.claim()
        self.assertEqual(runway.meter_active()["execution_id"], claim["execution_id"])
        self.finish(claim)
        self.assertIsNone(runway.meter_active()["execution_id"])

    def test_owner_review_revises_exact_artifact_then_approves_without_publishing(self):
        settled = runway.create(self.data)
        for _ in range(3):
            settled = self.finish(runway.claim())
        self.assertIsNone(runway.claim())
        original = settled["artifacts"][1]
        settled = runway.add_input({"id": settled["project"]["id"],
            "version": settled["project"]["version"], "request_id": "native:fixture-suggestion",
            "actor_id": "gateway-collaborator-fixture", "actor_name": "Fixture collaborator",
            "content": "Use a concrete founder control example in the revised hook.", "activate": False})
        source_input = settled["inputs"][-1]
        request = {"id": settled["project"]["id"], "version": settled["project"]["version"],
                   "request_id": "fixture-revision", "artifact_id": original["id"],
                   "digest": original["digest"], "decision": "revision_requested",
                   "instruction": "Tighten the second angle and state its evidence limit.",
                   "actor_id": "owner-fixture", "actor_name": "Fixture owner", "actor_owner": True}
        with self.assertRaises(ValueError):
            runway.review({**request, "actor_owner": False})
        with self.assertRaises(ValueError):
            runway.review({**request, "digest": "0" * 64})
        revised = runway.review(request)
        self.assertEqual(revised["project"]["status"], "needs_review")
        self.assertEqual(len(revised["reviews"]), 1)
        self.assertEqual(len(runway.review(request)["reviews"]), 1)
        self.assertIsNone(runway.claim())
        held = runway.prepare_revision_grant({"id": revised["project"]["id"],
            "version": revised["project"]["version"], "request_id": "fixture-revision-grant",
            "review_id": revised["reviews"][-1]["id"], "artifact_id": original["id"],
            "digest": original["digest"], "owner_actor": "owner-fixture", "actor_owner": True,
            "budget_mode": "fresh_pilot", "max_runs": 1, "max_model_requests": 4,
            "token_limit": 25000, "max_active_seconds": 300, "deadline_at": time.time() + 600})
        released = runway.release_revision_grant({"grant_id": held["revision_grants"][0]["id"],
            "owner_actor": "owner-fixture", "actor_owner": True, "transport_ready": True})
        self.assertEqual(released["project"]["source_runway_id"], settled["project"]["id"])
        self.assertEqual(released["inputs"][0]["source_input_id"], source_input["id"])
        self.assertEqual(released["inputs"][0]["actor_id"], "gateway-collaborator-fixture")
        claim = runway.claim()
        self.assertEqual(claim["step"]["kind"], "revision_angles")
        self.assertEqual(claim["review"]["artifact_id"], original["id"])
        self.assertEqual(claim["review"]["instruction"], request["instruction"])
        self.assertEqual(claim["inputs"][0]["source_input_id"], source_input["id"])
        self.assertIn("concrete founder control example", claim["inputs"][0]["content"])
        self.assertIsNone(runway.claim())
        revised = runway.settle({"execution_id": claim["execution_id"],
                                 "content": "Fixture materially revised angles",
                                 "source_urls": [self.data["sources"][0]["url"]],
                                 "usage": {"totalTokens": 200}}, True)
        self.assertEqual(revised["project"]["status"], "needs_review")
        latest = revised["artifacts"][-1]
        self.assertEqual(latest["kind"], "revision_angles")
        approved = runway.review({"id": revised["project"]["id"], "version": revised["project"]["version"],
                                  "request_id": "fixture-approve", "artifact_id": latest["id"],
                                  "digest": latest["digest"], "decision": "approved",
                                  "actor_id": "owner-second-fixture", "actor_name": "Second owner device", "actor_owner": True})
        self.assertEqual(approved["project"]["status"], "done")
        self.assertEqual(approved["reviews"][-1]["artifact_digest"], latest["digest"])
        self.assertIsNone(runway.claim())

    def test_deferred_revision_records_exact_instruction_without_admitting_work(self):
        settled = runway.create(self.data)
        for _ in range(3):
            settled = self.finish(runway.claim())
        original = settled["artifacts"][1]
        request = {"id": settled["project"]["id"], "version": settled["project"]["version"],
                   "request_id": "fixture-deferred-revision", "artifact_id": original["id"],
                   "digest": original["digest"], "decision": "revision_requested", "defer": True,
                   "instruction": "Remove the unsupported performance claim.",
                   "actor_id": "owner-fixture", "actor_name": "Fixture owner", "actor_owner": True}
        with self.assertRaises(ValueError):
            runway.review({**request, "actor_owner": False})
        with self.assertRaises(ValueError):
            runway.review({**request, "digest": "0" * 64})
        saved = runway.review(request)
        self.assertEqual(saved["project"]["status"], "needs_review")
        self.assertIn("metered model route", saved["project"]["wait_reason"])
        self.assertIsNone(saved["reviews"][-1]["step_id"])
        self.assertEqual(saved["reviews"][-1]["instruction"], request["instruction"])
        self.assertEqual(len(saved["steps"]), 3)
        self.assertIsNone(runway.claim())
        self.assertEqual(len(runway.review(request)["reviews"]), 1)

    def test_held_revision_grant_links_exact_review_without_creating_work(self):
        settled = runway.create(self.data)
        for _ in range(3):
            settled = self.finish(runway.claim())
        source = settled["artifacts"][1]
        rid = settled["project"]["id"]
        with runway.connection() as conn:
            conn.execute("UPDATE runways SET deadline_at=NULL WHERE id=?", (rid,))
        reviewed = runway.review({"id": rid, "version": settled["project"]["version"],
                                 "request_id": "held-review", "artifact_id": source["id"],
                                 "digest": source["digest"], "decision": "revision_requested",
                                 "instruction": "Make the second angle more concrete.", "defer": True,
                                 "actor_id": "owner-fixture", "actor_name": "Fixture owner", "actor_owner": True})
        grant = {"id": rid, "version": reviewed["project"]["version"],
                 "request_id": "held-grant", "review_id": reviewed["reviews"][-1]["id"],
                 "artifact_id": source["id"], "digest": source["digest"],
                 "owner_actor": "owner-fixture", "actor_owner": True,
                 "max_runs": 1, "max_model_requests": 4, "token_limit": 25000,
                 "max_active_seconds": 300, "deadline_at": time.time() + 600}
        held = runway.prepare_revision_grant(grant)
        self.assertEqual(held["project"]["status"], "needs_review")
        self.assertEqual(held["project"]["pilot_root_id"], rid)
        self.assertEqual(len(held["steps"]), 3)
        self.assertEqual(held["revision_grants"][0]["status"], "held_for_metering")
        self.assertEqual(held["revision_grants"][0]["source_artifact_digest"], source["digest"])
        self.assertEqual(runway.prepare_revision_grant(grant)["revision_grants"][0]["id"],
                         held["revision_grants"][0]["id"])
        self.assertIsNone(runway.claim())
        with self.assertRaisesRegex(ValueError, "different revision grant"):
            runway.prepare_revision_grant({**grant, "token_limit": 20000})
        with self.assertRaisesRegex(ValueError, "already has a grant"):
            runway.prepare_revision_grant({**grant, "request_id": "other-grant"})

    def test_fresh_pilot_release_links_old_draft_and_revises_only_after_grant(self):
        settled = runway.create(self.data)
        for _ in range(3):
            settled = self.finish(runway.claim())
        original = settled["artifacts"][1]
        rid = settled["project"]["id"]
        reviewed = runway.review({"id": rid, "version": settled["project"]["version"],
                                 "request_id": "fresh-review", "artifact_id": original["id"],
                                 "digest": original["digest"], "decision": "revision_requested", "defer": True,
                                 "instruction": "Make the first angle specific to a founder's first employee.",
                                 "actor_id": "owner-fixture", "actor_name": "Fixture owner", "actor_owner": True})
        held = runway.prepare_revision_grant({"id": rid, "version": reviewed["project"]["version"],
            "request_id": "fresh-grant", "review_id": reviewed["reviews"][-1]["id"],
            "artifact_id": original["id"], "digest": original["digest"], "owner_actor": "owner-fixture",
            "actor_owner": True, "budget_mode": "fresh_pilot", "max_runs": 1,
            "max_model_requests": 4, "token_limit": 25000, "max_active_seconds": 300,
            "deadline_at": time.time() + 600})
        grant_id = held["revision_grants"][0]["id"]
        release = {"grant_id": grant_id, "owner_actor": "owner-fixture", "actor_owner": True,
                   "transport_ready": True}
        with self.assertRaisesRegex(ValueError, "verified per-request"):
            runway.release_revision_grant({**release, "transport_ready": False})
        with self.assertRaisesRegex(ValueError, "missing or belongs"):
            runway.release_revision_grant({**release, "owner_actor": "another-owner"})
        new = runway.release_revision_grant(release)
        self.assertEqual(new["project"]["id"], grant_id)
        self.assertEqual(new["project"]["pilot_root_id"], grant_id)
        self.assertEqual(new["project"]["source_runway_id"], rid)
        self.assertEqual(new["project"]["source_review_id"], reviewed["reviews"][-1]["id"])
        self.assertEqual([step["kind"] for step in new["steps"]], ["revision_angles"])
        self.assertEqual(runway.release_revision_grant(release)["project"]["id"], grant_id)
        claim = runway.claim()
        self.assertEqual(claim["project"]["id"], grant_id)
        self.assertEqual(claim["review"]["instruction"], reviewed["reviews"][-1]["instruction"])
        self.assertEqual(claim["review"]["target_content"], original["content"])
        for number in range(4):
            request_id = f"fresh-network-{number}"
            runway.reserve_model_request({"request_id": request_id, "execution_id": claim["execution_id"],
                                          "request_digest": str(number + 1) * 64, "reserved_tokens": 100})
            runway.finish_model_request({"request_id": request_id, "status": "reported", "reported_tokens": 80})
        with self.assertRaisesRegex(ValueError, "request ceiling"):
            runway.reserve_model_request({"request_id": "fifth-network", "execution_id": claim["execution_id"],
                                          "request_digest": "f" * 64, "reserved_tokens": 100})
        revised = runway.settle({"execution_id": claim["execution_id"],
                                 "content": "A more specific first employee angle",
                                 "source_urls": [self.data["sources"][0]["url"]],
                                 "usage": {"totalTokens": 300}}, True)
        self.assertEqual(revised["project"]["status"], "needs_review")
        self.assertEqual(revised["project"]["token_used"], 320)
        self.assertNotEqual(revised["artifacts"][0]["digest"], original["digest"])
        self.assertEqual(runway.inspect({"id": rid})["revision_grants"][0]["released_runway_id"], grant_id)
        self.assertEqual(runway.inspect({"id": rid})["artifacts"][1]["digest"], original["digest"])

    def test_approved_linked_revision_requires_owner_adoption_and_keeps_history(self):
        created = runway.create(self.data)
        urls = [item["url"] for item in self.data["sources"]]
        note = {"audience": "Founders", "problem": "Marketing time",
                "evidence": [{"sourceUrl": item["url"], "quote": item["content"]}
                             for item in self.data["sources"]], "limitations": "Anecdotes only"}
        claim = runway.claim()
        settled = runway.settle({"execution_id": claim["execution_id"],
            "content": json.dumps(note), "source_urls": urls,
            "usage": {"totalTokens": 100}}, True)
        for _ in range(2): settled = self.finish(runway.claim())
        rid, original, audience = (settled["project"]["id"], settled["artifacts"][1],
                                   settled["artifacts"][0])
        brief = {key: "Fixture statement" for key in ("audience", "problem", "hypothesis",
            "proposition", "desired_behavior", "channel", "primary_metric", "metric_definition",
            "guardrail", "review_timing", "non_goals")}
        experiment = {"intervention": "Internal draft", "target_population": "Founders",
            "observation_window": "Seven days", "metric_source": "Owner notes",
            "decision_rule": "learning_only", "minimum_sample": 0}
        brief_request = {"id": rid, "project_version": settled["project"]["version"],
            "version": 0, "request_id": "linked-brief", "actor_id": "owner-fixture",
            "actor_owner": True, "source_artifact_id": audience["id"],
            "source_artifact_digest": audience["digest"], "brief": brief,
            "experiment": experiment}
        source = runway.save_campaign_brief(brief_request)
        source = runway.review({"id": rid, "version": source["project"]["version"],
            "request_id": "linked-review", "artifact_id": original["id"],
            "digest": original["digest"], "decision": "revision_requested",
            "instruction": "Make the founder hook specific.", "actor_id": "owner-fixture",
            "actor_name": "Fixture owner", "actor_owner": True})
        held = runway.prepare_revision_grant({"id": rid, "version": source["project"]["version"],
            "request_id": "linked-grant", "review_id": source["reviews"][-1]["id"],
            "artifact_id": original["id"], "digest": original["digest"],
            "owner_actor": "owner-fixture", "actor_owner": True, "budget_mode": "fresh_pilot",
            "max_runs": 1, "max_model_requests": 4, "token_limit": 25000,
            "max_active_seconds": 300, "deadline_at": time.time() + 600})
        revision = runway.release_revision_grant({"grant_id": held["revision_grants"][0]["id"],
            "owner_actor": "owner-fixture", "actor_owner": True, "transport_ready": True})
        claim = runway.claim()
        revision = runway.settle({"execution_id": claim["execution_id"],
            "content": "A more specific founder hook", "source_urls": [urls[0]],
            "usage": {"totalTokens": 100}}, True)
        revised = revision["artifacts"][0]
        adoption = {"id": rid, "request_id": "linked-adoption",
            "project_version": source["project"]["version"],
            "version": source["campaign"]["version"],
            "revision_runway_id": revision["project"]["id"],
            "revision_project_version": revision["project"]["version"],
            "revision_artifact_id": revised["id"],
            "revision_artifact_digest": revised["digest"],
            "revision_review_id": "0" * 32, "actor_id": "owner-fixture",
            "actor_owner": True}
        with self.assertRaisesRegex(ValueError, "unfinished"):
            runway.adopt_campaign_revision(adoption)
        revision = runway.review({"id": revision["project"]["id"],
            "version": revision["project"]["version"], "request_id": "linked-approval",
            "artifact_id": revised["id"], "digest": revised["digest"],
            "decision": "approved", "actor_id": "owner-fixture",
            "actor_name": "Fixture owner", "actor_owner": True})
        adoption.update(revision_project_version=revision["project"]["version"],
                        revision_review_id=revision["reviews"][-1]["id"])
        with self.assertRaisesRegex(ValueError, "Authenticated owner"):
            runway.adopt_campaign_revision({**adoption, "actor_owner": False})
        with self.assertRaisesRegex(ValueError, "Exact revised asset"):
            runway.adopt_campaign_revision({**adoption, "revision_artifact_digest": "0" * 64})
        selected = runway.adopt_campaign_revision(adoption)
        self.assertEqual(selected["campaign"]["asset_artifact_id"], revised["id"])
        self.assertEqual(selected["campaign"]["stage"], "align")
        self.assertEqual(len(selected["artifacts"]), 3)
        self.assertFalse(json.loads(selected["campaign_actions"][-1]["payload_json"])["external_effect"])
        self.assertEqual(runway.adopt_campaign_revision(adoption)["campaign"]["version"],
                         selected["campaign"]["version"])
        with self.assertRaisesRegex(ValueError, "different campaign action"):
            runway.adopt_campaign_revision({**adoption, "revision_review_id": "f" * 32})
        with self.assertRaisesRegex(ValueError, "unchanged owner internal campaign"):
            runway.adopt_campaign_revision({**adoption, "request_id": "stale-adoption"})
        edited = runway.save_campaign_brief({**brief_request,
            "request_id": "linked-brief-edit", "version": selected["campaign"]["version"],
            "project_version": selected["project"]["version"],
            "brief": {**brief, "hypothesis": "Updated internal learning hypothesis"}})
        self.assertEqual(edited["campaign"]["asset_artifact_id"], revised["id"])
        reaffirmed = runway.adopt_campaign_revision({**adoption,
            "request_id": "linked-reaffirmed", "version": edited["campaign"]["version"]})
        self.assertEqual(json.loads(reaffirmed["campaign_actions"][-1]["payload_json"])["brief_revision"], 3)
        self.assertFalse(json.loads(reaffirmed["campaign_actions"][-1]["payload_json"])["launch_authorized"])
        self.assertEqual(runway.inspect({"id": rid})["artifacts"][1]["digest"], original["digest"])

    def test_same_pilot_release_refuses_unmetered_historical_turns(self):
        settled = runway.create(self.data)
        for _ in range(3):
            settled = self.finish(runway.claim())
        original = settled["artifacts"][1]
        rid = settled["project"]["id"]
        reviewed = runway.review({"id": rid, "version": settled["project"]["version"],
                                 "request_id": "legacy-review", "artifact_id": original["id"],
                                 "digest": original["digest"], "decision": "revision_requested", "defer": True,
                                 "instruction": "Tighten the first angle.", "actor_id": "owner-fixture",
                                 "actor_name": "Fixture owner", "actor_owner": True})
        held = runway.prepare_revision_grant({"id": rid, "version": reviewed["project"]["version"],
            "request_id": "legacy-grant", "review_id": reviewed["reviews"][-1]["id"],
            "artifact_id": original["id"], "digest": original["digest"], "owner_actor": "owner-fixture",
            "actor_owner": True, "max_runs": 1, "max_model_requests": 4,
            "token_limit": 25000, "max_active_seconds": 300, "deadline_at": time.time() + 600})
        with self.assertRaisesRegex(ValueError, "Historical provider request count is unknown"):
            runway.release_revision_grant({"grant_id": held["revision_grants"][0]["id"],
                "owner_actor": "owner-fixture", "actor_owner": True, "transport_ready": True})
        self.assertEqual(len(runway.list_projects()["projects"]), 1)

    def test_revision_release_refuses_changed_source_after_grant(self):
        settled = runway.create(self.data)
        for _ in range(3):
            settled = self.finish(runway.claim())
        original = settled["artifacts"][1]
        rid = settled["project"]["id"]
        reviewed = runway.review({"id": rid, "version": settled["project"]["version"],
                                 "request_id": "stale-review", "artifact_id": original["id"],
                                 "digest": original["digest"], "decision": "revision_requested", "defer": True,
                                 "instruction": "Tighten the draft.", "actor_id": "owner-fixture",
                                 "actor_name": "Fixture owner", "actor_owner": True})
        held = runway.prepare_revision_grant({"id": rid, "version": reviewed["project"]["version"],
            "request_id": "stale-grant", "review_id": reviewed["reviews"][-1]["id"],
            "artifact_id": original["id"], "digest": original["digest"], "owner_actor": "owner-fixture",
            "actor_owner": True, "budget_mode": "fresh_pilot", "max_runs": 1,
            "max_model_requests": 4, "token_limit": 25000, "max_active_seconds": 300,
            "deadline_at": time.time() + 600})
        runway.add_input({"id": rid, "version": reviewed["project"]["version"],
                          "request_id": "changed-context", "actor_id": "owner-fixture",
                          "actor_name": "Fixture owner", "content": "New constraint after grant.",
                          "activate": False})
        with self.assertRaisesRegex(ValueError, "Source assignment changed"):
            runway.release_revision_grant({"grant_id": held["revision_grants"][0]["id"],
                "owner_actor": "owner-fixture", "actor_owner": True, "transport_ready": True})
        self.assertEqual(len(runway.list_projects()["projects"]), 1)

    def test_held_revision_grant_rejects_forgery_stale_brief_and_budget(self):
        settled = runway.create(self.data)
        for _ in range(3):
            settled = self.finish(runway.claim())
        source = settled["artifacts"][1]
        rid = settled["project"]["id"]
        reviewed = runway.review({"id": rid, "version": settled["project"]["version"],
                                 "request_id": "grant-review", "artifact_id": source["id"],
                                 "digest": source["digest"], "decision": "revision_requested",
                                 "instruction": "Remove the unsupported promise.", "defer": True,
                                 "actor_id": "owner-fixture", "actor_name": "Fixture owner", "actor_owner": True})
        grant = {"id": rid, "version": reviewed["project"]["version"],
                 "request_id": "grant-boundaries", "review_id": reviewed["reviews"][-1]["id"],
                 "artifact_id": source["id"], "digest": source["digest"],
                 "owner_actor": "owner-fixture", "actor_owner": True,
                 "max_runs": 1, "max_model_requests": 4, "token_limit": 25000,
                 "max_active_seconds": 300, "deadline_at": time.time() + 600}
        for change, message in [({"actor_owner": False}, "Owner authorization"),
                                ({"digest": "0" * 64}, "exact deferred"),
                                ({"review_id": "0" * 32}, "exact deferred"),
                                ({"version": grant["version"] - 1}, "Stale project"),
                                ({"deadline_at": time.time() + 3600}, "within the next 30 minutes"),
                                ({"max_model_requests": 21}, "exceed the pilot envelope"),
                                ({"max_runs": 4}, "remaining recorded pilot allowance")]:
            with self.subTest(change=change), self.assertRaisesRegex(ValueError, message):
                runway.prepare_revision_grant({**grant, **change})
        with runway.connection() as conn:
            conn.execute("UPDATE marketing_profile SET version=version+1 WHERE id='marketing'")
        with self.assertRaisesRegex(ValueError, "Owner brief changed"):
            runway.prepare_revision_grant(grant)
        with runway.connection() as conn:
            state = runway.snapshot(conn, rid)
        self.assertEqual(state["revision_grants"], [])
        self.assertIsNone(runway.claim())

    def test_competing_owner_grants_cannot_release_same_review_twice(self):
        settled = runway.create(self.data)
        for _ in range(3):
            settled = self.finish(runway.claim())
        source = settled["artifacts"][1]
        reviewed = runway.review({"id": settled["project"]["id"],
                                 "version": settled["project"]["version"],
                                 "request_id": "competing-review", "artifact_id": source["id"],
                                 "digest": source["digest"], "decision": "revision_requested",
                                 "instruction": "Narrow the second claim.", "defer": True,
                                 "actor_id": "owner-fixture", "actor_name": "Fixture owner", "actor_owner": True})
        grant = {"id": reviewed["project"]["id"], "version": reviewed["project"]["version"],
                 "review_id": reviewed["reviews"][-1]["id"], "artifact_id": source["id"],
                 "digest": source["digest"], "owner_actor": "owner-fixture", "actor_owner": True,
                 "max_runs": 1, "max_model_requests": 4, "token_limit": 25000,
                 "max_active_seconds": 300, "deadline_at": time.time() + 600}
        barrier = Barrier(2)

        def prepare(request_id):
            barrier.wait()
            try:
                return "saved", runway.prepare_revision_grant({**grant, "request_id": request_id})
            except ValueError as error:
                return "rejected", str(error)

        with ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(prepare, ("device-one", "device-two")))
        self.assertEqual([status for status, _ in results].count("saved"), 1)
        self.assertEqual([status for status, _ in results].count("rejected"), 1)
        self.assertIn("already has a grant", next(value for status, value in results if status == "rejected"))
        with runway.connection() as conn:
            state = runway.snapshot(conn, grant["id"])
        self.assertEqual(len(state["revision_grants"]), 1)
        self.assertIsNone(runway.claim())

    def test_collaborator_note_at_review_does_not_grant_work_and_stale_revision_is_held(self):
        settled = runway.create(self.data)
        for _ in range(3):
            settled = self.finish(runway.claim())
        project = settled["project"]
        note = {"id": project["id"], "version": project["version"],
                "request_id": "review-note", "actor_id": "collaborator-fixture",
                "actor_name": "Fixture collaborator", "content": "Please make the first angle concrete."}
        noted = runway.add_input(note)
        self.assertEqual(noted["project"]["status"], "needs_review")
        self.assertIsNone(runway.claim())
        self.assertEqual(len(runway.add_input(note)["inputs"]), 1)
        original = noted["artifacts"][1]
        ready = runway.review({"id": project["id"], "version": noted["project"]["version"],
                               "request_id": "owner-revision", "artifact_id": original["id"],
                               "digest": original["digest"], "decision": "revision_requested",
                               "instruction": "Incorporate the collaborator's concrete first-angle request.",
                               "actor_id": "owner-fixture", "actor_name": "Fixture owner", "actor_owner": True})
        self.assertEqual(ready["project"]["status"], "needs_review")
        self.assertIsNone(runway.claim())
        held = runway.prepare_revision_grant({"id": project["id"], "version": ready["project"]["version"],
            "request_id": "collaborator-revision-grant", "review_id": ready["reviews"][-1]["id"],
            "artifact_id": original["id"], "digest": original["digest"],
            "owner_actor": "owner-fixture", "actor_owner": True, "budget_mode": "fresh_pilot",
            "max_runs": 1, "max_model_requests": 4, "token_limit": 25000,
            "max_active_seconds": 300, "deadline_at": time.time() + 600})
        runway.release_revision_grant({"grant_id": held["revision_grants"][0]["id"],
            "owner_actor": "owner-fixture", "actor_owner": True, "transport_ready": True})
        claim = runway.claim()
        with runway.connection() as conn:
            conn.execute("UPDATE tasks SET version=version+1 WHERE id=?", (claim["step"]["task_id"],))
        stale = runway.settle({"execution_id": claim["execution_id"],
                              "content": "Changed output", "source_urls": [self.data["sources"][0]["url"]],
                              "usage": {"totalTokens": 200}}, True)
        self.assertEqual(stale["executions"][-1]["status"], "stale")
        self.assertEqual(len(stale["artifacts"]), 0)
        self.assertEqual(len(runway.inspect({"id": project["id"]})["artifacts"]), 3)
        self.assertEqual(stale["project"]["status"], "needs_review")

    def test_deadline_stops_new_claim_without_model_execution(self):
        with patch.object(runway.time, "time", return_value=1000):
            runway.create(self.data)
        with patch.object(runway.time, "time", return_value=2801):
            self.assertIsNone(runway.claim())
        with runway.connection() as conn:
            state = runway.snapshot(conn)
        self.assertEqual(state["project"]["status"], "needs_review")
        self.assertIn("deadline", state["project"]["wait_reason"])
        self.assertEqual(state["executions"], [])

    def test_legacy_null_deadline_cannot_admit_or_resume_work(self):
        created = runway.create(self.data)
        rid = created["project"]["id"]
        paused = runway.change({"id": rid, "version": created["project"]["version"]}, "pause")
        with runway.connection() as conn:
            conn.execute("UPDATE runways SET deadline_at=NULL WHERE id=?", (rid,))
        with self.assertRaisesRegex(ValueError, "bounded, unexpired"):
            runway.change({"id": rid, "version": paused["project"]["version"]}, "resume")
        with runway.connection() as conn:
            conn.execute("UPDATE runways SET status='ready' WHERE id=?", (rid,))
        self.assertIsNone(runway.claim())
        with runway.connection() as conn:
            state = runway.snapshot(conn, rid)
        self.assertEqual(state["project"]["status"], "needs_review")
        self.assertIn("no bounded deadline", state["project"]["wait_reason"])
        self.assertEqual(state["executions"], [])
        with self.assertRaisesRegex(ValueError, "cannot make that transition"):
            runway.change({"id": rid, "version": state["project"]["version"]}, "pause")

    def test_legacy_null_deadline_saves_review_without_admitting_revision(self):
        settled = runway.create(self.data)
        for _ in range(3):
            settled = self.finish(runway.claim())
        rid = settled["project"]["id"]
        with runway.connection() as conn:
            conn.execute("UPDATE runways SET deadline_at=NULL WHERE id=?", (rid,))
        draft = settled["artifacts"][1]
        request = {"id": rid, "version": settled["project"]["version"],
                   "request_id": "legacy-revision", "artifact_id": draft["id"],
                   "digest": draft["digest"], "decision": "revision_requested",
                   "instruction": "Make the evidence limit explicit.",
                   "actor_id": "owner-fixture", "actor_name": "Fixture owner", "actor_owner": True}
        saved = runway.review(request)
        self.assertEqual(saved["project"]["status"], "needs_review")
        self.assertIsNone(saved["reviews"][-1]["step_id"])
        self.assertIsNone(runway.claim())


if __name__ == "__main__": unittest.main()
