// Helpers for Hugo resources as the server returns them (src/Hugo.Core/Models/Resource.cs): settings arrive as a
// list of { key, valueKind, value } rows (the resource_settings child table, D-016) and the last connection test as
// lastTestResult (true, false, or absent).

/** Health from the last connection test: Healthy, Unhealthy, or Unknown when never tested. */
export function resourceHealth(resource) {
  if (resource?.healthStatus) return resource.healthStatus;
  if (resource?.lastTestResult === true) return 'Healthy';
  if (resource?.lastTestResult === false) return 'Unhealthy';
  return 'Unknown';
}

/** One stored setting value in its form shape: numbers for Integer, booleans for Boolean, arrays for StringList. */
export function settingFormValue(valueKind, value) {
  if (value === null || value === undefined) return value;
  if (typeof value !== 'string') return value;
  switch (valueKind) {
    case 'Integer': {
      const n = Number(value);
      return Number.isFinite(n) ? n : value;
    }
    case 'Boolean':
      return ['true', '1', 'yes'].includes(value.toLowerCase());
    case 'StringList':
      return value
        .split('\n')
        .map((s) => s.trim())
        .filter(Boolean);
    default:
      return value;
  }
}

/** Settings as a { key: value } map, from either the server's row list or an existing map. */
export function settingsToMap(settings) {
  if (!settings) return {};
  if (!Array.isArray(settings)) return { ...settings };
  return Object.fromEntries(settings.filter((s) => s && s.key).map((s) => [s.key, settingFormValue(s.valueKind, s.value)]));
}

/** Slot bindings ([{ slotName, secretId, secretFieldName }] or a map) as { slotName: { secretId, secretFieldName } }. */
export function slotBindingsToMap(bindings) {
  if (!bindings) return {};
  if (Array.isArray(bindings))
    return Object.fromEntries(
      bindings.map((b) => [b.slotName, { secretId: b.secretId, secretFieldName: b.secretFieldName || null, secretName: b.secretName || null }])
    );
  return bindings;
}

/** A stored resource as the create and edit form holds it. */
export function resourceToForm(resource) {
  return {
    ...resource,
    displayName: resource?.displayName || '',
    description: resource?.description || '',
    environment: resource?.environment || '',
    notes: resource?.notes || '',
    endpoint: resource?.endpoint || '',
    defaultAccessModeCeiling: resource?.defaultAccessModeCeiling || '',
    settings: settingsToMap(resource?.settings),
    secretBindings: slotBindingsToMap(resource?.secretBindings)
  };
}

/** Top-level resource request fields; any other key in a validation error is a connector setting. */
const RESOURCE_FIELDS = new Set([
  'name',
  'connectorType',
  'displayName',
  'description',
  'endpoint',
  'environment',
  'notes',
  'visibility',
  'defaultAccessModeCeiling',
  'secretBindings',
  'settings'
]);

/**
 * Field errors for the resource form. 400 SettingsInvalid keys its fields by setting key; the form shows those under
 * settings.<key> (and a slot error under slots.<name>), so they land next to the right control.
 */
export function resourceFieldErrors(fields, error) {
  if (!fields) return fields;
  const out = {};
  for (const [key, message] of Object.entries(fields)) {
    if (key.startsWith('settings.') || key.startsWith('slots.')) out[key] = message;
    else if (key.startsWith('secretBindings.')) out[`slots.${key.slice(15)}`] = message;
    else if (error?.code === 'SettingsInvalid' && !RESOURCE_FIELDS.has(key)) out[`settings.${key}`] = message;
    else out[key] = message;
  }
  return out;
}

/** Referrer rows from either a resource's bindings list or a secret's referrer list, in one display shape. */
export function referrerRows(result) {
  const list = Array.isArray(result) ? result : Array.isArray(result?.items) ? result.items : null;
  if (!list) {
    if (!result) return [];
    return ['profiles', 'bindings', 'resources', 'modelEndpoints'].flatMap((k) =>
      (result[k] || []).map((x) => ({ type: x.type || k, name: x.name || x.alias || x.id, id: x.id }))
    );
  }
  return list.map((x) => {
    if (x.agentProfileId || x.bindingId)
      return {
        type: 'bindings',
        id: x.bindingId || x.id,
        name: `${x.agentProfileName || x.agentProfileId} / ${x.alias}`,
        accessMode: x.accessMode,
        enabled: x.enabled
      };
    if (x.referrerType) return { type: referrerType(x.referrerType), id: x.referrerId, name: x.referrerName || x.referrerId, detail: x.detail };
    return { type: x.type, id: x.id, name: x.name || x.alias || x.id };
  });
}

/** Secret referrer types (Resource, ModelEndpoint) as the catalog's plural group keys. */
function referrerType(type) {
  const t = String(type || '').toLowerCase();
  if (t.startsWith('model')) return 'modelEndpoints';
  if (t.startsWith('profile') || t.startsWith('agent')) return 'profiles';
  if (t.startsWith('binding')) return 'bindings';
  return 'resources';
}
