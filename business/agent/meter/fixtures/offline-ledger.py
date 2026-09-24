"""Disposable Gateway probe: recognize one worker claim and refuse all sends.

Mount this file over /opt/hire/bin/runway.py only in a network-disabled test
container. It never reads or mutates the owner's actual hire ledger.
"""

import json
import os
import sys
import time
from pathlib import Path


def main() -> int:
    action = sys.argv[1] if len(sys.argv) > 1 else ""
    if action == "meter-active":
        execution_id = "a" * 32 if os.environ.get("OFFLINE_LEDGER_ACTIVE") == "1" else None
        print(json.dumps({"execution_id": execution_id,
                          "accounting_mode": "post_response" if os.environ.get("OFFLINE_LEDGER_POST_RESPONSE") == "1" else "hard_cap",
                          "deadline_at": time.time() + 900}))
        return 0
    if action == "model-reserve":
        request = json.load(sys.stdin)
        receipt_path = Path("/tmp/offline-meter-reserve.jsonl")
        first = not receipt_path.exists()
        with receipt_path.open("a", encoding="utf-8") as receipt:
            receipt.write(json.dumps({"request_id": request.get("request_id"),
                                      "request_digest": request.get("request_digest"),
                                      "reserved_tokens": request.get("reserved_tokens")}) + "\n")
        admitted = first and os.environ.get("OFFLINE_LEDGER_ADMIT_FIRST") == "1"
        print(json.dumps({"admitted": admitted,
                          "reason": "offline fixture refuses replay" if not admitted else None}))
        return 0
    if action == "model-finish":
        request = json.load(sys.stdin)
        if not Path("/tmp/offline-meter-reserve.jsonl").exists():
            raise RuntimeError("No offline request was reserved")
        if request.get("request_id") != "a" * 32 or request.get("reported_tokens") != 8:
            raise RuntimeError("Offline usage receipt does not match")
        print(json.dumps({"status": "reported", "reported_tokens": 8}))
        return 0
    print("offline fixture does not support that ledger action", file=sys.stderr)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
