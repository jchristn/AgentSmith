// Normalizes API errors into localized, operator-facing messages.

import { translateServerText, stripCodePrefix } from './serverText';

/**
 * Codes whose translation is generic ("The record was not found."). For these the server's own message is more
 * useful (it names the record and the reason), so it wins when the server sent one (M1-22, M1-24).
 */
const GENERIC_CODES = new Set([
  'NotFound',
  'Conflict',
  'InUse',
  'ValidationFailed',
  // 400 for an unknown list filter value; the server's message names the allowed values.
  'ValidationError',
  'InvalidBody',
  'SettingsInvalid',
  'Duplicate',
  'Http400',
  'Http404',
  'Http409',
  // 502 from GET /model-endpoints/{id}/models: the provider's own message says what went wrong.
  'ProviderUnreachable',
  'ProviderTimeout'
]);

/**
 * Localized message for an error. Order: a translation of a known server message pattern; the server's own message
 * for generic codes (NotFound, Conflict, ...); a translation of the stable error code; the server message.
 * @param {Function} t i18next translate function.
 * @param {unknown} err Error (ApiError or any Error).
 * @returns {string}
 */
export function errorMessage(t, err) {
  if (!err) return t('common.errors.loadFailed');
  const code = err.code;
  // Only a message from a JSON error body counts as server text; a bare status line ("Not Found") does not.
  const bodyMessage = err.body && typeof err.body === 'object' ? err.body.message : null;
  const serverText = typeof bodyMessage === 'string' && bodyMessage ? stripCodePrefix(bodyMessage, code) : '';
  if (serverText) {
    const known = translateServerText(t, serverText);
    if (known !== serverText) return known;
    if (GENERIC_CODES.has(code)) return serverText;
  }
  if (code) {
    const key = `errorCodes.${code}`;
    const translated = t(key, { defaultValue: '' });
    if (translated && translated !== key) return translated;
  }
  if (err.status === 0) return t('errorCodes.NetworkError');
  return serverText || err.message || t('common.errors.loadFailed');
}

/**
 * Detail suffix for an error: request ID and required permission when present.
 * @param {Function} t i18next translate function.
 * @param {unknown} err Error.
 * @returns {string}
 */
export function errorDetail(t, err) {
  const parts = [];
  if (err?.requiredPermission) parts.push(t('common.errors.requiredPermission', { permission: err.requiredPermission, resource: err.resourceType || '' }));
  if (err?.requestId) parts.push(t('common.errors.requestId', { id: err.requestId }));
  return parts.join(' ');
}

/** 404 codes that mean "the route exists but the record does not". Hugo answers RouteNotFound for unknown routes. */
const RECORD_MISS_CODES = new Set(['NotFound', 'RecordNotFound']);

/**
 * True when the error indicates the endpoint does not exist on this server yet: 404 on a route (RouteNotFound, or
 * any 404 without a record-miss code), or 405 and 501 from servers that answer unknown routes that way. Pages use it
 * to show a "this server does not expose this capability yet" state instead of an error.
 */
export function isMissingEndpoint(err) {
  if (err?.status === 405 || err?.status === 501) return true;
  return err?.status === 404 && !RECORD_MISS_CODES.has(err?.code);
}

/** Field codes the server attaches to 400 ValidationFailed (P9.17); `Invalid` has no translation of its own. */
export const FIELD_CODES = [
  'OutOfRange',
  'NotAnInteger',
  'NotABoolean',
  'NotAllowedValue',
  'TooLong',
  'UnknownSetting',
  'PathRequired',
  'OverriddenByEnvironment',
  'SettingsFileReadOnly',
  'ExpiryInPast'
];

/**
 * Per-field messages from a validation error: each field's machine code (fieldCodes) translated, falling back to
 * the server's message. `special(key, code)` may return a more specific translated message (for example a range
 * with the field's minimum and maximum), or null to use the generic one.
 * @returns {Object<string, string>|null}
 */
export function fieldErrorMessages(t, err, special) {
  const fields = err?.fields;
  if (!fields || typeof fields !== 'object') return null;
  const out = {};
  for (const [key, message] of Object.entries(fields)) {
    const code = err.fieldCodes?.[key];
    let text = null;
    if (code && FIELD_CODES.includes(code)) {
      text = (special && special(key, code)) || t(`common.fieldCodes.${code}`, { defaultValue: '' }) || null;
    }
    out[key] = text || message;
  }
  return out;
}

/**
 * Validation errors for list items arrive keyed by item, for example approvalRules[2].pattern. Forms show errors per
 * field, so each one is also added under its list (approvalRules) as "Item 3: message", keeping the original keys.
 * @param {Function} t i18next translate function.
 * @param {Object<string, string>|null} fields Field errors from fieldErrorMessages.
 * @returns {Object<string, string>|null}
 */
export function withListItemErrors(t, fields) {
  if (!fields) return fields;
  const out = { ...fields };
  for (const [key, message] of Object.entries(fields)) {
    const m = /^([A-Za-z0-9_]+)\[(\d+)\](?:\.[A-Za-z0-9_]+)?$/.exec(key);
    if (!m) continue;
    const text = t('common.errors.listItem', { index: Number(m[2]) + 1, message });
    out[m[1]] = out[m[1]] && out[m[1]] !== text ? `${out[m[1]]} ${text}` : text;
  }
  return out;
}
