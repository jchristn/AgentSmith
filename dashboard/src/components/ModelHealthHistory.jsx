import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { EnumBadge } from './StatusBadge';
import { LoadingState, ErrorPresenter } from './States';
import { useAuth } from '../context/AuthContext';
import { healthTone } from '../utils/constants';
import { formatDateTime, formatDuration } from '../i18n/formatters';

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

/** The health checks of one endpoint: status, source (Sweep or Validate), time, latency, and message. */
export function ModelHealthHistory({ endpointId }) {
  const { t, i18n } = useTranslation();
  const { apiClient } = useAuth();
  const [items, setItems] = useState(null);
  const [error, setError] = useState(null);

  const load = useCallback(() => {
    if (!apiClient || !endpointId) return undefined;
    let active = true;
    apiClient
      .getModelEndpointHealthHistory(endpointId, HEALTH_HISTORY_MAX)
      .then((list) => {
        if (!active) return;
        setItems(Array.isArray(list) ? list : []);
        setError(null);
      })
      .catch((e) => {
        if (!active) return;
        setItems([]);
        setError(e);
      });
    return () => {
      active = false;
    };
  }, [apiClient, endpointId]);

  useEffect(load, [load]);

  useEffect(() => {
    const onValidated = (e) => {
      if (!e?.detail?.endpointId || e.detail.endpointId === endpointId) load();
    };
    window.addEventListener(EVENT, onValidated);
    return () => window.removeEventListener(EVENT, onValidated);
  }, [endpointId, load]);

  const lng = i18n.language;
  return (
    <section className="model-health-history" data-testid="model-health-history">
      <h3 className="section-title">{t('resources.models.history.title')}</h3>
      <p className="field-hint">{t('resources.models.history.note')}</p>
      {error ? (
        <ErrorPresenter error={error} onRetry={load} />
      ) : items === null ? (
        <LoadingState />
      ) : items.length === 0 ? (
        <p className="text-muted">{t('resources.models.history.empty')}</p>
      ) : (
        <ul className="plain-list bare-list">
          {items.map((v) => (
            <li key={v.id || v.checkedUtc} data-testid="model-health-record">
              <EnumBadge group="healthStatus" value={v.status || 'Unknown'} tone={healthTone(v.status)} />{' '}
              <EnumBadge group="modelHealthSource" value={v.source} tone="info" /> <span className="text-muted">{formatDateTime(v.checkedUtc, lng)}</span>
              {v.latencyMs != null && <span className="text-muted"> {formatDuration(v.latencyMs, lng)}</span>}
              {v.message && <div className="field-hint">{v.message}</div>}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
