"""Read actual Plow worker receipts omitted by OpenClaw model-run transcripts.

No reservations, legacy estimates, synthetic fixtures or message bodies count.
The caller shares the official collector's response IDs and local-day buckets.
"""
import datetime
import json
import math
from pathlib import Path
import re
import sqlite3


def add_worker_usage(root, since, out, seen):
    ledger = Path(root) / 'hire' / 'hire.sqlite'
    if not ledger.exists():
        return  # A fresh install has no worker ledger until it does work.
    db = sqlite3.connect(ledger.resolve().as_uri() + '?mode=ro', uri=True)
    try:
        tables = {row[0] for row in db.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        if 'runway_model_requests' not in tables:
            return  # Onboarding creates the hire ledger before any worker runs.
        rows = db.execute('''SELECT m.request_digest, m.reported_tokens,
            m.created_at, r.request_digest, r.response_json
            FROM runway_model_requests m
            JOIN runway_executions e ON e.id=m.execution_id
            JOIN runways p ON p.id=e.runway_id
            LEFT JOIN runway_response_receipts r ON r.request_id=m.request_id
            WHERE m.status IN ('reported','overrun') AND m.created_at >= ?
            AND NOT EXISTS (SELECT 1 FROM runway_sources s
              WHERE (s.runway_id=p.id OR s.runway_id=p.pilot_root_id)
              AND s.url LIKE 'fixture://%')''', (since / 1000,)).fetchall()
    finally:
        db.close()
    for digest, total, stamp, receipt_digest, raw in rows:
        try:
            receipt = json.loads(raw)
            if not isinstance(receipt, dict):
                raise ValueError('receipt must be an object')
            # This terminal type is emitted only by the package's pinned Plow
            # completion meter; other transports have different terminal types.
            if receipt.get('terminal_type') != 'chat.completion.done':
                continue
            if (receipt.get('provider', 'plow') != 'plow'
                    or receipt.get('model', 'z-ai/glm-5.2') != 'z-ai/glm-5.2'):
                raise ValueError('unexpected provider or model')
            response = receipt.get('provider_response_id')
            inputs, outputs = receipt.get('input_tokens'), receipt.get('output_tokens')
            if (digest != receipt_digest or not re.fullmatch('[a-f0-9]{64}', digest or '')
                    or not isinstance(response, str) or not 1 <= len(response) <= 200
                    or any(type(n) is not int or n < 0 for n in (inputs, outputs, total))
                    or inputs + outputs != total
                    or not re.fullmatch('[a-f0-9]{64}', receipt.get('evidence_digest', ''))
                    or not isinstance(stamp, (int, float)) or not math.isfinite(stamp) or stamp <= 0):
                raise ValueError('inconsistent confirmed usage')
            date = datetime.datetime.fromtimestamp(stamp).date().isoformat()
        except (TypeError, ValueError, OverflowError, OSError) as error:
            raise sqlite3.DatabaseError('Invalid HireZero worker usage receipt') from error
        if response in seen:
            continue
        seen.add(response)
        bucket = out[date]['z-ai/glm-5.2']
        # Plow's receipt gives the complete prompt count, not a cache split.
        # Report it once as input; never add an invented cache counter.
        bucket['input'] += inputs
        bucket['output'] += outputs
