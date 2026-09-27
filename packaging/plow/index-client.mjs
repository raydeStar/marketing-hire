import {createHash} from 'node:crypto';

// Pin the exact upstream client: a future collector may already handle zstd.
export const upstreamClientSha256 = '970caf7534cd7d3b71ffee8f1a576f9da4dc494a508e8ab1998ee2ce6f4a2ac4';
export function adaptIndexClient(source) {
  if (createHash('sha256').update(source).digest('hex') !== upstreamClientSha256) {
    throw new Error('Plow Index client changed; review its transcript reader before packaging.');
  }
  const original = `                rows = db.execute(
                    "SELECT event_json, created_at FROM transcript_events WHERE created_at >= ?",
                    (since,)).fetchall()`;
  if (source.split(original).length !== 2) throw new Error('Plow Index transcript query changed.');
  return source.replace(original,
    '                from index_transcripts import read_transcript_rows\n' +
    '                rows = read_transcript_rows(db, since)');
}
