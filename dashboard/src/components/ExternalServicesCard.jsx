import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import CopyButton from './CopyButton';
import GrafanaDashboardLinks from './GrafanaDashboardLinks';
import { Badge } from './StatusBadge';
import { tip } from './Tooltip';
import { useAuth } from '../context/AuthContext';
import useServerInfo from '../hooks/useServerInfo';
import { GITHUB_URL } from '../utils/constants';
import { observabilityCards } from '../utils/observability';

export const OBSERVABILITY_DOCS_URL = `${GITHUB_URL}/blob/main/docs/TELEMETRY.md`;

const TONES = { reachable: 'success', unreachable: 'danger', checking: 'neutral', unknown: 'warning' };

/** How often Home re-reads GET /observability/status. The server caches probes for 30 seconds. */
const STATUS_REFRESH_MS = 30000;

/**
 * Home Observability section (P13.12, P13.14; replaces M1-05). One card per service that GET /server-info reports as
 * configured (Grafana, Prometheus, Loki, Tempo), each with the reachability the server probed
 * (GET /observability/status), the version it reported, and an Open link. With nothing configured a single note links
 * to the telemetry guide. Nothing is assumed: no localhost URLs and no default credentials.
 */
export default function ExternalServicesCard() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const info = useServerInfo();
  // undefined: checking; null: the status request failed; array: the server's answer.
  const [status, setStatus] = useState(undefined);

  const cards = info ? observabilityCards(info, status) : null;
  const anyConfigured = !!cards && cards.length > 0;

  useEffect(() => {
    if (!apiClient || !anyConfigured) return undefined;
    let cancelled = false;
    const load = () =>
      apiClient
        .getObservabilityStatus()
        .then((s) => !cancelled && setStatus(Array.isArray(s) ? s : null))
        .catch(() => !cancelled && setStatus(null));
    load();
    const id = setInterval(load, STATUS_REFRESH_MS);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, [apiClient, anyConfigured]);

  if (cards === null) return null;
  return (
    <section className="panel" aria-labelledby="ext-services-title" data-testid="observability">
      <h2 id="ext-services-title" className="section-title" {...tip(t('tips.views.home.observability'))}>
        {t('views.home.observability')}
      </h2>
      {cards.length === 0 ? (
        <p className="text-muted" data-testid="observability-setup">
          {t('views.home.observabilityNone')}{' '}
          <a href={OBSERVABILITY_DOCS_URL} target="_blank" rel="noopener noreferrer" {...tip(t('tips.views.home.observabilityDocs'))}>
            {t('views.home.observabilitySetup')}
          </a>
        </p>
      ) : (
        <ul className="external-list observability-cards">
          {cards.map((s) => {
            const name = t(`views.home.services.${s.id}`, { defaultValue: s.id });
            return (
              <li key={s.id} className="external-item observability-card" data-service={s.id} data-state={s.state}>
                <div className="observability-card-head">
                  <span className="external-name">{name}</span>
                  <span data-testid={`observability-state-${s.id}`}>
                    <Badge tone={TONES[s.state]} tip={t(`tips.views.home.observabilityState.${s.state}`, { service: name })}>
                      {t(`views.home.observabilityState.${s.state}`)}
                    </Badge>
                  </span>
                  {s.version && (
                    <span className="text-muted" data-testid={`observability-version-${s.id}`} {...tip(t('tips.views.home.observabilityVersion', { service: name }))}>
                      {t('views.home.observabilityVersion', { version: s.version })}
                    </span>
                  )}
                </div>
                <div className="external-url">
                  <code>{s.url}</code>
                  <CopyButton value={s.url} />
                </div>
                <div>
                  <a
                    className="button button-secondary button-sm"
                    href={s.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    data-testid={`observability-open-${s.id}`}
                    {...tip(t(`tips.views.home.services.${s.id}`, { defaultValue: t('tips.views.home.serviceGeneric') }))}
                  >
                    {t('views.home.observabilityOpen', { service: name })}
                  </a>
                </div>
              </li>
            );
          })}
        </ul>
      )}
      <GrafanaDashboardLinks info={info} />
    </section>
  );
}
