import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { EnumBadge } from './StatusBadge';
import { LoadingState, ErrorPresenter } from './States';
import { useAuth } from '../context/AuthContext';
import { healthTone } from '../utils/constants';
import { summarizeModelHealth, modelHealthStatus } from '../utils/modelHealth';
import { RefreshIcon } from './Icons';
import { tip } from './Tooltip';
import './ModelHealth.css';
import { formatDateTime, formatDuration, formatNumber } from '../i18n/formatters';

// Recent health checks of a model endpoint from GET /v1.0/api/model-endpoints/{id}/health-history (P8.34). The server
// writes one record per validate and per background sweep check, newest first, bounded per endpoint.

/** How many records the panel asks for (the server allows 1 to 1000 and keeps about 100 per endpoint). */
export const HEALTH_HISTORY_MAX = 50;

const EVENT = 'hugo:model-validation';

/**
 * Tell any open history panel of an endpoint that a validation just ran, so it reads the history again. Returns the
 * value unchanged, so it can sit in a promise chain.
 */
export function notifyValidated(endpointId, value) {
  try {
    window.dispatchEvent(new CustomEvent(EVENT, { detail: { endpointId } }));
  } catch {
    /* not in a browser */
  }
  return value;
}

/** Read-only summary of the bounded recent sample, with an optional per-check detail table. */
export function ModelHealthHistory({ endpointId }) {
  const { t, i18n } = useTranslation();
  const { apiClient } = useAuth();
  const [snapshot, setSnapshot] = useState({ endpointId: null, items: null, error: null, loading: false });
  const generation = useRef(0);
  const mounted = useRef(false);

  const load = useCallback(() => {
    if (!apiClient || !endpointId || !mounted.current) return;
    const request = ++generation.current;
    setSnapshot((previous) => ({ endpointId, items: previous.endpointId === endpointId ? previous.items : null, error: null, loading: true }));
    apiClient
      .getModelEndpointHealthHistory(endpointId, HEALTH_HISTORY_MAX)
      .then((list) => {
        if (!mounted.current || generation.current !== request) return;
        setSnapshot({
          endpointId,
          items: Array.isArray(list) ? list.filter((item) => item && typeof item === 'object').slice(0, HEALTH_HISTORY_MAX) : [],
          error: null,
          loading: false
        });
      })
      .catch((error) => {
        if (!mounted.current || generation.current !== request) return;
        setSnapshot({ endpointId, items: null, error, loading: false });
      });
  }, [apiClient, endpointId]);

  const invalidate = useCallback(() => {
    generation.current++;
  }, []);

  useEffect(() => {
    mounted.current = true;
    load();
    return () => {
      mounted.current = false;
      invalidate();
    };
  }, [load, invalidate]);

  useEffect(() => {
    const onValidated = (event) => {
      if (!event?.detail?.endpointId || event.detail.endpointId === endpointId) load();
    };
    window.addEventListener(EVENT, onValidated);
    return () => window.removeEventListener(EVENT, onValidated);
  }, [endpointId, load]);

  const current = snapshot.endpointId === endpointId ? snapshot : { items: null, error: null, loading: true };
  const { items, error, loading } = current;
  const summary = summarizeModelHealth(items || []);
  const lng = i18n.language;
  const date = (value) => (value ? formatDateTime(value, lng) : '-');
  const number = (value) => formatNumber(value, {}, lng);
  const statusLabel = (value) => t(`enums.healthStatus.${modelHealthStatus(value)}`);
  const stats = [
    ['status', <EnumBadge key="status" group="healthStatus" value={summary.status} tone={healthTone(summary.status)} />],
    ['successRate', summary.successRate == null ? '-' : new Intl.NumberFormat(lng, { style: 'percent', maximumFractionDigits: 1 }).format(summary.successRate)],
    ['checks', number(summary.total)],
    ['historySpan', summary.spanMs == null ? '-' : formatDuration(summary.spanMs, lng)],
    ['consecutiveHealthy', number(summary.consecutiveHealthy)],
    ['consecutiveUnhealthy', number(summary.consecutiveUnhealthy)]
  ];

  return (
    <section className="model-health-history" data-testid="model-health-history" aria-busy={loading}>
      <div className="model-health-heading">
        <h3 className="section-title">{t('resources.models.history.title')}</h3>
        <button type="button" className="button-secondary button-sm" disabled={loading} onClick={load} {...tip(t('tips.modelHealth.refresh'))}>
          <RefreshIcon size={14} /> {t('resources.models.health.refresh')}
        </button>
      </div>
      <p className="field-hint">{t('resources.models.health.observedNote', { count: summary.total })}</p>
      {error ? (
        <ErrorPresenter error={error} onRetry={load} />
      ) : items === null ? (
        <LoadingState />
      ) : items.length === 0 ? (
        <p className="text-muted">{t('resources.models.history.empty')}</p>
      ) : (
        <>
          <dl className="model-health-stats">
            {stats.map(([key, value]) => (
              <div className="model-health-stat" key={key}>
                <dt>{t(`resources.models.health.${key}`)}</dt>
                <dd>{value}</dd>
              </div>
            ))}
          </dl>
          <div className="model-health-chart" role="list" aria-label={t('resources.models.health.chartLabel')}>
            {summary.oldestFirst.map((item, index) => {
              const status = modelHealthStatus(item.status);
              const label = t('resources.models.health.chartCheck', {
                time: date(item.checkedUtc),
                status: statusLabel(status),
                latency: item.latencyMs == null ? '-' : formatDuration(item.latencyMs, lng)
              });
              return (
                <span
                  key={item.id || index}
                  role="listitem"
                  className={`model-health-bar model-health-${status.toLowerCase()}`}
                  title={label}
                  aria-label={label}
                />
              );
            })}
          </div>
          <div className="model-health-legend">
            {['Healthy', 'Unhealthy', 'Unknown'].map((status) => (
              <span key={status}>
                <i className={`model-health-swatch model-health-${status.toLowerCase()}`} aria-hidden="true" />
                {t(`resources.models.health.legend${status}`)}
              </span>
            ))}
          </div>
          {summary.lastError && (
            <div className="model-health-error">
              <strong>{t('resources.models.health.lastError')}</strong>
              <p>{summary.lastError}</p>
            </div>
          )}
          <dl className="model-health-times">
            {['firstCheck', 'lastCheck', 'lastHealthy', 'lastUnhealthy'].map((key) => (
              <div key={key}>
                <dt>{t(`resources.models.health.${key}`)}</dt>
                <dd>{date(summary[key])}</dd>
              </div>
            ))}
          </dl>
          <details className="model-health-details">
            <summary {...tip(t('tips.modelHealth.details'))}>{t('resources.models.health.details')}</summary>
            <div className="table-scroll">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>{t('resources.models.health.status')}</th>
                    <th>{t('resources.conversations.columns.source')}</th>
                    <th>{t('views.requests.columns.time')}</th>
                    <th>{t('resources.models.validation.latency')}</th>
                    <th>{t('components.chat.message')}</th>
                  </tr>
                </thead>
                <tbody>
                  {summary.newestFirst.map((item, index) => (
                    <tr key={item.id || index} data-testid="model-health-record">
                      <td>
                        <EnumBadge group="healthStatus" value={modelHealthStatus(item.status)} tone={healthTone(item.status)} />
                      </td>
                      <td>
                        <EnumBadge group="modelHealthSource" value={item.source} tone="info" />
                      </td>
                      <td>{date(item.checkedUtc)}</td>
                      <td>{item.latencyMs == null ? '-' : formatDuration(item.latencyMs, lng)}</td>
                      <td className="model-health-message">{item.message || '-'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </details>
        </>
      )}
    </section>
  );
}
