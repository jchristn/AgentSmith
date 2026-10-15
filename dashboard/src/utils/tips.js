// Tooltip text lookups shared by record configurations and views (D-030).

/**
 * Tooltip for a field or column of a record page: tips.resources.<id>.<name>, falling back to the shared
 * tips.common.fields.<last segment> (name, description, active, id, createdUtc, and so on). Returns '' when
 * neither exists, which the tooltip coverage tests report.
 */
export function resourceTip(t, id, name) {
  const own = optionalText(t, `tips.resources.${id}.${name}`);
  if (own) return own;
  const last = String(name).split('.').pop();
  return optionalText(t, `tips.common.fields.${last}`);
}

/**
 * Translated text for a key that may not exist, or ''. i18next ignores an empty defaultValue (returnEmptyString
 * is off) and hands back the key itself, so a missing key is detected by comparing with the key.
 */
export function optionalText(t, key, options) {
  const value = t(key, { ...(options || {}), defaultValue: '' });
  return typeof value === 'string' && value !== key ? value : '';
}

/** Tooltip for an enum value shown as a badge: tips.enums.<group>.<value>, or '' when there is none. */
export function enumTip(t, group, value) {
  if (!value) return '';
  return optionalText(t, `tips.enums.${group}.${value}`);
}
