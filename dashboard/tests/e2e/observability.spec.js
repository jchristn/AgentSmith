// Observability cards and Grafana deep links (P13.12, P13.14, P13.15, P13.19) against mocked APIs.

import { test, expect } from '@playwright/test';
import { installMocks, respond, OBSERVABILITY_CONFIGURED } from './mocks.js';

/** The mocked JSON body for a request, with `patch` merged in. */
function patched(route, patch) {
  const body = respond(route.request().method(), new URL(route.request().url()));
  return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ ...body, ...patch(body) }) });
}

async function login(page) {
  await page.goto('/');
  await page.locator('#tenantId').fill('default');
  await page.locator('#email').fill('admin@garrison');
  await page.locator('#password').fill('password');
  await page.getByTestId('login-submit').click();
  await expect(page.locator('.dashboard')).toBeVisible();
}

/** Serve server info with every observability service configured, and the given status answer. */
async function configured(page, status) {
  await page.route(/\/v1\.0\/api\/server-info$/, (route) => patched(route, () => ({ observability: OBSERVABILITY_CONFIGURED })));
  await page.route(/\/v1\.0\/api\/observability\/status$/, (route) =>
    status === 'error'
      ? route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ error: 'InternalError', code: 'InternalError', message: 'boom' }) })
      : route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(status) })
  );
}

const STATUS = [
  { service: 'grafana', configured: true, reachable: true, version: '11.2.0', checkedUtc: '2026-10-09T12:00:00Z' },
  { service: 'prometheus', configured: true, reachable: true, version: '2.54.1', checkedUtc: '2026-10-09T12:00:00Z' },
  { service: 'loki', configured: true, reachable: false, checkedUtc: '2026-10-09T12:00:00Z' },
  { service: 'tempo', configured: true, reachable: true, version: '2.6.0', checkedUtc: '2026-10-09T12:00:00Z' }
];

/** The single Explore pane encoded in a Grafana Explore URL. */
function explorePane(href) {
  const url = new URL(href);
  expect(url.origin).toBe('http://127.0.0.1:3000');
  expect(url.pathname).toBe('/explore');
  expect(url.searchParams.get('schemaVersion')).toBe('1');
  return JSON.parse(url.searchParams.get('panes')).gx;
}

test.use({ actionTimeout: 15000 });

test.beforeEach(async ({ page }) => {
  await installMocks(page);
});

test('P13.14 home shows one card per configured service with reachability, version, and an Open link', async ({ page }) => {
  await configured(page, STATUS);
  await login(page);
  const section = page.getByTestId('observability');
  await expect(section.locator('[data-service]')).toHaveCount(4);
  await expect(section.locator('[data-service]')).toHaveText([/Grafana/, /Prometheus/, /Loki/, /Tempo/]);
  await expect(page.getByTestId('observability-state-grafana')).toHaveText('Reachable');
  await expect(page.getByTestId('observability-state-loki')).toHaveText('Unreachable');
  await expect(page.getByTestId('observability-version-grafana')).toHaveText('Version 11.2.0');
  await expect(page.getByTestId('observability-version-loki')).toHaveCount(0);
  const open = page.getByTestId('observability-open-grafana');
  await expect(open).toHaveAttribute('href', 'http://127.0.0.1:3000/');
  await expect(open).toHaveAttribute('target', '_blank');
  await expect(open).toHaveAttribute('rel', 'noopener noreferrer');
  await expect(open).toHaveAttribute('data-tip', /.+/);
  await expect(page.getByTestId('observability-setup')).toHaveCount(0);
  await expect(section).not.toContainText('localhost');
  await expect(section).not.toContainText('admin');
  // Home KPIs link to the Overview dashboard.
  await expect(page.getByTestId('kpis-grafana')).toHaveAttribute('href', 'http://127.0.0.1:3000/d/garrison-overview?orgId=1');
});

test('P13.14 home hides unconfigured services and survives a failed status check', async ({ page }) => {
  await page.route(/\/v1\.0\/api\/server-info$/, (route) => patched(route, (body) => ({ observability: { ...body.observability, prometheusUrl: 'http://127.0.0.1:9090' } })));
  await page.route(/\/v1\.0\/api\/observability\/status$/, (route) => route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ error: 'InternalError', code: 'InternalError', message: 'boom' }) }));
  await login(page);
  const section = page.getByTestId('observability');
  await expect(section.locator('[data-service]')).toHaveCount(1);
  await expect(page.getByTestId('observability-state-prometheus')).toHaveText('Status unknown');
  await expect(page.getByTestId('kpi-agents')).toBeVisible();
  // No Grafana, so no Grafana links.
  await expect(page.getByTestId('kpis-grafana')).toHaveCount(0);
});

