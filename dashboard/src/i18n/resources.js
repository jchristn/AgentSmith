// Every translation catalog, loaded eagerly. Used by tests and tooling (scripts/i18n-check.mjs); the running app
// loads catalogs lazily through i18n/index.js so only the active locale is downloaded.
//
// English (en) is the source of truth. Every other real locale carries a complete translated catalog,
// checked for key and placeholder parity and deep-merged over English as a safety net. The pseudo-locales (en-XA expansion,
// ar-XB bidi) are generated from English.

import en from './locales/en.json';
import { pseudoCatalog } from './pseudo.js';
import { deepMerge } from './merge.js';

const modules = import.meta.glob('./locales/*.json', { eager: true });

/** Every translated catalog by locale code, before merging over English. Tests iterate this. */
const TRANSLATIONS = {};
for (const [file, mod] of Object.entries(modules)) {
  const code = file.replace('./locales/', '').replace('.json', '');
  if (code !== 'en') TRANSLATIONS[code] = mod.default || mod;
}

const resources = { en: { translation: en } };
for (const [code, catalog] of Object.entries(TRANSLATIONS)) resources[code] = { translation: deepMerge(en, catalog) };
resources['en-XA'] = { translation: pseudoCatalog('en-XA', en) };
resources['ar-XB'] = { translation: pseudoCatalog('ar-XB', en) };

export { en, TRANSLATIONS, deepMerge };
export default resources;
