import { useLocation, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';
import { usePendingApprovals } from '../context/ApprovalsContext';
import { formatNumber } from '../i18n/formatters';
import { NAV_SECTIONS, DASHBOARD_BASE, hubIsActive } from '../config/navConfig';
import { allowed } from '../utils/principal';
import { APP_VERSION } from '../utils/constants';
import * as Icons from './Icons';
import { tip } from './Tooltip';

/** 'local' when the server is on this machine (a loopback address), otherwise 'remote'. */
export function environmentOf(serverUrl) {
  try {
    const host = new URL(serverUrl).hostname;
    return host === '127.0.0.1' || host === '::1' || host === '[::1]' ? 'local' : 'remote';
  } catch {
    return 'remote';
  }
}

/** A hub is visible when its standalone page, or at least one of its tabs, is allowed. */
function hubVisible(hub, principal) {
  if (hub.page) return allowed(hub.page.requires, principal);
  return hub.tabs.some((tab) => allowed(tab.requires, principal));
}

/**
 * Grouped, role-aware sidebar rendered from config/navConfig.js (plan section 18.2), with a utility footer (setup
 * wizard, tour, version, environment). Each item is a hub; tabbed hubs show their pages as in-page tabs. The
 * Agents item carries the pending approval count.
 */
export default function Sidebar({ onNavigateMobile }) {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { pending } = usePendingApprovals();
  const { pathname } = useLocation();
  const { principal, serverUrl } = useAuth();
  const environment = environmentOf(serverUrl);
  const { setWizardOpen, setTourOpen } = useApp();

  const go = (path) => {
    navigate(path);
    onNavigateMobile?.();
  };

  return (
    <aside className="dashboard-sidebar" data-tour="sidebar">
      <div className="sidebar-brand">
        <img src="/logo.png" alt="" className="brand-logo" />
        <span className="sidebar-brand-text">
          <span>{t('common.appName')}</span>
          <span className="sidebar-brand-tagline">{t('common.tagline')}</span>
        </span>
      </div>
      <nav className="sidebar-nav" aria-label={t('nav.primary')}>
        {NAV_SECTIONS.map((section) => {
          const hubs = section.items.filter((hub) => hubVisible(hub, principal));
          if (hubs.length === 0) return null;
          return (
            <div className="sidebar-group" key={section.key}>
              <div className="sidebar-group-label">{t(`nav.sections.${section.key}`)}</div>
              {hubs.map((hub) => {
                const Icon = Icons[hub.icon];
                const label = t(`nav.hubs.${hub.key}.label`);
                const path = `${DASHBOARD_BASE}/${hub.slug}`;
                const active = hubIsActive(hub, pathname);
                return (
                  <a
                    key={hub.key}
                    href={path}
                    className={`sidebar-item ${active ? 'active' : ''}`}
                    aria-current={active ? 'page' : undefined}
                    onClick={(e) => {
                      if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return;
                      e.preventDefault();
                      go(path);
                    }}
                    {...tip(t(`tips.nav.hubs.${hub.key}`))}
                    data-nav={hub.key}
                  >
                    {Icon && <Icon size={18} />}
                    <span>{label}</span>
                    {(hub.page?.id === 'approvals' || hub.tabs?.some((tab) => tab.id === 'approvals')) && pending > 0 && (
                      <span
                        className="sidebar-badge"
                        data-testid="approvals-badge"
                        aria-label={t('nav.approvalsPending', { count: pending })}
                        {...tip(t('tips.nav.approvalsBadge', { count: pending }))}
                      >
                        {pending > 99 ? '99+' : formatNumber(pending, {}, i18n.language)}
                      </span>
                    )}
                  </a>
                );
              })}
            </div>
          );
        })}
      </nav>
      <div className="sidebar-footer">
        <button type="button" className="sidebar-utility" onClick={() => setWizardOpen(true)} {...tip(t('tips.nav.setup'))}>
          <Icons.WandIcon size={16} />
          <span>{t('nav.setup')}</span>
        </button>
        <button type="button" className="sidebar-utility" onClick={() => setTourOpen(true)} {...tip(t('tips.nav.tour'))}>
          <Icons.HelpIcon size={16} />
          <span>{t('nav.tour')}</span>
        </button>
        <div className="sidebar-version">
          <span>{t('nav.version', { version: APP_VERSION })}</span>
          <span className={`env-pill env-${environment}`} data-testid="environment" {...tip(t(`tips.nav.environment.${environment}`))}>
            {t(`nav.environment.${environment}`)}
          </span>
        </div>
      </div>
    </aside>
  );
}
