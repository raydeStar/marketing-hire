"""Shift grants in the metered ledger: synthetic data only, no model or network."""
import hashlib
import os
import sqlite3
import sys
import tempfile
import time
import unittest
from pathlib import Path
from unittest.mock import patch

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

    def test_a_turn_that_never_reached_the_provider_can_be_released_and_one_that_did_cannot(self):
        grant = self.grant(turns=4, tokens=200000)
        refused = runway.shift_claim({"runway_id": grant["id"]})["execution_id"]
        # The meter refused it before reserving (too large): no model request exists, yet the host recorded unknown.
        unknown = runway.shift_settle({"execution_id": refused, "status": "unknown"})
        self.assertEqual(25000, unknown["tokens"])
        with self.assertRaisesRegex(ValueError, "unknown"):
            runway.shift_claim({"runway_id": grant["id"]})
        released = runway.shift_reconcile({"execution_id": refused})
        self.assertEqual(("failed", 25000), (released["status"], released["refunded"]))
        self.assertEqual("failed", runway.shift_reconcile({"execution_id": refused})["status"])  # replay-safe
        ok = self.turn(grant, 3000)
        self.assertEqual(3000, ok["token_used"])  # the refused turn cost nothing
        # A turn that reached the provider stays unknown.
        reached = runway.shift_claim({"runway_id": grant["id"]})["execution_id"]
        runway.reserve_model_request({"request_id": reached, "execution_id": reached, "request_digest": "c" * 64, "reserved_tokens": 25000, "accounting_mode": "post_response"})
        runway.shift_settle({"execution_id": reached, "status": "unknown"})
        with self.assertRaisesRegex(ValueError, "reached the provider"):
            runway.shift_reconcile({"execution_id": reached})

    def audit(self, eid, final):
        directory = Path(self.temp.name) / "state"
        directory.mkdir(exist_ok=True)
        audit = sqlite3.connect(directory / "openclaw.sqlite")
        try:
            audit.execute("""CREATE TABLE IF NOT EXISTS audit_events(sequence INTEGER,event_id TEXT,occurred_at INTEGER,
                kind TEXT,action TEXT,status TEXT,actor_type TEXT,actor_id TEXT,agent_id TEXT,session_key TEXT,session_id TEXT,run_id TEXT)""")
            suffix = hashlib.sha256(eid.encode()).hexdigest()[:16]
            identity = ("agent", "runway-worker", "runway-worker", f"agent:runway-worker:internal-session-effects:{eid}-{suffix}",
                        f"internal-session-effects-{eid}-{suffix}", eid)
            stamp = int(time.time() * 1000)
            for sequence, action, status in ((1, "started", "started"), (2, "finished", final)):
                audit.execute("INSERT INTO audit_events VALUES(?,?,?,?,?,?,?,?,?,?,?,?)",
                              (sequence, f"audit-{eid}-{sequence}", stamp, "agent_run", f"agent.run.{action}", status, *identity))
            audit.commit()
        finally:
            audit.close()

    def test_a_turn_the_gateway_recorded_as_failed_is_released_but_stays_billed(self):
        grant = self.grant(turns=4, tokens=200000)
        eid = runway.shift_claim({"runway_id": grant["id"]})["execution_id"]
        runway.reserve_model_request({"request_id": eid, "execution_id": eid, "request_digest": "d" * 64, "reserved_tokens": 25000, "accounting_mode": "post_response"})
        # The provider refused the sign-in (HTTP 401); the host saw an error and could only record unknown.
        runway.shift_settle({"execution_id": eid, "status": "unknown", "error": "Authentication failed (provider returned HTTP 401)."})
        self.audit(eid, "failed")
        with patch.dict(os.environ, {"OPENCLAW_STATE_DIR": self.temp.name}):
            released = runway.shift_reconcile({"execution_id": eid})
            self.assertEqual(released["status"], runway.shift_reconcile({"execution_id": eid})["status"])  # replay-safe
        self.assertEqual(("failed", 0, 25000), (released["status"], released["refunded"], released["retained"]))
        # The shift carries on, and the failed turn's reservation is still on the bill.
        self.assertEqual(28000, self.turn(grant, 3000)["token_used"])
        # A run the Gateway says succeeded, but whose usage and answer never arrived, stays unknown.
        other = runway.shift_claim({"runway_id": grant["id"]})["execution_id"]
        runway.reserve_model_request({"request_id": other, "execution_id": other, "request_digest": "e" * 64, "reserved_tokens": 25000, "accounting_mode": "post_response"})
        runway.shift_settle({"execution_id": other, "status": "unknown"})
        self.audit(other, "succeeded")
        with patch.dict(os.environ, {"OPENCLAW_STATE_DIR": self.temp.name}), self.assertRaisesRegex(ValueError, "reached the provider"):
            runway.shift_reconcile({"execution_id": other})

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

    def test_a_turn_whose_report_arrived_late_is_settled_from_it_and_the_shift_goes_on(self):
        grant = self.grant(turns=4, tokens=200000)
        self.turn(grant, 3000)
        claim = runway.shift_claim({"runway_id": grant["id"]})
        eid = claim["execution_id"]
        runway.reserve_model_request({"request_id": eid, "execution_id": eid, "request_digest": "c" * 64, "reserved_tokens": 25000, "accounting_mode": "post_response"})
        # The provider's usage is reported, then another host starting up marks the still-open turn unknown before it settles.
        runway.finish_model_request({"request_id": eid, "status": "reported", "reported_tokens": 3580})
        runway.recover()
        self.assertEqual("unknown", runway.shift_settle({"execution_id": eid, "status": "succeeded"})["status"])
        with self.assertRaisesRegex(ValueError, "reconcile before another turn"):
            runway.shift_claim({"runway_id": grant["id"]})
        healed = runway.shift_heal({"runway_id": grant["id"]})
        self.assertEqual(("shift_idle", [3580]), (healed["status"], [item["tokens"] for item in healed["healed"]]))
        # Billed what was reported, not the reservation; the next turn is granted.
        with runway.connection() as conn:
            used = conn.execute("SELECT token_used FROM runways WHERE id=?", (grant["id"],)).fetchone()[0]
        self.assertEqual(6580, used)
        self.assertEqual(6580 + 2000, self.turn(grant, 2000)["token_used"])

    def test_every_shift_action_reads_its_input_from_the_command_line(self):
        # The host drives the ledger through the CLI with JSON on stdin; an action that ignored its input would fail there only.
        import subprocess
        grant = self.grant(turns=2)
        script = str(Path(runway.__file__))
        for action, data in (("shift-heal", {"runway_id": grant["id"]}), ("shift-claim", {"runway_id": grant["id"]})):
            done = subprocess.run([sys.executable, script, action], input=__import__("json").dumps(data), capture_output=True, text=True, env={**os.environ}, timeout=60)
            self.assertEqual(0, done.returncode, done.stderr)

    def test_heal_leaves_a_turn_without_a_report_unknown(self):
        grant = self.grant(turns=3, tokens=200000)
        claim = runway.shift_claim({"runway_id": grant["id"]})
        runway.reserve_model_request({"request_id": claim["execution_id"], "execution_id": claim["execution_id"], "request_digest": "d" * 64, "reserved_tokens": 25000, "accounting_mode": "post_response"})
        runway.shift_settle({"execution_id": claim["execution_id"], "status": "succeeded"})   # no report yet: unknown, billed its reservation
        self.assertEqual(("unknown", []), (runway.shift_heal({"runway_id": grant["id"]})["status"], runway.shift_heal({"runway_id": grant["id"]})["healed"]))
        with self.assertRaises(ValueError):
            runway.shift_claim({"runway_id": grant["id"]})
        # Its report arrives later: settled from it, and the reservation billed in its place is refunded down to the report.
        with runway.connection() as conn:
            conn.execute("UPDATE runway_model_requests SET status='reported',reported_tokens=4100 WHERE execution_id=?", (claim["execution_id"],))
        self.assertEqual("shift_idle", runway.shift_heal({"runway_id": grant["id"]})["status"])
        with runway.connection() as conn:
            self.assertEqual((4100, 0), tuple(conn.execute("SELECT token_used,token_reserved FROM runways WHERE id=?", (grant["id"],)).fetchone()))

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
