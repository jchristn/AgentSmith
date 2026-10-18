import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { EyeIcon, EyeOffIcon, PlusIcon, TrashIcon } from './Icons';
import { toLocalInput, toUtcIso } from '../utils/query';
import { tip } from './Tooltip';
import CopyButton from './CopyButton';

/**
 * Declarative form renderer. field: { name, type, label, tip?, hint?, options?, required?, min?, max?, rows?,
 * placeholder?, showIf?(data), readOnly?, fields? (objectList), span? }.
 * Types: text, textarea, code, number, checkbox, nullableToggle (null follows `defaultValue`), select, password, datetime, stringList, keyValue, objectList,
 * multiselect, json, custom (`render(data, { disabled, onChange, errors })` draws a whole-row panel, for example a live test
 * helper or a list editor that needs the whole record; onChange takes the next record).
 */
export default function FormFields({ fields, data, onChange, errors = {}, disabled = false, idPrefix = 'f' }) {
  return (
    <div className="form-grid">
      {fields
        .filter((f) => !f.showIf || f.showIf(data))
        .map((f) =>
          f.type === 'custom' ? (
            <div key={f.name} className="field span-full" data-field={f.name}>
              {f.render(data, { disabled, onChange, errors })}
            </div>
          ) : (
            <FormField
              key={f.name}
              field={f}
              value={data?.[f.name]}
              onChange={(v) => onChange({ ...data, [f.name]: v })}
              error={errors?.[f.name]}
              disabled={disabled || (typeof f.readOnly === 'function' ? f.readOnly(data) : !!f.readOnly)}
              id={`${idPrefix}-${f.name}`}
            />
          )
        )}
    </div>
  );
}

/** A single labeled field with inline validation text. `renderControl()` replaces the generated control (for example a grouped select). */
export function FormField({ field, value, onChange, error, disabled, id, renderControl }) {
  if (field.type === 'section') {
    return (
      <div className="form-section span-full">
        <div className="form-section-title" {...tip(field.tip)}>
          {field.label}
        </div>
        {field.hint && <div className="field-hint">{field.hint}</div>}
      </div>
    );
  }
  const wide = ['textarea', 'code', 'stringList', 'keyValue', 'objectList', 'json', 'multiselect'].includes(field.type) || field.span === 'full';
  if (field.type === 'nullableToggle') {
    // A boolean that may be left unset (null) to follow a default from elsewhere, such as a cluster setting. The box
    // shows the effective value; the reset button clears the record's own value again.
    const own = value !== null && value !== undefined;
    const effective = own ? !!value : !!field.defaultValue;
    return (
      <div className="field field-checkbox">
        <input
          id={id}
          type="checkbox"
          checked={effective}
          disabled={disabled}
          onChange={(e) => onChange(e.target.checked)}
          data-own={own ? 'true' : 'false'}
          {...tip(field.tip)}
        />
        <label htmlFor={id} {...tip(field.tip)}>
          {field.label}
        </label>
        {own && !disabled && field.resetLabel && (
          <button type="button" className="button-link" onClick={() => onChange(null)} data-testid={`${id}-reset`} {...tip(field.resetTip)}>
            {field.resetLabel}
          </button>
        )}
        {field.hint && <div className="field-hint">{field.hint}</div>}
      </div>
    );
  }
  if (field.type === 'checkbox') {
    return (
      <div className={`field field-checkbox ${wide ? 'span-full' : ''}`}>
        <input id={id} type="checkbox" checked={!!value} disabled={disabled} onChange={(e) => onChange(e.target.checked)} {...tip(field.tip)} />
        <label htmlFor={id} {...tip(field.tip)}>
          {field.label}
        </label>
        {field.hint && <div className="field-hint">{field.hint}</div>}
      </div>
    );
  }
  const identifier = field.identifier || /(^id$|Id$)/.test(field.name || '');
  const control = renderControl ? renderControl() : <FieldControl field={field} value={value} onChange={onChange} disabled={disabled} id={id} />;
  return (
    <div className={`field ${wide ? 'span-full' : ''} ${error ? 'has-error' : ''}`}>
      <label htmlFor={id} {...tip(field.tip)}>
        {field.label}
        {field.required && (
          <span className="required-mark" aria-hidden="true">
            {' '}
            *
          </span>
        )}
      </label>
      {identifier ? (
        <div className="field-identifier">
          {control}
          {typeof value === 'string' && value && <CopyButton value={value} tipText={field.copyTip} />}
        </div>
      ) : (
        control
      )}
      {field.hint && <div className="field-hint">{field.hint}</div>}
      {error && (
        <div className="field-error" role="alert">
          {error}
        </div>
      )}
    </div>
  );
}

