// Central locale registry for the Hugo dashboard.
// Canonical BCP 47 identifiers with direction, display, review, and font metadata.
// Adding a locale, including a right-to-left one, is an entry here plus a catalog (see docs/I18N.md).

import { STORAGE_KEYS } from '../utils/constants';

export const STORAGE_KEY = STORAGE_KEYS.locale;
export const DEFAULT_LOCALE = 'en';

// Font stacks that put the right script face first, so CJK, Devanagari, and Arabic text renders with proper glyphs.
const FONT_BASE = "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', 'Noto Sans'";
const FONT_ZH = `${FONT_BASE}, 'PingFang SC', 'Hiragino Sans GB', 'Microsoft YaHei', 'Noto Sans SC', 'Noto Sans CJK SC', 'Source Han Sans SC', sans-serif`;
const FONT_JA = `${FONT_BASE}, 'Hiragino Sans', 'Hiragino Kaku Gothic ProN', 'Yu Gothic', Meiryo, 'Noto Sans JP', 'Noto Sans CJK JP', sans-serif`;
const FONT_HI = `${FONT_BASE}, 'Noto Sans Devanagari', 'Kohinoor Devanagari', 'Nirmala UI', Mangal, sans-serif`;
const FONT_AR = `${FONT_BASE}, 'Noto Sans Arabic', 'Geeza Pro', 'Segoe UI', Tahoma, sans-serif`;

/**
 * Supported locales, in selector order (the I18N.md baseline set, then the pseudo-locales used in CI).
 *
 * Fields:
 *  - code: canonical BCP 47 tag; also the catalog file name and the i18next language.
 *  - englishName / nativeName: the selector shows nativeName (the autonym).
 *  - dir: 'ltr' or 'rtl'; applied to <html dir>.
 *  - intl: BCP 47 tag handed to Intl formatters (defaults to code).
 *  - aliases: lower-case tags and words that resolve to this locale.
 *  - variant: the regional variant the catalog is written in (zh is Simplified Chinese, pt is Brazilian Portuguese).
 *  - beta / betaLabel: true until a native speaker has reviewed the machine-seeded catalog; the selector appends betaLabel.
 *  - pseudo: generated from English for layout testing; hidden from the selector unless ?lang= selects it.
 *  - fontFamily: optional font stack applied to <html> as --locale-font.
 */
