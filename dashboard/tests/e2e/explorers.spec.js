import { test, expect } from '@playwright/test';
import { installMocks, login } from './mocks.js';
import { pathFor } from '../../src/config/navConfig.js';

test('MCP instructions stay collapsed and structured tool results replace the escaped compatibility envelope', async ({ page }) => {
  await installMocks(page);
  await page.route(/\/mcp(?:\/[^/?]+)?$/, async (route) => {
    const body = route.request().postDataJSON();
    if (body.method !== 'tools/call') return route.fallback();
    const payload = { documentSetVersion: 0, items: [] };
    return route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({ jsonrpc: '2.0', id: body.id, result: { content: [{ type: 'text', text: JSON.stringify(payload) }], structuredContent: payload } })
    });
  });
  await login(page, { expect });
  await page.goto(pathFor('mcpExplorer'));
  await page.getByTestId('mcp-connect').click();
  const instructions = page.getByTestId('mcp-instructions');
  await expect(instructions).not.toHaveAttribute('open', '');
  await expect(instructions.locator('p').first()).toBeHidden();
  await instructions.locator('summary').click();
  await expect(instructions.locator('p').first()).toBeVisible();
  await page.locator('[data-tool="sql_query"]').click();
  await page.locator('#mcp-arg-sql').fill('select 1');
  await page.getByTestId('mcp-call').click();
  const result = page.getByTestId('mcp-result');
  await expect(result.locator('pre')).toHaveText(JSON.stringify({ documentSetVersion: 0, items: [] }, null, 2));
  await expect(page.getByTestId('mcp-result-raw')).not.toHaveAttribute('open', '');
  await page.getByTestId('mcp-result-raw').locator('summary').click();
  await expect(page.getByTestId('mcp-result-raw')).toContainText('structuredContent');
});

test('A2A card exposes skills, examples and interfaces without executing until sent; raw JSON remains editable', async ({ page }) => {
  const requests = [];
  await installMocks(page, { onRequest: (request) => requests.push(request) });
  await page.route('**/.well-known/agent-card.json', (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        name: 'Synthetic investigator',
        version: '1.2',
        description: 'Investigate synthetic systems.',
        provider: { organization: 'Example team', url: 'https://example.test' },
        capabilities: { streaming: false, pushNotifications: false },
        defaultInputModes: ['text/plain'],
        defaultOutputModes: ['text/plain'],
        supportedInterfaces: [{ url: 'https://example.test/a2a', protocolBinding: 'JSONRPC' }],
        securitySchemes: { auth: { httpAuthSecurityScheme: { scheme: 'bearer', description: 'Session authentication' } } },
        skills: [{ id: 'logs', name: 'Inspect logs', description: 'Read synthetic logs.', tags: ['observability'], examples: ['Summarize recent errors.'] }]
      })
    })
  );
  await login(page, { expect });
  await page.goto(pathFor('a2aExplorer'));
  await page.getByTestId('a2a-card').click();
  const card = page.getByTestId('a2a-card-body');
  await expect(card).toContainText('Example team');
  await expect(card).toContainText('https://example.test/a2a');
  await expect(card).toContainText('Session authentication');
  await card.screenshot({ path: '/tmp/hugo-a2a-explorer-desktop.png' });
  await page.getByRole('button', { name: 'Toggle navigation', exact: true }).click();
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await card.screenshot({ path: '/tmp/hugo-a2a-explorer-mobile.png' });
  await page.setViewportSize({ width: 1280, height: 900 });
  await expect(page.getByTestId('a2a-card-json')).not.toHaveAttribute('open', '');
  await page.getByRole('button', { name: 'Try skill', exact: true }).click();
  await expect(page.locator('#a2a-message')).toHaveValue('Help me with Inspect logs.');
  expect(requests.filter((request) => request.method === 'POST' && request.path.startsWith('/v1.0/a2a/'))).toHaveLength(0);
  await page.getByRole('button', { name: 'Summarize recent errors.', exact: true }).click();
  await expect(page.locator('#a2a-message')).toHaveValue('Summarize recent errors.');
  await page.getByLabel('Edit message as JSON').check();
  const params = page.locator('#a2a-parameters');
  await params.fill('invalid');
  await page.getByTestId('a2a-send').click();
  await expect(page.locator('body')).toContainText('The request body is not valid JSON.');
  expect(requests.filter((request) => request.method === 'POST' && request.path.startsWith('/v1.0/a2a/'))).toHaveLength(0);
  await params.fill(
    JSON.stringify({
      message: { role: 'user', messageId: 'synthetic-message', parts: [{ kind: 'text', text: 'Custom JSON request' }] },
      metadata: { synthetic: true }
    })
  );
  await page.getByTestId('a2a-send').click();
  await expect(page.locator('body')).toContainText('Agent Smith received: Custom JSON request');
  const sent = requests.find((request) => request.method === 'POST' && request.path.startsWith('/v1.0/a2a/'));
  expect(sent.body.method).toBe('SendMessage');
  expect(sent.body.params.metadata).toEqual({ synthetic: true });
});