function FieldControl({ field, value, onChange, disabled, id }) {
  const { t } = useTranslation();
  const [reveal, setReveal] = useState(false);
  const tp = tip(field.tip);
  switch (field.type) {
    case 'textarea':
      return (
        <textarea
          id={id}
          {...tp}
          rows={field.rows || 4}
          value={value ?? ''}
          disabled={disabled}
          placeholder={field.placeholder}
          onChange={(e) => onChange(e.target.value)}
        />
      );
    case 'code':
    case 'json':
      return (
        <textarea
          id={id}
          {...tp}
          className="monospace code-input"
          rows={field.rows || 8}
          spellCheck={false}
          value={value ?? ''}
          disabled={disabled}
          placeholder={field.placeholder}
          onChange={(e) => onChange(e.target.value)}
        />
      );
    case 'number':
      return (
        <input
          id={id}
          {...tp}
          type="number"
          value={value ?? ''}
          min={field.min}
          max={field.max}
          step={field.step}
          disabled={disabled}
          placeholder={field.placeholder}
          onChange={(e) => onChange(e.target.value === '' ? null : Number(e.target.value))}
        />
      );
    case 'select':
      return (
        <select id={id} {...tp} value={value ?? ''} disabled={disabled} onChange={(e) => onChange(e.target.value === '' ? null : e.target.value)}>
          <option value="" disabled={field.required && !field.allowNone}>
            {field.noneLabel || t('common.generic.none')}
          </option>
          {(field.options || []).map((o) => {
            const opt = typeof o === 'object' ? o : { value: o, label: field.optionLabel ? field.optionLabel(o) : o };
            return (
              <option key={opt.value} value={opt.value}>
                {opt.label}
              </option>
            );
          })}
        </select>
      );
    case 'multiselect':
      return (
        <div className="checkbox-grid" id={id}>
          {(field.options || []).map((o) => {
            const opt = typeof o === 'object' ? o : { value: o, label: field.optionLabel ? field.optionLabel(o) : o };
            const list = Array.isArray(value) ? value : [];
            const optTip = tip(t('tips.form.option', { option: opt.label, field: field.label }));
            return (
              <label key={opt.value} className="checkbox-chip" {...optTip}>
                <input
                  type="checkbox"
                  {...optTip}
                  disabled={disabled}
                  checked={list.includes(opt.value)}
                  onChange={() => onChange(list.includes(opt.value) ? list.filter((x) => x !== opt.value) : [...list, opt.value])}
                />
                <span>{opt.label}</span>
              </label>
            );
          })}
        </div>
      );
    case 'password':
      return (
        <div className="password-wrap">
          <input
            id={id}
            {...tp}
            type={reveal ? 'text' : 'password'}
            value={value ?? ''}
            disabled={disabled}
            autoComplete="new-password"
            placeholder={field.placeholder}
            onChange={(e) => onChange(e.target.value)}
          />
          <button
            type="button"
            className="icon-button"
            onClick={() => setReveal((v) => !v)}
            aria-label={reveal ? t('common.actions.hide') : t('common.actions.reveal')}
            {...tip(t(reveal ? 'tips.form.hide' : 'tips.form.reveal'))}
          >
            {reveal ? <EyeOffIcon /> : <EyeIcon />}
          </button>
        </div>
      );
    case 'datetime':
      // futureOnly: the picker offers no past times (M1-27); validation still checks typed values.
      return (
        <input
          id={id}
          {...tp}
          type="datetime-local"
          value={toLocalInput(value)}
          min={field.futureOnly ? toLocalInput(new Date().toISOString()) : undefined}
          disabled={disabled}
          onChange={(e) => onChange(toUtcIso(e.target.value) ?? null)}
        />
      );
    case 'stringList':
      return (
        <textarea
          id={id}
          {...tp}
          className="monospace"
          rows={field.rows || 4}
          value={(Array.isArray(value) ? value : []).join('\n')}
          disabled={disabled}
          placeholder={field.placeholder || t('components.form.onePerLine')}
          onChange={(e) => onChange(e.target.value.split('\n'))}
          onBlur={(e) =>
            onChange(
              e.target.value
                .split('\n')
                .map((s) => s.trim())
                .filter(Boolean)
            )
          }
        />
      );
    case 'keyValue':
      return (
        <KeyValueEditor
          value={value}
          onChange={onChange}
          disabled={disabled}
          keyLabel={field.keyLabel}
          valueLabel={field.valueLabel}
          fieldLabel={field.label}
        />
      );
    case 'objectList':
      return (
        <ObjectListEditor
          value={value}
          onChange={onChange}
          disabled={disabled}
          fields={field.fields || []}
          addLabel={field.addLabel}
          ordered={field.ordered}
          fieldLabel={field.label}
          rowAdjust={field.rowAdjust}
        />
      );
    default:
      return (
        <input id={id} {...tp} type="text" value={value ?? ''} disabled={disabled} placeholder={field.placeholder} onChange={(e) => onChange(e.target.value)} />
      );
  }
}

