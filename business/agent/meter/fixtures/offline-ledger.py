"""Disposable Gateway probe: recognize one worker claim and refuse all sends.

Mount this file over /opt/hire/bin/runway.py only in a network-disabled test
container. It never reads or mutates the owner's actual hire ledger.
"""

import json
import os
import sys


def main() -> int:
    action = sys.argv[1] if len(sys.argv) > 1 else ""
    if action == "meter-active":
        execution_id = "a" * 32 if os.environ.get("OFFLINE_LEDGER_ACTIVE") == "1" else None
        print(json.dumps({"execution_id": execution_id}))
        return 0
    if action == "model-reserve":
        json.load(sys.stdin)
        print(json.dumps({"admitted": False, "reason": "offline fixture refuses dispatch"}))
        return 0
    print("offline fixture does not support that ledger action", file=sys.stderr)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
