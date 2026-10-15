import { useTranslation } from 'react-i18next';
import { tip } from './Tooltip';

/**
 * Generic filter row. `fields` describes the inputs; the parent owns the values.
 * field: { key, label, tip, type: 'text'|'select'|'datetime', options?, placeholder? }
 */
export default function FilterBar({ fields = [], values = {}, onChange, onApply, onClear }) {
  const { t } = useTranslation();

  const set = (key, value) => onChange?.({ ...values, [key]: value });

  return (
    <form
      className="filter-bar"
      onSubmit={(e) => {
        e.preventDefault();
        onApply?.();
      }}
    >
      {fields.map((f) => (
        <div className="field" key={f.key}>
          <label htmlFor={`filter-${f.key}`} {...tip(f.tip)}>{f.label}</label>
          {f.type === 'select' ? (
            <select id={`filter-${f.key}`} {...tip(f.tip)} value={values[f.key] ?? ''} onChange={(e) => set(f.key, e.target.value)}>
              {f.options.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          ) : (
            <input
              id={`filter-${f.key}`}
              {...tip(f.tip)}
              type={f.type === 'datetime' ? 'datetime-local' : 'text'}
              value={values[f.key] ?? ''}
              placeholder={f.placeholder}
              onChange={(e) => set(f.key, e.target.value)}
            />
          )}
        </div>
      ))}
      <div className="filter-actions">
        <button type="submit" className="button-primary button-sm" {...tip(t('tips.filters.apply'))}>
          {t('common.actions.apply')}
        </button>
        <button type="button" className="button-secondary button-sm" onClick={onClear} {...tip(t('tips.filters.clear'))}>
          {t('common.actions.clear')}
        </button>
      </div>
    </form>
  );
}
