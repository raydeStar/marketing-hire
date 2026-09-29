"""Small CLI contract checks against an isolated temporary hire database."""

import json
import os
import subprocess
import sqlite3
from contextlib import closing
import sys
import tempfile
import unittest
from pathlib import Path


CLI = Path(__file__).resolve().parents[1] / "bin" / "hire.py"


class TaskCliTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="marketing-hire-test-")
        self.addCleanup(self.directory.cleanup)

    def call(self, *arguments, payload=None):
        env = {**os.environ, "HIRE_STATE": self.directory.name}
        result = subprocess.run([sys.executable, str(CLI), *arguments], input=json.dumps(payload) if payload else None,
                                text=True, capture_output=True, env=env, check=False)
        return result.returncode, json.loads(result.stdout) if result.stdout else None, result.stderr

    def test_an_answer_by_text_is_added_word_for_word_and_the_task_goes_back_to_the_worker(self):
        code, task, error = self.call("task", "create", "--input-json", "-", payload={
            "request_id": "ask", "title": "Launch posts", "status": "needs_you", "action_state": "user_waiting",
            "blocker": "When does it launch?", "next_action": "Owner by text: draft 3 posts for the launch."})
        self.assertEqual(0, code, error)
        code, answered, error = self.call("task", "answer", "--id", task["id"], "--text", "It launches  October 20.")
        self.assertEqual(0, code, error)
        self.assertEqual(("ready", "agent_ready", None, task["version"] + 1), (answered["status"], answered["action_state"], answered["blocker"], answered["version"]))
        # The original ask stays; the answer follows it in the owner's words.
        self.assertEqual("Owner by text: draft 3 posts for the launch.\nOwner by text: It launches October 20.", answered["next_action"])
        # Sent twice (a retried text), it's one answer.
        code, again, error = self.call("task", "answer", "--id", task["id"], "--text", "It launches October 20.")
        self.assertEqual((0, answered["version"]), (code, again["version"]), error)
        code, _, error = self.call("task", "answer", "--id", "missing", "--text", "Hello")
        self.assertNotEqual(0, code)
        self.assertIn("task not found", error)

    def test_business_brief_keeps_evidence_examples_and_original_version(self):
        code, original, error = self.call("profile", "get")
        self.assertEqual(0, code, error)
        edit = {"request_id": "brief-evidence", "version": original["version"],
                "claims": "Only a draft; no proven ROI", "examples": "Owner writing sample"}
        code, saved, error = self.call("profile", "update", "--input-json", "-", payload=edit)
        self.assertEqual(0, code, error)
        self.assertEqual(edit["claims"], saved["claims"])
        self.assertEqual(edit["examples"], saved["examples"])
        code, replay, error = self.call("profile", "update", "--input-json", "-", payload=edit)
        self.assertEqual((0, saved), (code, replay), error)
        with closing(sqlite3.connect(Path(self.directory.name) / "hire.sqlite")) as conn:
            history = [json.loads(r[0]) for r in conn.execute(
                "SELECT content FROM marketing_profile_revisions ORDER BY version")]
        self.assertEqual([original, saved], history)
        code, _, error = self.call("profile", "update", "--input-json", "-", payload={
            **edit, "request_id": "stale-evidence", "claims": "A conflicting claim"})
        self.assertNotEqual(0, code)
        self.assertIn("stale profile version", error)

    def test_create_replay_update_and_stale_version(self):
        create = {"request_id": "create-1", "title": "Launch", "next_action": "Draft a brief",
                  "action_state": "agent_ready"}
        code, task, error = self.call("task", "create", "--input-json", "-", payload=create)
        self.assertEqual(0, code, error)
        self.assertEqual(1, task["version"])
        self.assertEqual("agent:main:marketing-task-" + task["id"], task["conversation_key"])
        code, replay, error = self.call("task", "create", "--input-json", "-", payload=create)
        self.assertEqual((0, task), (code, replay), error)
        update = {"request_id": "update-1", "version": 1, "status": "working"}
        code, changed, error = self.call("task", "update", "--id", task["id"], "--input-json", "-", payload=update)
        self.assertEqual(0, code, error)
        self.assertEqual((2, "working"), (changed["version"], changed["status"]))
        code, replay, error = self.call("task", "update", "--id", task["id"], "--input-json", "-", payload=update)
        self.assertEqual((0, changed), (code, replay), error)
        stale = {"request_id": "update-2", "version": 1, "status": "done"}
        code, _, error = self.call("task", "update", "--id", task["id"], "--input-json", "-", payload=stale)
        self.assertNotEqual(0, code)
        self.assertIn("stale task version", error)
        code, feed, error = self.call("feed", payload=None)
        self.assertEqual(0, code, error)
        self.assertEqual(["task", "task"], [item["kind"] for item in feed["events"]])

    def test_clearing_blocked_state_clears_stale_blocker(self):
        code, task, error = self.call("task", "create", "--input-json", "-", payload={
            "request_id": "blocked-create", "title": "Review message", "action_state": "blocked",
            "blocker": "Waiting for source"})
        self.assertEqual(0, code, error)
        code, changed, error = self.call("task", "update", "--id", task["id"], "--input-json", "-", payload={
            "request_id": "blocked-clear", "version": 1, "action_state": "agent_ready"})
        self.assertEqual(0, code, error)
        self.assertIsNone(changed["blocker"])

    def test_pause_survives_snapshot_with_a_single_audit_receipt(self):
        code, task, error = self.call("task", "create", "--input-json", "-", payload={
            "request_id": "pause-create", "title": "Deferred campaign", "action_state": "agent_ready"})
        self.assertEqual(0, code, error)
        change = {"request_id": "pause-once", "version": 1, "status": "paused"}
        code, paused, error = self.call("task", "update", "--id", task["id"], "--input-json", "-", payload=change)
        self.assertEqual(0, code, error)
        code, replay, error = self.call("task", "update", "--id", task["id"], "--input-json", "-", payload=change)
        self.assertEqual((0, paused), (code, replay), error)
        code, saved, error = self.call("snapshot")
        self.assertEqual(0, code, error)
        self.assertEqual("paused", saved["tasks"][0]["status"])
        self.assertEqual(2, len(saved["activity"]))
        self.assertGreater(saved["activity"][0]["id"], saved["activity"][1]["id"])
        self.assertEqual("paused", saved["activity"][0]["data"]["status"])

    def test_profile_and_evidence_are_versioned_and_replay_safe(self):
        code, profile, error = self.call("profile", "get")
        self.assertEqual(0, code, error)
        self.assertEqual("Chip", profile["display_name"])
        self.assertEqual("", profile["product_summary"])
        change = {"request_id": "profile-1", "version": profile["version"],
                  "audience": "Small teams testing marketing automation"}
        code, updated, error = self.call("profile", "update", "--input-json", "-", payload=change)
        self.assertEqual(0, code, error)
        self.assertEqual(profile["version"] + 1, updated["version"])
        code, replay, error = self.call("profile", "update", "--input-json", "-", payload=change)
        self.assertEqual((0, updated), (code, replay), error)
        code, _, error = self.call("profile", "update", "--input-json", "-", payload={
            "request_id": "profile-2", "version": profile["version"], "audience": "Other"})
        self.assertNotEqual(0, code)
        self.assertIn("stale profile version", error)

        code, task, error = self.call("task", "create", "--input-json", "-", payload={
            "request_id": "task-with-source", "title": "Research source"})
        self.assertEqual(0, code, error)
        source = {"request_id": "source-1", "url": "https://example.org/thread", "title": "Useful discussion",
                  "note": "Relevant question", "query": "marketing agent", "source": "hackernews"}
        code, proof, error = self.call("evidence", "add", "--task-id", task["id"], "--input-json", "-", payload=source)
        self.assertEqual(0, code, error)
        code, replay, error = self.call("evidence", "add", "--task-id", task["id"], "--input-json", "-", payload=source)
        self.assertEqual((0, proof), (code, replay), error)
        code, snapshot, error = self.call("snapshot")
        self.assertEqual(0, code, error)
        self.assertEqual(updated["audience"], snapshot["profile"]["audience"])
        self.assertEqual(proof["id"], snapshot["evidence"][0]["id"])

    def test_draft_decision_binds_exact_revision_and_content(self):
        args = ("draft", "add", "--channel", "local-test", "--destination", "https://example.org/thread",
                "--content", "Test draft; do not post", "--rationale", "Exercise the local gate",
                "--rules-url", "UNVERIFIED")
        code, added, error = self.call(*args)
        self.assertEqual(0, code, error)
        code, draft, error = self.call("draft", "get", "--id", str(added["draft"]))
        self.assertEqual(0, code, error)
        decide = ("draft", "decide", "--id", str(added["draft"]), "--decision", "approved",
                  "--by", "Local acceptance test", "--revision", str(draft["revision"]),
                  "--digest", draft["digest"], "--request-id", "decision-1")
        code, _, error = self.call(*decide[:-4], "--digest", "0" * 64, "--request-id", "wrong-digest")
        self.assertNotEqual(0, code)
        self.assertIn("stale draft revision or content", error)
        code, decided, error = self.call(*decide)
        self.assertEqual(0, code, error)
        self.assertEqual("approved", decided["status"])
        code, replay, error = self.call(*decide)
        self.assertEqual((0, decided), (code, replay), error)
        code, _, error = self.call(*decide[:-2], "--request-id", "decision-2")
        self.assertNotEqual(0, code)
        self.assertIn("already approved", error)


if __name__ == "__main__":
    unittest.main()
