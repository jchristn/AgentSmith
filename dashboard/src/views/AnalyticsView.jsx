import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import PageHeader from '../components/PageHeader';
import RangeTabs from '../components/RangeTabs';
import KpiCard from '../components/KpiCard';
import TimeSeriesChart from '../components/TimeSeriesChart';
import Explainer from '../components/Explainer';
import { LoadingState, UnavailableState, ErrorPresenter } from '../components/States';
import { tip } from '../components/Tooltip';
import { useAuth } from '../context/AuthContext';
import { REQUEST_RANGES, requestRangeToParams } from '../utils/constants';
import { isMissingEndpoint } from '../utils/errors';
import { normalizeSeries, SERIES_COLORS, EFFECT_COLORS } from '../utils/analytics';
import { formatNumber, formatDuration, formatPercent } from '../i18n/formatters';

const RANGES = Object.values(REQUEST_RANGES);

/**
 * Analytics (P8.38): token usage over time, tool calls by effect and connector, model latency and errors, and
 * approval turnaround, for a time range and optionally one profile. Each panel loads on its own, so one missing
 * analytics route does not hide the others.
 */
export default function AnalyticsView() {
  const { t, i18n } = useTranslation();
  const { apiClient } = useAuth();
  const [rangeId, setRangeId] = useState('day');
  const [profileId, setProfileId] = useState('');
  const [profiles, setProfiles] = useState([]);
  const [data, setData] = useState({});
  const [errors, setErrors] = useState({});
  const [loading, setLoading] = useState(true);
  const [range, setRange] = useState(() => requestRangeToParams('day'));

  useEffect(() => {
    apiClient
      .listProfiles({ pageNumber: 1, pageSize: 1000 })
      .then((r) => setProfiles(r.items || []))
      .catch(() => setProfiles([]));
  }, [apiClient]);

  const load = useCallback(() => {
    const params = requestRangeToParams(rangeId);
    setRange(params);
    setLoading(true);
    const query = { ...params, agentProfileId: profileId || undefined };
    const calls = { overview: apiClient.getAnalyticsOverview(query), tokens: apiClient.getAnalyticsTokens(query), tools: apiClient.getAnalyticsTools(query), models: apiClient.getAnalyticsModels(query) };
    Promise.allSettled(Object.values(calls)).then((results) => {
      const next = {};
      const errs = {};
      Object.keys(calls).forEach((k, i) => {
        if (results[i].status === 'fulfilled') next[k] = results[i].value;
        else errs[k] = results[i].reason;
      });
      setData(next);
      setErrors(errs);
      setLoading(false);
    });
  }, [apiClient, rangeId, profileId]);

  useEffect(load, [load]);

  const lng = i18n.language;
  const opts = { fromUtc: range.fromUtc, toUtc: range.toUtc, bucketMinutes: range.bucketMinutes };
  const tokens = useMemo(() => normalizeSeries(data.tokens, ['inputTokens', 'outputTokens'], opts), [data.tokens, range]); // eslint-disable-line react-hooks/exhaustive-deps
  const tools = useMemo(() => normalizeSeries(data.tools, ['Read', 'Write', 'Destructive'], opts), [data.tools, range]); // eslint-disable-line react-hooks/exhaustive-deps
  const models = useMemo(() => normalizeSeries(data.models, ['latencyMsP50', 'latencyMsP95'], { ...opts, nullGaps: true }), [data.models, range]); // eslint-disable-line react-hooks/exhaustive-deps
  const modelErrors = useMemo(() => normalizeSeries(data.models, ['errors'], opts), [data.models, range]); // eslint-disable-line react-hooks/exhaustive-deps

  const allMissing = ['overview', 'tokens', 'tools', 'models'].every((k) => isMissingEndpoint(errors[k]));
  const totals = data.overview?.totals || data.overview || {};
  const byConnector = data.tools?.byConnector || [];

  const panel = (key, content) => {
    if (isMissingEndpoint(errors[key])) return <UnavailableState feature={t(`views.analytics.panels.${key}`)} purpose={t(`views.analytics.purposes.${key}`)} endpoint={`/v1.0/api/analytics/${key}`} />;
    if (errors[key]) return <ErrorPresenter error={errors[key]} onRetry={load} />;
    return content;
  };

  return (
    <div>
      <PageHeader title={t('views.analytics.title')} subtitle={t('views.analytics.subtitle')} />
      <Explainer topic="analytics" />
      <div className="analytics-controls">
        <RangeTabs ranges={RANGES} value={rangeId} onChange={setRangeId} onRefresh={load} loading={loading} />
        <label className="filter-select" {...tip(t('tips.views.analytics.profile'))}>
          <span className="sr-only">{t('views.analytics.profile')}</span>
          <select value={profileId} onChange={(e) => setProfileId(e.target.value)} aria-label={t('views.analytics.profile')} {...tip(t('tips.views.analytics.profile'))}>
            <option value="">
              {t('views.analytics.profile')}: {t('common.generic.all')}
            </option>
            {profiles.map((p) => (
              <option key={p.id} value={p.id}>
                {p.displayName || p.name}
              </option>
            ))}
          </select>
        </label>
      </div>
      {loading && Object.keys(data).length === 0 && Object.keys(errors).length === 0 ? (
        <LoadingState />
      ) : allMissing ? (
        <UnavailableState feature={t('views.analytics.title')} purpose={t('views.analytics.purpose')} endpoint="/v1.0/api/analytics/overview" onRetry={load} />
      ) : (
        <>
          {panel(
            'overview',
            <div className="kpi-grid" data-testid="analytics-kpis">
              <KpiCard label={t('views.analytics.kpis.turns')} value={formatNumber(totals.turns ?? 0, {}, lng)} tip={t('tips.views.analytics.kpis.turns')} />
              <KpiCard label={t('views.analytics.kpis.toolCalls')} value={formatNumber(totals.toolCalls ?? 0, {}, lng)} tip={t('tips.views.analytics.kpis.toolCalls')} />
              <KpiCard label={t('views.analytics.kpis.failures')} value={formatNumber(totals.toolFailures ?? totals.failures ?? 0, {}, lng)} tone={(totals.toolFailures ?? totals.failures) > 0 ? 'danger' : undefined} tip={t('tips.views.analytics.kpis.failures')} />
              <KpiCard label={t('views.analytics.kpis.tokens')} value={formatNumber((totals.inputTokens ?? 0) + (totals.outputTokens ?? 0), { notation: 'compact' }, lng)} tip={t('tips.views.analytics.kpis.tokens')} />
              <KpiCard label={t('views.analytics.kpis.approvals')} value={formatNumber(totals.approvals ?? 0, {}, lng)} tip={t('tips.views.analytics.kpis.approvals')} />
              <KpiCard label={t('views.analytics.kpis.turnaround')} value={totals.approvalTurnaroundMsP50 != null ? formatDuration(totals.approvalTurnaroundMsP50, lng) : '-'} tip={t('tips.views.analytics.kpis.turnaround')} />
              <KpiCard label={t('views.analytics.kpis.approvalRate')} value={totals.approvalRate != null ? formatPercent(totals.approvalRate, lng) : '-'} tip={t('tips.views.analytics.kpis.approvalRate')} />
            </div>
          )}
          <section className="analytics-section">
            {panel(
              'tokens',
              <TimeSeriesChart
                id="tokens"
                testId="chart-tokens"
                title={t('views.analytics.panels.tokens')}
                help={t('views.analytics.help.tokens')}
                kind="stacked"
                times={tokens.times}
                series={tokens.series.map((s, i) => ({ ...s, color: SERIES_COLORS[i] }))}
                bucketSeconds={range.bucketMinutes * 60}
                format={(v) => formatNumber(v, {}, lng)}
              />
            )}
          </section>
          <section className="analytics-section">
            {panel(
              'tools',
              <>
                <TimeSeriesChart
                  id="tools"
                  testId="chart-tools"
                  title={t('views.analytics.panels.tools')}
                  help={t('views.analytics.help.tools')}
                  kind="stacked"
                  times={tools.times}
                  series={tools.series.map((s) => ({ ...s, color: EFFECT_COLORS[s.key], label: t(`enums.effect.${s.key}`) }))}
                  bucketSeconds={range.bucketMinutes * 60}
                  format={(v) => formatNumber(v, {}, lng)}
                />
                {byConnector.length > 0 && (
                  <div className="table-scroll">
                    <table className="data-table" data-testid="tools-by-connector">
                      <thead>
                        <tr>
                          <th scope="col" {...tip(t('tips.views.analytics.columns.connector'))}>{t('views.analytics.columns.connector')}</th>
                          <th scope="col" className="right" {...tip(t('tips.views.analytics.columns.calls'))}>{t('views.analytics.columns.calls')}</th>
                          <th scope="col" className="right" {...tip(t('tips.views.analytics.columns.failures'))}>{t('views.analytics.columns.failures')}</th>
                          <th scope="col" className="right" {...tip(t('tips.views.analytics.columns.p95'))}>{t('views.analytics.columns.p95')}</th>
                        </tr>
                      </thead>
                      <tbody>
                        {byConnector.map((row) => (
                          <tr key={row.connectorType || row.key}>
                            <td>
                              <code>{row.connectorType || row.key}</code>
                            </td>
                            <td className="right">{formatNumber(row.count ?? row.calls ?? 0, {}, lng)}</td>
                            <td className="right">{formatNumber(row.failures ?? 0, {}, lng)}</td>
                            <td className="right">{row.durationMsP95 != null ? formatDuration(row.durationMsP95, lng) : '-'}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </>
            )}
          </section>
          <section className="analytics-section">
            {panel(
              'models',
              <div className="chart-pair">
                <TimeSeriesChart
                  id="model-latency"
                  testId="chart-model-latency"
                  title={t('views.analytics.panels.modelLatency')}
                  help={t('views.analytics.help.modelLatency')}
                  kind="line"
                  times={models.times}
                  series={models.series.map((s, i) => ({ ...s, color: SERIES_COLORS[i + 2] }))}
                  bucketSeconds={range.bucketMinutes * 60}
                  format={(v) => formatDuration(v, lng)}
                />
                <TimeSeriesChart
                  id="model-errors"
                  testId="chart-model-errors"
                  title={t('views.analytics.panels.modelErrors')}
                  help={t('views.analytics.help.modelErrors')}
                  kind="stacked"
                  times={modelErrors.times}
                  series={modelErrors.series.map((s) => ({ ...s, color: 'var(--color-danger)' }))}
                  bucketSeconds={range.bucketMinutes * 60}
                  format={(v) => formatNumber(v, {}, lng)}
                />
              </div>
            )}
          </section>
        </>
      )}
    </div>
  );
}
