"""Small CLI contract checks against an isolated temporary hire database."""

import json
import os
import subprocess
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


if __name__ == "__main__":
    unittest.main()
