// Locale registry, lazy catalog loading, language switching (lang, dir, persistence), pseudo-locales, and the
// explicit-locale formatters across script families (I18N.md).

import { describe, it, expect, beforeAll, afterEach, vi } from 'vitest';
import i18n, { setActiveLocale, ensureLocale } from '../i18n';
import {
  canonicalizeLocale,
  getDirection,
  getLocaleMeta,
  getLocaleLabel,
  toIntlLocale,
  LOCALES,
  STORAGE_KEY,
  SUPPORTED_CODES,
  selectableLocales
} from '../i18n/localeRegistry';
import resources from '../i18n/resources';
import { pseudoString, bidiString } from '../i18n/pseudo';
import { formatNumber, formatDateTime, formatDuration, formatBytes, formatList, formatPercent, formatRelativeTime } from '../i18n/formatters';

beforeAll(async () => {
  await setActiveLocale('en');
});

afterEach(async () => {
  vi.useRealTimers();
  await setActiveLocale('en');
});

describe('locale registry', () => {
  it('registers the twelve I18N.md baseline locales plus two pseudo-locales', () => {
    expect(SUPPORTED_CODES).toEqual(['en', 'es', 'pt', 'fr', 'it', 'de', 'zh', 'ar', 'ru', 'ms', 'hi', 'ja', 'en-XA', 'ar-XB']);
    expect(LOCALES.filter((l) => !l.pseudo)).toHaveLength(12);
  });
  it('has a catalog for every registered locale', () => {
    expect(Object.keys(resources).sort()).toEqual([...SUPPORTED_CODES].sort());
  });
  it('canonicalizes aliases, regions, and the documented variants', () => {
    expect(canonicalizeLocale('de-AT')).toBe('de');
    expect(canonicalizeLocale('es-419')).toBe('es');
    expect(canonicalizeLocale('pt-BR')).toBe('pt');
    expect(canonicalizeLocale('pt_PT')).toBe('pt');
    expect(canonicalizeLocale('zh-Hans-CN')).toBe('zh');
    expect(canonicalizeLocale('zh-CN')).toBe('zh');
    expect(canonicalizeLocale('ar-EG')).toBe('ar');
    expect(canonicalizeLocale('hi-IN')).toBe('hi');
    expect(canonicalizeLocale('ms-MY')).toBe('ms');
    expect(canonicalizeLocale('ru-RU')).toBe('ru');
    expect(canonicalizeLocale('kanji')).toBe('ja');
    expect(canonicalizeLocale('en-xa')).toBe('en-XA');
    expect(canonicalizeLocale('AR-XB')).toBe('ar-XB');
    expect(canonicalizeLocale('xx-YY')).toBe('en');
    expect(canonicalizeLocale('')).toBe('en');
  });
  it('reports direction, Intl locale, autonyms, and font hints', () => {
    expect(getDirection('ar')).toBe('rtl');
    expect(getDirection('ar-XB')).toBe('rtl');
    expect(getDirection('ja')).toBe('ltr');
    expect(toIntlLocale('zh')).toBe('zh-Hans');
    expect(toIntlLocale('pt')).toBe('pt-BR');
    expect(toIntlLocale('ar-XB')).toBe('ar');
    expect(toIntlLocale('en-XA')).toBe('en');
    expect(getLocaleLabel('en')).toBe('English');
    expect(getLocaleLabel('fr')).toBe('Français (bêta)');
    expect(getLocaleLabel('ar')).toBe('العربية (تجريبي)');
    expect(getLocaleMeta('hi').fontFamily).toContain('Devanagari');
    expect(getLocaleMeta('zh').fontFamily).toContain('PingFang SC');
    for (const l of LOCALES) if (l.beta) expect(l.betaLabel, l.code).toBeTruthy();
  });
  it('hides pseudo-locales from the selector unless one is active', () => {
    expect(selectableLocales('en').some((l) => l.pseudo)).toBe(false);
    expect(selectableLocales('ar-XB').map((l) => l.code)).toContain('ar-XB');
  });
});

describe('pseudo-locales', () => {
  it('expands text by about a third and keeps placeholders', () => {
    const out = pseudoString('Delete {{name}} now');
    expect(out).toMatch(/^⟦.*⟧$/);
    expect(out).toContain('{{name}}');
    expect(out.length).toBeGreaterThan('Delete {{name}} now'.length * 1.25);
  });
  it('marks bidi text right to left', () => {
    expect(bidiString('Home').startsWith('\u200F')).toBe(true);
  });
});

