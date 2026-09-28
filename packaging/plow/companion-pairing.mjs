import {createHash, randomBytes, timingSafeEqual} from 'node:crypto';
import {mkdir, readFile, rename, writeFile} from 'node:fs/promises';
import path from 'node:path';

/** One owner-authenticated setup operation; no catalog settings or public shared keys. */
export function companionPairing({file, identity}) {
  const nonce = randomBytes(32).toString('hex');
  const digest = value => createHash('sha256').update(value).digest('hex');
  function validate(value, current) {
    if (value?.version !== 1 || value.workspace !== current.workspace || value.ownerUid !== current.ownerUid ||
        value.brokerOrigin !== 'https://hirezero.app' || value.workspaceOrigin !== `https://${current.workspace}.work.hirezero.app` ||
        !/^[A-Za-z0-9_-]{43,100}$/.test(value.credential || '')) throw new Error('Invalid companion binding');
    return value;
  }
  async function read(current = identity()) {
    try {
      const text = await readFile(file, 'utf8');
      if (text.length > 4096) throw new Error('Oversized binding');
      return validate(JSON.parse(text), current);
    } catch (error) { if (error.code === 'ENOENT') return null; throw error; }
  }
  async function handle(incoming, outgoing, owner, origin) {
    const current = identity();
    const send = (status, value) => outgoing.writeHead(status, {'Content-Type': 'application/json', 'Cache-Control': 'no-store',
      'X-Content-Type-Options': 'nosniff', 'Referrer-Policy': 'no-referrer'}).end(JSON.stringify(value));
    if (!current || owner !== current.ownerUid || origin !== `https://${current.workspace}.plow.run`) return send(403, {error: 'Owner sign-in required.'});
    if (incoming.method === 'POST') {
      const supplied = incoming.headers['x-hirezero-setup'];
      if (incoming.headers.origin !== origin || incoming.headers['content-type'] !== 'application/json' ||
          typeof supplied !== 'string' || !/^[a-f0-9]{64}$/.test(supplied) || !timingSafeEqual(Buffer.from(supplied), Buffer.from(nonce)))
        return send(403, {error: 'Refresh the owner connection.'});
      let body = '';
      for await (const chunk of incoming) { body += chunk; if (Buffer.byteLength(body) > 4096) return send(413, {error: 'Connection is too large.'}); }
      let value;
      try { value = validate(JSON.parse(body), current); } catch { return send(400, {error: 'This connection does not belong to this workspace.'}); }
      await mkdir(path.dirname(file), {recursive: true, mode: 0o700});
      await writeFile(file + '.new', JSON.stringify(value), {mode: 0o600});
      await rename(file + '.new', file);
    } else if (incoming.method !== 'GET') return send(405, {error: 'Unsupported connection request.'});
    const value = await read(current);
    return send(200, {protocol: 1, workspace: current.workspace, owner: current.ownerUid, nonce,
      credentialHash: value ? digest(value.credential) : null});
  }
  return {read, handle};
}
