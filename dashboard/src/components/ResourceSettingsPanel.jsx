import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import SchemaForm, { schemaErrorMessages } from './SchemaForm';
import SecretSlotPicker from './SecretField';
import CodeBlock from './CodeBlock';
import { FormField } from './FormFields';
import { tip } from './Tooltip';
import ConnectorIcon from './ConnectorIcon';
import { useAuth } from '../context/AuthContext';
import { normalizeSchema, initialValues, validateSchemaValues, toSubmitValues } from '../utils/schemaForm';
import { errorMessage } from '../utils/errors';
import { enumLabel } from '../utils/enums';
import { slotBindingsToMap } from '../utils/resources';
import { connectorSlotLabel } from '../utils/tips';

/**
 * A connector's settings schema. The server's connector view carries the descriptors as `settings` (a list); older
 * descriptors nest them as `settingsSchema.settings`.
 */
export function connectorSchema(connector) {
  if (!connector) return null;
  return connector.settingsSchema ?? connector.settings ?? null;
}

/** Group connector descriptors by category for the picker. */
export function connectorsByCategory(connectors) {
  const out = {};
  for (const c of connectors || []) {
    const cat = c.category || 'Other';
    (out[cat] = out[cat] || []).push(c);
  }
  return out;
}

/**
 * Validate a resource's connector part: the connector is chosen, the settings satisfy the schema, and every required
 * secret slot is filled. Returns { field: message }.
 */
export function validateResourceConnector(t, data, connector) {
  const errors = {};
  if (!data.connectorType) errors.connectorType = t('common.errors.required');
  if (!connector) return errors;
  const settingErrors = schemaErrorMessages(t, validateSchemaValues(normalizeSchema(connectorSchema(connector)), data.settings || {}));
  for (const [k, v] of Object.entries(settingErrors)) errors[`settings.${k}`] = v;
  for (const slot of connector.secretSlots || []) {
    if (slot.required && !data.secretBindings?.[slot.name]?.secretId) errors[`slots.${slot.name}`] = t('common.errors.required');
  }
  return errors;
}

/** Request body fields for the connector part: typed settings and slot bindings. */
export function resourceConnectorBody(data, connector) {
  const settings = connector ? toSubmitValues(normalizeSchema(connectorSchema(connector)), data.settings || {}) : data.settings || {};
  const secretBindings = Object.entries(data.secretBindings || {})
    .filter(([, v]) => v?.secretId)
    .map(([slotName, v]) => ({ slotName, secretId: v.secretId, secretFieldName: v.secretFieldName || null }));
  return { connectorType: data.connectorType, settings, secretBindings };
}

/** Normalize a stored resource's slot bindings (array or map) into { slotName: { secretId, secretFieldName } }. */
export function slotBindingsToForm(bindings) {
  return slotBindingsToMap(bindings);
}

/**
 * The connector part of the resource form (P8.29): the connector picker (create only), the settings form generated
 * from the connector's schema, a secret picker per slot (choose an existing secret of an accepted kind or create one
 * inline), and Test Connection, which tests the unsaved settings before anything is stored.
 */