/** Editable list of { key, value } pairs (or an object map when asObject is set). */
export function KeyValueEditor({ value, onChange, disabled, keyLabel, valueLabel, fieldLabel = '' }) {
  const { t } = useTranslation();
  const isObject = value && !Array.isArray(value) && typeof value === 'object';
  const rows = isObject ? Object.entries(value).map(([key, v]) => ({ key, value: v })) : Array.isArray(value) ? value : [];
  const emit = (next) => {
    if (isObject) {
      const obj = {};
      for (const r of next) if (r.key) obj[r.key] = r.value;
      onChange(obj);
    } else onChange(next);
  };
  return (
    <div className="kv-editor">
      {rows.map((r, i) => (
        <div className="kv-row" key={i}>
          <input
            {...tip(t('tips.form.key', { field: fieldLabel }))}
            aria-label={keyLabel || t('components.form.key')}
            placeholder={keyLabel || t('components.form.key')}
            value={r.key ?? ''}
            disabled={disabled}
            onChange={(e) => emit(rows.map((x, j) => (j === i ? { ...x, key: e.target.value } : x)))}
          />
          <input
            {...tip(t('tips.form.value', { field: fieldLabel }))}
            aria-label={valueLabel || t('components.form.value')}
            placeholder={valueLabel || t('components.form.value')}
            value={r.value ?? ''}
            disabled={disabled}
            onChange={(e) => emit(rows.map((x, j) => (j === i ? { ...x, value: e.target.value } : x)))}
          />
          <button
            type="button"
            className="icon-button"
            disabled={disabled}
            onClick={() => emit(rows.filter((_, j) => j !== i))}
            aria-label={t('common.actions.remove')}
            {...tip(t('tips.form.removeRow'))}
          >
            <TrashIcon size={14} />
          </button>
        </div>
      ))}
      <button
        type="button"
        className="button-secondary button-sm"
        disabled={disabled}
        onClick={() => emit([...rows, { key: '', value: '' }])}
        {...tip(t('tips.form.addRow', { field: fieldLabel }))}
      >
        <PlusIcon size={14} /> {t('components.form.addRow')}
      </button>
    </div>
  );
}

/**
 * Editable list of objects with typed sub-fields; supports reordering for ordered lists. A sub-field's `options`
 * and `default` may be functions of the row, and `rowAdjust(next, previous)` lets the parent keep related values in
 * step (for example a binding's kind follows the resource the user picked). New rows pass through rowAdjust too.
 */
export function ObjectListEditor({ value, onChange, disabled, fields, addLabel, ordered = false, fieldLabel = '', rowAdjust }) {
  const { t } = useTranslation();
  const rows = Array.isArray(value) ? value : [];
  const optionsFor = (f, row) => (typeof f.options === 'function' ? f.options(row) : f.options || []);
  const firstOption = (f, row) => {
    const o = optionsFor(f, row)[0];
    return o !== null && typeof o === 'object' ? o.value : o;
  };
  const blank = () => {
    const row = {};
    for (const f of fields) {
      if (f.type === 'checkbox') row[f.name] = false;
      else if (typeof f.default === 'function') row[f.name] = f.default(row);
      else row[f.name] = f.default ?? (f.type === 'select' && f.required ? (firstOption(f, row) ?? '') : '');
    }
    return rowAdjust ? rowAdjust(row, null) : row;
  };
  const update = (i, next) => onChange(rows.map((r, j) => (j === i ? (rowAdjust ? rowAdjust(next, r) : next) : r)));
  const move = (i, d) => {
    const j = i + d;
    if (j < 0 || j >= rows.length) return;
    const next = [...rows];
    [next[i], next[j]] = [next[j], next[i]];
    onChange(next);
  };
  return (
    <div className="object-list">
      {rows.length === 0 && <div className="text-muted object-list-empty">{t('components.form.noItems')}</div>}
      {rows.map((r, i) => (
        <div className="object-list-row" key={i}>
          <div className="object-list-index monospace">{i + 1}</div>
          <div className="object-list-fields">
            {fields.map((f) => (
              <FormField
                key={f.name}
                field={typeof f.options === 'function' ? { ...f, options: f.options(r) } : f}
                id={`ol-${i}-${f.name}`}
                value={r[f.name]}
                disabled={disabled}
                onChange={(v) => update(i, { ...r, [f.name]: v })}
              />
            ))}
          </div>
          <div className="object-list-actions">
            {ordered && (
              <>
                <button
                  type="button"
                  className="icon-button"
                  disabled={disabled || i === 0}
                  onClick={() => move(i, -1)}
                  aria-label={t('components.form.moveUp')}
                  {...tip(t('tips.form.moveUp'))}
                >
                  {'↑'}
                </button>
                <button
                  type="button"
                  className="icon-button"
                  disabled={disabled || i === rows.length - 1}
                  onClick={() => move(i, 1)}
                  aria-label={t('components.form.moveDown')}
                  {...tip(t('tips.form.moveDown'))}
                >
                  {'↓'}
                </button>
              </>
            )}
            <button
              type="button"
              className="icon-button"
              disabled={disabled}
              onClick={() => onChange(rows.filter((_, j) => j !== i))}
              aria-label={t('common.actions.remove')}
              {...tip(t('tips.form.removeItem'))}
            >
              <TrashIcon size={14} />
            </button>
          </div>
        </div>
      ))}
      <button
        type="button"
        className="button-secondary button-sm"
        disabled={disabled}
        onClick={() => onChange([...rows, blank()])}
        {...tip(t('tips.form.addItem', { field: fieldLabel }))}
      >
        <PlusIcon size={14} /> {addLabel || t('components.form.addItem')}
      </button>
    </div>
  );
}
