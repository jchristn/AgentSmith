import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import PageHeader from '../components/PageHeader';
import Explainer from '../components/Explainer';
import { LoadingState, UnavailableState, ErrorPresenter, EmptyState } from '../components/States';
import { Badge, EnumBadge } from '../components/StatusBadge';
import { tip } from '../components/Tooltip';
import { PlugIcon, RefreshIcon } from '../components/Icons';
import { useAuth } from '../context/AuthContext';
import { pathFor } from '../config/navConfig';
import { ENUMS } from '../utils/constants';
import { isMissingEndpoint } from '../utils/errors';
import { enumLabel } from '../utils/enums';
import { formatNumber } from '../i18n/formatters';

/** Filter connectors by category and a search over type, family, name, and description. */
export function filterConnectors(connectors, { category, search }) {
  const q = String(search || '')
    .trim()
    .toLowerCase();
  return (connectors || []).filter((c) => {
    if (category && (c.category || 'Other') !== category) return false;
    if (!q) return true;
    return [c.type, c.family, c.displayName, c.description].some((v) => String(v || '').toLowerCase().includes(q));
  });
}

/**
 * Connector catalog (P8.33): every service family the server can reach, as cards grouped by category with a search.
 * A card opens the connector's detail page (settings schema, secret slots, tools with effects, default
 * instructions). Plugin connectors that failed to load are listed by the server with their error.
 */
export default function ConnectorsView() {
  const { t, i18n } = useTranslation();
  const { apiClient } = useAuth();
  const navigate = useNavigate();
  const [connectors, setConnectors] = useState(null);
  const [error, setError] = useState(null);
  const [category, setCategory] = useState('');
  const [search, setSearch] = useState('');

  const load = useCallback(() => {
    setError(null);
    apiClient
      .listConnectors({ pageNumber: 1, pageSize: 1000 })
      .then((r) => setConnectors(r.items || []))
      .catch((e) => {
        setConnectors([]);
        setError(e);
      });
  }, [apiClient]);

  useEffect(load, [load]);

  const visible = useMemo(() => filterConnectors(connectors, { category, search }), [connectors, category, search]);

  return (
    <div>
      <PageHeader
        title={t('views.connectors.title')}
        subtitle={t('views.connectors.subtitle')}
        actions={
          <button type="button" className="button-secondary" onClick={load} {...tip(t('tips.table.refresh'))}>
            <RefreshIcon size={14} /> {t('components.table.refresh')}
          </button>
        }
      />
      <Explainer topic="connectors" />
      {connectors === null ? (
        <LoadingState />
      ) : isMissingEndpoint(error) ? (
        <UnavailableState feature={t('views.connectors.title')} purpose={t('views.connectors.purpose')} endpoint="/v1.0/api/connectors" onRetry={load} />
      ) : error ? (
        <ErrorPresenter error={error} onRetry={load} />
      ) : (
        <>
          <div className="filter-inline connector-filters">
            <input type="search" className="filter-search" value={search} placeholder={t('views.connectors.search')} aria-label={t('views.connectors.search')} onChange={(e) => setSearch(e.target.value)} {...tip(t('tips.views.connectors.search'))} />
            <label className="filter-select" {...tip(t('tips.views.connectors.category'))}>
              <span className="sr-only">{t('views.connectors.category')}</span>
              <select value={category} onChange={(e) => setCategory(e.target.value)} aria-label={t('views.connectors.category')} {...tip(t('tips.views.connectors.category'))}>
                <option value="">
                  {t('views.connectors.category')}: {t('common.generic.all')}
                </option>
                {ENUMS.connectorCategory.map((c) => (
                  <option key={c} value={c}>
                    {enumLabel(t, 'connectorCategory', c)}
                  </option>
                ))}
              </select>
            </label>
            <span className="text-muted">{t('views.connectors.count', { count: visible.length, shown: formatNumber(visible.length, {}, i18n.language) })}</span>
          </div>
          {visible.length === 0 ? (
            <EmptyState title={t('views.connectors.emptyTitle')} body={t('views.connectors.emptyBody')} />
          ) : (
            <div className="card-grid" data-testid="connector-grid">
              {visible.map((c) => (
                <button key={c.type} type="button" className="connector-card" onClick={() => navigate(pathFor('connectors', c.type))} data-connector={c.type} {...tip(t('tips.views.connectors.card', { name: c.displayName || c.type }))}>
                  <div className="connector-card-head">
                    <PlugIcon size={18} />
                    <strong>{c.displayName || c.type}</strong>
                    <EnumBadge group="connectorCategory" value={c.category || 'Other'} tone="info" />
                  </div>
                  <p className="connector-card-body">{c.description || t('views.connectors.noDescription')}</p>
                  <div className="connector-card-meta">
                    <code>{c.type}</code>
                    <span className="text-muted">{t('views.connectors.toolCount', { count: (c.tools || []).length })}</span>
                    {c.loadError && <Badge tone="danger">{t('views.connectors.loadError')}</Badge>}
                  </div>
                </button>
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}
