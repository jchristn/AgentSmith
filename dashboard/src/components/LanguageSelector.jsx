import { useTranslation } from 'react-i18next';
import { selectableLocales, canonicalizeLocale, getLocaleLabel } from '../i18n/localeRegistry';
import { setActiveLocale } from '../i18n';
import { tip } from './Tooltip';

/**
 * Locale <select>, rendering autonyms. Pseudo-locales appear only while one is active
 * (?lang=en-XA or ?lang=ar-XB).
 * Used in login, topbar, and settings.
 */
export default function LanguageSelector({ compact = false, className = '' }) {
  const { i18n, t } = useTranslation();
  const current = canonicalizeLocale(i18n.resolvedLanguage || i18n.language);

  return (
    <label className={`topbar-lang ${className}`} {...tip(t('tips.topbar.language'))}>
      {!compact && <span>{t('common.language.label')}</span>}
      <select
        aria-label={t('common.language.label')}
        {...tip(t('tips.topbar.language'))}
        value={current}
        onChange={(e) => setActiveLocale(e.target.value)}
        data-testid="language-select"
      >
        {selectableLocales(current).map((l) => (
          <option key={l.code} value={l.code} lang={l.code} dir={l.dir}>
            {getLocaleLabel(l.code)}
          </option>
        ))}
      </select>
    </label>
  );
}
