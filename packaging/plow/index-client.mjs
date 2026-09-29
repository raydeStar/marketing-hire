import {createHash} from 'node:crypto';

// Base 771198a owns JSON/zstd decoding. Review future collectors before adapting them.
export const upstreamClientSha256 = '5be521644ade0f041e83370ac457edc8ad85410e14265f1b1243807772de9a5b';

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
  const decodeAnchor = '                raw = _event_json(raw, blob, utf8_bytes)';
  if (source.split(decodeAnchor).length !== 2) throw new Error('Plow Index transcript decoder changed.');
  const workerAnchor = '    return out\n\n\ndef from_hermes(';
  if (source.split(workerAnchor).length !== 2) throw new Error('Plow Index collector boundary changed.');
  return adaptIndexCollectorExecution(source.replace(decodeAnchor,
    // An unreadable row is not an idle employee; never report a smaller day.
    '                if raw is None and blob is None:\n' +
    '                    raise ValueError("transcript event has no JSON or compressed payload")\n' +
    decodeAnchor).replace(workerAnchor,
    '    try:\n' +
    '        from index_worker import add_worker_usage\n' +
    '        add_worker_usage(root, since, out, seen)\n' +
    '    except sqlite3.Error as error:\n' +
    '        FAILURES.append(f"hirezero worker: {type(error).__name__}: {error}")\n' +
    workerAnchor));
}
