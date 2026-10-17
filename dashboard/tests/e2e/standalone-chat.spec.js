import { test, expect } from '@playwright/test';
import { installMocks } from './mocks.js';
async function chatMocks(page, { publicChat = true } = {}) {
  await installMocks(page);
  let conversations = 0;
  await page.route('**/v1.0/chat/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    let body;
    if (path === '/v1.0/chat/test-link')
      body = { name: 'Hugo', displayName: 'Agent Smith', accessMode: publicChat ? 'Public' : 'Authenticated', authenticationRequired: !publicChat };
    else if (path.endsWith('/conversations')) body = { id: `conversation-${++conversations}`, sessionToken: `session-${conversations}` };
    else if (path.endsWith('/messages')) {
      expect(request.headers()['x-hugo-chat-session']).toMatch(/^session-/);
      if (request.method() === 'GET')
        body = {
          messages: [{ role: 'assistant', content: 'Restored safe answer', thinkingContent: 'hidden reasoning', toolCalls: [{ name: 'hidden tool' }] }]
        };
      else
        body = { text: 'Safe response', tokensIn: 10, tokensOut: 3, status: 'Completed', thinking: 'hidden reasoning', tools: [{ output: 'hidden result' }] };
    } else body = { cancelled: true };
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
  });
  return { conversations: () => conversations };
}
test('public chat sends safely, resumes a scoped session and starts over', async ({ page }) => {
  const calls = await chatMocks(page);
  await page.goto('/chat/test-link');
  await expect(page.getByRole('heading', { name: 'Agent Smith' })).toBeVisible();
  await expect(page.locator('.sidebar')).toHaveCount(0);
  await page.getByRole('textbox').fill('Hello');
  await page.getByTestId('chat-send').click();
  await expect(page.getByTestId('chat-message-assistant')).toContainText('Safe response');
  await expect(page.getByTestId('chat-usage')).toBeVisible();
  await expect(page.locator('body')).not.toContainText('hidden');
  expect(calls.conversations()).toBe(1);
  await page.reload();
  await expect(page.getByTestId('chat-message-assistant')).toContainText('Restored safe answer');
  await page.getByRole('button', { name: 'New chat', exact: true }).click();
  await expect(page.getByTestId('chat-message-assistant')).toHaveCount(0);
  await page.getByRole('textbox').fill('New session');
  await page.getByTestId('chat-send').click();
  await expect(page.getByTestId('chat-message-assistant')).toContainText('Safe response');
  expect(calls.conversations()).toBe(2);
});
test('authenticated chat returns from login to the exact local chat route', async ({ page }) => {
  await chatMocks(page, { publicChat: false });
  await page.goto('/chat/test-link');
  await page.getByRole('link', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(/returnTo=/);
  await page.locator('#email').fill('admin@hugo.local');
  await page.locator('#password').fill('password');
  await page.getByTestId('login-submit').click();
  await expect(page).toHaveURL(/\/chat\/test-link$/);
  await expect(page.getByRole('textbox')).toBeVisible();
  await expect(page.locator('.sidebar')).toHaveCount(0);
});
for (const width of [390, 768, 1280])
  for (const theme of ['light', 'dark']) {
    test(`standalone chat renders ${width}px ${theme}`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width, height: 860 });
      await chatMocks(page);
      await page.addInitScript((value) => localStorage.setItem('hugo_theme', value), theme);
      await page.goto('/chat/test-link');
      await page.getByRole('textbox').fill('Please summarize today’s work.');
      await page.getByTestId('chat-send').click();
      await expect(page.getByTestId('chat-message-assistant')).toContainText('Safe response');
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await page.screenshot({ path: testInfo.outputPath(`standalone-${width}-${theme}.png`), fullPage: true });
    });
  }

