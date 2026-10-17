// Visual QA (P8.51): screenshots of the shell and key pages at 1280, 768, and 390 pixels in light and dark themes,
// saved under test-results/visual for review and CI artifacts. Each capture also checks for clipped controls: no
// horizontal page scroll and every visible button fitting its own text.

import { test, expect } from '@playwright/test';
import { installMocks, login } from './mocks.js';
import { pathFor } from '../../src/config/navConfig.js';

const VIEWPORTS = [
  { name: 'desktop', width: 1280, height: 900 },
  { name: 'tablet', width: 768, height: 1024 },
  { name: 'mobile', width: 390, height: 844 }
];

const SHOTS = [
  { name: 'shell', path: pathFor('home') },
  { name: 'home', path: pathFor('home') },
  { name: 'chat', path: pathFor('chat', 'cnv_0001') },
  { name: 'profile', path: pathFor('profiles', 'prf_0001') },
  { name: 'resources', path: pathFor('resources') },
  { name: 'requests', path: pathFor('requests') },
  { name: 'request-details', path: pathFor('requests'), open: async (page) => page.locator('.data-table tbody tr').first().click(), wait: '.modal' },
  { name: 'explorer', path: pathFor('explorer') },
  { name: 'settings', path: pathFor('settings') }
];

for (const theme of ['light', 'dark']) {
  for (const vp of VIEWPORTS) {
    test(`captures key pages at ${vp.name} in ${theme} theme`, async ({ page }) => {
      await page.setViewportSize({ width: vp.width, height: vp.height });
      await installMocks(page);
      await page.addInitScript((t) => localStorage.setItem('hugo_theme', t), theme);
      await login(page, { expect });
      await expect(page.locator('body')).toHaveAttribute('data-theme', theme);
      for (const shot of SHOTS) {
        await page.goto(shot.path);
        await expect(page.locator('.page-header-title').first()).toBeVisible();
        await expect(page.locator('.loading-spinner')).toHaveCount(0);
        if (shot.open) {
          await shot.open(page);
          await expect(page.locator(shot.wait)).toBeVisible();
          await expect(page.locator('.loading-spinner')).toHaveCount(0);
        }
        await page.screenshot({ path: `test-results/visual/${theme}-${vp.name}-${shot.name}.png`, fullPage: !shot.open });
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
        expect(overflow, `${shot.name} ${vp.name} ${theme}`).toBeLessThanOrEqual(1);
        const clipped = await page.evaluate(() =>
          [...document.querySelectorAll('button')]
            .filter((b) => b.offsetParent !== null && b.textContent.trim() && b.scrollWidth > b.clientWidth + 2 && getComputedStyle(b).overflow !== 'visible')
            .map((b) => b.textContent.trim())
        );
        expect(clipped, `${shot.name} ${vp.name} ${theme}`).toEqual([]);
      }
    });
  }
}

test('captures the unavailable-capability state and the chat approval card', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await installMocks(page, { missing: ['resources'] });
  await login(page, { expect });
  await page.goto(pathFor('resources'));
  await expect(page.getByTestId('unavailable-state')).toBeVisible();
  await page.screenshot({ path: 'test-results/visual/light-desktop-unavailable.png' });
  await page.goto(pathFor('chat', 'cnv_0001'));
  await page.getByTestId('chat-input').fill('Restart nginx on web-01');
  await page.getByTestId('chat-input').press('Enter');
  await expect(page.getByTestId('approval-card').first()).toBeVisible();
  await page.screenshot({ path: 'test-results/visual/light-desktop-chat-approval.png' });
});

