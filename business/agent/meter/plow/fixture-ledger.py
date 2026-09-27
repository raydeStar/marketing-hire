"""Fictional ledger for the network-disabled Plow Gateway check only."""
import json
import sys
import time
from pathlib import Path

mode = json.loads(Path('/tmp/plow-meter-mode.json').read_text())
action = sys.argv[1]
request = json.load(sys.stdin) if action != 'meter-active' else None
if action == 'meter-active':
    result = dict(execution_id=mode['execution'], accounting_mode='post_response', deadline_at=time.time() + 90)
elif action == 'model-reserve':
    path = Path('/tmp/plow-meter-reservations.jsonl')
    earlier = [json.loads(line) for line in path.read_text().splitlines()] if path.exists() else []
    with path.open('a') as stream:
        stream.write(json.dumps(request) + '\n')
    result = dict(admitted=mode['admit'] and not any(item['request_id'] == request['request_id'] for item in earlier))
elif action == 'model-finish':
    with Path('/tmp/plow-meter-receipts.jsonl').open('a') as stream:
        stream.write(json.dumps(request) + '\n')
    result = dict(status=request['status'])
else:
    raise RuntimeError('Unsupported fictional ledger action')
print(json.dumps(result))