export const LOCALES = [
  { code: 'en', englishName: 'English', nativeName: 'English', dir: 'ltr', aliases: ['en-us', 'en-gb', 'en-ca', 'en-au', 'english'] },
  { code: 'es', englishName: 'Spanish', nativeName: 'Español', dir: 'ltr', aliases: ['es-es', 'es-mx', 'es-419', 'spanish'], beta: true, betaLabel: 'beta' },
  { code: 'pt', englishName: 'Portuguese', nativeName: 'Português', dir: 'ltr', intl: 'pt-BR', variant: 'pt-BR', aliases: ['pt-br', 'pt-pt', 'portuguese'], beta: true, betaLabel: 'beta' },
  { code: 'fr', englishName: 'French', nativeName: 'Français', dir: 'ltr', aliases: ['fr-fr', 'fr-ca', 'fr-be', 'fr-ch', 'french'], beta: true, betaLabel: 'bêta' },
  { code: 'it', englishName: 'Italian', nativeName: 'Italiano', dir: 'ltr', aliases: ['it-it', 'it-ch', 'italian'], beta: true, betaLabel: 'beta' },
  { code: 'de', englishName: 'German', nativeName: 'Deutsch', dir: 'ltr', aliases: ['de-de', 'de-at', 'de-ch', 'german'], beta: true, betaLabel: 'Beta' },
  {
    code: 'zh',
    englishName: 'Mandarin Chinese',
    nativeName: '中文',
    dir: 'ltr',
    intl: 'zh-Hans',
    variant: 'zh-Hans',
    aliases: ['zh-hans', 'zh-cn', 'zh-sg', 'zh-hans-cn', 'cmn', 'cmn-hans', 'chinese', 'mandarin'],
    beta: true,
    betaLabel: '测试版',
    fontFamily: FONT_ZH
  },
  { code: 'ar', englishName: 'Arabic', nativeName: 'العربية', dir: 'rtl', aliases: ['ar-sa', 'ar-eg', 'ar-ae', 'arabic'], beta: true, betaLabel: 'تجريبي', fontFamily: FONT_AR },
  { code: 'ru', englishName: 'Russian', nativeName: 'Русский', dir: 'ltr', aliases: ['ru-ru', 'russian'], beta: true, betaLabel: 'бета' },
  { code: 'ms', englishName: 'Malay', nativeName: 'Bahasa Melayu', dir: 'ltr', aliases: ['ms-my', 'malay'], beta: true, betaLabel: 'beta' },
  { code: 'hi', englishName: 'Hindi', nativeName: 'हिन्दी', dir: 'ltr', aliases: ['hi-in', 'hindi'], beta: true, betaLabel: 'बीटा', fontFamily: FONT_HI },
  { code: 'ja', englishName: 'Japanese', nativeName: '日本語', dir: 'ltr', aliases: ['ja-jp', 'kanji', 'japanese'], beta: true, betaLabel: 'ベータ版', fontFamily: FONT_JA },
  { code: 'en-XA', englishName: 'Pseudo (expansion)', nativeName: 'Ƥşéüðö (expansion)', dir: 'ltr', intl: 'en', aliases: ['pseudo', 'en-xa', 'xa'], pseudo: true },
  { code: 'ar-XB', englishName: 'Pseudo (bidi)', nativeName: '\u200FPseudo·RTL\u200F', dir: 'rtl', intl: 'ar', aliases: ['rtl', 'ar-xb', 'xb', 'zz'], pseudo: true }
];

export const SUPPORTED_CODES = LOCALES.map((l) => l.code);

/** Locales shown in the language selector: the real locales, plus a pseudo-locale only while it is active. */
export function selectableLocales(active) {
  return LOCALES.filter((l) => !l.pseudo || l.code === active);
}

// Lower-case lookup of every canonical code and alias.
const LOOKUP = new Map();
for (const l of LOCALES) {
  LOOKUP.set(l.code.toLowerCase(), l.code);
  for (const a of l.aliases || []) LOOKUP.set(a.toLowerCase(), l.code);
}

/**
 * Normalize an arbitrary locale string to a supported canonical code. Matching is case-insensitive and drops
 * subtags from the end until something matches ('es-AR' to 'es', 'zh-Hans-CN' to 'zh', 'pt-BR' to 'pt').
 */
export function canonicalizeLocale(input) {
  if (!input) return DEFAULT_LOCALE;
  const parts = String(input).replace(/_/g, '-').toLowerCase().split('-');
  while (parts.length) {
    const hit = LOOKUP.get(parts.join('-'));
    if (hit) return hit;
    parts.pop();
  }
  return DEFAULT_LOCALE;
}

/** Look up locale metadata (falls back to the default locale entry). */
export function getLocaleMeta(code) {
  const c = canonicalizeLocale(code);
  return LOCALES.find((l) => l.code === c) || LOCALES[0];
}

/** Direction for a locale ('ltr' | 'rtl'). */
export function getDirection(code) {
  return getLocaleMeta(code).dir;
}

/** Selector text: the autonym, plus the localized beta label while the catalog awaits native review. */
export function getLocaleLabel(code) {
  const meta = getLocaleMeta(code);
  return meta.beta && meta.betaLabel ? `${meta.nativeName} (${meta.betaLabel})` : meta.nativeName;
}

/**
 * The BCP 47 tag actually handed to Intl formatters. The pseudo-locales are not real Intl locales: en-XA formats as
 * English, and ar-XB as Arabic to exercise RTL numeral and date shaping. zh formats as Simplified Chinese and pt as
 * Brazilian Portuguese, the documented variants. Every other locale maps to itself.
 */
export function toIntlLocale(code) {
  const meta = getLocaleMeta(code);
  return meta.intl || meta.code;
}
