// Connector settings schemas (Hugo.Connectors.Abstractions ConnectorSettingsSchema and SettingDescriptor, JSON
// camelCase): normalization, defaults, validation, and conversion to the typed values the server stores (D-016).
//
// Descriptor: { key, label, help, kind: String|Text|Integer|Boolean|StringList|Enum, required, defaultValue (string
// or null), minimum, maximum, allowedValues, pattern, group, showInSummary }.

import { ENUMS } from './constants';

const KINDS = ENUMS.settingValueKind;

/** The kind as its name, accepting the numeric enum value too (SettingValueKindEnum order). */
export function kindOf(descriptor) {
  const k = descriptor?.kind;
  if (typeof k === 'number') return KINDS[k] || 'String';
  if (typeof k === 'string' && KINDS.includes(k)) return k;
  if (typeof k === 'string') {
    const hit = KINDS.find((x) => x.toLowerCase() === k.toLowerCase());
    if (hit) return hit;
  }
  return 'String';
}

/** Accept a schema object ({ settings }), a bare array, or nothing; return clean descriptors in declared order. */
export function normalizeSchema(schema) {
  const list = Array.isArray(schema) ? schema : Array.isArray(schema?.settings) ? schema.settings : [];
  return list
    .filter((d) => d && d.key)
    .map((d) => ({
      key: String(d.key),
      label: d.label || String(d.key),
      help: d.help || '',
      kind: kindOf(d),
      required: !!d.required,
      defaultValue: d.defaultValue ?? null,
      minimum: d.minimum ?? null,
      maximum: d.maximum ?? null,
      allowedValues: Array.isArray(d.allowedValues) ? d.allowedValues : [],
      pattern: d.pattern || null,
      group: d.group || 'General',
      showInSummary: !!d.showInSummary
    }));
}

/** Group descriptors by `group`, keeping the order groups first appear in. */
export function groupSchema(descriptors) {
  const groups = [];
  const index = new Map();
  for (const d of descriptors) {
    if (!index.has(d.group)) {
      index.set(d.group, groups.length);
      groups.push({ name: d.group, settings: [] });
    }
    groups[index.get(d.group)].settings.push(d);
  }
  return groups;
}

/** Convert a stored or default value (often a string on the wire) to the form's value for a kind. */
export function toFormValue(kind, value) {
  if (value === undefined || value === null) return kind === 'Boolean' ? false : kind === 'StringList' ? [] : '';
  switch (kind) {
    case 'Boolean':
      return typeof value === 'boolean' ? value : String(value).toLowerCase() === 'true';
    case 'Integer':
      return value === '' ? '' : Number.isNaN(Number(value)) ? String(value) : Number(value);
    case 'StringList':
      if (Array.isArray(value)) return value.map(String);
      return String(value)
        .split(/[\n,]/)
        .map((s) => s.trim())
        .filter(Boolean);
    default:
      return String(value);
  }
}

/** Initial form values: each setting's stored value, else its default. */
export function initialValues(descriptors, stored = {}) {
  const out = {};
  for (const d of descriptors) {
    const has = stored && Object.prototype.hasOwnProperty.call(stored, d.key) && stored[d.key] !== null && stored[d.key] !== undefined;
    out[d.key] = toFormValue(d.kind, has ? stored[d.key] : d.defaultValue);
  }
  return out;
}

function isEmpty(kind, value) {
  if (kind === 'Boolean') return false;
  if (kind === 'StringList') return !Array.isArray(value) || value.filter((s) => String(s).trim()).length === 0;
  return value === undefined || value === null || String(value).trim() === '';
}

/**
 * Validate form values against the schema. Returns { key: { code, params } } with codes Required, NotAnInteger,
 * OutOfRange, NotAllowedValue, and PatternMismatch (translated by components.schemaForm.errors.<code>).
 */
export function validateSchemaValues(descriptors, values = {}) {
  const errors = {};
  for (const d of descriptors) {
    const v = values[d.key];
    if (isEmpty(d.kind, v)) {
      if (d.required) errors[d.key] = { code: 'Required', params: {} };
      continue;
    }
    if (d.kind === 'Integer') {
      const n = Number(v);
      if (!Number.isInteger(n)) {
        errors[d.key] = { code: 'NotAnInteger', params: {} };
        continue;
      }
      if ((d.minimum !== null && n < d.minimum) || (d.maximum !== null && n > d.maximum)) {
        errors[d.key] = { code: 'OutOfRange', params: { minimum: d.minimum ?? '-', maximum: d.maximum ?? '-' } };
      }
      continue;
    }
    if (d.kind === 'Enum' && d.allowedValues.length && !d.allowedValues.includes(String(v))) {
      errors[d.key] = { code: 'NotAllowedValue', params: { values: d.allowedValues.join(', ') } };
      continue;
    }
    if ((d.kind === 'String' || d.kind === 'Text') && d.pattern) {
      let re = null;
      try {
        re = new RegExp(d.pattern);
      } catch {
        re = null;
      }
      if (re && !re.test(String(v))) errors[d.key] = { code: 'PatternMismatch', params: { pattern: d.pattern } };
      continue;
    }
    if ((d.kind === 'String' || d.kind === 'Text') && d.maximum !== null && String(v).length > d.maximum) {
      errors[d.key] = { code: 'TooLong', params: { maximum: d.maximum } };
    }
  }
  return errors;
}

/** Typed values for the request body: integers as numbers, booleans as booleans, lists as trimmed arrays. Empty optional values are omitted. */
export function toSubmitValues(descriptors, values = {}) {
  const out = {};
  for (const d of descriptors) {
    const v = values[d.key];
    if (isEmpty(d.kind, v)) continue;
    if (d.kind === 'Integer') out[d.key] = Number(v);
    else if (d.kind === 'Boolean') out[d.key] = !!v;
    else if (d.kind === 'StringList') out[d.key] = v.map((s) => String(s).trim()).filter(Boolean);
    else out[d.key] = String(v);
  }
  return out;
}

/** One-line summary of the settings marked showInSummary, for tables and pickers. */
export function schemaSummary(descriptors, values = {}) {
  return descriptors
    .filter((d) => d.showInSummary && !isEmpty(d.kind, values[d.key]))
    .map((d) => (Array.isArray(values[d.key]) ? values[d.key].join(', ') : String(values[d.key])))
    .join(' ');
}
