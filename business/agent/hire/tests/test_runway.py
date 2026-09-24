"""Synthetic ledger fixtures: no model, network, or real owner data."""
import os
import tempfile
import unittest
from pathlib import Path
import sys
import time

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

    def test_source_url_must_be_exactly_allowlisted_shape(self):
        self.data["sources"][0]["url"] += "&redirect=https://example.com"
        with self.assertRaises(ValueError):
            runway.create(self.data)


if __name__ == "__main__": unittest.main()
