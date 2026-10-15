import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Badge } from './StatusBadge';
import { formatDateTime, formatDuration } from '../i18n/formatters';

// The server keeps only the last health check per model endpoint (healthStatus, lastHealthUtc, lastHealthMessage);
// it does not expose a health history route. The dashboard keeps the validations run in this browser session so an
// operator can compare attempts (P8.34). The list lives in sessionStorage and is capped per endpoint.

const STORAGE_KEY = 'hugo_model_validations';
const MAX_PER_ENDPOINT = 20;
const EVENT = 'hugo:model-validation';

function readAll() {
  try {
    return JSON.parse(sessionStorage.getItem(STORAGE_KEY) || '{}') || {};
  } catch {
    return {};
  }
}

/** Validations recorded this session for one endpoint, newest first. */
export function validationHistory(endpointId) {
  const list = readAll()[endpointId];
  return Array.isArray(list) ? list : [];
}

/**
 * Record a validation result ({ success, latencyMs, error, problems, model, dimensions }) for an endpoint and return
 * the result unchanged, so it can sit in a promise chain.
 */
export function recordValidation(endpointId, result) {
  if (!endpointId) return result;
  try {
    const all = readAll();
    const entry = {
      utc: new Date().toISOString(),
      success: !!result?.success,
      latencyMs: result?.latencyMs ?? null,
      error: result?.error || (result?.problems || []).join(' ') || null,
      model: result?.model || null,
      dimensions: result?.dimensions ?? null
    };
    all[endpointId] = [entry, ...(all[endpointId] || [])].slice(0, MAX_PER_ENDPOINT);
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(all));
    window.dispatchEvent(new CustomEvent(EVENT, { detail: { endpointId } }));
  } catch {
    /* storage unavailable */
  }
  return result;
}

/** The validations of one endpoint run in this browser session, with a note that the server keeps only the last. */
export function ModelHealthHistory({ endpointId }) {
  const { t, i18n } = useTranslation();
  const [items, setItems] = useState(() => validationHistory(endpointId));

  useEffect(() => {
    setItems(validationHistory(endpointId));
    const onChange = (e) => {
      if (!e?.detail?.endpointId || e.detail.endpointId === endpointId) setItems(validationHistory(endpointId));
    };
    window.addEventListener(EVENT, onChange);
    return () => window.removeEventListener(EVENT, onChange);
  }, [endpointId]);

  return (
    <section className="model-health-history" data-testid="model-health-history">
      <h3 className="section-title">{t('resources.models.history.title')}</h3>
      <p className="field-hint">{t('resources.models.history.note')}</p>
      {items.length === 0 ? (
        <p className="text-muted">{t('resources.models.history.empty')}</p>
      ) : (
        <ul className="plain-list bare-list">
          {items.map((v) => (
            <li key={v.utc}>
              <Badge tone={v.success ? 'success' : 'danger'}>{v.success ? t('components.result.succeeded') : t('components.result.failed')}</Badge>{' '}
              <span className="text-muted">{formatDateTime(v.utc, i18n.language)}</span>
              {v.latencyMs != null && <span className="text-muted"> {formatDuration(v.latencyMs, i18n.language)}</span>}
              {v.error && <div className="field-hint">{v.error}</div>}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
