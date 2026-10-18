import { test, expect } from '@playwright/test';
import { installMocks, login } from './mocks.js';
import { pathFor } from '../../src/config/navConfig.js';

const feedback = {
  id: 'msg_feedback',
  agentProfileId: 'prf_0001',
  agentName: 'Hugo',
  feedbackRating: 'Negative',
  feedbackComment: 'Please include citations',
  feedbackUpdatedUtc: '2026-10-17T12:00:00Z',
  requestText: 'Find **Alice**',
  responseText: '## User found\n\nAlice is active.\n\n<script>alert(1)</script>',
  conversationId: 'conv_feedback',
  turnId: 'turn_feedback',
  inputTokens: 123,
  outputTokens: 17,
  timeToFirstTokenMs: 42,
  durationMs: 1200,
  modelName: 'test-model',
  modelEndpointId: 'mdl_0001',
  toolCallCount: 2,
  finishReason: 'Completed',
  source: 'Rest'
};

for (const width of [390, 1280]) {
  test(`reviews agent feedback with request, response and telemetry at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await installMocks(page);
    const selected = [];
    await page.route('**/v1.0/api/feedback**', async (route) => {
      const url = new URL(route.request().url());
      if (url.pathname.endsWith('/msg_feedback')) return route.fulfill({ json: feedback });
      selected.push(url.searchParams.get('agentProfileId'));
      return route.fulfill({
        json: {
          objects: url.searchParams.get('agentProfileId') === 'prf_0001' ? [feedback] : [],
          totalRecords: url.searchParams.get('agentProfileId') === 'prf_0001' ? 1 : 0,
          maxResults: 25,
          skip: 0
        }
      });
    });
    await login(page, { expect });
    await page.goto(pathFor('feedback'));
    await expect(page.getByRole('tab', { name: 'Feedback', exact: true })).toBeVisible();
    await page.getByLabel('Agent', { exact: true }).selectOption('prf_0001');
    await expect(page.getByText('Please include citations')).toBeVisible();
    expect(selected).toContain('prf_0001');
    await page.getByRole('button', { name: 'View details', exact: true }).click();
    const detail = page.getByTestId('feedback-details');
    await expect(detail.getByRole('heading', { name: 'User found' })).toBeVisible();
    await expect(detail.locator('strong')).toHaveText('Alice');
    await expect(detail).toContainText('test-model');
    await expect(detail).toContainText('conv_feedback');
    await expect(detail.locator('script')).toHaveCount(0);
    const bounds = await detail.boundingBox();
    if (width === 1280) expect(bounds.width).toBeGreaterThan(1100);
    else expect(bounds.width).toBeLessThanOrEqual(width);
    for (const id of ['msg_feedback', 'prf_0001', 'conv_feedback', 'turn_feedback', 'mdl_0001']) {
      const identifier = detail.locator('.copyable-id').filter({ has: page.locator('code', { hasText: id }) });
      await expect(identifier.getByRole('button', { name: 'Copy', exact: true })).toBeVisible();
      expect(await identifier.locator('code').evaluate((code) => getComputedStyle(code).fontFamily)).toContain('monospace');
    }
    // Exercise both non-secure HTTP copying and denied secure clipboard fallback inside the modal.
    await page.evaluate((secure) => {
      Object.defineProperty(window, 'isSecureContext', { configurable: true, value: secure });
      Object.defineProperty(navigator, 'clipboard', {
        configurable: true,
        value: {
          writeText: async () => {
            throw new Error('Denied');
          }
        }
      });
      document.execCommand = (command) => {
        const selected = document.activeElement;
        window.__copiedIdentifier = command === 'copy' && selected.closest('[role="dialog"]') ? selected.value : null;
        return !!window.__copiedIdentifier;
      };
    }, width === 1280);
    const copy = detail
      .locator('.copyable-id')
      .filter({ has: page.locator('code', { hasText: 'conv_feedback' }) })
      .getByRole('button');
    await copy.click();
    await expect(copy).toHaveAccessibleName('Copied');
    expect(await page.evaluate(() => window.__copiedIdentifier)).toBe('conv_feedback');
    await expect(copy).toBeFocused();
    await expect(detail.locator('textarea')).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath(`feedback-details-${width}.png`), fullPage: true });
    await detail.getByRole('button', { name: 'Close', exact: true }).click();
    await page.getByLabel('Agent', { exact: true }).selectOption('prf_0002');
    await expect(page.getByText('No feedback has been submitted for this selection.')).toBeVisible();
    await expect(page.getByText('Please include citations')).toHaveCount(0);
  });
}
