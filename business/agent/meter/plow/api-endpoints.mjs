// The deployment supplies this trust boundary. Cloud installs use an authenticated
// per-install proxy; its host, port and path need not match the public Plow API.
export function plowApiEndpoints(apiBase = process.env.PLOW_API_BASE ?? 'https://api.plow.co') {
  try {
    if (typeof apiBase !== 'string' || !apiBase.trim()) return null;
    const root = new URL(apiBase);
    if (!['http:', 'https:'].includes(root.protocol) || root.username || root.password || root.search || root.hash)
      return null;
    const base = root.href.replace(/\/+$/, '');
    const api = base + '/v1';
    return Object.freeze({base, api, completion: api + '/chat/completions'});
  } catch { return null; }
}

export function isPlowChannelRequest(requestUrl, endpoints) {
  if (!endpoints) return false;
  const url = new URL(requestUrl);
  if (url.username || url.password || url.hash) return false;
  const target = url.origin + url.pathname;
  if (!target.startsWith(endpoints.api + '/')) return false;
  const path = target.slice(endpoints.api.length);
  return /^\/(?:chats|lines|agents|auth|identity)(?:\/|$)/.test(path) || path === '/ws/ticket';
}
