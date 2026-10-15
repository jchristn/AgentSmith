import { useTranslation } from 'react-i18next';
import { tip } from './Tooltip';
import { translateServerText } from '../utils/serverText';

/**
 * The `warnings` array of a create or update response or a manifest apply item (I-08), or an empty array. Each
 * entry is { field, code, given, stored, message }; code ValueClamped means the value sent was out of range and
 * the nearest allowed value was stored.
 * @param {object|null|undefined} result Response body or manifest apply item.
 * @returns {Array<object>}
 */
export function fieldWarningsOf(result) {
  const list = result && Array.isArray(result.warnings) ? result.warnings : [];
  return list.filter((w) => w && typeof w === 'object');
}

/**
 * Non-blocking notice listing the field warnings the server returned for a save (I-08). The record was saved; the
 * notice says which values were stored differently from what was sent. Renders nothing when there are none.
 * `compact` drops the title and body for use inside a manifest results row. `onDismiss` adds a dismiss button.
 */
export default function FieldWarnings({ warnings, onDismiss = null, compact = false, testId = 'field-warnings' }) {
  const { t } = useTranslation();
  const list = Array.isArray(warnings) ? warnings.filter((w) => w && typeof w === 'object') : [];
  if (!list.length) return null;
  return (
    <div className="warning-banner field-warnings" role="note" data-testid={testId}>
      {!compact && (
        <div className="field-warnings-head">
          <strong>{t('components.fieldWarnings.title', { count: list.length })}</strong>
          {onDismiss && (
            <button type="button" className="button-ghost button-sm" onClick={onDismiss} data-testid={`${testId}-dismiss`} {...tip(t('tips.fieldWarnings.dismiss'))}>
              {t('components.fieldWarnings.dismiss')}
            </button>
          )}
        </div>
      )}
      {!compact && <p>{t('components.fieldWarnings.body')}</p>}
      <ul className="field-warnings-list">
        {list.map((w, i) => (
          <li key={`${w.field}-${i}`} data-field={w.field} data-code={w.code}>
            <code>{w.field}</code>
            {': '}
            <span>{w.message ? translateServerText(t, w.message) : t(`components.fieldWarnings.codes.${w.code}`, { defaultValue: w.code || '' })}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
