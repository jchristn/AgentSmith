import { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import PageHeader from '../components/PageHeader';
import ActivityChart from '../components/ActivityChart';
import KpiCard from '../components/KpiCard';
import ExternalServicesCard from '../components/ExternalServicesCard';
import GrafanaLink from '../components/GrafanaLink';
import { EffectBadge } from '../components/StatusBadge';
import { useGrafana } from '../hooks/useServerInfo';
import { dashboardUrl } from '../utils/grafana';
import { ServerIcon, CpuIcon, ChatIcon, LinkIcon, PlayIcon, ShieldCheckIcon, AlertTriangleIcon } from '../components/Icons';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';
import { usePendingApprovals } from '../context/ApprovalsContext';
import { useServerEvent } from '../context/EventsContext';
import { requestRangeToParams } from '../utils/constants';
import { pathFor } from '../config/navConfig';
import { formatNumber, formatRelativeTime, formatTime } from '../i18n/formatters';
import { tip } from '../components/Tooltip';

const settle = (promise) => promise.then((value) => ({ ok: true, value })).catch((error) => ({ ok: false, error }));

/** Start of today in local time, as ISO, for the "tokens today" KPI. */
export function startOfToday(now = new Date()) {
  const d = new Date(now);
  d.setHours(0, 0, 0, 0);
  return d.toISOString();
}

/**
 * Load every Home panel independently (partial failure handling): request totals, tool call totals and recent
 * failures, unhealthy resources, model endpoints, and today's tokens. Each entry is { ok, value } or { ok, error }.
 */
export async function loadHomeData(apiClient) {
  const day = requestRangeToParams('day');
  const [requests, tools, failures, resources, unhealthy, models, analytics] = await Promise.all([
    settle(apiClient.getRequestHistorySummary(day)),
    settle(apiClient.getToolInvocationSummary({ fromUtc: day.fromUtc, toUtc: day.toUtc })),
    settle(apiClient.listToolInvocations({ pageNumber: 1, pageSize: 5, status: 'Failed' })),
    settle(apiClient.listResources({ pageNumber: 1, pageSize: 1 })),
    settle(apiClient.listResources({ pageNumber: 1, pageSize: 5, health: 'Unhealthy' })),
    settle(apiClient.listModelEndpoints({ pageNumber: 1, pageSize: 1 })),
    settle(apiClient.getAnalyticsOverview({ fromUtc: startOfToday(), toUtc: new Date().toISOString() }))
  ]);
  return { requests, tools, failures, resources, unhealthy, models, analytics };
}

/**
 * Command center (P8.19): what Hugo is doing and what needs attention. KPIs (requests, tool calls, failures, pending
 * approvals, unhealthy resources, tokens today), request activity, recent failed tool calls, unhealthy resources,
 * common workflows, and the external services card low on the page.
 */
export default function HomeView() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { apiClient } = useAuth();
  const { setWizardOpen } = useApp();
  const { pending } = usePendingApprovals();
  const [data, setData] = useState(null);
  const [reqRange, setReqRange] = useState('day');
  const [reqSummary, setReqSummary] = useState(null);
  const [reqLoading, setReqLoading] = useState(false);
  const [loadedAt, setLoadedAt] = useState(null);
  const grafana = useGrafana();
  const lng = i18n.language;

  const load = useCallback(async () => {
    if (!apiClient) return;
    setData(await loadHomeData(apiClient));
    setLoadedAt(new Date().toISOString());
  }, [apiClient]);

  const loadRequests = useCallback(() => {
    if (!apiClient) return;
    setReqLoading(true);
    apiClient
      .getRequestHistorySummary(requestRangeToParams(reqRange))
      .then(setReqSummary)
      .catch(() => setReqSummary(null))
      .finally(() => setReqLoading(false));
  }, [apiClient, reqRange]);

  useEffect(() => {
    load();
    const id = setInterval(load, 30000);
    return () => clearInterval(id);
  }, [load]);
  useEffect(loadRequests, [loadRequests]);
  useServerEvent(['tool.invoked', 'resource.health'], load);

  const num = (entry, pick) => (entry?.ok ? formatNumber(pick(entry.value) ?? 0, {}, lng) : '-');
  const toolTotals = data?.tools?.ok ? data.tools.value?.totals || data.tools.value || {} : {};
  const reqTotals = data?.requests?.ok ? data.requests.value || {} : {};
  const failedRequests = reqTotals.totalFailure ?? reqTotals.failureCount ?? null;
  const failedTools = toolTotals.failed ?? toolTotals.failures ?? null;
  const tokensToday = data?.analytics?.ok ? (data.analytics.value?.totals?.inputTokens ?? 0) + (data.analytics.value?.totals?.outputTokens ?? 0) : null;
  const firstRun = data && data.resources?.ok && data.models?.ok && (data.resources.value.totalCount ?? 0) === 0 && (data.models.value.totalCount ?? 0) === 0;
  const missingPanels = data ? ['tools', 'resources', 'models', 'analytics'].filter((k) => data[k] && !data[k].ok).length : 0;

  return (
    <div>
      <PageHeader
        title={t('views.home.title')}
        subtitle={t('views.home.subtitle')}
        actions={loadedAt && <span className="text-muted">{t('views.home.lastLoaded', { time: formatTime(loadedAt, lng, { timeStyle: 'medium' }) })}</span>}
      />

      {missingPanels > 0 && (
        <div className="warning-banner" data-testid="home-partial">
          {t('views.home.partial', { count: missingPanels })}
        </div>
      )}
      {firstRun && (
        <div className="callout" data-testid="setup-callout">
          <div>
            <strong>{t('views.home.getStartedTitle')}</strong>
            <p className="text-muted">{t('views.home.getStartedBody')}</p>
          </div>
          <button type="button" className="button-primary" onClick={() => setWizardOpen(true)} {...tip(t('tips.nav.setup'))}>
            {t('views.home.runSetup')}
          </button>
        </div>
      )}

      <div className="kpi-grid" data-tour="kpis">
        <KpiCard label={t('views.home.kpis.requests')} tip={t('tips.views.home.kpis.requests')} value={num(data?.requests, (v) => v.totalCount ?? v.total)} note={t('views.home.kpis.last24h')} onClick={() => navigate(pathFor('requests'))} testId="kpi-requests" />
        <KpiCard label={t('views.home.kpis.toolCalls')} tip={t('tips.views.home.kpis.toolCalls')} value={num(data?.tools, (v) => (v.totals || v).total ?? (v.totals || v).count)} note={t('views.home.kpis.last24h')} onClick={() => navigate(pathFor('toolInvocations'))} testId="kpi-tool-calls" />
        <KpiCard
          label={t('views.home.kpis.failures')}
          tip={t('tips.views.home.kpis.failures')}
          value={failedRequests === null && failedTools === null ? '-' : formatNumber((failedRequests || 0) + (failedTools || 0), {}, lng)}
          note={t('views.home.kpis.last24h')}
          tone={(failedRequests || 0) + (failedTools || 0) > 0 ? 'danger' : undefined}
          onClick={() => navigate(pathFor('toolInvocations', null, { status: 'Failed' }))}
          testId="kpi-failures"
        />
        <KpiCard label={t('views.home.kpis.approvals')} tip={t('tips.views.home.kpis.approvals')} value={pending === null || pending === undefined ? '-' : formatNumber(pending, {}, lng)} tone={pending > 0 ? 'warning' : undefined} onClick={() => navigate(pathFor('approvals'))} testId="kpi-approvals" />
        <KpiCard label={t('views.home.kpis.unhealthy')} tip={t('tips.views.home.kpis.unhealthy')} value={num(data?.unhealthy, (v) => v.totalCount)} tone={data?.unhealthy?.ok && data.unhealthy.value.totalCount > 0 ? 'danger' : undefined} onClick={() => navigate(pathFor('resources', null, { health: 'Unhealthy' }))} testId="kpi-unhealthy" />
        <KpiCard label={t('views.home.kpis.tokens')} tip={t('tips.views.home.kpis.tokens')} value={tokensToday === null ? '-' : formatNumber(tokensToday, { notation: 'compact' }, lng)} note={t('views.home.kpis.today')} onClick={() => navigate(pathFor('analytics'))} testId="kpi-tokens" />
      </div>
      {grafana && (
        <div className="grafana-links">
          <GrafanaLink href={dashboardUrl(grafana, 'overview')} label={t('views.home.kpisGrafana')} tipText={t('tips.views.home.kpisGrafana')} testId="kpis-grafana" />
        </div>
      )}

      <div className="home-chart">
        <ActivityChart
          summary={reqSummary}
          rangeId={reqRange}
          onRangeChange={setReqRange}
          onRefresh={loadRequests}
          loading={reqLoading}
          title={t('views.home.requestActivity')}
          onBucketClick={(b) => navigate(pathFor('requests', null, { fromUtc: b.bucketStartUtc, toUtc: b.bucketEndUtc }))}
        />
      </div>

      <div className="home-grid">
        <section className="panel" aria-labelledby="failures-title" data-testid="home-failures">
          <h2 id="failures-title" className="section-title">
            {t('views.home.recentFailures')}
          </h2>
          {!data ? null : !data.failures.ok ? (
            <p className="text-muted">{t('views.home.failuresUnavailable')}</p>
          ) : (data.failures.value.items || []).length === 0 ? (
            <p className="text-muted">{t('views.home.noFailures')}</p>
          ) : (
            <ul className="plain-list">
              {data.failures.value.items.map((r) => (
                <li key={r.id}>
                  <a
                    href={pathFor('toolInvocations', null, { toolName: r.toolName })}
                    onClick={(e) => {
                      e.preventDefault();
                      navigate(pathFor('toolInvocations', null, { status: 'Failed', toolName: r.toolName }));
                    }}
                    {...tip(t('tips.views.home.openFailure'))}
                  >
                    <code>{r.toolName}</code>
                  </a>{' '}
                  <EffectBadge effect={r.effect} /> <span className="text-muted">{r.resourceAlias || ''}</span> <span className="text-muted">{formatRelativeTime(r.createdUtc, lng)}</span>
                  {r.errorMessage && <div className="field-hint">{r.errorMessage}</div>}
                </li>
              ))}
            </ul>
          )}
        </section>

        <section className="panel" aria-labelledby="unhealthy-title" data-testid="home-unhealthy">
          <h2 id="unhealthy-title" className="section-title">
            {t('views.home.unhealthyTitle')}
          </h2>
          {!data ? null : !data.unhealthy.ok ? (
            <p className="text-muted">{t('views.home.resourcesUnavailable')}</p>
          ) : (data.unhealthy.value.items || []).length === 0 ? (
            <p className="text-muted">{t('views.home.allHealthy')}</p>
          ) : (
            <ul className="plain-list">
              {data.unhealthy.value.items.map((r) => (
                <li key={r.id}>
                  <AlertTriangleIcon size={14} /> <strong>{r.name}</strong> <span className="text-muted">{r.connectorType}</span>
                  {r.lastTestMessage && <div className="field-hint">{r.lastTestMessage}</div>}
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>

      <section className="panel" aria-labelledby="cta-title">
        <h2 id="cta-title" className="section-title">
          {t('views.home.cta.title')}
        </h2>
        <div className="cta-grid">
          <button type="button" className="cta-card" onClick={() => navigate(pathFor('resources'))} {...tip(t('tips.views.home.cta.addResource'))}>
            <ServerIcon /> <span>{t('views.home.cta.addResource')}</span>
          </button>
          <button type="button" className="cta-card" onClick={() => navigate(pathFor('models'))} {...tip(t('tips.views.home.cta.addModel'))}>
            <CpuIcon /> <span>{t('views.home.cta.addModel')}</span>
          </button>
          <button type="button" className="cta-card" onClick={() => navigate(pathFor('chat'))} {...tip(t('tips.views.home.cta.chat'))}>
            <ChatIcon /> <span>{t('views.home.cta.chat')}</span>
          </button>
          <button type="button" className="cta-card" onClick={() => navigate(pathFor('profiles'))} {...tip(t('tips.views.home.cta.connect'))}>
            <LinkIcon /> <span>{t('views.home.cta.connect')}</span>
          </button>
          <button type="button" className="cta-card" onClick={() => navigate(pathFor('approvals'))} {...tip(t('tips.views.home.cta.approvals'))}>
            <ShieldCheckIcon /> <span>{t('views.home.cta.approvals')}</span>
          </button>
          <button type="button" className="cta-card" onClick={() => navigate(pathFor('explorer'))} {...tip(t('tips.views.home.cta.apiExplorer'))}>
            <PlayIcon /> <span>{t('views.home.cta.apiExplorer')}</span>
          </button>
        </div>
      </section>

      <ExternalServicesCard />
    </div>
  );
}
