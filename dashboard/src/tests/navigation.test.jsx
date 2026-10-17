import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { setActiveLocale } from '../i18n';
import AuthContext from '../context/AuthContext';
import AppContext from '../context/AppContext';
import { ApprovalsProvider } from '../context/ApprovalsContext';
import Sidebar from '../components/Sidebar';
import HubView from '../views/hubs/HubView';
import LegacyRedirect from '../components/LegacyRedirect';
import { HUBS, PAGES, LEGACY_REDIRECTS, findHub, pathFor } from '../config/navConfig';

// Keep routing and authorization real while isolating the panels from network requests.
vi.mock('../views/hubs/hubPanels', async () => {
  const { PAGES } = await import('../config/navConfig');
  return {
    PAGE_VIEWS: Object.fromEntries(PAGES.map((page) => [page.id, () => <div data-testid="page-panel">{page.id}</div>])),
    DETAIL_VIEWS: {}
  };
});

beforeAll(async () => {
  await setActiveLocale('en');
});
afterEach(cleanup);

function Location() {
  const location = useLocation();
  return (
    <output data-testid="location">
      {location.pathname}
      {location.search}
      {location.hash}
    </output>
  );
}

function renderNavigation(ui, path, principal = {}) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthContext.Provider value={{ principal, serverUrl: 'http://127.0.0.1:8800' }}>
        <AppContext.Provider value={{ setWizardOpen: vi.fn(), setTourOpen: vi.fn() }}>
          <ApprovalsProvider value={{ pending: 3 }}>
            {ui}
            <Location />
          </ApprovalsProvider>
        </AppContext.Provider>
      </AuthContext.Provider>
    </MemoryRouter>
  );
}

