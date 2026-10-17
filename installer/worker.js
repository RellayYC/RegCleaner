// Cloudflare Worker: hands the files of a private Backblaze B2 bucket only to RegKeeper (X-RegKeeper-Key header).
// Settings > Variables and Secrets: KEY (the "key" from update.json), B2_KEY_ID, B2_KEY (a read-only B2 application key), B2_BUCKET.
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

export default {
  async fetch(req, env) {
    const name = new URL(req.url).pathname.slice(1);
    if (req.method !== 'GET' || req.headers.get('X-RegKeeper-Key') !== env.KEY || !/^[\w.-]+$/.test(name))
      return new Response('Not found', { status: 404 });
    const { token, url } = await b2(env);
    const r = await fetch(`${url}/file/${env.B2_BUCKET}/${name}`, { headers: { Authorization: token } });
    if (!r.ok) return new Response('Not found', { status: r.status === 404 ? 404 : 502 });
    const headers = { 'Content-Type': 'application/octet-stream', 'Cache-Control': 'no-store' };
    if (r.headers.has('Content-Length')) headers['Content-Length'] = r.headers.get('Content-Length');
    return new Response(r.body, { headers });
  },
};
