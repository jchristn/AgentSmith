import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';
import { findHub, hubBySlug, detailPageBySlug, pageBySlug, DASHBOARD_BASE } from '../config/navConfig';
import { allowed } from '../utils/principal';
import { STORAGE_KEYS, APPROVAL_POLL_MS } from '../utils/constants';
import { createEventStream } from '../utils/eventStream';
import { ApprovalsProvider } from '../context/ApprovalsContext';
import { EventsProvider } from '../context/EventsContext';
import Sidebar from './Sidebar';
import Topbar from './Topbar';
import SetupWizard from './SetupWizard';
import Tour from './Tour';
import LegacyRedirect from './LegacyRedirect';
import PasswordChangeModal from './PasswordChangeModal';
import { EmptyState } from './States';
import HubView from '../views/hubs/HubView';
import { tip } from './Tooltip';
import { AlertTriangleIcon } from './Icons';
import { PAGE_VIEWS, DETAIL_VIEWS } from '../views/hubs/hubPanels';

/**
 * Resolve /dashboard/:section[/:id] to what the main area renders: a hub (tabbed or standalone),
 * a detail view, a redirect for a tab URL, or a redirect home for anything unknown.
 */
export function resolveRoute(section, id) {
  if (id) {
    const page = detailPageBySlug(section);
    if (page && DETAIL_VIEWS[page.id]) return { kind: 'detail', page, View: DETAIL_VIEWS[page.id] };
  }
  const hub = hubBySlug(section);
  if (hub && !id) {
    if (hub.tabs) return { kind: 'hub', hub };
    return { kind: 'page', page: hub.page, View: PAGE_VIEWS[hub.page.id] };
  }
  // A tab URL (or a detail-style URL with an unknown record route) lands on its hub and tab.
  const legacy = pageBySlug(section);
  if (legacy) return { kind: 'redirect', to: `${DASHBOARD_BASE}/${findHub(legacy.hub).slug}`, tab: legacy.defaultTab ? null : legacy.tab };
  return { kind: 'redirect', to: `${DASHBOARD_BASE}/home` };
}

