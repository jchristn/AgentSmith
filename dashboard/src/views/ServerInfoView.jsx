import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import PageHeader from '../components/PageHeader';
import CopyButton from '../components/CopyButton';
import CodeBlock from '../components/CodeBlock';
import { ErrorBanner, LoadingState } from '../components/States';
import { Badge } from '../components/StatusBadge';
import { useAuth } from '../context/AuthContext';
import { APP_VERSION } from '../utils/constants';
import { roleKey } from '../utils/principal';
import { errorMessage } from '../utils/errors';
import { formatDateTime } from '../i18n/formatters';
import { enumLabel } from '../utils/enums';
import { tip } from '../components/Tooltip';

function Row({ label, value, copy, tip: tipText }) {
  return (
    <div className="info-row">
      <div className="info-label" {...tip(tipText)}>
        {label}
      </div>
      <div className="info-value">
        {value ?? '-'}
        {copy && value && <CopyButton value={String(copy === true ? value : copy)} tipText={tipText} />}
      </div>
    </div>
  );
}

/** Server info: copyable endpoints (REST, MCP, A2A, OpenAI-compatible, OpenAPI), version, features, and the principal. */
export default function ServerInfoView() {
  const { t, i18n } = useTranslation();
  const { apiClient, serverUrl, principal } = useAuth();
  const [info, setInfo] = useState(null);
  const [health, setHealth] = useState(null);
  const [error, setError] = useState(null);

  const load = useCallback(() => {
    if (!apiClient) return;
    apiClient.getServerInfo().then(setInfo).catch(setError);
    apiClient
      .healthCheck()
      .then(setHealth)
      .catch(() => setHealth(null));
  }, [apiClient]);
  useEffect(load, [load]);

  const restUrl = info?.restUrl || info?.publicBaseUrl || serverUrl;
  const mcpUrl = info?.mcpUrl || info?.mcpPublicUrl || (restUrl ? `${restUrl.replace(/\/$/, '')}/mcp` : null);
  const openAiUrl = info?.openAiUrl || (restUrl ? `${restUrl.replace(/\/$/, '')}/v1.0/openai/v1` : null);
  const a2aUrl = info?.a2aUrl || (restUrl ? `${restUrl.replace(/\/$/, '')}/v1.0/a2a/${principal?.tenantId || ''}` : null);
  const flags = info?.featureFlags || info?.features || null;

  return (
    <div>
      <PageHeader title={t('views.serverInfo.title')} subtitle={t('views.serverInfo.subtitle')} />
      {error && <ErrorBanner message={errorMessage(t, error)} onRetry={load} />}
      {!info && !error && <LoadingState />}
      <div className="info-grid">
        <section className="panel">
          <h2 className="section-title">{t('views.serverInfo.endpoints')}</h2>
          <Row label={t('views.serverInfo.rest')} tip={t('tips.views.serverInfo.rest')} value={<code>{restUrl}</code>} copy={restUrl} />
          <Row label={t('views.serverInfo.mcp')} tip={t('tips.views.serverInfo.mcp')} value={mcpUrl ? <code>{mcpUrl}</code> : null} copy={mcpUrl} />
          <Row label={t('views.serverInfo.a2a')} tip={t('tips.views.serverInfo.a2a')} value={a2aUrl ? <code>{a2aUrl}</code> : null} copy={a2aUrl} />
          <Row
            label={t('views.serverInfo.openai')}
            tip={t('tips.views.serverInfo.openai')}
            value={openAiUrl ? <code>{openAiUrl}</code> : null}
            copy={openAiUrl}
          />
          <Row
            label={t('views.serverInfo.openapi')}
            tip={t('tips.views.serverInfo.openapi')}
            value={<code>{info?.openApiUrl || `${serverUrl}/openapi.json`}</code>}
            copy={info?.openApiUrl || `${serverUrl}/openapi.json`}
          />
        </section>
        <section className="panel">
          <h2 className="section-title">{t('views.serverInfo.server')}</h2>
          <Row label={t('views.serverInfo.version')} tip={t('tips.views.serverInfo.version')} value={info?.version || health?.version} />
          <Row label={t('views.serverInfo.dashboardVersion')} tip={t('tips.views.serverInfo.dashboardVersion')} value={APP_VERSION} />
          <Row label={t('views.serverInfo.node')} tip={t('tips.views.serverInfo.node')} value={info?.nodeId || health?.node || null} />
          <Row label={t('views.serverInfo.database')} tip={t('tips.views.serverInfo.database')} value={info?.databaseType} />
          <Row
            label={t('views.serverInfo.health')}
            tip={t('tips.views.serverInfo.health')}
            value={
              health ? (
                <Badge
                  tone={health.status === 'healthy' ? 'success' : 'warning'}
                  tip={t(health.status === 'healthy' ? 'tips.topbar.health.healthy' : 'tips.topbar.health.unhealthy')}
                >
                  {t(health.status === 'healthy' ? 'topbar.health.healthy' : 'topbar.health.unhealthy')}
                </Badge>
              ) : (
                <Badge tone="danger">{t('topbar.health.unhealthy')}</Badge>
              )
            }
          />
          <Row
            label={t('views.serverInfo.connectors')}
            tip={t('tips.views.serverInfo.connectors')}
            value={Array.isArray(info?.connectors) ? info.connectors.length : info?.connectorCount}
          />
          <Row label={t('views.serverInfo.routes')} tip={t('tips.views.serverInfo.routes')} value={info?.operationScopeCount ?? info?.routeCount} />
          <Row
            label={t('views.serverInfo.serverTime')}
            tip={t('tips.views.serverInfo.serverTime')}
            value={health?.utc ? formatDateTime(health.utc, i18n.language, { dateStyle: 'medium', timeStyle: 'medium' }) : null}
          />
        </section>
        <section className="panel">
          <h2 className="section-title">{t('views.serverInfo.principal')}</h2>
          <Row label={t('views.serverInfo.principalName')} tip={t('tips.views.serverInfo.principalName')} value={principal?.principalName} />
          <Row
            label={t('views.serverInfo.principalType')}
            tip={t('tips.views.serverInfo.principalType')}
            value={principal?.principalType ? enumLabel(t, 'principalType', principal.principalType) : null}
          />
          <Row label={t('views.serverInfo.role')} tip={t('tips.views.serverInfo.role')} value={t(roleKey(principal))} />
          <Row
            label={t('views.serverInfo.tenant')}
            tip={t('tips.views.serverInfo.tenant')}
            value={principal?.tenantId ? <code>{principal.tenantId}</code> : null}
            copy={principal?.tenantId}
          />
          <Row
            label={t('views.serverInfo.expires')}
            tip={t('tips.views.serverInfo.expires')}
            value={principal?.expiresUtc ? formatDateTime(principal.expiresUtc, i18n.language) : null}
          />
        </section>
        {flags && (
          <section className="panel">
            <h2 className="section-title">{t('views.serverInfo.features')}</h2>
            <CodeBlock value={flags} prettyJson />
          </section>
        )}
      </div>
      {info && (
        <details className="panel details-section">
          <summary {...tip(t('tips.views.serverInfo.rawJson'))}>{t('common.actions.viewJson')}</summary>
          <CodeBlock value={info} prettyJson />
        </details>
      )}
    </div>
  );
}