describe('consolidated dashboard navigation', () => {
  it('retains all 28 unique pages in nine hubs', () => {
    const ids = [
      'home',
      'chat',
      'profiles',
      'approvals',
      'conversations',
      'resources',
      'secrets',
      'connectors',
      'models',
      'promptTemplates',
      'knowledge',
      'toolInvocations',
      'feedback',
      'requests',
      'analytics',
      'audit',
      'explorer',
      'mcpExplorer',
      'a2aExplorer',
      'tenants',
      'users',
      'credentials',
      'roles',
      'assignments',
      'sessions',
      'settings',
      'serverInfo',
      'plugins'
    ];
    expect(PAGES.map((page) => page.id).sort()).toEqual(ids.sort());
    expect(new Set(PAGES.map((page) => page.slug)).size).toBe(28);
    expect(HUBS).toHaveLength(9);
  });

  it.each([
    ['profiles', 'profile/one', '/dashboard/profiles/profile%2Fone'],
    ['resources', 'res one', '/dashboard/resources/res%20one'],
    ['connectors', 'ssh', '/dashboard/connectors/ssh'],
    ['promptTemplates', 'prompt_1', '/dashboard/prompts/prompt_1'],
    ['chat', 'cnv_1', '/dashboard/chat/cnv_1']
  ])('retains the %s detail URL and its extra query parameters', (page, id, path) => {
    expect(pathFor(page, id, { filter: 'a b' })).toBe(`${path}?filter=a+b`);
  });

  it('highlights the owning hub for resource details and exposes exactly nine sidebar links', () => {
    const { container } = renderNavigation(<Sidebar />, '/dashboard/resources/res_1');
    const links = container.querySelectorAll('a[data-nav]');
    expect(links).toHaveLength(9);
    expect(container.querySelector('a[aria-current="page"]').getAttribute('data-nav')).toBe('connections');
    expect(container.querySelector('a[data-nav="connections"]').getAttribute('href')).toBe('/dashboard/connections');
    expect(screen.getByTestId('approvals-badge').textContent).toBe('3');
  });

  it.each([
    [{}, ['credentials']],
    [{ isTenantAdmin: true }, ['users', 'credentials', 'roles', 'assignments', 'sessions']],
    [{ isAdmin: true }, ['tenants', 'users', 'credentials', 'roles', 'assignments', 'sessions']]
  ])('filters access tabs for principal %j, including a hidden bookmarked tab', (principal, visible) => {
    renderNavigation(<HubView hubKey="access" />, '/dashboard/access?tab=tenants', principal);
    expect(screen.getAllByRole('tab').map((tab) => tab.getAttribute('data-tab-key'))).toEqual(visible);
    expect(screen.getByTestId('page-panel').textContent).toBe(visible[0]);
  });

  it.each([
    [{}, ['server-info'], 'serverInfo'],
    [{ isTenantAdmin: true }, ['settings', 'server-info'], 'settings'],
    [{ isAdmin: true }, ['settings', 'server-info', 'plugins'], 'plugins']
  ])('filters system tabs for principal %j without rendering hidden plugins', (principal, visible, active) => {
    renderNavigation(<HubView hubKey="system" />, '/dashboard/system?tab=plugins', principal);
    expect(screen.getAllByRole('tab').map((tab) => tab.getAttribute('data-tab-key'))).toEqual(visible);
    expect(screen.getByTestId('page-panel').textContent).toBe(active);
  });

  it('opens a bookmarked activity tab, mounts one panel, and clears tab-specific filters on switching', () => {
    renderNavigation(<HubView hubKey="activity" />, '/dashboard/activity?tab=analytics&status=Failed');
    expect(screen.getAllByTestId('page-panel')).toHaveLength(1);
    expect(screen.getByTestId('page-panel').textContent).toBe('analytics');
    fireEvent.click(screen.getAllByRole('tab').find((tab) => tab.getAttribute('data-tab-key') === 'audit'));
    expect(screen.getByTestId('page-panel').textContent).toBe('audit');
    expect(screen.getByTestId('location').textContent).toBe('/dashboard/activity?tab=audit');
  });

  it('opens Feedback as an Activity tab and retains its agent filter', () => {
    renderNavigation(<HubView hubKey="activity" />, '/dashboard/activity?tab=feedback&agentProfileId=agp_one');
    expect(screen.getByTestId('page-panel').textContent).toBe('feedback');
    expect(screen.getByTestId('location').textContent).toContain('agentProfileId=agp_one');
    expect(pathFor('feedback')).toBe('/dashboard/activity?tab=feedback');
  });

  it('falls back to the first visible tab for an unknown tab', () => {
    renderNavigation(<HubView hubKey="agents" />, '/dashboard/agents?tab=missing');
    expect(screen.getByTestId('page-panel').textContent).toBe('profiles');
  });

  it.each(LEGACY_REDIRECTS.filter((redirect) => !['rbac', 'settings'].includes(redirect.from)))(
    'preserves filters and hash when redirecting $from',
    (redirect) => {
      renderNavigation(
        <Routes>
          <Route path={`/dashboard/${redirect.from}`} element={<LegacyRedirect to={redirect.to} tab={redirect.tab} />} />
          <Route path="*" element={<div>destination</div>} />
        </Routes>,
        `/dashboard/${redirect.from}?tab=wrong&filter=needs+review&filter=second#record`
      );
      const destination = new URL(screen.getByTestId('location').textContent, 'http://dashboard.test');
      expect(destination.pathname).toBe(redirect.to);
      expect(destination.searchParams.get('tab')).toBe(redirect.tab || null);
      expect(destination.searchParams.getAll('filter')).toEqual(['needs review', 'second']);
      expect(destination.hash).toBe('#record');
    }
  );

  it.each([
    ['rbac', 'roles', 'access', 'roles'],
    ['rbac', 'assignments', 'access', 'assignments'],
    ['rbac', 'plugins', 'access', 'roles'],
    ['settings', 'server-info', 'system', 'server-info'],
    ['settings', 'plugins', 'system', 'settings']
  ])('maps old %s tab %s into %s without losing filters or hash', (from, requested, hub, expectedTab) => {
    const redirect = LEGACY_REDIRECTS.find((entry) => entry.from === from);
    expect(redirect).toBeDefined();
    renderNavigation(
      <Routes>
        <Route path={`/dashboard/${from}`} element={<LegacyRedirect to={redirect.to} tab={redirect.tab} tabs={redirect.tabs} />} />
        <Route path="*" element={<div>destination</div>} />
      </Routes>,
      `/dashboard/${from}?tab=${requested}&filter=active#selected`
    );
    const destination = new URL(screen.getByTestId('location').textContent, 'http://dashboard.test');
    expect(destination.pathname).toBe(`/dashboard/${hub}`);
    expect(destination.searchParams.get('tab')).toBe(expectedTab);
    expect(destination.searchParams.get('filter')).toBe('active');
    expect(destination.hash).toBe('#selected');
  });

  it('defines all required tab groups in their intended order', () => {
    expect(findHub('connections').tabs.map((page) => page.slug)).toEqual(['secrets', 'resources', 'connectors', 'models']);
    expect(findHub('instructions').tabs.map((page) => page.slug)).toEqual(['prompts', 'knowledge']);
    expect(findHub('developerTools').tabs.map((page) => page.slug)).toEqual(['explorer', 'mcp-explorer', 'a2a-explorer']);
  });
});