for (const theme of ['light', 'dark']) {
  for (const vp of VIEWPORTS.filter((viewport) => viewport.name !== 'tablet')) {
    test(`captures the separate model health modal at ${vp.name} in ${theme} theme`, async ({ page }) => {
      await page.setViewportSize({ width: vp.width, height: vp.height });
      await installMocks(page);
      await page.addInitScript((value) => localStorage.setItem('hugo_theme', value), theme);
      await login(page, { expect });
      await page.goto(pathFor('models'));
      await page.locator('.data-table tbody tr').first().locator('[aria-haspopup="menu"]').first().click();
      await page.getByRole('menuitem', { name: 'Health', exact: true }).click();
      const modal = page.getByTestId('model-health-modal');
      await expect(modal).toBeVisible();
      await expect(modal.locator('.model-health-chart')).toBeVisible();
      await expect(modal.locator('input, select, textarea')).toHaveCount(0);
      await page.screenshot({ path: `test-results/visual/${theme}-${vp.name}-model-health.png` });
      await modal.locator('summary').click();
      await expect(modal.getByTestId('model-health-record')).toHaveCount(3);
      await page.screenshot({ path: `test-results/visual/${theme}-${vp.name}-model-health-details.png` });
      const horizontalOverflow = await modal.evaluate((element) => ({
        page: document.documentElement.scrollWidth - document.documentElement.clientWidth,
        dialog: element.scrollWidth - element.clientWidth
      }));
      expect(horizontalOverflow.page).toBeLessThanOrEqual(1);
      expect(horizontalOverflow.dialog).toBeLessThanOrEqual(1);
    });
  }
}

const PICKER_CONNECTORS = [
  ['ssh', 'SSH host', 'Compute'],
  ['s3', 'Amazon S3', 'Storage'],
  ['azure-blob', 'Azure Blob Storage', 'Storage'],
  ['gcs', 'Google Cloud Storage', 'Storage'],
  ['sqlite', 'SQLite', 'Database'],
  ['mysql', 'MySQL', 'Database'],
  ['sqlserver', 'SQL Server', 'Database'],
  ['postgresql', 'PostgreSQL', 'Database'],
  ['solarwinds-observability', 'SolarWinds Observability', 'Logging'],
  ['redis', 'Redis', 'Database'],
  ['http', 'HTTP API', 'Network'],
  ['prometheus', 'Prometheus', 'Monitoring'],
  ['loki', 'Grafana Loki', 'Logging'],
  ['elasticsearch', 'Elasticsearch', 'Database'],
  ['aws', 'Amazon Web Services', 'Cloud'],
  ['kubernetes', 'Kubernetes', 'Compute'],
  ['docker', 'Docker', 'Compute'],
  ['mcp', 'MCP server', 'Integration'],
  ['a2a-agent', 'A2A agent', 'Integration']
].map(([type, displayName, category]) => ({ type, displayName, category, settings: [], secretSlots: [], tools: [] }));

for (const theme of ['light', 'dark']) {
  for (const vp of VIEWPORTS.filter((viewport) => viewport.name !== 'tablet')) {
    test(`captures all connector choices at ${vp.name} in ${theme} theme`, async ({ page }) => {
      await page.setViewportSize({ width: vp.width, height: vp.height });
      await installMocks(page);
      await page.route('**/v1.0/api/connectors?*', (route) => route.fulfill({ json: PICKER_CONNECTORS }));
      await page.addInitScript((value) => localStorage.setItem('hugo_theme', value), theme);
      await login(page, { expect });
      await page.goto(pathFor('resources'));
      await page.getByTestId('add-button').click();
      const picker = page.getByTestId('connector-picker');
      await expect(picker.getByRole('radio')).toHaveCount(19);
      await expect(picker.getByRole('radio', { name: 'SSH host', exact: true })).toBeInViewport();
      const order = await page.evaluate(() => {
        const picker = document.querySelector('[data-testid="connector-picker"]');
        const metadata = document.querySelector('#resources-name');
        return !!(picker.compareDocumentPosition(metadata) & Node.DOCUMENT_POSITION_FOLLOWING);
      });
      expect(order).toBe(true);
      await page.screenshot({ path: `test-results/visual/${theme}-${vp.name}-connector-picker.png` });
      const overflow = await picker.evaluate((element) => ({
        horizontal: element.scrollWidth - element.clientWidth,
        innerScroll: [...element.querySelectorAll('*')].some(
          (node) => ['auto', 'scroll'].includes(getComputedStyle(node).overflowY) && node.scrollHeight > node.clientHeight + 1
        )
      }));
      expect(overflow).toEqual({ horizontal: 0, innerScroll: false });
      const ssh = picker.getByRole('radio', { name: 'SSH host', exact: true });
      await ssh.focus();
      await page.keyboard.press('Space');
      await expect(ssh).toBeChecked();
      await expect(picker.getByRole('radio', { checked: true })).toHaveCount(1);
    });
  }
}
