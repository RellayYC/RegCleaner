// Gate oracles for GATES.md. Each subcommand asserts everything it checks, exits 1 on the first failure
// and prints its success marker only at the end. Usage: node scripts/gates/check.mjs <name> [args]
import { execFileSync, spawnSync } from 'node:child_process';
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';

const root = join(import.meta.dirname, '..', '..');
const src = join(root, 'src', 'RegKeeper');
const fail = (msg) => { console.error('FAIL: ' + msg); process.exit(1); };
const read = (p) => existsSync(p) ? readFileSync(p, 'utf8').replace(/^﻿/, '') : fail('missing ' + relative(root, p));
const walk = (dir, out = []) => {
  for (const n of readdirSync(dir)) {
    if (['bin', 'obj', '.git', 'node_modules'].includes(n)) continue;
    const p = join(dir, n);
    statSync(p).isDirectory() ? walk(p, out) : out.push(p);
  }
  return out;
};

/** RFC 4180 CSV -> array of objects keyed by the header row. */
function csv(path) {
  const text = read(path), rows = [];
  let row = [], cell = '', q = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (q) {
      if (c === '"' && text[i + 1] === '"') { cell += '"'; i++; }
      else if (c === '"') q = false;
      else cell += c;
    } else if (c === '"') q = true;
    else if (c === ',') { row.push(cell); cell = ''; }
    else if (c === '\n' || c === '\r') {
      if (c === '\r' && text[i + 1] === '\n') i++;
      row.push(cell); cell = '';
      if (row.some((x) => x !== '')) rows.push(row);
      row = [];
    } else cell += c;
  }
  row.push(cell);
  if (row.some((x) => x !== '')) rows.push(row);
  const [head, ...body] = rows;
  return body.map((r, n) => {
    if (r.length !== head.length) fail(`${relative(root, path)} row ${n + 2}: ${r.length} cells, header has ${head.length}`);
    return Object.fromEntries(head.map((h, i) => [h.trim(), r[i].trim()]));
  });
}

const navViews = () => [...read(join(src, 'MainWindow.xaml.cs')).matchAll(/typeof\((\w+View)\)/g)].map((m) => m[1]);