test('profile link controls save, copy, rotate and disable without creating credentials', async ({ page }, testInfo) => {
  await installMocks(page);
  await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);
  let profile = {
    id: 'prf_0001',
    name: 'Hugo',
    displayName: 'Hugo',
    personaName: 'Agent Smith',
    userAddress: 'Mr. Anderson',
    visibility: 'Tenant',
    active: true,
    exposeMcp: true,
    chatEnabled: false,
    chatAccessMode: 'Authenticated',
    chatLinkId: null
  };
  const updates = [];
  await page.route('**/v1.0/api/agent-profiles/prf_0001', async (route) => {
    if (route.request().method() === 'PUT') {
      const body = route.request().postDataJSON();
      updates.push(body);
      profile = { ...profile, ...body, chatLinkId: body.rotateChatLink ? 'rotated-link' : profile.chatLinkId || 'original-link' };
    }
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify(profile) });
  });
  await page.route('**/v1.0/api/tenants/default/credentials?**', (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        objects: [
          { id: 'owned', userId: 'usr_admin', name: 'Chat visitor', active: true, authMode: 'Hybrid' },
          { id: 'other', userId: 'another-user', name: 'Other owner', active: true },
          { id: 'inactive', userId: 'usr_admin', name: 'Inactive key', active: false },
          { id: 'session', userId: 'usr_admin', name: 'Session exchange key', active: true, authMode: 'SessionExchange' }
        ],
        totalRecords: 4
      })
    })
  );
  await page.goto('/');
  await page.locator('#email').fill('admin@hugo.local');
  await page.locator('#password').fill('password');
  await page.getByTestId('login-submit').click();
  await expect(page.locator('.dashboard')).toBeVisible();
  await page.goto('/dashboard/profiles/prf_0001?tab=connect');
  const panel = page.getByTestId('profile-chat-link');
  await panel.getByLabel('Enable chat link', { exact: true }).check();
  const saveButton = panel.getByRole('button', { name: 'Save', exact: true });
  const authenticatedSize = await saveButton.boundingBox();
  expect(authenticatedSize.height).toBeLessThan(40);
  await panel.getByLabel('Access', { exact: true }).selectOption('Public');
  const publicSize = await saveButton.boundingBox();
  expect(publicSize.height).toBe(authenticatedSize.height);
  expect(publicSize.width).toBe(authenticatedSize.width);
  await panel.getByLabel('Access', { exact: true }).selectOption('Authenticated');
  expect((await saveButton.boundingBox()).height).toBe(authenticatedSize.height);
  await panel.getByLabel('Access', { exact: true }).selectOption('Public');
  await expect(panel.getByRole('option', { name: 'Other owner' })).toHaveCount(0);
  await expect(panel.getByRole('option', { name: 'Inactive key' })).toHaveCount(0);
  await expect(panel.getByRole('option', { name: 'Session exchange key' })).toHaveCount(0);
  await expect(panel.getByRole('button', { name: 'Save', exact: true })).toBeDisabled();
  await panel.getByLabel('Public chat credential', { exact: true }).selectOption('owned');
  await panel.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(panel.getByRole('link', { name: 'Open chat' })).toHaveAttribute('href', /\/chat\/original-link$/);
  expect(updates[0]).toMatchObject({ chatEnabled: true, chatAccessMode: 'Public', chatCredentialId: 'owned', rotateChatLink: false });
  await page.screenshot({ path: testInfo.outputPath('profile-chat-link-management.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath('profile-chat-link-management-mobile.png'), fullPage: true, animations: 'disabled' });
  await panel.getByRole('button', { name: /Copy/ }).click();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toMatch(/\/chat\/original-link$/);
  await panel.getByRole('button', { name: 'Rotate link' }).click();
  await expect(panel.getByRole('link', { name: 'Open chat' })).toHaveAttribute('href', /\/chat\/rotated-link$/);
  await panel.getByLabel('Enable chat link', { exact: true }).uncheck();
  await panel.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(panel.getByRole('link', { name: 'Open chat' })).toHaveCount(0);
});

test('disabled links show a safe unavailable page', async ({ page }) => {
  await installMocks(page);
  await page.route('**/v1.0/chat/**', (route) => route.fulfill({ status: 404, contentType: 'application/json', body: '{"message":"Internal identifier"}' }));
  await page.goto('/chat/revoked-link');
  await expect(page.getByRole('alert')).toContainText('unavailable');
  await expect(page.locator('body')).not.toContainText('Internal identifier');
  await expect(page.getByRole('textbox')).toHaveCount(0);
});

for (const status of [403, 500]) {
  test(`chat metadata failure ${status} is not presented as a disabled link`, async ({ page }) => {
    await installMocks(page);
    await page.route('**/v1.0/chat/**', (route) =>
      route.fulfill({ status, contentType: 'application/json', body: JSON.stringify({ code: 'permission_denied', message: 'Internal diagnostic' }) })
    );
    await page.goto('/chat/test-link');
    await expect(page.getByRole('alert')).toContainText(status === 403 ? 'permissions' : 'Unable to load');
    await expect(page.getByRole('alert')).not.toContainText('disabled');
    await expect(page.locator('body')).not.toContainText('Internal diagnostic');
    await expect(page.getByRole('textbox')).toHaveCount(0);
  });
}

test('standalone theme uses accessible sun and moon icons', async ({ page }) => {
  await chatMocks(page);
  await page.goto('/chat/test-link');
  const dark = page.getByRole('button', { name: 'Switch to dark theme', exact: true });
  await expect(dark.locator('svg')).toBeVisible();
  await expect(dark).toHaveText('');
  await dark.click();
  const light = page.getByRole('button', { name: 'Switch to light theme', exact: true });
  await expect(light.locator('svg')).toBeVisible();
  await expect(page.locator('body')).toHaveAttribute('data-theme', 'dark');
  await light.click();
  await expect(dark).toBeVisible();
});
