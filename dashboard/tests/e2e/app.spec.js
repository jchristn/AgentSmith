// Shell, navigation, and every route against mocked Hugo APIs (no backend required).

import { test, expect } from '@playwright/test';
import { installMocks, login, CAPABILITY_PREFIXES } from './mocks.js';
import { PAGES, HUBS, NAV_SECTIONS, pathFor } from '../../src/config/navConfig.js';

const VIEWPORTS = [
  { name: 'desktop', width: 1280, height: 860 },
  { name: 'tablet', width: 768, height: 1024 },
  { name: 'mobile', width: 390, height: 844 }
];

test.beforeEach(async ({ page }) => {
  // Every confirmation must be a custom modal, never a native dialog.
  page.on('dialog', (dialog) => {
    throw new Error(`Native dialog used: ${dialog.message()}`);
  });
});

test('logs in with tenant discovery and default credentials, lands on Home, and logs out', async ({ page }) => {
  await installMocks(page);
  await page.goto('/');
  await expect(page).toHaveTitle('Hugo');
  await expect(page.locator('#serverUrl')).toHaveValue('http://127.0.0.1:4173');
  await page.locator('#email').fill('admin@hugo.local');
  await expect(page.getByTestId('tenant-found')).toContainText('Default');
  await expect(page.locator('#tenantId')).toHaveCount(0);
  await page.locator('#password').fill('password');
  await page.getByTestId('login-submit').click();
  await expect(page.locator('.page-header-title')).toHaveText('Home');
  await expect(page).toHaveTitle('Home | Hugo');
  await expect(page.locator('.sidebar-brand')).toContainText('Hugo');
  await expect(page.locator('.kpi-card')).toHaveCount(6);
  await expect(page.getByTestId('kpi-unhealthy').locator('.kpi-value')).toHaveText('1');
  await expect(page.getByTestId('kpi-approvals').locator('.kpi-value')).toHaveText('2');
  await expect(page.getByTestId('observability')).toBeVisible();
  await page.getByTestId('logout').click();
  await expect(page.getByTestId('login-submit')).toBeVisible();
});

test('shows the default credentials hint for a local server and fills them in', async ({ page }) => {
  await installMocks(page);
  await page.goto('/');
  await page.locator('#serverUrl').fill('http://127.0.0.1:8800');
  await expect(page.getByTestId('default-credentials')).toContainText('admin@hugo.local');
  await page.getByTestId('default-credentials').getByRole('button').click();
  await expect(page.locator('#email')).toHaveValue('admin@hugo.local');
  await expect(page.locator('#password')).toHaveValue('password');
});

test('reports bad credentials without leaving the login page', async ({ page }) => {
  await installMocks(page);
  await page.route('**/v1.0/token', (route) => (route.request().method() === 'POST' ? route.fulfill({ status: 401, contentType: 'application/json', body: JSON.stringify({ error: 'InvalidCredentials', message: 'bad' }) }) : route.fallback()));
  await page.goto('/');
  await page.locator('#email').fill('admin@hugo.local');
  await page.locator('#password').fill('wrong');
  await page.getByTestId('login-submit').click();
  await expect(page.locator('.error-banner')).toBeVisible();
  await expect(page.getByTestId('login-submit')).toBeVisible();
});

test('forces a password change for a seeded account', async ({ page }) => {
  await installMocks(page);
  await page.route('**/v1.0/token/details', (route) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ principalType: 'User', principalName: 'anderson@hugo.local', tenantId: 'default', userId: 'usr_0002', isTenantAdmin: true, mustChangePassword: true }) }));
  await login(page, { expect });
  const dialog = page.getByTestId('password-change');
  await expect(dialog).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(dialog).toBeVisible();
  await dialog.locator('#pwd-currentPassword').fill('password');
  await dialog.locator('#pwd-newPassword').fill('a-long-new-passphrase');
  await dialog.locator('#pwd-confirmPassword').fill('a-long-new-passphrase');
  await dialog.locator('.button-primary').click();
  await expect(dialog).toHaveCount(0);
});

test('groups the sidebar as plan section 18.2 and highlights every route', async ({ page }) => {
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await installMocks(page);
  await login(page, { expect });
  await expect(page.locator('.sidebar-group-label')).toHaveText(['Start', 'Agents', 'Connect', 'Configure', 'Observe', 'Develop', 'Administer']);
  await expect(page.locator('.sidebar-item')).toHaveCount(HUBS.length);
  expect(NAV_SECTIONS.flatMap((s) => s.items)).toHaveLength(HUBS.length);
  for (const p of PAGES) {
    await page.goto(pathFor(p.id));
    await expect(page.locator('.page-header-title').first(), p.id).toBeVisible();
    await expect(page.locator(`.sidebar-item[data-nav="${p.hub}"]`), p.id).toHaveAttribute('aria-current', 'page');
    if (p.tab) await expect(page.locator(`.hub-tab-strip [data-tab-key="${p.tab}"]`), p.id).toHaveAttribute('aria-selected', 'true');
    await expect(page.locator('.loading-spinner'), p.id).toHaveCount(0);
    await expect(page.getByTestId('unavailable-state'), p.id).toHaveCount(0);
  }
  expect(errors).toEqual([]);
});