/** Authenticated shell: sidebar, topbar, live events, pending approvals, lockdown banner, and the routed view. */
export default function Dashboard() {
  const { t } = useTranslation();
  const { section = 'home', id } = useParams();
  const { apiClient, principal, passwordPromptOpen } = useAuth();
  const { sidebarCollapsed, toggleSidebar, tenantId, setWizardOpen, lockdown, setLockdown } = useApp();
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const [health, setHealth] = useState(null);
  const [mode, setMode] = useState('stopped');
  const [pendingApprovals, setPendingApprovals] = useState(null);
  const approvalsReadable = useRef(true);
  const streamRef = useRef(null);
  const route = resolveRoute(section, id);

  // Pending approvals for the sidebar and topbar badges: re-read on approval events, on polling ticks, and every
  // 30 seconds. A 403 or 404 (no Approval Read permission, or a server without approvals yet) hides the badge.
  const ready = !!apiClient;

  const refreshApprovals = useCallback(() => {
    if (!ready || !approvalsReadable.current) return;
    apiClient
      .countPendingApprovals()
      .then((count) => setPendingApprovals(count))
      .catch((e) => {
        if (e?.status === 403 || e?.status === 404 || e?.status === 405) approvalsReadable.current = false;
        setPendingApprovals(null);
      });
  }, [apiClient, ready]);

  // The tenant's lockdown state, so the banner and the topbar control are right after sign-in and tenant switches.
  useEffect(() => {
    if (!ready) return;
    apiClient
      .getLockdown()
      .then((s) => setLockdown(!!s?.lockdown))
      .catch(() => {});
  }, [apiClient, ready, tenantId, setLockdown]);

  useEffect(() => {
    approvalsReadable.current = true;
    refreshApprovals();
    const timer = setInterval(refreshApprovals, APPROVAL_POLL_MS);
    return () => clearInterval(timer);
  }, [refreshApprovals, tenantId]);

  // The tenant event stream, with polling fallback. Views subscribe through EventsContext.
  const stream = useMemo(() => {
    if (!apiClient) return null;
    const s = createEventStream({
      connect: (handlers) => apiClient.streamEvents(handlers.onEvent, { onOpen: handlers.onOpen, onEnd: handlers.onEnd }),
      onMode: setMode,
      poll: () => emitPoll()
    });
    const pollListeners = new Set();
    const emitPoll = () => pollListeners.forEach((fn) => fn(null, { event: 'poll' }));
    return {
      start: () => s.start(),
      stop: () => s.stop(),
      on: (event, fn) => {
        if (event === 'poll') {
          pollListeners.add(fn);
          return () => pollListeners.delete(fn);
        }
        return s.on(event, fn);
      }
    };
  }, [apiClient]);

  useEffect(() => {
    if (!stream || !ready) return undefined;
    streamRef.current = stream;
    stream.start();
    const offs = [
      stream.on('approval.created', refreshApprovals),
      stream.on('approval.resolved', refreshApprovals),
      stream.on('poll', refreshApprovals),
      stream.on('lockdown.changed', (data) => setLockdown(!!(data?.lockdown ?? data?.enabled)))
    ];
    return () => {
      offs.forEach((off) => off());
      stream.stop();
    };
  }, [stream, ready, refreshApprovals, setLockdown]);

  const events = useMemo(() => ({ mode, on: stream ? stream.on : () => () => {} }), [mode, stream]);
  const approvals = useMemo(
    () => ({ pending: pendingApprovals, approvalsPending: pendingApprovals, reviewsPending: null, refresh: refreshApprovals }),
    [pendingApprovals, refreshApprovals]
  );

  useEffect(() => {
    if (!apiClient) return undefined;
    let active = true;
    const check = () =>
      apiClient
        .healthCheck()
        .then(() => active && setHealth(true))
        .catch(() => active && setHealth(false));
    check();
    const timer = setInterval(check, 30000);
    return () => {
      active = false;
      clearInterval(timer);
    };
  }, [apiClient]);

  // Offer the setup wizard automatically for a fresh tenant: no model endpoints and no resources yet. A server that
  // does not expose those routes yet does not trigger it.
  useEffect(() => {
    if (!ready || !tenantId) return;
    if (localStorage.getItem(`${STORAGE_KEYS.setupDismissed}:${tenantId}`) === 'true') return;
    Promise.all([apiClient.listModelEndpoints({ pageNumber: 1, pageSize: 1 }), apiClient.listResources({ pageNumber: 1, pageSize: 1 })])
      .then(([models, resources]) => {
        if ((models.totalCount ?? 0) === 0 && (resources.totalCount ?? 0) === 0) setWizardOpen(true);
      })
      .catch(() => {});
  }, [apiClient, ready, tenantId, setWizardOpen]);

  if (route.kind === 'redirect') return <LegacyRedirect to={route.to} tab={route.tab} />;

  const permitted = route.kind === 'hub' || allowed(route.page.requires, principal);
  let content;
  if (!permitted) content = <EmptyState title={t('common.states.forbiddenTitle')} body={t('common.states.forbiddenBody')} />;
  else if (route.kind === 'hub') content = <HubView key={`${route.hub.key}-${tenantId}`} hubKey={route.hub.key} apiClient={apiClient} />;
  else {
    const View = route.View;
    content = <View key={`${route.page.id}-${id || ''}-${tenantId}`} recordId={id ? decodeURIComponent(id) : undefined} apiClient={apiClient} />;
  }

  const cls = ['dashboard'];
  if (sidebarCollapsed) cls.push('sidebar-collapsed');
  if (mobileNavOpen) cls.push('mobile-nav-open');

  return (
    <EventsProvider value={events}>
      <ApprovalsProvider value={approvals}>
        <div className={cls.join(' ')}>
          <a className="skip-link" href="#main-content" {...tip(t('tips.nav.skipToContent'))}>
            {t('nav.skipToContent')}
          </a>
          <Sidebar onNavigateMobile={() => setMobileNavOpen(false)} />
          {mobileNavOpen && <div className="mobile-nav-scrim" onClick={() => setMobileNavOpen(false)} aria-hidden="true" />}
          <Topbar
            onToggleSidebar={() => {
              if (window.innerWidth <= 900) setMobileNavOpen((m) => !m);
              else toggleSidebar();
            }}
            health={health}
            mode={mode}
          />
          <main className="dashboard-content" id="main-content">
            {lockdown && (
              <div className="lockdown-banner" role="alert" data-testid="lockdown-banner">
                <AlertTriangleIcon size={16} />
                <span>{t('topbar.lockdown.banner')}</span>
              </div>
            )}
            {content}
          </main>
          <SetupWizard />
          <Tour />
          <PasswordChangeModal open={!!passwordPromptOpen} />
        </div>
      </ApprovalsProvider>
    </EventsProvider>
  );
}
