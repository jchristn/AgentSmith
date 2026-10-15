// Pseudo-locale generation from the English catalog (I18N.md: expansion and bidi pseudo-locales).
//
// en-XA accents every letter, wraps the text in brackets, and pads it about 35 percent longer, so clipped or
// concatenated strings stand out. ar-XB marks every string right-to-left and mirrors brackets, so layouts that
// ignore dir="rtl" stand out. {{placeholders}} and $t() nesting are preserved in both.

const ACCENT_MAP = {
  a: 'á', b: 'ƀ', c: 'ç', d: 'ð', e: 'é', f: 'ƒ', g: 'ĝ', h: 'ĥ', i: 'í', j: 'ĵ', k: 'ķ', l: 'ĺ', m: 'ɱ', n: 'ñ', o: 'ó', p: 'þ', q: 'ǫ', r: 'ŕ', s: 'š', t: 'ţ', u: 'ú', v: 'ṽ', w: 'ŵ', x: 'ẋ', y: 'ý', z: 'ž',
  A: 'Á', B: 'Ɓ', C: 'Ç', D: 'Ð', E: 'É', F: 'Ƒ', G: 'Ĝ', H: 'Ĥ', I: 'Í', J: 'Ĵ', K: 'Ķ', L: 'Ĺ', M: 'Ṁ', N: 'Ñ', O: 'Ó', P: 'Þ', Q: 'Ǫ', R: 'Ŕ', S: 'Š', T: 'Ţ', U: 'Ú', V: 'Ṽ', W: 'Ŵ', X: 'Ẋ', Y: 'Ý', Z: 'Ž'
};

const TOKEN = /(\{\{[^}]+\}\}|\$t\([^)]*\))/g;
const IS_TOKEN = /^(\{\{[^}]+\}\}|\$t\([^)]*\))$/;

/** Expansion pseudo-string: accented, bracketed, about 35 percent longer. Placeholders survive. */
export function pseudoString(value) {
  const parts = String(value).split(TOKEN);
  const accented = parts.map((part) => (IS_TOKEN.test(part) ? part : part.replace(/[a-zA-Z]/g, (ch) => ACCENT_MAP[ch] || ch))).join('');
  const visible = accented.replace(TOKEN, '').length;
  const pad = '·'.repeat(Math.max(1, Math.round(visible * 0.35)));
  return `⟦${accented}${pad}⟧`;
}

/** Bidi pseudo-string: right-to-left marks around the text, so it renders and wraps as RTL. */
export function bidiString(value) {
  return `\u200F⟦${String(value)}⟧\u200F`;
}

/** Recursively transform every leaf string of a catalog. */
export function transformCatalog(node, fn) {
  if (typeof node === 'string') return fn(node);
  if (Array.isArray(node)) return node.map((n) => transformCatalog(n, fn));
  if (node && typeof node === 'object') {
    const out = {};
    for (const [key, value] of Object.entries(node)) out[key] = transformCatalog(value, fn);
    return out;
  }
  return node;
}

/** Build the catalog for a pseudo-locale code from English. */
export function pseudoCatalog(code, en) {
  return transformCatalog(en, code === 'ar-XB' ? bidiString : pseudoString);
}
