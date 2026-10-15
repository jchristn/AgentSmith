import { useEffect, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import { HubProvider } from '../../context/HubContext';
import { UrlTabs } from '../../components/Tabs';
import { EmptyState } from '../../components/States';
import { findHub, TAB_PARAM } from '../../config/navConfig';
import { allowed } from '../../utils/principal';
import { PAGE_VIEWS } from './hubPanels';

const EMBEDDED = { embedded: true };

/**
 * A tabbed hub page: the hub title plus a URL-synced (?tab=) tab strip. Tabs, labels, and gating
 * come from config/navConfig.js; components come from hubPanels.jsx. Tab views keep their own
 * internals but render a compact header (no duplicate page title).
 */
export default function HubView({ hubKey, apiClient }) {
  const { t } = useTranslation();
  const { principal } = useAuth();
  const [searchParams] = useSearchParams();
  const hub = findHub(hubKey);
  const title = hub ? t(`nav.hubs.${hub.key}.label`) : '';

  const tabs = useMemo(
    () =>
      (hub?.tabs || [])
        .filter((page) => allowed(page.requires, principal) && PAGE_VIEWS[page.id])
        .map((page) => {
          const View = PAGE_VIEWS[page.id];
          return {
            id: page.slug,
            label: t(`nav.items.${page.id}.label`),
            tip: t(`tips.nav.tabs.${page.id}`),
            render: () => (
              <HubProvider value={EMBEDDED}>
                <View key={page.id} apiClient={apiClient} />
              </HubProvider>
            )
          };
        }),
    [hub, principal, t, apiClient]
  );

  const requested = searchParams.get(TAB_PARAM);
  const active = tabs.find((tab) => tab.id === requested) || tabs[0];
  const appName = t('common.appName');

  useEffect(() => {
    if (!title) return;
    document.title = active && active.label !== title ? `${active.label} | ${title} | ${appName}` : `${title} | ${appName}`;
  }, [active, title, appName]);

  if (!hub) return null;

  return (
    <div className="hub" data-hub={hub.key}>
      <header className="page-header hub-header">
        <div className="page-header-text">
          <h1 className="page-header-title">{title}</h1>
          <div className="page-header-subtitle">{t(`nav.hubs.${hub.key}.tip`)}</div>
        </div>
      </header>
      {tabs.length > 0 ? <UrlTabs tabs={tabs} label={title} /> : <EmptyState title={t('common.states.forbiddenTitle')} body={t('common.states.forbiddenBody')} />}
    </div>
  );
}
