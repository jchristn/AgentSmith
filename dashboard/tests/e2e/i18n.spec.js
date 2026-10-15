// Language selection before and after sign-in, persistence across reload and logout, lang and dir sync, RTL and
// expansion pseudo-locales without layout overflow, and a smoke check of every locale (I18N.md delivery gates).

import { test, expect } from '@playwright/test';
import { installMocks, login } from './mocks.js';
import { pathFor } from '../../src/config/navConfig.js';
import { LOCALES } from '../../src/i18n/localeRegistry.js';
import en from '../../src/i18n/locales/en.json' with { type: 'json' };

test.beforeEach(async ({ page }) => {
  await installMocks(page);
});

test('picks a language before sign-in and keeps it through login, reload, and logout', async ({ page }) => {
  await page.goto('/');
  await page.getByTestId('language-select').selectOption('de');
  await expect(page.locator('html')).toHaveAttribute('lang', 'de');
  await expect(page.locator('.login-brand h1')).not.toHaveText(en.login.title);
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('lang', 'de');
  await login(page, { expect });
  await expect(page.locator('.sidebar-item[data-nav="home"]')).toContainText('Startseite');
  await page.getByTestId('logout').click();
  await expect(page.locator('html')).toHaveAttribute('lang', 'de');
  expect(await page.evaluate(() => localStorage.getItem('hugo.locale'))).toBe('de');
});

test('switches language from the topbar without a reload, including RTL Arabic', async ({ page }) => {
  await login(page, { expect });
  const marker = await page.evaluate(() => (window.__noReload = true));
  expect(marker).toBe(true);
  await page.locator('.topbar').getByTestId('language-select').selectOption('ar');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.locator('html')).toHaveAttribute('lang', 'ar');
  expect(await page.evaluate(() => window.__noReload)).toBe(true);
  await expect(page.locator('.sidebar-group-label').first()).not.toHaveText('Start');
  const sidebar = await page.locator('.dashboard-sidebar').boundingBox();
  const main = await page.locator('.dashboard-content').boundingBox();
  expect(sidebar.x).toBeGreaterThan(main.x);
  await page.locator('.topbar').getByTestId('language-select').selectOption('en');
  await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
});

test('every locale translates the navigation and page chrome', async ({ page }) => {
  await login(page, { expect });
  for (const locale of LOCALES.filter((l) => !l.pseudo && l.code !== 'en')) {
    await page.goto(`${pathFor('resources')}?lang=${locale.code}`);
    await expect(page.locator('html'), locale.code).toHaveAttribute('lang', locale.code);
    await expect(page.locator('html'), locale.code).toHaveAttribute('dir', locale.dir);
    await expect(page.locator('.sidebar-item[data-nav="resources"]'), locale.code).not.toHaveText(en.nav.hubs.resources.label);
    await expect(page.locator('.page-header-title'), locale.code).not.toHaveText(en.resources.resources.title);
    await expect(page).not.toHaveTitle(/^Resources/);
  }
});

for (const code of ['en-XA', 'ar-XB']) {
  for (const width of [1280, 390]) {
    test(`pseudo-locale ${code} keeps pages usable at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await login(page, { expect });
      for (const id of ['home', 'resources', 'secrets', 'requests', 'explorer', 'settings', 'chat', 'approvals']) {
        await page.goto(`${pathFor(id)}?lang=${code}`);
        await expect(page.locator('.page-header-title').first(), id).toContainText('⟦');
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
        expect(overflow, `${id} ${code} ${width}`).toBeLessThanOrEqual(1);
      }
      await page.goto(`${pathFor('resources')}?lang=${code}`);
      await page.getByTestId('add-button').click();
      await expect(page.locator('.modal-footer .button-primary')).toBeInViewport();
    });
  }
}