test('P13.14 home shows the single setup note when nothing is configured', async ({ page }) => {
  let statusCalls = 0;
  await page.route(/\/v1\.0\/api\/observability\/status$/, (route) => {
    statusCalls += 1;
    return route.fallback();
  });
  await login(page);
  await expect(page.getByTestId('observability-setup')).toContainText('Set up observability');
  await expect(page.getByTestId('observability-setup').locator('a')).toHaveAttribute('href', /docs\/TELEMETRY\.md$/);
  await expect(page.getByTestId('observability').locator('[data-service]')).toHaveCount(0);
  await expect(page.getByTestId('kpis-grafana')).toHaveCount(0);
  expect(statusCalls).toBe(0);
});

test('P13.15 run detail links its trace in Tempo and its logs in Loki', async ({ page }) => {
  await configured(page, STATUS);
  await login(page);
  await page.goto('/dashboard/runs/run_0001');
  await expect(page.getByTestId('run-trace-id')).toContainText('4bf92f3577b34da6a3ce929d0e0e0001');

  const trace = page.getByTestId('run-view-trace');
  await expect(trace).toHaveAttribute('target', '_blank');
  await expect(trace).toHaveAttribute('data-tip', /.+/);
  const tracePane = explorePane(await trace.getAttribute('href'));
  expect(tracePane.datasource).toBe('garrison-tempo');
  expect(tracePane.queries[0]).toEqual({ refId: 'A', datasource: { type: 'tempo', uid: 'garrison-tempo' }, queryType: 'traceql', query: '4bf92f3577b34da6a3ce929d0e0e0001' });

  const logs = page.getByTestId('run-view-logs');
  const logsPane = explorePane(await logs.getAttribute('href'));
  expect(logsPane.datasource).toBe('garrison-loki');
  expect(logsPane.queries[0]).toEqual({ refId: 'A', datasource: { type: 'loki', uid: 'garrison-loki' }, expr: '{app="garrison"} |= "run_0001"', queryType: 'range' });
  // The window covers the run: created minus 5 minutes to completed plus 5 minutes.
  const from = Number(logsPane.range.from);
  const to = Number(logsPane.range.to);
  expect(to - from).toBeGreaterThan(10 * 60 * 1000);
  expect(tracePane.range).toEqual(logsPane.range);
});

test('P13.15 run detail hides the trace link when the run has no trace ID', async ({ page }) => {
  await configured(page, STATUS);
  await page.route(/\/tenants\/[^/]+\/runs\/run_0001$/, (route) => patched(route, () => ({ traceId: null })));
  await login(page);
  await page.goto('/dashboard/runs/run_0001');
  await expect(page.getByTestId('run-view-logs')).toBeVisible();
  await expect(page.getByTestId('run-view-trace')).toHaveCount(0);
  await expect(page.getByTestId('run-trace-id')).toHaveCount(0);
});

test('P13.15 nodes open the Workers dashboard filtered to the node', async ({ page }) => {
  await configured(page, STATUS);
  await page.addInitScript(() => {
    window.__opened = [];
    window.open = (url, target, features) => {
      window.__opened.push({ url: String(url), target, features });
      return null;
    };
  });
  await login(page);
  await page.goto('/dashboard/nodes');
  const row = page.locator('.data-table tbody tr', { hasText: 'garrison-worker-0' });
  await row.locator('.action-menu-trigger').click();
  const item = page.getByRole('menuitem', { name: 'Open in Grafana' });
  await expect(item).toHaveAttribute('data-tip', /garrison-worker-0/);
  await item.click();
  const opened = await page.evaluate(() => window.__opened);
  expect(opened).toEqual([{ url: 'http://127.0.0.1:3000/d/garrison-workers?orgId=1&var-node=garrison-worker-0', target: '_blank', features: 'noopener,noreferrer' }]);
});

test('P13.15 request history and analytics link to their Grafana dashboards', async ({ page }) => {
  await configured(page, STATUS);
  await login(page);
  await page.goto('/dashboard/requests');
  await expect(page.getByTestId('requests-grafana')).toHaveAttribute('href', 'http://127.0.0.1:3000/d/garrison-http?orgId=1');
  await page.goto('/dashboard/analytics?range=week');
  const link = page.getByTestId('analytics-grafana');
  await expect(link).toHaveAttribute('href', /\/d\/garrison-runs\?orgId=1&from=\d+&to=\d+$/);
  const url = new URL(await link.getAttribute('href'));
  const span = Number(url.searchParams.get('to')) - Number(url.searchParams.get('from'));
  expect(Math.abs(span - 7 * 24 * 3600 * 1000)).toBeLessThan(60 * 60 * 1000);
});

