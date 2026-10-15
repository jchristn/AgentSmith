// Normalizes the settings API response into a flat field list with metadata.

function inferType(value) {
  if (typeof value === 'boolean') return 'boolean';
  if (typeof value === 'number') return 'number';
  return 'string';
}

function flatten(obj, prefix, out) {
  for (const [k, v] of Object.entries(obj || {})) {
    const key = k.charAt(0).toUpperCase() + k.slice(1);
    const path = prefix ? `${prefix}.${key}` : key;
    if (v && typeof v === 'object' && !Array.isArray(v)) flatten(v, path, out);
    else out.push({ path, value: v, type: Array.isArray(v) ? 'list' : inferType(v), source: 'File', applyMode: 'RestartRequired', editable: true, group: prefix ? prefix.split('.')[0] : 'General' });
  }
}

/** Map the server's field type names (int, long, double, bool, enum, secret, string, list) onto the editor's. */
function editorType(f) {
  const raw = String(f.type || inferType(f.value)).toLowerCase();
  if (['int', 'int32', 'int64', 'long', 'short', 'integer'].includes(raw)) return 'integer';
  if (['double', 'float', 'decimal', 'number'].includes(raw)) return 'number';
  if (['bool', 'boolean'].includes(raw)) return 'boolean';
  return raw;
}

/** Server values are text ("8700", "true"); convert them to the editor's value types. */
function editorValue(type, value) {
  if (value === null || value === undefined) return value;
  if (type === 'boolean') return typeof value === 'boolean' ? value : String(value).toLowerCase() === 'true';
  if ((type === 'integer' || type === 'number') && value !== '' && !Number.isNaN(Number(value))) return Number(value);
  return value;
}

/**
 * Normalize: { fields: [...] } | { groups: [{ name, fields }] } | raw settings object -> field array.
 * Server field: { path, group, description, type (int|bool|enum|secret|string), value (text), isSecret, isSet, source,
 * applyMode, editable, minimum, maximum, allowedValues }.
 * Editor field: { path, value, type (integer|number|boolean|enum|secret|string|list), isSecret, isSet, source,
 * applyMode, editable, min, max, group, options }.
 */
export function normalizeSettings(response) {
  if (!response) return [];
  let fields = [];
  if (Array.isArray(response.fields)) fields = response.fields;
  else if (Array.isArray(response.groups)) fields = response.groups.flatMap((g) => (g.fields || []).map((f) => ({ group: g.name || g.group, ...f })));
  else flatten(response, '', fields);
  return fields.map((f) => {
    const type = editorType(f);
    const options = f.options || f.allowedValues;
    return {
      ...f,
      path: f.path || f.name,
      group: f.group || (f.path || '').split('.')[0] || 'General',
      type,
      value: f.isSecret ? f.value : editorValue(type, f.value),
      min: f.min ?? f.minimum,
      max: f.max ?? f.maximum,
      options: Array.isArray(options) && options.length ? options : undefined,
      editable: f.editable !== false && f.source !== 'Environment',
      applyMode: f.applyMode || 'RestartRequired'
    };
  });
}

/** PUT /v1.0/api/settings body: { values: [{ path, value }] } with every value as text (SettingsUpdateRequest). */
export function settingsUpdateBody(values) {
  return {
    values: Object.entries(values).map(([path, v]) => ({ path, value: v === null || v === undefined ? null : Array.isArray(v) ? v.join(',') : String(v) }))
  };
}

/** Group fields by group name, preserving first-seen order. */
export function groupSettings(fields) {
  const groups = [];
  const index = new Map();
  for (const f of fields) {
    if (!index.has(f.group)) {
      index.set(f.group, groups.length);
      groups.push({ name: f.group, fields: [] });
    }
    groups[index.get(f.group)].fields.push(f);
  }
  return groups;
}

/**
 * Validate one edited setting value against its metadata (M1-01). Nothing is clamped: an out-of-range value is an
 * error the operator must fix. Returns null when the value is valid, otherwise { key, params } where key is a
 * translation key under views.settings.errors.
 * @param {object} field Editor field from normalizeSettings.
 * @param {unknown} value Edited value.
 * @returns {{ key: string, params: object } | null}
 */
export function validateSetting(field, value) {
  if (!field) return null;
  const hasMin = field.min !== undefined && field.min !== null && field.min !== '';
  const hasMax = field.max !== undefined && field.max !== null && field.max !== '';
  if (field.type === 'integer' || field.type === 'number') {
    if (value === '' || value === null || value === undefined || Number.isNaN(Number(value))) return { key: 'number', params: {} };
    const n = Number(value);
    if (field.type === 'integer' && !Number.isInteger(n)) return { key: 'integer', params: {} };
    if (hasMin && hasMax && (n < Number(field.min) || n > Number(field.max))) return { key: 'range', params: { min: field.min, max: field.max } };
    if (hasMin && n < Number(field.min)) return { key: 'min', params: { min: field.min } };
    if (hasMax && n > Number(field.max)) return { key: 'max', params: { max: field.max } };
    return null;
  }
  if (field.type === 'boolean') return typeof value === 'boolean' || value === 'true' || value === 'false' ? null : { key: 'boolean', params: {} };
  if (Array.isArray(field.options) && field.options.length && !field.options.includes(value)) return { key: 'allowed', params: { values: field.options.join(', ') } };
  return null;
}

/** True when a settings update response reports that nothing was saved (an older server may answer 200 with errors). */
export function settingsSaveFailed(result) {
  if (!result || typeof result !== 'object') return false;
  if (result.saved === false) return true;
  return !!(result.errors && typeof result.errors === 'object' && Object.keys(result.errors).length > 0);
}
