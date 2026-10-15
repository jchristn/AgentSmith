// Secret kinds and their value fields (plan section 7.4 secret_values: one encrypted row per field). The dashboard
// renders a kind-specific form from this table; field names are the wire names.
//
// Field: { name, secret (masked input), multiline, file (accepts a dropped file), required }.

export const SECRET_FIELDS = {
  Password: [
    { name: 'username', secret: false, required: false },
    { name: 'password', secret: true, required: true }
  ],
  PrivateKey: [
    { name: 'privateKey', secret: true, multiline: true, file: true, required: true },
    { name: 'passphrase', secret: true, required: false }
  ],
  ApiToken: [{ name: 'token', secret: true, required: true }],
  AccessKeyPair: [
    { name: 'accessKeyId', secret: false, required: true },
    { name: 'secretAccessKey', secret: true, required: true },
    { name: 'sessionToken', secret: true, required: false }
  ],
  ServiceAccountJson: [{ name: 'json', secret: true, multiline: true, file: true, required: true }],
  Kubeconfig: [{ name: 'kubeconfig', secret: true, multiline: true, file: true, required: true }],
  ConnectionString: [{ name: 'connectionString', secret: true, required: true }],
  Certificate: [
    { name: 'certificate', secret: false, multiline: true, file: true, required: true },
    { name: 'privateKey', secret: true, multiline: true, file: true, required: false },
    { name: 'passphrase', secret: true, required: false }
  ],
  Generic: [{ name: 'value', secret: true, multiline: true, required: true }]
};

/** Fields of a kind (Generic for unknown kinds). */
export function secretFieldsFor(kind) {
  return SECRET_FIELDS[kind] || SECRET_FIELDS.Generic;
}

/** Every distinct field name across kinds, for translation coverage. */
export const SECRET_FIELD_NAMES = [...new Set(Object.values(SECRET_FIELDS).flatMap((list) => list.map((f) => f.name)))];

/**
 * Validate values for a kind. On create every required field is needed; on replace (`partial`) only supplied
 * fields are sent, so nothing is required but at least one value must be present.
 * Returns { field: 'required' } or { _form: 'noValues' }.
 */
export function validateSecretValues(kind, values = {}, { partial = false } = {}) {
  const errors = {};
  const fields = secretFieldsFor(kind);
  if (partial) {
    if (!fields.some((f) => String(values[f.name] ?? '').length > 0)) errors._form = 'noValues';
    return errors;
  }
  for (const f of fields) if (f.required && !String(values[f.name] ?? '').length) errors[f.name] = 'required';
  return errors;
}

/** Only the fields with a value, trimmed of nothing (keys and certificates keep their exact whitespace). */
export function secretValuesBody(kind, values = {}) {
  const out = {};
  for (const f of secretFieldsFor(kind)) {
    const v = values[f.name];
    if (v !== undefined && v !== null && String(v).length > 0) out[f.name] = String(v);
  }
  return out;
}

/**
 * A non-secret description of an imported file, shown before saving so the operator knows the right file was
 * picked: the PEM block type, a service account's project and client email, or a kubeconfig's contexts. Never
 * returns key material.
 */
export function describeSecretFile(field, text) {
  const value = String(text || '');
  const pem = /-----BEGIN ([A-Z0-9 ]+)-----/.exec(value);
  if (pem) return { kind: 'pem', type: pem[1] };
  if (field === 'json') {
    try {
      const obj = JSON.parse(value);
      if (obj && typeof obj === 'object') return { kind: 'serviceAccount', projectId: obj.project_id || '', clientEmail: obj.client_email || '' };
    } catch {
      return { kind: 'invalidJson' };
    }
  }
  if (field === 'kubeconfig') {
    const section = /(^|\n)contexts:\s*\n([\s\S]*?)(\n[a-z-]+:|$)/.exec(value);
    const names = section ? [...section[2].matchAll(/\n?\s*name:\s*([^\n]+)/g)].map((m) => m[1].trim()) : [];
    return { kind: 'kubeconfig', contexts: names };
  }
  return null;
}

/** Secrets usable for a slot: those whose kind the slot accepts (all of them when the slot accepts any). */
export function secretsForSlot(secrets, slot) {
  const kinds = Array.isArray(slot?.acceptedKinds) ? slot.acceptedKinds : [];
  if (kinds.length === 0) return secrets || [];
  return (secrets || []).filter((s) => kinds.includes(s.kind) || kinds.includes(s.secretKind));
}
