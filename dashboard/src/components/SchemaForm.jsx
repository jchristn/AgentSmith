import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { FormField } from './FormFields';
import { normalizeSchema, groupSchema } from '../utils/schemaForm';
import { optionalText } from '../utils/tips';

/**
 * Renders a connector settings schema as a form (P8.11): one control per setting kind (String text, Text textarea,
 * Integer number with minimum and maximum, Boolean checkbox, StringList one-per-line, Enum select), grouped in
 * fieldsets by `group`, with help text and per-field errors.
 *
 * Labels and help come from the descriptor (server text). A translation under
 * connectors.<connectorType>.settings.<key>.label / .help, or components.schemaForm.groups.<group>, wins when present,
 * so built-in connectors can be localized without changing the server.
 *
 * Props: schema (ConnectorSettingsSchema or descriptor array), values ({ key: value }), onChange(nextValues),
 * errors ({ key: message }), disabled, idPrefix, connectorType.
 */
export default function SchemaForm({ schema, values = {}, onChange, errors = {}, disabled = false, idPrefix = 'setting', connectorType = '' }) {
  const { t } = useTranslation();
  const descriptors = useMemo(() => normalizeSchema(schema), [schema]);
  const groups = useMemo(() => groupSchema(descriptors), [descriptors]);

  if (descriptors.length === 0) return <div className="text-muted schema-form-empty">{t('components.schemaForm.noSettings')}</div>;

  const text = (key, suffix, fallback) => (connectorType ? optionalText(t, `connectors.${connectorType}.settings.${key}.${suffix}`) : '') || fallback;

  const fieldFor = (d) => {
    const label = text(d.key, 'label', d.label);
    const help = text(d.key, 'help', d.help);
    const base = { name: d.key, label, required: d.required, tip: help || t('tips.schemaForm.setting', { label }) };
    const range = d.minimum !== null || d.maximum !== null ? t('components.schemaForm.range', { minimum: d.minimum ?? '-', maximum: d.maximum ?? '-' }) : '';
    const hint = [help, d.kind === 'Integer' ? range : ''].filter(Boolean).join(' ');
    switch (d.kind) {
      case 'Text':
        return { ...base, type: 'textarea', rows: 4, hint };
      case 'Integer':
        return { ...base, type: 'number', min: d.minimum ?? undefined, max: d.maximum ?? undefined, step: 1, hint };
      case 'Boolean':
        return { ...base, type: 'checkbox', hint };
      case 'StringList':
        return { ...base, type: 'stringList', rows: 3, hint };
      case 'Enum':
        return { ...base, type: 'select', options: d.allowedValues.map((v) => ({ value: v, label: optionalText(t, `connectors.${connectorType}.settings.${d.key}.values.${v}`) || v })), hint };
      default:
        return { ...base, type: 'text', placeholder: d.defaultValue ?? undefined, hint };
    }
  };

  return (
    <div className="schema-form" data-testid="schema-form">
      {groups.map((group) => (
        <fieldset key={group.name} className="schema-group">
          <legend>{optionalText(t, `components.schemaForm.groups.${group.name}`) || group.name}</legend>
          <div className="form-grid">
            {group.settings.map((d) => (
              <FormField
                key={d.key}
                id={`${idPrefix}-${d.key}`}
                field={fieldFor(d)}
                value={values[d.key]}
                error={errors[d.key]}
                disabled={disabled}
                onChange={(v) => onChange?.({ ...values, [d.key]: v })}
              />
            ))}
          </div>
        </fieldset>
      ))}
    </div>
  );
}

/** Translate validateSchemaValues() output into { key: message }. */
export function schemaErrorMessages(t, errors) {
  const out = {};
  for (const [key, e] of Object.entries(errors || {})) out[key] = t(`components.schemaForm.errors.${e.code}`, e.params);
  return out;
}