export default function ResourceSettingsPanel({ data, onChange, errors = {}, disabled = false, mode = 'create' }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [connectors, setConnectors] = useState(null);
  const [connectorsError, setConnectorsError] = useState(null);
  const [secrets, setSecrets] = useState([]);
  const [test, setTest] = useState(null);
  const [testing, setTesting] = useState(false);

  useEffect(() => {
    apiClient
      .listConnectors({ pageNumber: 1, pageSize: 1000 })
      .then((r) => setConnectors(r.items || []))
      .catch((e) => {
        setConnectors([]);
        setConnectorsError(e);
      });
    apiClient
      .listSecrets({ pageNumber: 1, pageSize: 1000 })
      .then((r) => setSecrets(r.items || []))
      .catch(() => setSecrets([]));
  }, [apiClient]);

  const connector = useMemo(() => (connectors || []).find((c) => c.type === data.connectorType) || null, [connectors, data.connectorType]);
  const schema = connectorSchema(connector);

  // The form keeps the chosen connector's descriptor (as _connector, never sent) so validation and the request body
  // can use its schema.
  useEffect(() => {
    if (connector && data._connector !== connector) onChange({ ...data, _connector: connector });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [connector]);

  const choose = (type) => {
    const next = (connectors || []).find((c) => c.type === type);
    onChange({
      ...data,
      connectorType: type,
      _connector: next || null,
      settings: initialValues(normalizeSchema(connectorSchema(next)), {}),
      secretBindings: {}
    });
    setTest(null);
  };

  const settingErrors = Object.fromEntries(
    Object.entries(errors)
      .filter(([k]) => k.startsWith('settings.'))
      .map(([k, v]) => [k.slice(9), v])
  );

  const runTest = async () => {
    setTesting(true);
    setTest(null);
    try {
      const body = { ...resourceConnectorBody(data, connector), name: data.name || undefined, endpoint: data.endpoint || undefined };
      const result = await apiClient.testResourceSettings(body);
      setTest({ ok: result?.success ?? result?.ok ?? true, result });
    } catch (e) {
      setTest({ ok: false, error: errorMessage(t, e) });
    } finally {
      setTesting(false);
    }
  };

  const grouped = connectorsByCategory(connectors);

  return (
    <div className="resource-settings" data-testid="resource-settings">
      {connectorsError && <div className="warning-banner">{t('views.resources.form.connectorsUnavailable')}</div>}
      <FormField
        id="resource-connectorType"
        field={{
          name: 'connectorType',
          type: 'custom-select',
          label: t('resources.resources.fields.connectorType'),
          required: true,
          tip: t('tips.resources.resources.connectorType')
        }}
        value={data.connectorType}
        error={errors.connectorType}
        disabled
        renderControl={() => (
          <select
            id="resource-connectorType"
            value={data.connectorType || ''}
            disabled={disabled || mode === 'edit'}
            onChange={(e) => choose(e.target.value)}
            {...tip(t('tips.resources.resources.connectorType'))}
          >
            <option value="">{t('views.resources.form.chooseConnector')}</option>
            {Object.entries(grouped).map(([cat, list]) => (
              <optgroup key={cat} label={enumLabel(t, 'connectorCategory', cat)}>
                {list.map((c) => (
                  <option key={c.type} value={c.type}>
                    {c.displayName || c.type}
                  </option>
                ))}
              </optgroup>
            ))}
          </select>
        )}
      />
      {connector?.description && (
        <p className="field-hint">
          <ConnectorIcon type={connector.type} iconKey={connector.iconKey} size={12} /> {connector.description}
        </p>
      )}
      {connector && (
        <>
          <h4 className="form-section-title">{t('views.resources.form.settings')}</h4>
          <SchemaForm
            schema={schema}
            values={data.settings || {}}
            errors={settingErrors}
            disabled={disabled}
            connectorType={connector.type}
            idPrefix="resource-setting"
            onChange={(settings) => onChange({ ...data, settings })}
          />
          {(connector.secretSlots || []).length > 0 && (
            <>
              <h4 className="form-section-title">{t('views.resources.form.secrets')}</h4>
              <div className="form-grid">
                {connector.secretSlots.map((slot) => (
                  <SecretSlotPicker
                    key={slot.name}
                    slot={{ ...slot, label: connectorSlotLabel(t, connector.type, slot) }}
                    secrets={secrets}
                    value={data.secretBindings?.[slot.name]}
                    error={errors[`slots.${slot.name}`]}
                    disabled={disabled}
                    idPrefix="resource-slot"
                    createSecret={async (body) => {
                      const created = await apiClient.createSecret(body);
                      setSecrets((prev) => [...prev, created]);
                      return created;
                    }}
                    onChange={(v) => onChange({ ...data, secretBindings: { ...(data.secretBindings || {}), [slot.name]: v } })}
                  />
                ))}
              </div>
            </>
          )}
          {!disabled && (
            <div className="test-connection">
              <button
                type="button"
                className="button-secondary"
                onClick={runTest}
                disabled={testing}
                data-testid="test-connection"
                {...tip(t('tips.views.resources.form.test'))}
              >
                {testing ? t('views.resources.form.testing') : t('views.resources.form.test')}
              </button>
              {test && (
                <div className={test.ok ? 'success-banner' : 'error-banner'} data-testid="test-result">
                  <strong>{test.ok ? t('components.result.succeeded') : t('components.result.failed')}</strong> {test.error || test.result?.message || ''}
                </div>
              )}
              {(test?.result?.validationErrors || []).length > 0 && (
                <ul className="plain-list field-hint" data-testid="test-validation">
                  {test.result.validationErrors.map((v, i) => (
                    <li key={v.key || v.field || i}>{typeof v === 'string' ? v : `${v.key || v.field || ''} ${v.message || ''}`.trim()}</li>
                  ))}
                </ul>
              )}
              {test?.result?.details && Object.keys(test.result.details).length > 0 && <CodeBlock value={test.result.details} prettyJson />}
            </div>
          )}
        </>
      )}
    </div>
  );
}
