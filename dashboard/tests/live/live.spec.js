// Read-only checks against a live Garrison deployment (no mocks). Nothing is created or deleted.

import { test, expect } from '@playwright/test';
import { PAGES, LEGACY_REDIRECTS, pathFor } from '../../src/config/navConfig.js';

const TENANT = process.env.GARRISON_TENANT_ID || 'default';
const EMAIL = process.env.GARRISON_ADMIN_EMAIL || 'admin@garrison';
const PASSWORD = process.env.GARRISON_ADMIN_PASSWORD || 'password';

async function login(page) {
  await page.goto('/');
  await page.locator('#tenantId').fill(TENANT);
  await page.locator('#email').fill(EMAIL);
  await page.locator('#password').fill(PASSWORD);
  await page.getByTestId('login-submit').click();
  await expect(page.locator('.dashboard')).toBeVisible();
}

test('signs in and visits every hub and tab without page errors', async ({ page }) => {
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  page.on('dialog', (dialog) => {
    throw new Error(`Native dialog used: ${dialog.message()}`);
  });
  await login(page);
  for (const p of PAGES) {
    await page.goto(pathFor(p.id));
    await expect(page.locator('.page-header-title').first(), p.id).toBeVisible();
    await page.waitForLoadState('networkidle');
    await expect(page.locator('.loading-spinner'), p.id).toHaveCount(0);
    await expect(page.locator('.state-block', { hasText: 'permission' }), p.id).toHaveCount(0);
    await expect(page.locator('.error-banner'), p.id).toHaveCount(0);
    if (p.tab) await expect(page.locator(`.hub-tab-strip [data-tab-key="${p.tab}"]`), p.id).toHaveAttribute('aria-selected', 'true');
  }
  expect(errors).toEqual([]);
});

test('pre-hub routes redirect to their hub and tab', async ({ page }) => {
  await login(page);
  for (const r of LEGACY_REDIRECTS) {
    await page.goto(`/dashboard/${r.from}`);
    await expect(page, r.from).toHaveURL(new RegExp(`${r.to}\\?tab=${r.tab}$`));
  }
});

test('home KPIs show real numbers', async ({ page }) => {
  await login(page);
  const nodes = page.getByTestId('kpi-nodes').locator('.kpi-value');
  await expect(nodes).toHaveText(/^\d+ \/ \d+$/);
  await expect(page.locator('.kpi-value', { hasText: 'NaN' })).toHaveCount(0);
});

test('lists the registered worker nodes', async ({ page }) => {
  await login(page);
  await page.goto(pathFor('nodes'));
  await expect(page.locator('table tbody tr').first()).toBeVisible();
});

test('analytics loads charts, or explains that the server predates the analytics endpoint', async ({ page }) => {
  await login(page);
  await page.goto(pathFor('analytics'));
  await expect(page.locator('.page-header-title').first()).toHaveText('Analytics');
  await page.waitForLoadState('networkidle');
  await expect(page.locator('.loading-spinner')).toHaveCount(0);
  const unavailable = page.getByText('Analytics is not available on this server yet');
  if (await unavailable.count()) return;
  await expect(page.getByTestId('analytics-filters')).toBeVisible();
  const empty = page.getByTestId('analytics-empty');
  if (await empty.count()) return;
  await expect(page.getByTestId('analytics-totals')).toBeVisible();
  await expect(page.getByTestId('analytics-chart-ttft').locator('svg[role="img"]')).toBeVisible();
});

test('run detail shows the telemetry panel', async ({ page }) => {
  await login(page);
  await page.goto(pathFor('runs'));
  const first = page.locator('table tbody tr').first();
  if (!(await first.count())) return;
  await first.click();
  await expect(page.getByTestId('run-telemetry')).toBeVisible();
  await expect(page.locator('[data-telemetry="timeToFirstTokenMs"]')).toBeVisible();
  // A finished run has a stored transcript; the event list must load it.
  await expect(page.getByText(/^[1-9]\d* events?$/)).toBeVisible();
});