const checks = {
  /** dotnet test (optionally filtered): 0 failed, at least `min` passed. */
  tests(filter, min = '1') {
    const args = ['test', join(root, 'RegKeeper.sln'), '--nologo'];
    if (filter) args.push('--filter', `FullyQualifiedName~${filter}`);
    const r = spawnSync('dotnet', args, { encoding: 'utf8', maxBuffer: 64 << 20 });
    const out = r.stdout + r.stderr;
    const m = out.match(/Failed:\s+(\d+), Passed:\s+(\d+)/);
    if (r.status !== 0 || !m) fail('dotnet test failed\n' + out.split('\n').filter((l) => /error|Failed|Assert/.test(l)).slice(0, 30).join('\n'));
    if (+m[1] !== 0) fail(`${m[1]} tests failed`);
    if (+m[2] < +min) fail(`only ${m[2]} tests passed, need ${min}`);
    console.log(`TESTS GREEN passed=${m[2]}`);
  },

  /** Log of scripts/gates/admin-run.cmd: high integrity, admin tests passed, nothing failed, newer than the tests. */
  admin() {
    const log = join(root, 'artifacts', 'admin', 'run.txt');
    const text = read(log);
    if (!text.includes('S-1-16-12288')) fail('run was not elevated (no high integrity level)');
    for (const t of ['ForceRule_DeletesTrustedInstallerFiles_AsAdmin', 'ComponentStore_RealReportParses_AsAdmin'])
      if (!new RegExp(`Passed\\s+\\S*${t}`).test(text)) fail(t + ' did not pass');
    const m = text.match(/Passed:\s+(\d+)/);
    if (!text.includes('Test Run Successful.') || !m || /Failed:\s+[1-9]/.test(text)) fail('elevated suite did not succeed');
    const newest = Math.max(...walk(join(root, 'tests')).concat(walk(src)).filter((p) => p.endsWith('.cs')).map((p) => statSync(p).mtimeMs));
    if (statSync(log).mtimeMs < newest) fail('log is older than the code, rerun scripts/gates/admin-run.cmd elevated');
    console.log(`ADMIN OK passed=${m[1]}`);
  },

  /** Tweak catalog: enough entries, every one complete and well-typed. */
  tweaks(min = '30') {
    const list = JSON.parse(read(join(src, 'Data', 'tweaks.json')));
    const names = new Set();
    for (const t of list) {
      const id = t.name ?? '?';
      if (!t.category || !t.name || !t.description) fail(`tweak ${id}: category/name/description required`);
      if (names.has(t.name)) fail('duplicate tweak ' + t.name);
      names.add(t.name);
      if (t.restart && !['explorer', 'logoff', 'reboot'].includes(t.restart)) fail(`${id}: bad restart ${t.restart}`);
      if (!t.values?.length) fail(`${id}: no values`);
      for (const v of t.values) {
        if (!/^HKEY_(LOCAL_MACHINE|CURRENT_USER)\\/.test(v.key ?? '')) fail(`${id}: key must be a full HKLM/HKCU path: ${v.key}`);
        if (typeof v.name !== 'string') fail(`${id}: value name missing`);
        if (v.offDeleteKey && !v.key.toLowerCase().startsWith(v.offDeleteKey.toLowerCase() + '\\')) fail(`${id}: offDeleteKey must be an ancestor of key`);
        const type = v.type ?? 'dword';
        if (!['dword', 'qword', 'sz', 'expand'].includes(type)) fail(`${id}: bad type ${type}`);
        if (v.on === undefined || v.on === null) fail(`${id}: "on" must set something`);
        for (const x of [v.on, v.off]) {
          if (x === undefined || x === null) continue;
          if (['dword', 'qword'].includes(type) ? !Number.isInteger(x) || x < 0 : typeof x !== 'string') fail(`${id}: value ${x} does not fit ${type}`);
        }
        if (JSON.stringify(v.on) === JSON.stringify(v.off ?? null)) fail(`${id}: on == off`);
      }
    }
    if (list.length < +min) fail(`${list.length} tweaks, need ${min}`);
    console.log(`TWEAKS CATALOG OK n=${list.length}`);
  },

  /** Every navigation entry is a real XAML view; no stubs left. */
  views() {
    if (existsSync(join(src, 'Modules', '_Stubs.cs'))) fail('Modules/_Stubs.cs still exists');
    const xaml = new Set(walk(src).filter((p) => p.endsWith('.xaml')).map((p) => p.split(/[\\/]/).pop().replace('.xaml', '')));
    const views = navViews();
    if (views.length < 12) fail(`only ${views.length} nav views found`);
    for (const v of views) if (!xaml.has(v)) fail(`${v} has no .xaml`);
    console.log(`ALL VIEWS REAL n=${views.length}`);
  },

  /** Recon artifacts: map covers every module, feature matrix is well-formed and covers every module. */
  recon() {
    const map = read(join(root, 'replica', 'recon-map.md'));
    const rows = csv(join(root, 'replica', 'features.csv'));
    const areas = new Set(rows.map((r) => r.area));
    for (const r of rows) {
      if (!r.feature || !r.area) fail('row without feature/area: ' + JSON.stringify(r));
      if (!['must', 'should', 'could'].includes(r.priority)) fail(`${r.feature}: priority ${r.priority}`);
      if (!['yes', 'no'].includes(r.original)) fail(`${r.feature}: original ${r.original}`);
      if (!['yes', 'partial', 'no', 'skip'].includes(r.clone)) fail(`${r.feature}: clone ${r.clone}`);
      if (r.clone === 'skip' && !r.notes) fail(`${r.feature}: skip without a reason`);
      if (r.original === 'no' && !/^F\d+$/.test(r.ref ?? '')) fail(`${r.feature}: added feature must ref a feedback id`);
    }
    const titles = [...read(join(src, 'MainWindow.xaml.cs')).matchAll(/new\("([^"]+)", "[^"]*", typeof/g)].map((m) => m[1]);
    for (const t of titles) {
      if (!areas.has(t)) fail(`features.csv has no rows for module "${t}"`);
      if (!map.includes('## ' + t)) fail(`recon-map.md has no "## ${t}" section`);
    }
    console.log(`RECON OK features=${rows.length} areas=${areas.size}`);
  },

  /** Reviews: every row linked and quoted; feedback themes have ids and every id is in the feature matrix. */
  reviews(min = '15') {
    const rows = csv(join(root, 'replica', 'reviews.csv'));
    const sources = new Set();
    for (const r of rows) {
      if (!/^https?:\/\/\S+$/.test(r.url)) fail('review without a link: ' + r.text?.slice(0, 60));
      if ((r.text ?? '').length < 15) fail('review text too short: ' + r.url);
      if (!/^F\d+$/.test(r.theme ?? '')) fail('review without a theme id: ' + r.url);
      sources.add(new URL(r.url).hostname.replace(/^www\./, ''));
    }
    if (rows.length < +min) fail(`${rows.length} reviews, need ${min}`);
    if (sources.size < 3) fail(`reviews from ${sources.size} sites, need 3`);
    const fb = read(join(root, 'replica', 'feedback.md'));
    const ids = new Set([...fb.matchAll(/^#{2,3} (F\d+)\b/gm)].map((m) => m[1]));
    if (ids.size === 0) fail('feedback.md has no "## F<n>" themes');
    for (const r of rows) if (!ids.has(r.theme)) fail(`review theme ${r.theme} missing in feedback.md`);
    const refs = new Set(csv(join(root, 'replica', 'features.csv')).map((r) => r.ref).filter(Boolean));
    for (const id of ids) if (!refs.has(id) && !new RegExp(`^#{2,3} ${id}\\b.*\\n(?:.*\\n)*?Решение: отклонено`, 'm').test(fb)) fail(`${id} is neither in features.csv nor rejected in feedback.md`);
    console.log(`REVIEWS OK n=${rows.length} sites=${sources.size} themes=${ids.size}`);
  },

  /** Weighted parity of the clone against the original's features (added features excluded). */
  parity(shouldMin = '0.9') {
    const rows = csv(join(root, 'replica', 'features.csv')).filter((r) => r.original === 'yes' && r.clone !== 'skip');
    const credit = { yes: 1, partial: 0.5, no: 0 };
    const score = (p) => { const s = rows.filter((r) => r.priority === p); return [s.reduce((a, r) => a + credit[r.clone], 0), s.length]; };
    const [mustDone, must] = score('must'), [shouldDone, should] = score('should');
    const missing = rows.filter((r) => r.priority !== 'could' && r.clone !== 'yes').map((r) => `${r.priority}: ${r.area} / ${r.feature} (${r.clone})`);
    if (must === 0) fail('no must features in the matrix');
    if (mustDone < must) fail(`must ${mustDone}/${must}\n` + missing.join('\n'));
    if (should && shouldDone / should < +shouldMin) fail(`should ${shouldDone}/${should} < ${shouldMin}\n` + missing.join('\n'));
    const added = csv(join(root, 'replica', 'features.csv')).filter((r) => r.original === 'no');
    if (added.some((r) => r.priority === 'must' && r.clone !== 'yes')) fail('a must feature from feedback is not done');
    console.log(`PARITY OK must=${mustDone}/${must} should=${shouldDone}/${should} added=${added.filter((r) => r.clone === 'yes').length}/${added.length}`);
  },

  /** WCAG contrast of every text/background pair in both themes. */
  contrast() {
    // #AARRGGBB is laid over the theme's Bg first, which is how translucent overlays are actually seen
    const rgb = (hex, under) => {
      const h = hex.replace('#', ''), c = h.slice(-6).match(/../g).map((x) => parseInt(x, 16) / 255);
      if (h.length < 8) return c;
      const a = parseInt(h.slice(0, 2), 16) / 255, u = rgb(under);
      return c.map((v, i) => a * v + (1 - a) * u[i]);
    };
    let bg;
    const lum = (hex) => {
      const c = rgb(hex, bg).map((v) => v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4);
      return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
    };
    const ratio = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };
    // [foreground, background, minimum]: 4.5 body text, 3 for large text and non-text UI
    const pairs = [['Text', 'Bg', 4.5], ['Text', 'Pane', 4.5], ['Text', 'Surface', 4.5], ['Muted', 'Bg', 4.5], ['Muted', 'Pane', 4.5],
      ['OnAccent', 'Accent', 4.5], ['OnAccent', 'AccentHover', 4.5], ['OnAccent', 'Danger', 4.5], ['Accent', 'Bg', 3], ['Border', 'Bg', 1.3],
      ['Text', 'Selection', 4.5], ['Text', 'Alt', 4.5], ['Text', 'Hover', 4.5], ['DangerText', 'Bg', 4.5], ['DangerText', 'Surface', 4.5], ['Muted', 'Surface', 4.5],
      ['Subtle', 'Surface', 4.5], ['Muted', 'CardTop', 4.5], ['Subtle', 'CardTop', 4.5], ['Text', 'Tile1', 4.5], ['OnAccent', 'SegFirst', 4.5],
      ['OnDanger', 'Danger', 4.5], ['OnDanger', 'DangerDeep', 4.5], ['DangerText', 'DangerSoft', 4.5], ['DangerText', 'CardTop', 4.5], ['Text', 'HotTop', 4.5]];
    let n = 0;
    for (const theme of ['Light', 'Dark']) {
      const x = read(join(src, 'Themes', theme + '.xaml'));
      const colors = Object.fromEntries([...x.matchAll(/<Color x:Key="(\w+)">(#[0-9A-Fa-f]{6,8})<\/Color>/g)].map((m) => [m[1], m[2]]));
      bg = colors.Bg;
      for (const [f, b, min] of pairs) {
        if (!colors[f] || !colors[b]) fail(`${theme}: color ${colors[f] ? b : f} not defined`);
        const r = ratio(colors[f], colors[b]);
        if (r < min) fail(`${theme}: ${f} on ${b} = ${r.toFixed(2)} < ${min}`);
        n++;
      }
    }
    console.log(`CONTRAST OK pairs=${n}`);
  },

  /** Views take colours from the theme only (so dark mode works). Self-tests the scanner on a known positive. */
  colors() {
    const rx = /(Background|Foreground|BorderBrush|Fill|Stroke|Color)="(#[0-9A-Fa-f]{3,8}|White|Black|Gray|LightGray|Red|Blue|Green)"/g;
    if (!'<Border Background="#FFFFFF"/>'.match(rx)) fail('scanner self-test failed');
    const bad = [];
    for (const p of walk(src).filter((p) => p.endsWith('.xaml') && !/[\\/]Themes[\\/]/.test(p)))
      for (const m of read(p).matchAll(rx)) bad.push(`${relative(root, p)}: ${m[0]}`);
    if (bad.length) fail('hardcoded colours:\n' + bad.join('\n'));
    console.log('NO HARDCODED COLORS');
  },

  /** QA: test plan covers every module; no open S1/S2 bugs. */
  qa() {
    const plan = read(join(root, 'replica', 'test-plan.md'));
    const titles = [...read(join(src, 'MainWindow.xaml.cs')).matchAll(/new\("([^"]+)", "[^"]*", typeof/g)].map((m) => m[1]);
    for (const t of titles) if (!plan.includes('## ' + t)) fail(`test-plan.md has no "## ${t}"`);
    const bugs = read(join(root, 'replica', 'bugs.md')).split(/^### /m).slice(1);
    const open = bugs.filter((b) => /Severity: S[12]/.test(b) && /Status: open/.test(b)).map((b) => b.split('\n')[0]);
    if (open.length) fail('open S1/S2 bugs:\n' + open.join('\n'));
    const unresolved = bugs.filter((b) => !/Status: (open|fixed in [0-9a-f]{7}|won't fix: \S)/.test(b));
    if (unresolved.length) fail('bugs with a malformed Status:\n' + unresolved.map((b) => b.split('\n')[0]).join('\n'));
    console.log(`QA OK bugs=${bugs.length} open=${bugs.filter((b) => /Status: open/.test(b)).length}`);
  },

  /** Nothing of the original's name left in what ships. Self-tests on MODLOG.md, which legitimately names it. */
  sweep() {
    const rx = /reg\s*organi[sz]er|chemtable|regorganizer/i;
    if (!rx.test(read(join(root, 'MODLOG.md')))) fail('positive control failed: MODLOG.md should mention the original');
    const shipped = [...walk(join(root, 'src')), ...walk(join(root, 'tests')), ...['README.md', 'LICENSE'].map((f) => join(root, f)).filter(existsSync)]
      .filter((p) => /\.(cs|xaml|json|md|txt|ini|manifest|csproj|resx)$|LICENSE$/.test(p));
    const hits = shipped.filter((p) => rx.test(readFileSync(p, 'utf8'))).map((p) => relative(root, p));
    if (hits.length) fail('original name found in:\n' + hits.join('\n'));
    console.log(`SWEEP CLEAN files=${shipped.length}`);
  },

  /** Own icon wired into the exe and the window. */
  icon() {
    const proj = read(join(src, 'RegKeeper.csproj'));
    const m = proj.match(/<ApplicationIcon>([^<]+)<\/ApplicationIcon>/) ?? fail('csproj has no ApplicationIcon');
    const ico = readFileSync(join(src, m[1]));
    if (ico.readUInt16LE(0) !== 0 || ico.readUInt16LE(2) !== 1 || ico.readUInt16LE(4) < 3) fail('icon is not a multi-size .ico');
    if (!/Icon="/.test(read(join(src, 'MainWindow.xaml')))) fail('MainWindow has no Icon');
    console.log(`ICON OK sizes=${ico.readUInt16LE(4)}`);
  },

  /** README states what it does, how to install, the risk and the license. */
  readme() {
    const r = read(join(root, 'README.md'));
    for (const h of ['## Возможности', '## Установка', '## Безопасность', '## Лицензия']) if (!r.includes(h)) fail('README.md lacks ' + h);
    for (const t of navViews().length ? [...read(join(src, 'MainWindow.xaml.cs')).matchAll(/new\("([^"]+)", "[^"]*", typeof/g)].map((m) => m[1]) : [])
      if (!r.includes(t)) fail(`README does not mention module "${t}"`);
    if (!existsSync(join(root, 'LICENSE'))) fail('LICENSE missing');
    console.log('README OK');
  },

  /**
   * English interface: every Cyrillic C# literal goes through L.T (or its line says "i18n: keep"), and every key -
   * from code, XAML, tweaks.json and the built-in cleanup rules - has a Cyrillic-free entry in Data/en.json.
   * `list` prints the missing keys as JSON instead of failing on the first.
   */
  i18n(mode) {
    const lit = read(join(root, 'scripts', 'gates', 'i18n.mjs')) && import('./i18n.mjs');
    return lit.then(({ literals, hasCyr, wrapped, kept, xamlTexts }) => {
      const en = JSON.parse(read(join(src, 'Data', 'en.json')));
      const keys = new Map(), bare = [];
      const add = (k, where) => { if (!keys.has(k)) keys.set(k, where); };
      for (const f of walk(join(root, 'src'))) {
        const rel = relative(root, f);
        if (f.endsWith('.cs')) {
          const text = read(f);
          for (const t of literals(text)) {
            if (!hasCyr(t.key)) continue;
            if (t.raw) { for (const m of t.key.matchAll(/^\s*(?:\[(.+?)\]|Warning=(.+))\s*$/gm)) add(m[1] ?? m[2], rel); continue; }
            if (wrapped(text, t)) add(t.key, rel);
            else if (!kept(text, t)) bare.push(`${rel}:${text.slice(0, t.start).split('\n').length} ${JSON.stringify(t.key)}`);
          }
        } else if (f.endsWith('.xaml')) for (const k of xamlTexts(read(f))) add(k, rel);
      }
      for (const t of JSON.parse(read(join(src, 'Data', 'tweaks.json')))) for (const k of [t.category, t.name, t.description]) if (hasCyr(k)) add(k, 'tweaks.json');
      for (const k of ['Приложения', 'Интернет', 'Мультимедиа', 'Утилиты', 'Браузеры', 'Файл', 'Папка', 'Ключ реестра', 'Значение реестра', 'Все пользователи', 'Все пользователи, 32-бит', 'Текущий пользователь']) add(k, 'data values shown through L.T');
      if (bare.length) fail(`${bare.length} Cyrillic literals outside L.T:\n` + bare.join('\n'));
      const missing = [...keys.keys()].filter((k) => !(k in en));
      if (mode === 'list') { console.log(JSON.stringify(Object.fromEntries(missing.map((k) => [k, ''])), null, 1)); return; }
      if (missing.length) fail(`${missing.length} keys without English, e.g.\n` + missing.slice(0, 10).map((k) => `${JSON.stringify(k)} (${keys.get(k)})`).join('\n'));
      const cyr = Object.entries(en).filter(([, v]) => hasCyr(v) && v !== 'Русский');
      if (cyr.length) fail('English entries with Cyrillic: ' + cyr.slice(0, 5).map(([k]) => k).join(', '));
      const holes = Object.entries(en).filter(([k, v]) => (k.match(/\{\d+\}/g) ?? []).sort().join() !== (v.match(/\{\d+\}/g) ?? []).sort().join());
      if (holes.length) fail('placeholders differ: ' + holes.slice(0, 5).map(([k]) => k).join(' | '));
      console.log(`I18N OK keys=${keys.size}`);
    });
  },

  /** Release build: self-contained single-file exes for the app and the watcher. */
  publish() {
    const out = join(root, 'artifacts', 'publish');
    for (const p of ['src/RegKeeper/RegKeeper.csproj', 'src/RegKeeper.Watcher/RegKeeper.Watcher.csproj']) {
      try {
        execFileSync('dotnet', ['publish', join(root, p), '-c', 'Release', '-r', 'win-x64', '--self-contained', '-p:PublishSingleFile=true',
          '-p:IncludeNativeLibrariesForSelfExtract=true', '-o', out, '--nologo'], { stdio: 'pipe', maxBuffer: 64 << 20 });
      } catch (e) { fail(`publish ${p}\n${e.stdout}`); }
    }
    for (const exe of ['RegKeeper.exe', 'RegKeeper.Watcher.exe']) {
      const f = join(out, exe);
      if (!existsSync(f) || statSync(f).size < 20 << 20) fail(`${exe} missing or not self-contained`);
    }
    console.log('PUBLISH OK');
  },
};

const [name, ...args] = process.argv.slice(2);
(checks[name] ?? (() => fail('unknown check ' + name)))(...args);
