// Update channel tooling; the signing key lives in %USERPROFILE%\.regkeeper, never in the repo.
//   node installer/release.mjs keys          once: signing.pem + update.json (GitHub download url, public key)
//   node installer/release.mjs <file.msi>    signs the release and publishes the MSI + latest.txt (JSON line + signature line)
//                                            as GitHub release v<version>; needs the GitHub CLI logged in (gh auth login)
import { createHash, generateKeyPairSync, sign } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { basename, join } from 'node:path';
import { homedir, tmpdir } from 'node:os';

const dir = join(homedir(), '.regkeeper'), cfgPath = join(dir, 'update.json'), keyPath = join(dir, 'signing.pem');

const arg = process.argv[2];
if (arg === 'keys') {
  if (existsSync(keyPath)) throw new Error(`${keyPath} exists: a new signing key would lock out every installed copy`);
  mkdirSync(dir, { recursive: true });
  const { privateKey, publicKey } = generateKeyPairSync('ec', { namedCurve: 'P-256' });
  writeFileSync(keyPath, privateKey.export({ type: 'pkcs8', format: 'pem' }));
  writeFileSync(cfgPath, JSON.stringify({
    url: 'https://github.com/YOUR-NAME/RegKeeper/releases/latest/download',
    pub: publicKey.export({ type: 'spki', format: 'der' }).toString('base64'),
  }, null, 2));
  console.log(`written ${keyPath} and ${cfgPath}\nput your GitHub name (and repo, if not RegKeeper) into url; back up both files`);
} else if (arg?.endsWith('.msi')) {
  const cfg = JSON.parse(readFileSync(cfgPath, 'utf8'));
  const repo = /github\.com\/([^/]+\/[^/]+)\/releases\/latest\/download/.exec(cfg.url)?.[1];
  if (!repo || repo.startsWith('YOUR-NAME/')) throw new Error(`url in ${cfgPath} is not https://github.com/<name>/<repo>/releases/latest/download`);
  const file = basename(arg), version = /^RegKeeper-(\d+\.\d+\.\d+)-x64\.msi$/.exec(file)?.[1];
  if (!version) throw new Error('expected RegKeeper-x.y.z-x64.msi');
  const json = Buffer.from(JSON.stringify({ version, file, sha256: createHash('sha256').update(readFileSync(arg)).digest('hex') }));
  const sig = sign('sha256', json, { key: readFileSync(keyPath), dsaEncoding: 'ieee-p1363' }).toString('base64');
  const tmp = mkdtempSync(join(tmpdir(), 'rk-')), latest = join(tmp, 'latest.txt');
  writeFileSync(latest, Buffer.concat([json, Buffer.from('\n' + sig)])); // one file: the release and its signature can never be out of step
  try {
    // gh uploads into a draft and publishes it last, so latest/download never names a file that is not there yet
    execFileSync('gh', ['release', 'create', `v${version}`, arg, latest, '--repo', repo, '--title', `RegKeeper ${version}`, '--notes', ''], { stdio: 'inherit' });
  } finally { rmSync(tmp, { recursive: true, force: true }); }
} else console.log('usage: node installer/release.mjs keys | <RegKeeper-x.y.z-x64.msi>');
