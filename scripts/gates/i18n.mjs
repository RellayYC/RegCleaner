// C# string literal scanner for the i18n gate: finds every literal (nested ones inside interpolation holes too)
// with its runtime text, holes numbered {0}, {1}... the way L.Text builds lookup keys.
const CYR = /[А-Яа-яЁё]/;
export const hasCyr = (s) => CYR.test(s);

const ESC = { n: '\n', r: '\r', t: '\t', '0': '\0', '\\': '\\', '"': '"', "'": "'", a: '\x07', b: '\b', f: '\f', v: '\v' };

/** @returns {{start:number,end:number,key:string,raw:boolean}[]} in source order, outer before nested */
export function literals(src) {
  const out = [];
  const isStart = (i) => src[i] === '"' || (/[$@]/.test(src[i]) && /^[$@]{1,3}"/.test(src.slice(i, i + 4)) && !/\w/.test(src[i - 1] ?? ''));

  function skipChar(i) { // at the opening '
    let j = i + 1;
    if (src[j] === '\\') j += 2; else j += 1;
    while (j < src.length && src[j] !== "'") j++;
    return j + 1;
  }

  function readStr(i) {
    let j = i, dollars = 0, verbatim = false;
    while (src[j] === '$' || src[j] === '@') { if (src[j] === '$') dollars++; else verbatim = true; j++; }
    if (src.startsWith('"""', j)) { // raw string: no wrapping, callers read it as data
      let q = 0; while (src[j + q] === '"') q++;
      const close = src.indexOf('"'.repeat(q), j + q);
      const tok = { start: i, end: close + q, key: src.slice(j + q, close), raw: true };
      out.push(tok);
      return tok.end;
    }
    const tok = { start: i, end: 0, key: '', raw: false };
    out.push(tok);
    let k = j + 1, key = '', holes = 0;
    while (k < src.length) {
      const c = src[k];
      if (verbatim && c === '"' && src[k + 1] === '"') { key += '"'; k += 2; continue; }
      if (c === '"') { k++; break; }
      if (!verbatim && c === '\\') {
        const e = src[k + 1];
        if (e === 'u') { key += String.fromCharCode(parseInt(src.slice(k + 2, k + 6), 16)); k += 6; continue; }
        key += ESC[e] ?? e; k += 2; continue;
      }
      if (dollars && c === '{' && src[k + 1] === '{') { key += '{'; k += 2; continue; }
      if (dollars && c === '}' && src[k + 1] === '}') { key += '}'; k += 2; continue; }
      if (dollars && c === '{') { k = readHole(k + 1); key += `{${holes++}}`; continue; }
      key += c; k++;
    }
    tok.end = k;
    tok.key = key;
    return k;
  }

  function readHole(k) { // after the '{'; returns the index after the closing '}'
    let depth = 0;
    while (k < src.length) {
      const c = src[k];
      if (isStart(k)) { k = readStr(k); continue; }
      if (c === "'") { k = skipChar(k); continue; }
      if (c === '(' || c === '[' || c === '{') depth++;
      else if (c === ')' || c === ']') depth--;
      else if (c === '}') { if (depth === 0) return k + 1; depth--; }
      k++;
    }
    return k;
  }

  let i = 0;
  while (i < src.length) {
    const c = src[i], n = src[i + 1];
    if (c === '/' && n === '/') { const e = src.indexOf('\n', i); i = e < 0 ? src.length : e; continue; }
    if (c === '/' && n === '*') { i = src.indexOf('*/', i + 2) + 2; continue; }
    if (c === "'") { i = skipChar(i); continue; }
    if (isStart(i)) { i = readStr(i); continue; }
    i++;
  }
  return out;
}

/** Is the literal the direct argument of L.T( ... ) */
export const wrapped = (src, tok) => /L\.T\(\s*$/.test(src.slice(Math.max(0, tok.start - 20), tok.start));

/** The line carries an "i18n: keep" comment: the literal is data (parsed input, a stored kind), not interface text. */
export const kept = (src, tok) => {
  const lineEnd = src.indexOf('\n', tok.start);
  return /i18n: keep/.test(src.slice(src.lastIndexOf('\n', tok.start) + 1, lineEnd < 0 ? src.length : lineEnd));
};

const ENT = { quot: '"', amp: '&', lt: '<', gt: '>', apos: "'" };
/** Literal interface text of a XAML file: attribute values with Cyrillic, entities decoded. */
export const xamlTexts = (xml) =>
  [...xml.matchAll(/\b(?:Content|Text|Header|ToolTip|Title)="([^"{][^"]*)"/g)].map((m) =>
    m[1].replace(/&(#x?[0-9a-f]+|\w+);/gi, (_, e) => e[0] === '#' ? String.fromCodePoint(e[1] === 'x' ? parseInt(e.slice(2), 16) : +e.slice(1)) : ENT[e])).filter(hasCyr);
