// Read-only checks against a live Hugo deployment (no mocks). Nothing is created or deleted. Run with
// `npm run test:live`; pages whose backend route is not implemented yet show the unavailable state, which is allowed.

import { test, expect } from '@playwright/test';
import { PAGES, pathFor } from '../../src/config/navConfig.js';

const EMAIL = process.env.HUGO_ADMIN_EMAIL || 'admin@hugo.local';
const PASSWORD = process.env.HUGO_ADMIN_PASSWORD || 'password';
const TENANT = process.env.HUGO_TENANT_ID || 'default';

async function login(page) {
  await page.goto('/');
  await page.locator('#email').fill(EMAIL);
  const tenant = page.locator('#tenantId');
  if (await tenant.count()) await tenant.fill(TENANT);
  await page.locator('#password').fill(PASSWORD);
  await page.getByTestId('login-submit').click();
  await expect(page.locator('.dashboard')).toBeVisible();
}

test('signs in and visits every page without page errors', async ({ page }) => {
  test.setTimeout(180000);
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  page.on('dialog', (dialog) => {
    throw new Error(`Native dialog used: ${dialog.message()}`);
  });
  await login(page);
  for (const p of PAGES) {
    await page.goto(pathFor(p.id));
    // A page the signed-in role may not use shows the forbidden state instead of its header.
    await expect(page.locator('.page-header-title, main .state-block').first(), p.id).toBeVisible();
    // The tenant event stream (Server-Sent Events) keeps a request open, so network idle is a short, bounded wait.
    await page.waitForLoadState('networkidle', { timeout: 3000 }).catch(() => {});
    await expect(page.locator('.loading-spinner'), p.id).toHaveCount(0);
  }
  expect(errors).toEqual([]);
});