test('P13.15 no Grafana links appear when Grafana is not configured', async ({ page }) => {
  await login(page);
  await page.goto('/dashboard/runs/run_0001');
  await expect(page.getByTestId('run-trace-id')).toBeVisible();
  await expect(page.getByTestId('run-view-trace')).toHaveCount(0);
  await expect(page.getByTestId('run-view-logs')).toHaveCount(0);
  await page.goto('/dashboard/requests');
  await expect(page.locator('.kpi-grid')).toBeVisible();
  await expect(page.getByTestId('requests-grafana')).toHaveCount(0);
  await page.goto('/dashboard/analytics');
  await expect(page.locator('.page-header-title')).toBeVisible();
  await expect(page.getByTestId('analytics-grafana')).toHaveCount(0);
  await page.goto('/dashboard/nodes');
  const row = page.locator('.data-table tbody tr', { hasText: 'garrison-worker-0' });
  await row.locator('.action-menu-trigger').click();
  await expect(page.getByRole('menuitem', { name: 'Cordon' })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: 'Open in Grafana' })).toHaveCount(0);
});

test('P13.27 home lists one Grafana dashboard link per section, each with a tooltip', async ({ page }) => {
  await configured(page, STATUS);
  await login(page);
  const list = page.getByTestId('grafana-dashboards');
  await expect(list).toBeVisible();
  const sections = [
    ['overview', 'garrison-overview', 'Overview'],
    ['http', 'garrison-http', 'HTTP API'],
    ['runs', 'garrison-runs', 'Runs'],
    ['agents', 'garrison-agents', 'Agents'],
    ['models', 'garrison-models', 'Models and tokens'],
    ['workers', 'garrison-workers', 'Workers'],
    ['kubernetes', 'garrison-kubernetes', 'Kubernetes executor'],
    ['protocols', 'garrison-protocols', 'Protocols'],
    ['scheduler', 'garrison-scheduler', 'Scheduler and queue'],
    ['database', 'garrison-database', 'Database'],
    ['controlPlane', 'garrison-control-plane', 'Control plane'],
    ['security', 'garrison-security', 'Security and auth'],
    ['integrations', 'garrison-integrations', 'Integrations'],
    ['logs', 'garrison-logs', 'Logs and traces']
  ];
  await expect(list.locator('a')).toHaveCount(sections.length);
  for (const [key, uid, label] of sections) {
    const link = page.getByTestId(`grafana-dashboard-${key}`);
    await expect(link).toHaveText(label);
    await expect(link).toHaveAttribute('href', `http://127.0.0.1:3000/d/${uid}?orgId=1`);
    await expect(link).toHaveAttribute('target', '_blank');
    await expect(link).toHaveAttribute('data-tip', /.+/);
  }
});

test('P13.27 home skips sections whose UID is blank', async ({ page }) => {
  await page.route(/\/v1\.0\/api\/server-info$/, (route) =>
    patched(route, () => ({ observability: { ...OBSERVABILITY_CONFIGURED, dashboards: { ...OBSERVABILITY_CONFIGURED.dashboards, kubernetes: '', logs: '' } } }))
  );
  await login(page);
  await expect(page.getByTestId('grafana-dashboards').locator('a')).toHaveCount(12);
  await expect(page.getByTestId('grafana-dashboard-kubernetes')).toHaveCount(0);
  await expect(page.getByTestId('grafana-dashboard-logs')).toHaveCount(0);
  await page.unroute(/\/v1\.0\/api\/server-info$/);
});

test('P13.27 no section dashboard links when Grafana is not configured or reports no dashboards', async ({ page }) => {
  await login(page);
  await expect(page.getByTestId('kpi-agents')).toBeVisible();
  await expect(page.getByTestId('grafana-dashboards')).toHaveCount(0);
  await page.goto('/dashboard/agents/agt_0001');
  await expect(page.locator('.page-header-title')).toBeVisible();
  await expect(page.getByTestId('agent-grafana')).toHaveCount(0);
});

test('P13.27 home hides the section list when server info has no dashboards', async ({ page }) => {
  await page.route(/\/v1\.0\/api\/server-info$/, (route) => patched(route, () => ({ observability: { ...OBSERVABILITY_CONFIGURED, dashboards: null } })));
  await login(page);
  await expect(page.getByTestId('kpis-grafana')).toBeVisible();
  await expect(page.getByTestId('grafana-dashboards')).toHaveCount(0);
});

test('P13.27 agent detail opens the Agents dashboard filtered to the agent and tenant', async ({ page }) => {
  await configured(page, STATUS);
  await login(page);
  await page.goto('/dashboard/agents/agt_0001');
  const link = page.getByTestId('agent-grafana');
  await expect(link).toHaveAttribute('href', 'http://127.0.0.1:3000/d/garrison-agents?orgId=1&var-tenant=default&var-agent=echo-agent');
  await expect(link).toHaveAttribute('data-tip', /.+/);
});

test('P13.13 settings shows the Observability group with translated labels', async ({ page }) => {
  await login(page);
  await page.goto('/dashboard/settings');
  await expect(page.locator('legend', { hasText: 'Observability' })).toBeVisible();
  await expect(page.locator('label[for="setting-Observability.GrafanaUrl"]')).toContainText('Grafana URL');
  await expect(page.locator('label[for="setting-Observability.GrafanaInternalUrl"]')).toContainText('Grafana internal URL');
  await expect(page.locator('[id="setting-Observability.GrafanaUrl"]')).toBeEditable();
});
