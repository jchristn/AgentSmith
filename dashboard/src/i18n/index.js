// i18next bootstrap. main.jsx awaits `i18nReady` before rendering, so the active locale's catalog is loaded before
// the first meaningful paint (no English flash). English is bundled; every other catalog is a lazily loaded chunk.
//
// Resolution order (I18N.md): explicit ?lang= selection, the persisted choice (localStorage hugo.locale), the
// browser preference, then English. Changing language goes through setActiveLocale(), which loads the catalog first.

import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import LanguageDetector from 'i18next-browser-languagedetector';

import en from './locales/en.json';
import { pseudoCatalog } from './pseudo.js';
import { deepMerge } from './merge.js';
import { STORAGE_KEY, DEFAULT_LOCALE, SUPPORTED_CODES, canonicalizeLocale, getLocaleMeta } from './localeRegistry.js';

const loaders = import.meta.glob(['./locales/*.json', '!./locales/en.json']);

function syncDocument(nextLocale) {
  if (typeof document === 'undefined') return;
  const meta = getLocaleMeta(canonicalizeLocale(nextLocale));
  const root = document.documentElement;
  root.lang = meta.code;
  root.dir = meta.dir;
  if (meta.fontFamily) root.style.setProperty('--locale-font', meta.fontFamily);
  else root.style.removeProperty('--locale-font');
}

/** Load a locale's catalog into i18next (no-op when it is already loaded). */
export async function ensureLocale(code) {
  const locale = canonicalizeLocale(code);
  if (i18n.hasResourceBundle(locale, 'translation')) return locale;
  const meta = getLocaleMeta(locale);
  let catalog = en;
  if (meta.pseudo) catalog = pseudoCatalog(locale, en);
  else if (locale !== 'en') {
    const loader = loaders[`./locales/${locale}.json`];
    if (loader) {
      const mod = await loader();
      catalog = deepMerge(en, mod.default || mod);
    }
  }
  i18n.addResourceBundle(locale, 'translation', catalog, true, true);
  return locale;
}

/** Switch the active locale: load its catalog, then change language (which syncs lang/dir and persists it). */
export async function setActiveLocale(code) {
  const locale = await ensureLocale(code);
  await i18n.changeLanguage(locale);
  return locale;
}

if (!i18n.isInitialized) {
  i18n
    .use(LanguageDetector)
    .use(initReactI18next)
    .init({
      resources: { en: { translation: en } },
      partialBundledLanguages: true,
      fallbackLng: DEFAULT_LOCALE,
      supportedLngs: SUPPORTED_CODES,
      load: 'currentOnly',
      defaultNS: 'translation',
      ns: ['translation'],
      returnNull: false,
      returnEmptyString: false,
      interpolation: { escapeValue: false },
      detection: {
        order: ['querystring', 'localStorage', 'navigator'],
        lookupQuerystring: 'lang',
        lookupLocalStorage: STORAGE_KEY,
        caches: ['localStorage'],
        convertDetectedLanguage: canonicalizeLocale
      }
    });
}

syncDocument(i18n.resolvedLanguage || i18n.language || DEFAULT_LOCALE);
i18n.on('languageChanged', syncDocument);

/** Resolves once the detected locale's catalog is loaded and active. */
export const i18nReady = ensureLocale(i18n.language || DEFAULT_LOCALE)
  .then((locale) => i18n.changeLanguage(locale))
  .catch(() => i18n.changeLanguage(DEFAULT_LOCALE));

export default i18n;
