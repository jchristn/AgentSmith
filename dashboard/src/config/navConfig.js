// Single source of truth for dashboard navigation: nine hubs grouped into Workspace and Administration.
//
// The sidebar renders NAV_SECTIONS. Each section holds hubs (sidebar items). A hub is either a
// standalone page (`page`) or a tabbed page (`tabs`), where the active tab lives in `?tab=`.
// Every page that existed before the hubs keeps its slug: it is the tab key, the legacy URL
// `/dashboard/<slug>` replace-redirects to its hub and tab, and detail routes such as
// `/dashboard/profiles/<id>` keep working.
//
// Fields:
//   section: { key (i18n nav.sections.<key>), items }
//   hub:     { key (i18n nav.hubs.<key>.label/.tip, data-nav), slug, icon (name in components/Icons),
//              page? (standalone page id), tabs? (array of pages) }
//   page:    { id (view id and i18n nav.items.<id>), slug (URL segment and tab key), requires?, detail? }
//   A standalone page with `detail: true` also owns `/dashboard/<slug>/<id>` (for example a conversation in Chat).
//
// requires: undefined (everyone), 'admin' (system or tenant admin), 'systemAdmin'.
// Client-side gating is UX only; the server enforces authorization.
// This module is plain data (no JSX) so Node-based tests can import it.

export const DASHBOARD_BASE = '/dashboard';
export const TAB_PARAM = 'tab';

export const NAV_SECTIONS = [
  {
    key: 'workspace',
    items: [
      { key: 'home', slug: 'home', icon: 'HomeIcon', page: { id: 'home', slug: 'home' } },
      { key: 'chat', slug: 'chat', icon: 'ChatIcon', page: { id: 'chat', slug: 'chat', detail: true } },
      {
        key: 'agents',
        slug: 'agents',
        icon: 'BotIcon',
        tabs: [
          { id: 'profiles', slug: 'profiles', detail: true },
          { id: 'approvals', slug: 'approvals' },
          { id: 'conversations', slug: 'conversations' }
        ]
      },
      {
        key: 'connections',
        slug: 'connections',
        icon: 'ServerIcon',
        tabs: [
          { id: 'resources', slug: 'resources', detail: true },
          { id: 'secrets', slug: 'secrets' },
          { id: 'connectors', slug: 'connectors', detail: true },
          { id: 'models', slug: 'models' }
        ]
      },
      {
        key: 'instructions',
        slug: 'instructions',
        icon: 'BookIcon',
        tabs: [
          { id: 'promptTemplates', slug: 'prompts', detail: true },
          { id: 'knowledge', slug: 'knowledge' }
        ]
      },
      {
        key: 'activity',
        slug: 'activity',
        icon: 'ChartIcon',
        tabs: [
          { id: 'toolInvocations', slug: 'tool-invocations' },
          { id: 'requests', slug: 'requests' },
          { id: 'analytics', slug: 'analytics' },
          { id: 'audit', slug: 'audit' }
        ]
      },
      {
        key: 'developerTools',
        slug: 'developer-tools',
        icon: 'PlayIcon',
        tabs: [
          { id: 'explorer', slug: 'explorer' },
          { id: 'mcpExplorer', slug: 'mcp-explorer' },
          { id: 'a2aExplorer', slug: 'a2a-explorer' }
        ]
      }
    ]
  },
  {
    key: 'administration',
    items: [
      {
        key: 'access',
        slug: 'access',
        icon: 'LockIcon',
        tabs: [
          { id: 'tenants', slug: 'tenants', requires: 'systemAdmin' },
          { id: 'users', slug: 'users', requires: 'admin' },
          { id: 'credentials', slug: 'credentials' },
          { id: 'roles', slug: 'roles', requires: 'admin' },
          { id: 'assignments', slug: 'assignments', requires: 'admin' },
          { id: 'sessions', slug: 'sessions', requires: 'admin' }
        ]
      },
      {
        key: 'system',
        slug: 'system',
        icon: 'SettingsIcon',
        tabs: [
          { id: 'settings', slug: 'settings', requires: 'admin', legacyTabs: ['settings', 'server-info'] },
          { id: 'serverInfo', slug: 'server-info' },
          { id: 'plugins', slug: 'plugins', requires: 'systemAdmin' }
        ]
      }
    ]
  }
];

