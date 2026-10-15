import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import PageHeader from '../components/PageHeader';
import Tabs from '../components/Tabs';
import SchemaForm from '../components/SchemaForm';
import PromptEditor from '../components/PromptEditor';
import CodeBlock from '../components/CodeBlock';
import JsonViewerModal from '../components/JsonViewerModal';
import { LoadingState, UnavailableState, ErrorPresenter } from '../components/States';
import { EffectBadge, EnumBadge } from '../components/StatusBadge';
import { tip } from '../components/Tooltip';
import { useAuth } from '../context/AuthContext';
import { pathFor } from '../config/navConfig';
import { isMissingEndpoint } from '../utils/errors';
import { normalizeSchema, initialValues } from '../utils/schemaForm';
import { enumLabel } from '../utils/enums';

const TABS = ['overview', 'settings', 'tools', 'instructions'];

/**
 * One connector (P8.33): its description, the settings schema rendered as the form a resource will get (plus a
 * reference table), the secret slots with the kinds each accepts, the tools with their base effects, and the default
 * instructions. Instructions are edited per layer on the Prompt Templates page under the connector's template key.
 */
export default function ConnectorDetailView({ recordId }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const navigate = useNavigate();
  const [connector, setConnector] = useState(null);
  const [error, setError] = useState(null);
  const [tab, setTab] = useState('overview');
  const [preview, setPreview] = useState({});
  const [jsonOpen, setJsonOpen] = useState(false);

  const load = useCallback(() => {
    apiClient
      .getConnector(recordId)
      .then((c) => {
        setConnector(c);
        setPreview(initialValues(normalizeSchema(c?.settingsSchema), {}));
      })
      .catch(setError);
  }, [apiClient, recordId]);

  useEffect(load, [load]);
  const settings = useMemo(() => normalizeSchema(connector?.settingsSchema), [connector]);

  if (isMissingEndpoint(error)) {
    return (
      <div>
        <PageHeader title={recordId} subtitle={t('views.connectors.subtitle')} />
        <UnavailableState feature={t('views.connectors.title')} purpose={t('views.connectors.purpose')} endpoint="/v1.0/api/connectors/{type}" onRetry={load} />
      </div>
    );
  }
  if (error) return <ErrorPresenter error={error} onRetry={load} />;
  if (!connector) return <LoadingState />;

  const tools = connector.tools || [];
  const slots = connector.secretSlots || [];

  return (
    <div>
      <PageHeader
        title={connector.displayName || connector.type}
        subtitle={connector.description || t('views.connectors.noDescription')}
        actions={
          <>
            <button type="button" className="button-secondary" onClick={() => navigate(pathFor('connectors'))} {...tip(t('tips.views.connectors.back'))}>
              {t('common.actions.back')}
            </button>
            <button type="button" className="button-secondary" onClick={() => setJsonOpen(true)} {...tip(t('tips.actions.viewJson'))}>
              {t('common.actions.viewJson')}
            </button>
            <button type="button" className="button-primary" onClick={() => navigate(pathFor('resources'))} {...tip(t('tips.views.connectors.addResource'))}>
              {t('views.connectors.addResource')}
            </button>
          </>
        }
      />
      <Tabs label={connector.displayName || connector.type} active={tab} onChange={setTab} tabs={TABS.map((id) => ({ id, label: t(`views.connectors.tabs.${id}`), tip: t(`tips.views.connectors.tabs.${id}`) }))} />
      {tab === 'overview' && (
        <div className="panel">
          <dl className="kv-grid">
            <dt>{t('views.connectors.fields.type')}</dt>
            <dd>
              <code>{connector.type}</code>
            </dd>
            <dt>{t('views.connectors.fields.family')}</dt>
            <dd>{connector.family || '-'}</dd>
            <dt>{t('views.connectors.fields.category')}</dt>
            <dd>
              <EnumBadge group="connectorCategory" value={connector.category || 'Other'} tone="info" />
            </dd>
            <dt>{t('views.connectors.fields.version')}</dt>
            <dd>{connector.version || '-'}</dd>
            <dt>{t('views.connectors.fields.documentation')}</dt>
            <dd>
              {connector.documentationUrl ? (
                <a href={connector.documentationUrl} target="_blank" rel="noopener noreferrer">
                  {connector.documentationUrl}
                </a>
              ) : (
                '-'
              )}
            </dd>
          </dl>
          {connector.loadError && <div className="error-banner">{connector.loadError}</div>}
          <h3 className="section-title">{t('views.connectors.slots')}</h3>
          {slots.length === 0 ? (
            <div className="text-muted">{t('views.connectors.noSlots')}</div>
          ) : (
            <ul className="plain-list" data-testid="connector-slots">
              {slots.map((s) => (
                <li key={s.name}>
                  <strong>{s.label || s.name}</strong> <code>{s.name}</code> {s.required && <span className="pill pill-warning">{t('common.generic.required')}</span>}{' '}
                  <span className="text-muted">{(s.acceptedKinds || []).map((k) => enumLabel(t, 'secretKind', k)).join(', ')}</span>
                  {s.help && <div className="field-hint">{s.help}</div>}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
      {tab === 'settings' && (
        <div className="panel" data-testid="connector-settings">
          <p className="text-muted">{t('views.connectors.settingsBody')}</p>
          <SchemaForm schema={connector.settingsSchema} values={preview} onChange={setPreview} connectorType={connector.type} idPrefix="connector-preview" />
          <h3 className="section-title">{t('views.connectors.reference')}</h3>
          <div className="table-scroll">
            <table className="data-table">
              <thead>
                <tr>
                  {['key', 'kind', 'required', 'default', 'range', 'group'].map((c) => (
                    <th key={c} scope="col" {...tip(t(`tips.views.connectors.columns.${c}`))}>
                      {t(`views.connectors.columns.${c}`)}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {settings.map((s) => (
                  <tr key={s.key}>
                    <td>
                      <code>{s.key}</code>
                    </td>
                    <td>{enumLabel(t, 'settingValueKind', s.kind)}</td>
                    <td>{s.required ? t('common.boolean.yes') : t('common.boolean.no')}</td>
                    <td>{s.defaultValue ?? '-'}</td>
                    <td>{s.allowedValues.length ? s.allowedValues.join(', ') : s.minimum !== null || s.maximum !== null ? `${s.minimum ?? '-'} .. ${s.maximum ?? '-'}` : '-'}</td>
                    <td>{s.group}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
      {tab === 'tools' && (
        <div className="panel" data-testid="connector-tools">
          {tools.length === 0 ? (
            <div className="text-muted">{t('views.connectors.noTools')}</div>
          ) : (
            <div className="table-scroll">
              <table className="data-table">
                <thead>
                  <tr>
                    <th scope="col" {...tip(t('tips.views.profile.tools.name'))}>{t('views.profile.tools.name')}</th>
                    <th scope="col" {...tip(t('tips.views.profile.tools.effect'))}>{t('views.profile.tools.effect')}</th>
                    <th scope="col" {...tip(t('tips.views.profile.tools.description'))}>{t('views.profile.tools.description')}</th>
                  </tr>
                </thead>
                <tbody>
                  {tools.map((tool) => (
                    <tr key={tool.name}>
                      <td>
                        <code>{tool.name}</code>
                      </td>
                      <td>
                        <EffectBadge effect={tool.baseEffect || tool.effect} />
                        {tool.classifiedPerCall && <span className="text-muted"> {t('views.profile.tools.perCall')}</span>}
                      </td>
                      <td className="cell-wrap">{tool.description || '-'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
      {tab === 'instructions' && (
        <div data-testid="connector-instructions">
          <div className="info-banner">
            <span>{t('views.connectors.instructionsBody', { key: connector.defaultInstructionsKey || `connector.${connector.type}.instructions` })}</span>
            <button type="button" className="button-primary button-sm" onClick={() => navigate(pathFor('promptTemplates', null, { search: connector.defaultInstructionsKey || connector.type }))} {...tip(t('tips.views.connectors.editInstructions'))}>
              {t('views.connectors.editInstructions')}
            </button>
          </div>
          {connector.defaultInstructions ? <PromptEditor value={connector.defaultInstructions} readOnly id="connector-instructions" label={t('views.connectors.builtInInstructions')} /> : <CodeBlock value="" emptyText={t('views.connectors.noInstructions')} />}
        </div>
      )}
      <JsonViewerModal open={jsonOpen} value={connector} onClose={() => setJsonOpen(false)} />
    </div>
  );
}
