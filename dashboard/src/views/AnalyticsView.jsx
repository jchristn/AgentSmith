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
import { analyticsQuery, reportSeries, SERIES_COLORS, INTERVAL_SECONDS } from '../utils/analytics';
import { formatNumber, formatDuration } from '../i18n/formatters';

const RANGES = Object.values(REQUEST_RANGES);
const REPORTS = ['overview', 'tokens', 'tools', 'models'];

const TOOL_COLUMNS = ['toolName', 'connectorType', 'calls', 'errors', 'denied', 'approvals', 'avgDurationMs'];
const MODEL_COLUMNS = ['name', 'provider', 'turns', 'inputTokens', 'outputTokens', 'avgTimeToFirstTokenMs', 'avgTurnDurationMs'];

/** A table of report rows; `cell(row, column)` renders one cell, numeric columns align right. */
function ReportTable({ rows, columns, cell, numeric, testId }) {
  const { t } = useTranslation();
  if (!rows?.length) return <p className="text-muted">{t('views.analytics.noRows')}</p>;
  return (
    <div className="table-scroll">
      <table className="data-table" data-testid={testId}>
        <thead>
          <tr>
            {columns.map((c) => (
              <th key={c} scope="col" className={numeric.includes(c) ? 'right' : undefined} {...tip(t(`tips.views.analytics.columns.${c}`))}>
                {t(`views.analytics.columns.${c}`)}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={row.toolName || row.modelEndpointId || i}>
              {columns.map((c) => (
                <td key={c} className={numeric.includes(c) ? 'right' : undefined}>
                  {cell(row, c)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/**
 * Analytics (P8.38): conversations, turns, tokens, tool calls, approvals, and latency for a time range, from the
 * server's bucketed reports (GET /v1.0/api/analytics/{overview,tokens,tools,models} with from, to, and interval).
 * Each panel loads on its own, so one failing report does not hide the others.
 */
export default function AnalyticsView() {
  const { t, i18n } = useTranslation();
  const { apiClient } = useAuth();
  const [rangeId, setRangeId] = useState('day');
  const [data, setData] = useState({});
  const [errors, setErrors] = useState({});
  const [loading, setLoading] = useState(true);
  const [query, setQuery] = useState(() => analyticsQuery('day', requestRangeToParams('day')));

  const load = useCallback(() => {
    const next = analyticsQuery(rangeId, requestRangeToParams(rangeId));
    setQuery(next);
    setLoading(true);
    Promise.allSettled(REPORTS.map((kind) => apiClient.getAnalyticsReport(kind, next))).then((results) => {
      const reports = {};
      const errs = {};
      REPORTS.forEach((kind, i) => {
        if (results[i].status === 'fulfilled') reports[kind] = results[i].value;
        else errs[kind] = results[i].reason;
      });
      setData(reports);
      setErrors(errs);
      setLoading(false);
    });
  }, [apiClient, rangeId]);

  useEffect(load, [load]);

  const lng = i18n.language;
  const bucketSeconds = INTERVAL_SECONDS[data.overview?.interval || query.interval] || 3600;
  const tokens = useMemo(() => reportSeries(data.tokens, ['inputTokens', 'outputTokens']), [data.tokens]);
  const tools = useMemo(() => reportSeries(data.tools, ['toolCalls', 'toolErrors', 'approvals']), [data.tools]);
  const latency = useMemo(() => reportSeries(data.models, ['avgTimeToFirstTokenMs', 'avgTurnDurationMs'], { nullGaps: true }), [data.models]);

  const allMissing = REPORTS.every((k) => isMissingEndpoint(errors[k]));
  const overview = data.overview || {};
  const totals = overview.totals || {};
  const num = (v) => formatNumber(v ?? 0, {}, lng);
  const ms = (v) => (v != null ? formatDuration(v, lng) : '-');

  const panel = (key, content) => {
    if (isMissingEndpoint(errors[key]))
      return (
        <UnavailableState feature={t(`views.analytics.panels.${key}`)} purpose={t(`views.analytics.purposes.${key}`)} endpoint={`/v1.0/api/analytics/${key}`} />
      );
    if (errors[key]) return <ErrorPresenter error={errors[key]} onRetry={load} />;
    return content;
  };

  return (
    <div>
      <PageHeader title={t('views.analytics.title')} subtitle={t('views.analytics.subtitle')} />
      <Explainer topic="analytics" />
      <div className="analytics-controls">
        <RangeTabs ranges={RANGES} value={rangeId} onChange={setRangeId} onRefresh={load} loading={loading} />
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
              <KpiCard label={t('views.analytics.kpis.conversations')} value={num(overview.conversations)} tip={t('tips.views.analytics.kpis.conversations')} />
              <KpiCard label={t('views.analytics.kpis.turns')} value={num(totals.turns)} tip={t('tips.views.analytics.kpis.turns')} />
              <KpiCard
                label={t('views.analytics.kpis.tokens')}
                value={formatNumber((totals.inputTokens ?? 0) + (totals.outputTokens ?? 0), { notation: 'compact' }, lng)}
                tip={t('tips.views.analytics.kpis.tokens')}
              />
              <KpiCard label={t('views.analytics.kpis.toolCalls')} value={num(totals.toolCalls)} tip={t('tips.views.analytics.kpis.toolCalls')} />
              <KpiCard
                label={t('views.analytics.kpis.failures')}
                value={num(totals.toolErrors)}
                tone={totals.toolErrors > 0 ? 'danger' : undefined}
                tip={t('tips.views.analytics.kpis.failures')}
              />
              <KpiCard label={t('views.analytics.kpis.approvals')} value={num(overview.approvalsRequested)} tip={t('tips.views.analytics.kpis.approvals')} />
              <KpiCard label={t('views.analytics.kpis.firstToken')} value={ms(totals.avgTimeToFirstTokenMs)} tip={t('tips.views.analytics.kpis.firstToken')} />
              <KpiCard label={t('views.analytics.kpis.turnDuration')} value={ms(totals.avgTurnDurationMs)} tip={t('tips.views.analytics.kpis.turnDuration')} />
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
                bucketSeconds={bucketSeconds}
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
                  kind="line"
                  times={tools.times}
                  series={tools.series.map((s, i) => ({ ...s, color: s.key === 'toolErrors' ? 'var(--color-danger)' : SERIES_COLORS[i] }))}
                  bucketSeconds={bucketSeconds}
                  format={(v) => formatNumber(v, {}, lng)}
                />
                <h3 className="section-title">{t('views.analytics.toolsTable')}</h3>
                <ReportTable
                  rows={data.tools?.tools}
                  columns={TOOL_COLUMNS}
                  numeric={['calls', 'errors', 'denied', 'approvals', 'avgDurationMs']}
                  testId="analytics-tools"
                  cell={(row, c) =>
                    c === 'toolName' ? (
                      <code>{row.toolName}</code>
                    ) : c === 'connectorType' ? (
                      row.connectorType || '-'
                    ) : c === 'avgDurationMs' ? (
                      ms(row.avgDurationMs)
                    ) : (
                      num(row[c])
                    )
                  }
                />
              </>
            )}
          </section>
          <section className="analytics-section">
            {panel(
              'models',
              <>
                <TimeSeriesChart
                  id="model-latency"
                  testId="chart-model-latency"
                  title={t('views.analytics.panels.modelLatency')}
                  help={t('views.analytics.help.modelLatency')}
                  kind="line"
                  times={latency.times}
                  series={latency.series.map((s, i) => ({ ...s, color: SERIES_COLORS[i + 2] }))}
                  bucketSeconds={bucketSeconds}
                  format={(v) => formatDuration(v, lng)}
                />
                <h3 className="section-title">{t('views.analytics.modelsTable')}</h3>
                <ReportTable
                  rows={data.models?.models}
                  columns={MODEL_COLUMNS}
                  numeric={['turns', 'inputTokens', 'outputTokens', 'avgTimeToFirstTokenMs', 'avgTurnDurationMs']}
                  testId="analytics-models"
                  cell={(row, c) =>
                    c === 'name' ? (
                      <span>
                        <strong>{row.name || row.modelEndpointId || t('views.analytics.unknownModel')}</strong>
                        {row.model && <span className="text-muted"> {row.model}</span>}
                      </span>
                    ) : c === 'provider' ? (
                      (row.provider && t(`enums.modelProvider.${row.provider}`, { defaultValue: row.provider })) || '-'
                    ) : c.startsWith('avg') ? (
                      ms(row[c])
                    ) : (
                      num(row[c])
                    )
                  }
                />
              </>
            )}
          </section>
        </>
      )}
    </div>
  );
}