/** Every hub (sidebar item), in sidebar order. */
export const HUBS = NAV_SECTIONS.flatMap((section) => section.items.map((hub) => ({ ...hub, section: section.key })));

/** Every page (standalone page or hub tab) with its hub key and, for tabs, its tab key. */
export const PAGES = HUBS.flatMap((hub) =>
  hub.tabs
    ? hub.tabs.map((tab, index) => ({ ...tab, hub: hub.key, tab: tab.slug, defaultTab: index === 0 }))
    : [{ ...hub.page, hub: hub.key, tab: null, defaultTab: true }]
);

/** Find a hub by key. */
export function findHub(key) {
  return HUBS.find((hub) => hub.key === key) || null;
}

/** Find a hub by its URL slug. */
export function hubBySlug(slug) {
  return HUBS.find((hub) => hub.slug === slug) || null;
}

/** Find a page by id. */
export function findPage(id) {
  return PAGES.find((page) => page.id === id) || null;
}

/** Find a page by its (pre-hub) URL slug. */
export function pageBySlug(slug) {
  return PAGES.find((page) => page.slug === slug) || null;
}

/** Find the page that owns detail routes (`/dashboard/<slug>/<id>`) for a slug. */
export function detailPageBySlug(slug) {
  return PAGES.find((page) => page.detail && page.slug === slug) || null;
}

function withQuery(path, params) {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params || {})) {
    if (value !== undefined && value !== null && value !== '') search.append(key, value);
  }
  const qs = search.toString();
  return qs ? `${path}?${qs}` : path;
}

/**
 * URL for a page id. With a record id, the detail route (`/dashboard/<slug>/<id>`); otherwise the
 * hub URL with `?tab=` (omitted for the hub's default tab). `query` adds extra parameters.
 */
export function pathFor(id, recordId, query) {
  const page = findPage(id);
  if (!page) return withQuery(`${DASHBOARD_BASE}/${id}`, query);
  if (recordId) return withQuery(`${DASHBOARD_BASE}/${page.slug}/${encodeURIComponent(recordId)}`, query);
  const hub = findHub(page.hub);
  const params = {};
  if (page.tab && !page.defaultTab) params[TAB_PARAM] = page.tab;
  return withQuery(`${DASHBOARD_BASE}/${hub.slug}`, { ...params, ...(query || {}) });
}

/** Pre-hub routes that redirect to a hub and tab. A tab whose slug is its hub's slug needs none. */
export const LEGACY_REDIRECTS = [
  ...PAGES.filter((page) => page.tab && page.slug !== findHub(page.hub).slug).map((page) => ({
    from: page.slug,
    to: `${DASHBOARD_BASE}/${findHub(page.hub).slug}`,
    tab: page.tab,
    ...(page.legacyTabs ? { tabs: page.legacyTabs } : {})
  })),
  { from: 'rbac', to: `${DASHBOARD_BASE}/access`, tab: 'roles', tabs: ['roles', 'assignments'] }
];

/** True when a hub owns the current pathname (its own URL, or a detail route of one of its tabs). */
export function hubIsActive(hub, pathname) {
  const slugs = [hub.slug, ...(hub.tabs || []).filter((tab) => tab.detail).map((tab) => tab.slug)];
  return slugs.some((slug) => {
    const base = `${DASHBOARD_BASE}/${slug}`;
    return pathname === base || pathname.startsWith(`${base}/`);
  });
}

/** The hub that owns a pathname, or null. */
export function hubForPath(pathname) {
  return HUBS.find((hub) => hubIsActive(hub, pathname)) || null;
}
