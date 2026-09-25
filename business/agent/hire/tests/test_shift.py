"""Shift grants in the metered ledger: synthetic data only, no model or network."""
import os
import sys
import tempfile
import time
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "bin"))
import runway


class ShiftLedgerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.prior = os.environ.get("HIRE_STATE")
        os.environ["HIRE_STATE"] = self.temp.name

    def tearDown(self):
        if self.prior is None: os.environ.pop("HIRE_STATE", None)
        else: os.environ["HIRE_STATE"] = self.prior
        self.temp.cleanup()

    def grant(self, turns=2, tokens=60000, deadline=None, request_id="shift-a"):
        return runway.shift_open({"request_id": request_id, "owner_actor": "owner", "turn_limit": turns, "token_limit": tokens,
                                  "deadline_at": deadline or time.time() + 1800, "actor_owner": True, "accept_post_response_accounting": True})

    def turn(self, grant, tokens=4000):
        claim = runway.shift_claim({"runway_id": grant["id"]})
        eid = claim["execution_id"]
        # The meter sees exactly this execution, as it does for a campaign step.
        self.assertEqual(eid, runway.meter_active()["execution_id"])
        self.assertEqual("post_response", runway.meter_active()["accounting_mode"])
        reserved = runway.reserve_model_request({"request_id": eid, "execution_id": eid, "request_digest": "a" * 64,
                                                 "reserved_tokens": 25000, "accounting_mode": "post_response"})
        self.assertTrue(reserved["admitted"])
        runway.finish_model_request({"request_id": eid, "status": "reported", "reported_tokens": tokens})
        return runway.shift_settle({"execution_id": eid, "status": "succeeded"})

    def test_grant_meters_each_turn_and_stops_at_its_allowance(self):
        with self.assertRaises(ValueError): runway.shift_open({"request_id": "x", "owner_actor": "o", "turn_limit": 2, "token_limit": 60000, "deadline_at": time.time() + 60})
        grant = self.grant()
        self.assertEqual(grant["id"], self.grant()["id"])  # replay
        self.assertIsNone(runway.meter_active()["execution_id"])
        first = self.turn(grant, 4000)
        self.assertEqual(("succeeded", 4000), (first["status"], first["tokens"]))
        second = self.turn(grant, 5000)
        self.assertEqual(9000, second["token_used"])
        with self.assertRaisesRegex(ValueError, "turn allowance"):
            runway.shift_claim({"runway_id": grant["id"]})
        closed = runway.shift_close({"request_id": "shift-a"})
        self.assertEqual("none", runway.shift_close({"request_id": "never-live"})["status"])
        self.assertEqual("completed", closed["status"])

    def test_unknown_outcome_blocks_further_turns_and_shifts_stay_out_of_campaign_views(self):
        grant = self.grant(turns=3)
        claim = runway.shift_claim({"runway_id": grant["id"]})
        with self.assertRaisesRegex(ValueError, "running"):
            runway.shift_claim({"runway_id": grant["id"]})
        runway.reserve_model_request({"request_id": claim["execution_id"], "execution_id": claim["execution_id"], "request_digest": "b" * 64,
                                      "reserved_tokens": 25000, "accounting_mode": "post_response"})
        # The provider outcome never arrived: the turn is unknown and billed at its reservation.
        settled = runway.shift_settle({"execution_id": claim["execution_id"], "status": "succeeded"})
        self.assertEqual(("unknown", 25000), (settled["status"], settled["tokens"]))
        with self.assertRaises(ValueError):
            runway.shift_claim({"runway_id": grant["id"]})
        with runway.connection() as conn:
            self.assertIsNone(runway.snapshot(conn))
        self.assertEqual([], runway.list_projects()["projects"])
        with self.assertRaisesRegex(ValueError, "Another"):
            self.grant(request_id="shift-b")

    def test_deadline_and_token_ceiling_are_enforced(self):
        grant = self.grant(turns=5, tokens=25000)
        self.turn(grant, 1000)
        with self.assertRaisesRegex(ValueError, "token allowance"):
            runway.shift_claim({"runway_id": grant["id"]})
        with runway.connection() as conn:
            conn.execute("UPDATE runways SET deadline_at=? WHERE id=?", (time.time() - 1, grant["id"]))
        with self.assertRaisesRegex(ValueError, "deadline"):
            runway.shift_claim({"runway_id": grant["id"]})


if __name__ == "__main__":
    unittest.main()