describe('language switching', () => {
  it('loads a catalog on demand, syncs html lang and dir, and persists the choice', async () => {
    await setActiveLocale('ar');
    expect(document.documentElement.lang).toBe('ar');
    expect(document.documentElement.dir).toBe('rtl');
    expect(localStorage.getItem(STORAGE_KEY)).toBe('ar');
    expect(i18n.t('nav.items.home.label')).not.toBe('Home');
    await setActiveLocale('de');
    expect(document.documentElement.dir).toBe('ltr');
    expect(i18n.t('nav.items.home.label')).toBe('Startseite');
    await setActiveLocale('en');
    expect(localStorage.getItem(STORAGE_KEY)).toBe('en');
    expect(i18n.t('nav.items.home.label')).toBe('Home');
  });
  it.each([['es'], ['pt'], ['fr'], ['it'], ['zh'], ['ru'], ['ms'], ['hi'], ['ja']])(
    'switches to %s with a translated catalog and the right font hint',
    async (code) => {
      await setActiveLocale(code);
      expect(document.documentElement.lang).toBe(code);
      expect(i18n.t('common.actions.cancel')).not.toBe('Cancel');
      const font = document.documentElement.style.getPropertyValue('--locale-font');
      if (['zh', 'ja', 'hi'].includes(code)) expect(font).not.toBe('');
      else expect(font).toBe('');
    }
  );
  it('generates pseudo-locale catalogs from English', async () => {
    await ensureLocale('en-XA');
    await setActiveLocale('en-XA');
    expect(i18n.t('nav.items.home.label')).toMatch(/^⟦Ĥóɱé·+⟧$/);
    await setActiveLocale('ar-XB');
    expect(document.documentElement.dir).toBe('rtl');
  });
});

describe('formatters across script families', () => {
  const iso = '2026-03-04T05:06:07Z';
  const norm = (v) => String(v).replace(/[\s\u00a0\u202f]+/g, ' ');
  it('formats numbers per locale', () => {
    expect(formatNumber(1234567.5, {}, 'en')).toBe('1,234,567.5');
    expect(formatNumber(1234567.5, {}, 'de')).toBe('1.234.567,5');
    expect(norm(formatNumber(1234567.5, {}, 'fr'))).toBe('1 234 567,5');
    expect(formatNumber(1234567.5, {}, 'hi')).toBe('12,34,567.5');
    expect(formatNumber(12, {}, 'ar')).toMatch(/١٢|12/);
  });
  it('formats dates per locale', () => {
    const opts = { year: 'numeric', month: 'long', timeZone: 'UTC' };
    expect(formatDateTime(iso, 'en', opts)).toBe('March 2026');
    expect(formatDateTime(iso, 'de', opts)).toBe('März 2026');
    expect(formatDateTime(iso, 'ja', opts)).toBe('2026年3月');
    expect(formatDateTime(iso, 'zh', opts)).toBe('2026年3月');
    expect(formatDateTime(iso, 'pt', opts)).toBe('março de 2026');
    expect(formatDateTime(iso, 'ru', opts)).toMatch(/март/i);
  });
  it('formats durations, bytes, lists, and percents', () => {
    expect(formatDuration(500, 'en')).toMatch(/^500 /);
    expect(formatDuration(null, 'en')).toBe('-');
    expect(formatBytes(2048, 'en')).toBe('2 KB');
    expect(formatList(['a', 'b', 'c'], 'en')).toBe('a, b, and c');
    expect(formatList(['a', 'b', 'c'], 'es')).toBe('a, b y c');
    expect(formatPercent(50, 'en', { alreadyPercent: true })).toMatch(/50/);
  });
  it('formats relative time in the active locale', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-03-04T05:11:07Z'));
    expect(formatRelativeTime(iso, 'en')).toBe('5 minutes ago');
    expect(formatRelativeTime(iso, 'es')).toBe('hace 5 minutos');
    vi.setSystemTime(new Date('2026-03-04T05:06:08Z'));
    expect(formatRelativeTime(iso, 'en')).toBe(i18n.t('common.relativeTime.now'));
  });
});
