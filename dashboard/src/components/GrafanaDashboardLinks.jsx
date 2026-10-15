import { useTranslation } from 'react-i18next';
import { LinkIcon } from './Icons';
import { tip } from './Tooltip';
import { sectionDashboardLinks } from '../utils/grafana';

/**
 * Links to the Garrison folder's section dashboards in Grafana (P13.27), one per section, built from server-info
 * `observability.dashboards`. Renders nothing when Grafana is not configured or no dashboards are reported.
 */
export default function GrafanaDashboardLinks({ info }) {
  const { t } = useTranslation();
  const links = sectionDashboardLinks(info);
  if (links.length === 0) return null;
  return (
    <div className="grafana-dashboards" data-testid="grafana-dashboards">
      <h3 className="grafana-dashboards-title" {...tip(t('tips.views.home.grafanaDashboards'))}>
        {t('views.home.grafanaDashboards')}
      </h3>
      <ul className="grafana-dashboard-list">
        {links.map((l) => (
          <li key={l.key}>
            <a
              className="button button-secondary button-sm"
              href={l.url}
              target="_blank"
              rel="noopener noreferrer"
              data-testid={`grafana-dashboard-${l.key}`}
              data-uid={l.uid}
              {...tip(t(`tips.views.home.grafanaSections.${l.key}`))}
            >
              <LinkIcon size={14} /> {t(`views.home.grafanaSections.${l.key}`)}
            </a>
          </li>
        ))}
      </ul>
    </div>
  );
}
