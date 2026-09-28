import {createHash} from 'node:crypto';

// Pin the exact upstream client: a future collector may already handle zstd.
export const upstreamClientSha256 = '970caf7534cd7d3b71ffee8f1a576f9da4dc494a508e8ab1998ee2ce6f4a2ac4';

export function adaptIndexCollectorExecution(source) {
  const before = '[exe, "usage", "daily", "--json"]';
  const after = '[exe, "usage", "daily", "--json", "--offline", "--no-sync"]';
  if (source.split(after).length === 2 && !source.includes(before)) return source;
  if (source.split(before).length !== 2) throw new Error('Plow Index agentsview invocation changed.');
  // The boot loop has just synced. Query it without starting a second household.
  return source.replace(before, after);
}

export function adaptIndexClient(source) {
  if (createHash('sha256').update(source).digest('hex') !== upstreamClientSha256) {
    throw new Error('Plow Index client changed; review its transcript reader before packaging.');
  }
  const original = `                rows = db.execute(
                    "SELECT event_json, created_at FROM transcript_events WHERE created_at >= ?",
                    (since,)).fetchall()`;
  if (source.split(original).length !== 2) throw new Error('Plow Index transcript query changed.');
  const workerAnchor = '    return out\n\n\ndef from_hermes(';
  if (source.split(workerAnchor).length !== 2) throw new Error('Plow Index collector boundary changed.');
  return adaptIndexCollectorExecution(source.replace(original,
    '                from index_transcripts import read_transcript_rows\n' +
    '                rows = read_transcript_rows(db, since)').replace(workerAnchor,
    '    try:\n' +
    '        from index_worker import add_worker_usage\n' +
    '        add_worker_usage(root, since, out, seen)\n' +
    '    except sqlite3.Error as error:\n' +
    '        FAILURES.append(f"hirezero worker: {type(error).__name__}: {error}")\n' +
    workerAnchor));
}
