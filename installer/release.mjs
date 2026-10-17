// Update channel tooling; the keys live in %USERPROFILE%\.regkeeper, never in the repo.
//   node installer/release.mjs keys          once: signing.pem + update.json (Worker url, client key, public key, B2 upload key)
//   node installer/release.mjs <file.msi>    signs the release for the MSI, uploads the MSI, then latest.txt (JSON line + signature line) to B2
import { createHash, generateKeyPairSync, randomBytes, sign } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { basename, join } from 'node:path';
import { homedir } from 'node:os';

const dir = join(homedir(), '.regkeeper'), cfgPath = join(dir, 'update.json'), keyPath = join(dir, 'signing.pem');
const sha = (alg, data) => createHash(alg).update(data).digest('hex');
const post = (token, body) => ({ method: 'POST', headers: { Authorization: token }, body: JSON.stringify(body) });
async function call(url, init) {
  const r = await fetch(url, init);
  if (!r.ok) throw new Error(`${url}: ${r.status} ${await r.text()}`);
  return r.json();
}

const arg = process.argv[2];
if (arg === 'keys') {
  if (existsSync(keyPath)) throw new Error(`${keyPath} exists: a new signing key would lock out every installed copy`);
  mkdirSync(dir, { recursive: true });
  const { privateKey, publicKey } = generateKeyPairSync('ec', { namedCurve: 'P-256' });
  writeFileSync(keyPath, privateKey.export({ type: 'pkcs8', format: 'pem' }));
  writeFileSync(cfgPath, JSON.stringify({
    url: 'https://regkeeper-update.YOUR-SUBDOMAIN.workers.dev',
    key: randomBytes(24).toString('hex'),
    pub: publicKey.export({ type: 'spki', format: 'der' }).toString('base64'),
    b2KeyId: '', b2Key: '', b2Bucket: '',
  }, null, 2));
  console.log(`written ${keyPath} and ${cfgPath}\nfill url and the b2 fields (an application key that can write to the bucket); back up both files`);
} else if (arg?.endsWith('.msi')) {
  const cfg = JSON.parse(readFileSync(cfgPath, 'utf8'));
  const file = basename(arg), version = /^RegKeeper-(\d+\.\d+\.\d+)-x64\.msi$/.exec(file)?.[1];
  if (!version) throw new Error('expected RegKeeper-x.y.z-x64.msi');
  const msi = readFileSync(arg);
  const json = Buffer.from(JSON.stringify({ version, file, sha256: sha('sha256', msi) }));
  const sig = sign('sha256', json, { key: readFileSync(keyPath), dsaEncoding: 'ieee-p1363' }).toString('base64');
  const latest = Buffer.concat([json, Buffer.from('\n' + sig)]); // one file: the release and its signature can never be out of step

  const a = await call('https://api.backblazeb2.com/b2api/v2/b2_authorize_account',
    { headers: { Authorization: 'Basic ' + Buffer.from(`${cfg.b2KeyId}:${cfg.b2Key}`).toString('base64') } });
  const bucketId = a.allowed?.bucketId ?? (await call(`${a.apiUrl}/b2api/v2/b2_list_buckets`,
    post(a.authorizationToken, { accountId: a.accountId, bucketName: cfg.b2Bucket }))).buckets[0]?.bucketId;
  if (!bucketId) throw new Error(`no bucket ${cfg.b2Bucket}`);
  // the MSI first, so latest.txt never names a file that is not there yet
  for (const [name, body] of [[file, msi], ['latest.txt', latest]]) {
    const u = await call(`${a.apiUrl}/b2api/v2/b2_get_upload_url`, post(a.authorizationToken, { bucketId }));
    await call(u.uploadUrl, { method: 'POST', body, headers: {
      Authorization: u.authorizationToken, 'X-Bz-File-Name': name, 'Content-Type': 'b2/x-auto', 'X-Bz-Content-Sha1': sha('sha1', body),
    } });
    console.log('uploaded', name);
  }
} else console.log('usage: node installer/release.mjs keys | <RegKeeper-x.y.z-x64.msi>');
