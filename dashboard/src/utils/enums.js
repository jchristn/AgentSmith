// Translated labels for enum values (G-16). The wire value is always the enum name; the UI shows enums.<group>.<value>.

import { ENUMS } from './constants';

/** Translated label for one enum value, falling back to the raw value for unknown values. '-' for empty. */
export function enumLabel(t, group, value) {
  if (value === null || value === undefined || value === '') return '-';
  return t(`enums.${group}.${value}`, { defaultValue: String(value) });
}

/** Select options { value, label } for an enum group: the value is submitted, the label is shown. */
export function enumOptions(t, group, values = ENUMS[group] || []) {
  return values.map((value) => ({ value, label: enumLabel(t, group, value) }));
}

/** A list of enum values as one translated, comma-separated string. */
export function enumList(t, group, values, separator = ', ') {
  return (values || []).map((v) => enumLabel(t, group, v)).join(separator);
}