test('every Hugo page explains itself when the server does not expose its route yet', async ({ page }) => {
  await installMocks(page, { missing: Object.keys(CAPABILITY_PREFIXES) });
  await login(page, { expect });
  const pages = ['chat', 'profiles', 'approvals', 'conversations', 'resources', 'secrets', 'connectors', 'models', 'knowledge', 'toolInvocations', 'analytics', 'sessions', 'plugins'];
  for (const id of pages) {
    await page.goto(pathFor(id));
    const state = page.getByTestId('unavailable-state');
    await expect(state, id).toBeVisible();
    await expect(state, id).toContainText('/v1.0/');
    await expect(state, id).not.toContainText(/coming soon/i);
    await expect(page.getByTestId('add-button'), id).toHaveCount(0);
  }
  await page.goto(pathFor('mcpExplorer'));
  await page.getByTestId('mcp-connect').click();
  await expect(page.getByTestId('unavailable-state')).toContainText('/mcp');
  await page.goto(pathFor('a2aExplorer'));
  await page.getByTestId('a2a-card').click();
  await expect(page.getByTestId('unavailable-state')).toContainText('/v1.0/a2a/');
});

test('detail routes highlight their hub', async ({ page }) => {
  await installMocks(page);
  await login(page, { expect });
  for (const [path, hub] of [
    ['/dashboard/chat/cnv_0001', 'chat'],
    ['/dashboard/profiles/prf_0001', 'profiles'],
    ['/dashboard/connectors/ssh', 'connectors'],
    ['/dashboard/prompts/pt_0003', 'prompts'],
    ['/dashboard/assignments', 'rbac']
  ]) {
    await page.goto(path);
    await expect(page.locator(`.sidebar-item[data-nav="${hub}"]`), path).toHaveAttribute('aria-current', 'page');
  }
  await page.goto('/dashboard/nowhere');
  await expect(page).toHaveURL(/\/dashboard\/home$/);
});

test('topbar shows context, pending approvals, lockdown, theme, and GitHub', async ({ page }) => {
  await installMocks(page);
  await login(page, { expect });
  const header = page.locator('.dashboard-header');
  await expect(header).toContainText('http://127.0.0.1:4173');
  await expect(header).toContainText('admin@hugo.local');
  await expect(page.getByTestId('live-chip')).toContainText('Polling');
  await expect(page.getByTestId('topbar-approvals')).toContainText('2');
  await expect(page.getByTestId('approvals-badge')).toHaveText('2');
  await expect(header.locator('a[href="https://github.com/jchristn/AgentSmith"]')).toHaveCount(1);
  await page.getByTestId('topbar-approvals').click();
  await expect(page).toHaveURL(/\/dashboard\/approvals\?status=Pending$/);
  await page.getByTestId('lockdown-toggle').click();
  await page.locator('.modal .button-danger').click();
  await expect(page.getByTestId('lockdown-banner')).toBeVisible();
  const before = await page.locator('body').getAttribute('data-theme');
  await page.getByTestId('theme-toggle').click();
  await expect(page.locator('body')).not.toHaveAttribute('data-theme', before || 'light');
  await page.reload();
  await expect(page.locator('body')).not.toHaveAttribute('data-theme', before || 'light');
});

test('switches to live mode when the event stream is available', async ({ page }) => {
  await installMocks(page, { events: 'event: approval.created\ndata: {"id":"apr_0001"}\n\n' });
  await login(page, { expect });
  await expect(page.getByTestId('live-chip')).toBeVisible();
});

for (const vp of VIEWPORTS) {
  test(`has no horizontal page scroll on key pages at ${vp.name} width`, async ({ page }) => {
    await page.setViewportSize({ width: vp.width, height: vp.height });
    await installMocks(page);
    await login(page, { expect });
    for (const id of ['home', 'chat', 'resources', 'requests', 'explorer', 'settings', 'profiles']) {
      await page.goto(pathFor(id));
      await expect(page.locator('.page-header-title').first()).toBeVisible();
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      expect(overflow, `${id} at ${vp.name}`).toBeLessThanOrEqual(1);
    }
  });
}
