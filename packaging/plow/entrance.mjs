import {createServer, request as httpRequest} from 'node:http';

const uid = /^[A-Za-z0-9_-]{3,128}$/;

export function publicOrigin(host) {
  if (typeof host !== 'string' || !/^[a-z0-9][a-z0-9-]*\.exe\.xyz:3000$/i.test(host))
    throw new Error('The hosted cockpit needs an authenticated Plow VM web origin.');
  return 'https://' + host.toLowerCase();
}

export function entrance({localOrigin, startHost, upstreamPort = 5184}) {
  const local = localOrigin ? new URL(localOrigin) : null;
  if (local && (!['localhost', '127.0.0.1', '[::1]'].includes(local.hostname) || local.protocol !== 'http:' || local.pathname !== '/'))
    throw new Error('Local development must use an exact HTTP loopback origin.');
  let establishedOrigin = local?.origin;
  let started;
  const server = createServer(async (incoming, outgoing) => {
    try {
      const peer = incoming.socket.remoteAddress;
      const owner = incoming.headers['x-plow-user'];
      if (local) {
        if (incoming.headers.host !== local.host ||
            (incoming.headers.origin && incoming.headers.origin !== local.origin) ||
            incoming.headers['sec-fetch-site'] === 'cross-site') {
          outgoing.writeHead(403).end(); return;
        }
      } else {
        if (!['127.0.0.1', '::1', '::ffff:127.0.0.1'].includes(peer) || typeof owner !== 'string' || !uid.test(owner)) {
          outgoing.writeHead(403).end(); return;
        }
        const origin = publicOrigin(incoming.headers.host);
        if (establishedOrigin && origin !== establishedOrigin) { outgoing.writeHead(403).end(); return; }
        establishedOrigin = origin;
      }
      // One authenticated origin for this process. Each boot learns it from Plow's owner-only ingress.
      started ??= startHost(establishedOrigin, Boolean(local), local ? 'plow-local-owner' : owner);
      await started;
      const headers = {...incoming.headers};
      for (const name of Object.keys(headers)) {
        if (name.startsWith('x-plow-') || name.startsWith('x-exedev-') || name.startsWith('x-forwarded-') ||
            ['forwarded', 'x-real-ip', 'connection'].includes(name)) delete headers[name];
      }
      headers.host = new URL(establishedOrigin).host;
      headers['x-plow-user'] = local ? 'plow-local-owner' : owner;
      const upstream = httpRequest({hostname: '127.0.0.1', port: upstreamPort, path: incoming.url, method: incoming.method, headers}, response => {
        outgoing.writeHead(response.statusCode, response.headers); response.pipe(outgoing);
      });
      upstream.on('error', () => { if (!outgoing.headersSent) outgoing.writeHead(503, {'Content-Type': 'application/json'}); outgoing.end('{"error":"The cockpit is starting. Refresh in a moment."}'); });
      outgoing.on('close', () => upstream.destroy());
      incoming.pipe(upstream);
    } catch {
      // No tokens, proxy headers or submitted content belong in startup diagnostics.
      if (!outgoing.headersSent) outgoing.writeHead(503, {'Content-Type': 'application/json'});
      outgoing.end('{"error":"The cockpit is not ready. Check the package startup log."}');
    }
  });
  return server;
}
