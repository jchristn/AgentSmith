// Translations for text the server generates in English without a machine code.
//
// Some messages carry no machine code, only English text built from a fixed template. Each
// pattern below matches one of those templates and maps it to a translation key with its captured values as
// placeholders. Text that matches no pattern is returned unchanged, so new server messages still show (in English).

/** [regular expression, translation key under common.serverText, capture names]. Order matters: first match wins. */
export const SERVER_TEXT_PATTERNS = [
  // Field warnings: a value outside its range that the server clamped.
  [/^(\S+) (\S+) is outside the allowed range (\S+) to (\S+); (\S+) was stored\.$/, 'valueClamped', ['field', 'given', 'minimum', 'maximum', 'stored']],
  // Approvals: the pending message a tool result carries while a call waits.
  [/^Waiting for a person to approve (.+); approval (\S+) expires at (.+)\.$/s, 'approvalWaitingExpires', ['subject', 'id', 'expires']],
  [/^Waiting for a person to approve (.+); approval (\S+)\.$/s, 'approvalWaiting', ['subject', 'id']],
  [/^(.+) is used by (.+)\.$/, 'inUse', ['what', 'users']]
];

/** Enum groups for captured values that are enum names, so they show translated too. */
const ENUM_CAPTURES = {};

/**
 * Translate one server-generated message. Returns the translation when a known pattern matches and the
 * translation exists, otherwise the original text.
 * @param {Function} t i18next translate function.
 * @param {string} text Server text.
 * @returns {string}
 */
export function translateServerText(t, text) {
  if (typeof text !== 'string' || !text) return text;
  const trimmed = text.trim();
  for (const [re, key, names] of SERVER_TEXT_PATTERNS) {
    const m = trimmed.match(re);
    if (!m) continue;
    const values = {};
    names.forEach((name, i) => {
      const raw = m[i + 1];
      values[name] = ENUM_CAPTURES[name] ? t(`enums.${ENUM_CAPTURES[name]}.${raw}`, { defaultValue: raw }) : raw;
    });
    const translated = t(`common.serverText.${key}`, { ...values, defaultValue: '', interpolation: { escapeValue: false } });
    return translated || text;
  }
  return text;
}

/** Drop a leading "Code: " prefix that some server messages repeat (M1-30), for example "RunFinished: Run ... finished". */
export function stripCodePrefix(message, code) {
  if (typeof message !== 'string') return message;
  if (code && message.startsWith(`${code}: `)) return message.slice(code.length + 2);
  return message;
}
