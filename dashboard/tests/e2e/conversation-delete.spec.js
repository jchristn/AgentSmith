import { test, expect } from '@playwright/test';
import { installMocks, login } from './mocks.js';
import { pathFor } from '../../src/config/navConfig.js';

async function setup(page, { failId, gate } = {}) {
  const requests = [];
  const deleted = [];
  let items = [
    { id: 'cnv_0001', title: 'First chat', agentProfileId: 'prf_0001', source: 'Dashboard', createdUtc: '2026-10-18T12:00:00Z' },
    { id: 'cnv_0002', title: 'Second chat', agentProfileId: 'prf_0001', source: 'Dashboard', createdUtc: '2026-10-18T13:00:00Z' }
  ];
  page.on('dialog', (dialog) => {
    throw new Error(`Unexpected native dialog: ${dialog.type()}`);
  });
  await installMocks(page, { onRequest: (request) => requests.push(request) });
  await page.route(/\/v1\.0\/api\/conversations(?:\/cnv_\d+)?(?:\?.*)?$/, async (route) => {
    const url = new URL(route.request().url());
    if (route.request().method() === 'DELETE') {
      const id = url.pathname.split('/').pop();
      deleted.push(id);
      if (gate) await gate;
      if (id === failId)
        return route.fulfill({
          status: 409,
          contentType: 'application/json',
          body: JSON.stringify({ code: 'TurnInProgress', message: 'Stop this active conversation before deleting it.' })
        });
      items = items.filter((item) => item.id !== id);
      return route.fulfill({ status: 204 });
    }
    if (url.pathname === '/v1.0/api/conversations' && route.request().method() === 'GET') {
      const search = (url.searchParams.get('search') || '').toLowerCase();
      const visible = items.filter((item) => item.title.toLowerCase().includes(search));
      return route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ objects: visible, totalRecords: visible.length, maxResults: 100, skip: 0 })
      });
    }
    return route.fallback();
  });
  await login(page, { expect });
  return { deleted, requests };
}

async function selectShown(page) {
  await page.getByRole('checkbox', { name: 'Select shown conversations', exact: true }).check();
  await page.getByTestId('chat-delete-selected').click();
  return page.getByRole('dialog');
}

test('chat sidebar bulk deletion reviews titles and copyable IDs, cancels safely, and deletes only after modal confirmation', async ({ page }) => {
  const { deleted } = await setup(page);
  await page.goto(pathFor('chat'));
  const dialog = await selectShown(page);
  await expect(dialog).toContainText('First chat');
  await expect(dialog).toContainText('Second chat');
  await expect(dialog).toContainText('all their messages');
  await expect(dialog.locator('.copyable-id')).toHaveCount(2);
  await expect(dialog.getByRole('button', { name: 'Copy', exact: true })).toHaveCount(2);
  await dialog.screenshot({ path: '/tmp/hugo-chat-delete-desktop.png' });
  await page.setViewportSize({ width: 390, height: 844 });
  const box = await dialog.boundingBox();
  expect(box.x).toBeGreaterThanOrEqual(0);
  expect(box.x + box.width).toBeLessThanOrEqual(390);
  await dialog.screenshot({ path: '/tmp/hugo-chat-delete-mobile.png' });
  await page.setViewportSize({ width: 1280, height: 900 });
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  expect(deleted).toEqual([]);
  await page.getByTestId('chat-delete-selected').click();
  await dialog.getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.locator('.chat-list-items li')).toHaveCount(0);
  expect(deleted).toEqual(['cnv_0001', 'cnv_0002']);
});

test('a running conversation stays selected after a partial failure and is not automatically cancelled', async ({ page }) => {
  const { deleted, requests } = await setup(page, { failId: 'cnv_0002' });
  await page.goto(pathFor('chat'));
  const dialog = await selectShown(page);
  await dialog.getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.locator('.chat-list-items li')).toHaveCount(1);
  await expect(page.getByRole('checkbox', { name: 'Select row: Second chat', exact: true })).toBeChecked();
  await expect(page.locator('.chat-list')).toContainText('Stop this active conversation before deleting it.');
  expect(deleted).toEqual(['cnv_0001', 'cnv_0002']);
  expect(requests.some((request) => request.path.endsWith('/cancel'))).toBe(false);
});

test('deleting the open conversation returns to new chat and confirmation cannot be dismissed while the request is running', async ({ page }) => {
  let release;
  const gate = new Promise((resolve) => {
    release = resolve;
  });
  const { deleted } = await setup(page, { gate });
  await page.goto(pathFor('chat', 'cnv_0001'));
  await page.getByRole('checkbox', { name: 'Select row: First chat', exact: true }).check();
  await page.getByTestId('chat-delete-selected').click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(dialog.getByRole('button', { name: 'Delete', exact: true })).toBeDisabled();
  await page.keyboard.press('Escape');
  await expect(dialog).toBeVisible();
  release();
  await expect(page).toHaveURL(new RegExp(`${pathFor('chat')}$`));
  await expect(page.getByRole('checkbox', { name: 'Select row: First chat', exact: true })).toHaveCount(0);
  expect(deleted).toEqual(['cnv_0001']);
});

test('search clears hidden selections before a batch is confirmed', async ({ page }) => {
  const { deleted } = await setup(page);
  await page.goto(pathFor('chat'));
  await page.getByRole('checkbox', { name: 'Select row: First chat', exact: true }).check();
  await page.locator('.chat-list input[type="search"]').fill('Second');
  await expect(page.locator('.chat-list-items li')).toHaveCount(1);
  await expect(page.getByTestId('chat-delete-selected')).toBeDisabled();
  const dialog = await selectShown(page);
  await expect(dialog).toContainText('Second chat');
  await expect(dialog).not.toContainText('First chat');
  await dialog.getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(deleted).toEqual(['cnv_0002']);
});

test('Agents conversation table uses the same conversation-specific confirmation for multiple rows', async ({ page }) => {
  const { deleted } = await setup(page);
  await page.goto(pathFor('conversations'));
  await page.getByRole('checkbox', { name: 'Select all rows on this page', exact: true }).check();
  await page.getByRole('button', { name: 'Delete selected', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toContainText('all their messages');
  await expect(dialog).toContainText('First chat');
  await expect(dialog).toContainText('Second chat');
  await dialog.getByRole('button', { name: 'Delete', exact: true }).click();
  await expect.poll(() => deleted.length).toBe(2);
  expect(deleted).toEqual(['cnv_0001', 'cnv_0002']);
});
