// Accessibility and responsive pass (P8.52): landmarks, names for every control, labelled form fields, keyboard
// access to the main workflows, focus trapping in modals, reduced motion, and tap target sizes on phones.

import { test, expect } from '@playwright/test';
import { installMocks, login } from './mocks.js';
import { pathFor } from '../../src/config/navConfig.js';

const PAGES = [
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
  'sessions',
  'settings',
  'plugins'
];

/** Controls without an accessible name, and form fields without a label, on the current page. */
async function unnamed(page) {
  return page.evaluate(() => {
    const visible = (el) => el.offsetParent !== null || el.getClientRects().length > 0;
    const name = (el) => (el.getAttribute('aria-label') || el.getAttribute('aria-labelledby') || el.getAttribute('title') || el.textContent || '').trim();
    const labelled = (el) => {
      if (el.getAttribute('aria-label') || el.getAttribute('aria-labelledby')) return true;
      if (el.id && document.querySelector(`label[for="${CSS.escape(el.id)}"]`)) return true;
      return !!el.closest('label');
    };
    const out = [];
    for (const el of document.querySelectorAll('button, a[href], [role="tab"], [role="menuitem"]'))
      if (visible(el) && !name(el)) out.push(`${el.tagName} ${el.className}`);
    for (const el of document.querySelectorAll('input:not([type="hidden"]), select, textarea'))
      if (visible(el) && !labelled(el)) out.push(`${el.tagName} ${el.id || el.name || el.className}`);
    for (const el of document.querySelectorAll('img')) if (!el.hasAttribute('alt')) out.push(`IMG ${el.src}`);
    return out;
  });
}

test.beforeEach(async ({ page }) => {
  await installMocks(page);
});

test('every page has landmarks and names every control', async ({ page }) => {
  await login(page, { expect });
  await expect(page.locator('nav[aria-label]')).toHaveCount(1);
  await expect(page.locator('header.dashboard-header')).toHaveCount(1);
  await expect(page.locator('main#main-content')).toHaveCount(1);
  for (const id of PAGES) {
    await page.goto(pathFor(id));
    await expect(page.locator('.page-header-title').first(), id).toBeVisible();
    await expect(page.locator('h1'), id).not.toHaveCount(0);
    expect(await unnamed(page), id).toEqual([]);
  }
  await page.goto('/dashboard/profiles/prf_0001');
  expect(await unnamed(page)).toEqual([]);
});

test('the login page names every control', async ({ page }) => {
  await page.goto('/');
  expect(await unnamed(page)).toEqual([]);
});

test('modals trap focus, close on Escape, and return focus to the opener', async ({ page }) => {
  await login(page, { expect });
  await page.goto(pathFor('resources'));
  const opener = page.getByTestId('add-button');
  await opener.focus();
  await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await expect.poll(() => page.evaluate(() => !!document.activeElement?.closest('[role="dialog"]'))).toBe(true);
  for (let i = 0; i < 40; i += 1) {
    await page.keyboard.press('Tab');
    expect(await page.evaluate(() => !!document.activeElement?.closest('[role="dialog"]'))).toBe(true);
  }
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  await expect(opener).toBeFocused();
});

test('the skip link and keyboard-only navigation reach the main workflow', async ({ page }) => {
  await login(page, { expect });
  await page.keyboard.press('Tab');
  await expect(page.locator('.skip-link')).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/#main-content$/);
  await page.locator('.sidebar-item[data-nav="chat"]').focus();
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/dashboard\/chat$/);
  await page.getByTestId('chat-input').focus();
  await page.keyboard.type('hello');
  await page.keyboard.press('Enter');
  // The new conversation gets its own URL once the streamed turn ends.
  await expect(page).toHaveURL(/\/dashboard\/chat\/cnv_0042$/);
});

test('honors reduced motion', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await login(page, { expect });
  const duration = await page.evaluate(() => {
    const el = document.createElement('span');
    el.className = 'pulse-dot';
    document.body.appendChild(el);
    return getComputedStyle(el).animationDuration;
  });
  expect(parseFloat(duration)).toBeLessThan(0.01);
});

test('phone layouts keep tap targets usable and the navigation reachable', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(page, { expect });
  for (const id of ['home', 'resources', 'chat', 'approvals']) {
    await page.goto(pathFor(id));
    await expect(page.locator('.page-header-title').first()).toBeVisible();
    const small = await page.evaluate(() =>
      [...document.querySelectorAll('main button, main a[href], header button, header select')]
        // Inline text links are exempt (WCAG 2.5.8); everything else needs a 24 by 24 pixel target.
        .filter((el) => el.offsetParent !== null && !(el.tagName === 'A' && getComputedStyle(el).display === 'inline'))
        .map((el) => ({ el, r: el.getBoundingClientRect() }))
        .filter(({ r }) => r.width > 0 && (r.width < 24 || r.height < 24))
        .map(({ el, r }) => `${el.tagName}.${el.className} ${Math.round(r.width)}x${Math.round(r.height)}`)
    );
    expect(small, id).toEqual([]);
  }
  await page.getByRole('button', { name: 'Toggle navigation' }).click();
  await page.locator('.sidebar-item[data-nav="connections"]').click();
  await expect(page).toHaveURL(/\/dashboard\/connections$/);
});
