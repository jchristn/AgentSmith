import { Fragment, useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import PageHeader from '../components/PageHeader';
import Tabs from '../components/Tabs';
import JsonViewerModal from '../components/JsonViewerModal';
import ResourceKnowledge from '../components/ResourceKnowledge';
import { TryToolPanel } from '../components/TryToolModal';
import { Badge, EnumBadge } from '../components/StatusBadge';
import { LoadingState, ErrorPresenter, UnavailableState } from '../components/States';
import { tip } from '../components/Tooltip';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { pathFor } from '../config/navConfig';
import { healthTone, accessModeTone } from '../utils/constants';
import { errorMessage, isMissingEndpoint } from '../utils/errors';
import { resourceHealth, settingsToMap, referrerRows } from '../utils/resources';
import { connectorSchema } from '../components/ResourceSettingsPanel';
import { normalizeSchema } from '../utils/schemaForm';
import { formatDateTime, formatDuration } from '../i18n/formatters';

const TABS = ['overview', 'tools', 'knowledge'];

/**
 * One resource (P8.28 to P8.30, P8.53): Overview (connection, settings, secret slots, last test with Test Connection,
 * and the profiles that bind it), Try a Tool (dry run, then execute as a profile), and Knowledge (database and table
 * context, intelligence, and relationships for SQL resources).
 */
export default function ResourceDetailView({ recordId }) {
  const { t, i18n } = useTranslation();
  const { apiClient } = useAuth();
  const { addToast } = useToast();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const tab = TABS.includes(searchParams.get('tab')) ? searchParams.get('tab') : 'overview';
  const [resource, setResource] = useState(null);
  const [connector, setConnector] = useState(null);
  const [referrers, setReferrers] = useState(null);
  const [error, setError] = useState(null);
  const [testing, setTesting] = useState(false);
  const [test, setTest] = useState(null);
  const [jsonOpen, setJsonOpen] = useState(false);

  const load = useCallback(() => {
    apiClient
      .getResource(recordId)
      .then((r) => {
        setResource(r);
        setError(null);
        apiClient
          .getConnector(r.connectorType)
          .then(setConnector)
          .catch(() => setConnector(null));
      })
      .catch(setError);
    apiClient
      .getResourceReferrers(recordId)
      .then(setReferrers)
      .catch(() => setReferrers([]));
  }, [apiClient, recordId]);

  useEffect(load, [load]);

  const runTest = async () => {
    setTesting(true);
    try {
      const result = await apiClient.testResource(recordId);
      setTest(result);
      addToast({
        type: result?.success ? 'success' : 'warning',
        message: result?.message || t(result?.success ? 'components.result.succeeded' : 'components.result.failed')
      });
      load();
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    } finally {
      setTesting(false);
    }
  };

  if (isMissingEndpoint(error))
    return (
      <div>
        <PageHeader title={t('views.resourceDetail.title')} subtitle={t('resources.resources.subtitle')} />
        <UnavailableState feature={t('resources.resources.title')} purpose={t('resources.resources.purpose')} endpoint="/v1.0/api/resources/{id}" />
      </div>
    );
  if (error) return <ErrorPresenter error={error} onRetry={load} />;
  if (!resource) return <LoadingState />;

  const health = resourceHealth(resource);
  const settings = settingsToMap(resource.settings);
  const descriptors = normalizeSchema(connectorSchema(connector));
  const labelOf = (key) => descriptors.find((d) => d.key === key)?.label || key;
  const refs = referrerRows(referrers);

  return (
    <div className="resource-detail">
      <PageHeader
        title={resource.displayName || resource.name}
        subtitle={
          <span className="inline-meta">
            <Badge tone="info">{connector?.displayName || resource.connectorType}</Badge>
            {resource.environment && <Badge>{resource.environment}</Badge>}
            <EnumBadge group="healthStatus" value={health} tone={healthTone(health)} />
          </span>
        }
        actions={
          <>
            <button type="button" className="button-secondary" onClick={() => navigate(pathFor('resources'))} {...tip(t('tips.views.resourceDetail.back'))}>
              {t('common.actions.back')}
            </button>
            <button type="button" className="button-secondary" onClick={() => setJsonOpen(true)} {...tip(t('tips.actions.viewJson'))}>
              {t('common.actions.viewJson')}
            </button>
            <button
              type="button"
              className="button-primary"
              onClick={runTest}
              disabled={testing}
              data-testid="resource-test"
              {...tip(t('tips.resources.resources.actions.test'))}
            >
              {testing ? t('views.resources.form.testing') : t('resources.resources.actions.test')}
            </button>
          </>
        }
      />
      <Tabs
        label={resource.name}
        active={tab}
        onChange={(id) => setSearchParams(new URLSearchParams({ tab: id }), { replace: true })}
        tabs={TABS.map((id) => ({ id, label: t(`views.resourceDetail.tabs.${id}`), tip: t(`tips.views.resourceDetail.tabs.${id}`) }))}
      />

      {tab === 'overview' && (
        <div className="panel" data-testid="resource-overview">
          {test && (
            <div className={test.success ? 'success-banner' : 'error-banner'} data-testid="resource-test-result">
              <strong>{test.success ? t('components.result.succeeded') : t('components.result.failed')}</strong> {test.message}{' '}
              {test.latencyMs != null && (
                <span className="text-muted">{t('components.result.latency', { value: formatDuration(test.latencyMs, i18n.language) })}</span>
              )}
            </div>
          )}
          <dl className="kv-grid">
            <dt>{t('resources.resources.fields.name')}</dt>
            <dd>
              <code>{resource.name}</code>
            </dd>
            <dt>{t('resources.resources.fields.connectorType')}</dt>
            <dd>
              <a
                href={pathFor('connectors', resource.connectorType)}
                onClick={(e) => {
                  e.preventDefault();
                  navigate(pathFor('connectors', resource.connectorType));
                }}
                {...tip(t('tips.views.resourceDetail.connector'))}
              >
                {connector?.displayName || resource.connectorType}
              </a>
            </dd>
            <dt>{t('resources.resources.fields.endpoint')}</dt>
            <dd>{resource.endpoint ? <code className="cell-wrap">{resource.endpoint}</code> : '-'}</dd>
            <dt>{t('resources.resources.fields.environment')}</dt>
            <dd>{resource.environment || '-'}</dd>
            <dt>{t('resources.resources.fields.defaultAccessModeCeiling')}</dt>
            <dd>
              {resource.defaultAccessModeCeiling ? (
                <EnumBadge group="accessMode" value={resource.defaultAccessModeCeiling} tone={accessModeTone(resource.defaultAccessModeCeiling)} />
              ) : (
                '-'
              )}
            </dd>
            <dt>{t('resources.resources.columns.lastTestUtc')}</dt>
            <dd>
              {resource.lastTestUtc ? formatDateTime(resource.lastTestUtc, i18n.language) : t('common.generic.never')}
              {resource.lastTestMessage && <div className="field-hint">{resource.lastTestMessage}</div>}
            </dd>
            <dt>{t('resources.resources.fields.description')}</dt>
            <dd className="cell-wrap">{resource.description || '-'}</dd>
            <dt>{t('resources.resources.fields.notes')}</dt>
            <dd className="cell-wrap">{resource.notes || '-'}</dd>
          </dl>
          <h3 className="section-title">{t('views.resources.form.settings')}</h3>
          {Object.keys(settings).length === 0 ? (
            <p className="text-muted">{t('views.resourceDetail.noSettings')}</p>
          ) : (
            <dl className="kv-grid" data-testid="resource-settings-summary">
              {Object.entries(settings).map(([key, value]) => (
                <Fragment key={key}>
                  <dt>{labelOf(key)}</dt>
                  <dd>
                    <code className="cell-wrap">{Array.isArray(value) ? value.join(', ') : String(value)}</code>
                  </dd>
                </Fragment>
              ))}
            </dl>
          )}
          <h3 className="section-title">{t('views.resources.form.secrets')}</h3>
          {(resource.secretBindings || []).length === 0 ? (
            <p className="text-muted">{t('views.resourceDetail.noSecrets')}</p>
          ) : (
            <ul className="plain-list">
              {resource.secretBindings.map((b) => (
                <li key={b.slotName}>
                  <code>{b.slotName}</code> {b.secretName || b.secretId}
                </li>
              ))}
            </ul>
          )}
          <h3 className="section-title">{t('views.resourceDetail.boundBy')}</h3>
          {referrers === null ? (
            <LoadingState />
          ) : refs.length === 0 ? (
            <p className="text-muted">{t('views.resourceDetail.notBound')}</p>
          ) : (
            <ul className="plain-list" data-testid="resource-bindings">
              {refs.map((r) => (
                <li key={r.id}>
                  <code>{r.name}</code> {r.accessMode && <EnumBadge group="accessMode" value={r.accessMode} tone={accessModeTone(r.accessMode)} />}{' '}
                  {r.enabled === false && <Badge>{t('common.generic.disabled')}</Badge>}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {tab === 'tools' && (
        <div className="panel">
          <TryToolPanel resource={resource} />
        </div>
      )}

      {tab === 'knowledge' && <ResourceKnowledge resource={resource} connector={connector} />}

      <JsonViewerModal open={jsonOpen} value={resource} onClose={() => setJsonOpen(false)} />
    </div>
  );
}
