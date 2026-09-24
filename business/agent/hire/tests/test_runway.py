"""Synthetic ledger fixtures: no model, network, or real owner data."""
import os
import tempfile
import unittest
from pathlib import Path
import sys
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
        if self.prior is None: os.environ.pop("HIRE_STATE", None)
        else: os.environ["HIRE_STATE"] = self.prior
        self.temp.cleanup()

    def finish(self, claim, usage=100):
        return runway.settle({"execution_id": claim["execution_id"], "content": "Fixture validated deliverable",
                              "source_urls": [self.data["sources"][0]["url"]],
                              "usage": {"totalTokens": usage}}, True)

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
        self.assertEqual(state["project"]["status"], "unknown")
        self.assertEqual(state["project"]["token_reserved"], 25000)

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

    def test_owner_review_revises_exact_artifact_then_approves_without_publishing(self):
        settled = runway.create(self.data)
        for _ in range(3):
            settled = self.finish(runway.claim())
        self.assertIsNone(runway.claim())
        original = settled["artifacts"][1]
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
        self.assertEqual(revised["project"]["status"], "ready")
        self.assertEqual(len(revised["reviews"]), 1)
        self.assertEqual(len(runway.review(request)["reviews"]), 1)
        claim = runway.claim()
        self.assertEqual(claim["step"]["kind"], "revision_angles")
        self.assertEqual(claim["review"]["artifact_id"], original["id"])
        self.assertEqual(claim["review"]["instruction"], request["instruction"])
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
        self.assertEqual(ready["project"]["status"], "ready")
        claim = runway.claim()
        with runway.connection() as conn:
            conn.execute("UPDATE tasks SET version=version+1 WHERE id=?", (claim["step"]["task_id"],))
        stale = runway.settle({"execution_id": claim["execution_id"],
                              "content": "Changed output", "source_urls": [self.data["sources"][0]["url"]],
                              "usage": {"totalTokens": 200}}, True)
        self.assertEqual(stale["executions"][-1]["status"], "stale")
        self.assertEqual(len(stale["artifacts"]), 3)
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

    def test_legacy_null_deadline_cannot_admit_revision(self):
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
        with self.assertRaisesRegex(ValueError, "bounded, unexpired"):
            runway.review(request)
        saved = runway.review({**request, "defer": True})
        self.assertIsNone(saved["reviews"][-1]["step_id"])
        self.assertIsNone(runway.claim())


if __name__ == "__main__": unittest.main()
