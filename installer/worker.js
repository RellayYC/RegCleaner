// Cloudflare Worker: hands the files of a private Backblaze B2 bucket only to RegKeeper.
// A request carries X-RegKeeper-Auth: <unix time>.<hex HMAC-SHA256(KEY, "<unix time>/<file>")>, valid 10 minutes either way.
// Settings > Variables and Secrets: KEY (the "key" from update.json), B2_KEY_ID, B2_KEY (a read-only B2 application key), B2_BUCKET.
const enc = new TextEncoder();
let auth; // B2 tokens live 24 h; one per isolate is plenty

async function b2(env) {
  if (auth && auth.until > Date.now()) return auth;
  const r = await fetch('https://api.backblazeb2.com/b2api/v2/b2_authorize_account', {
    headers: { Authorization: 'Basic ' + btoa(`${env.B2_KEY_ID}:${env.B2_KEY}`) },
  });
  if (!r.ok) throw new Error(`b2 auth ${r.status}`);
  const j = await r.json();
  return (auth = { token: j.authorizationToken, url: j.downloadUrl, until: Date.now() + 20 * 3600e3 });
}

async function signed(env, name, header) {
  const [t, mac] = (header ?? '').split('.');
  if (!(Math.abs(Date.now() / 1000 - Number(t)) < 600) || !/^[0-9a-f]{64}$/.test(mac ?? '')) return false;
  const key = await crypto.subtle.importKey('raw', enc.encode(env.KEY), { name: 'HMAC', hash: 'SHA-256' }, false, ['verify']);
  return crypto.subtle.verify('HMAC', key, Uint8Array.from(mac.match(/../g), (h) => parseInt(h, 16)), enc.encode(`${t}/${name}`));
}

export default {
  async fetch(req, env) {
    const name = new URL(req.url).pathname.slice(1);
    if (req.method !== 'GET' || !/^[\w.-]+$/.test(name)) return new Response('Not found', { status: 404 });
    // 403 carries the server time in Date: the app retries once with it when the PC clock is off
    if (!(await signed(env, name, req.headers.get('X-RegKeeper-Auth')))) return new Response('Forbidden', { status: 403 });
    const { token, url } = await b2(env);
    const r = await fetch(`${url}/file/${env.B2_BUCKET}/${name}`, { headers: { Authorization: token } });
    if (!r.ok) return new Response('Not found', { status: r.status === 404 ? 404 : 502 });
    const headers = { 'Content-Type': 'application/octet-stream', 'Cache-Control': 'no-store' };
    if (r.headers.has('Content-Length')) headers['Content-Length'] = r.headers.get('Content-Length');
    return new Response(r.body, { headers });
  },
};
