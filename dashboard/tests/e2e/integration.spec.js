// The pages wired to the integration routes (P8.33, P8.34, P8.35, P8.38, P8.53) against mocks with the server's real
// shapes: layered default instructions on a connector, the layered Prompt Templates list and editor, the resource
// detail page with Try a Tool and the Knowledge tab, the compiled-instructions source map, model validation history,
// derived analytics, and the approval error codes.

import { test, expect } from '@playwright/test';
import { installMocks, login } from './mocks.js';
import { pathFor } from '../../src/config/navConfig.js';

test.beforeEach(async ({ page }) => {
  page.on('dialog', (dialog) => {
    throw new Error(`Native dialog used: ${dialog.message()}`);
  });
});

test('edits a connector default instructions per layer with reset, versions, diff, and the update notice', async ({ page }) => {
  const requests = [];
  await installMocks(page, { onRequest: (r) => requests.push(r) });
  await login(page, { expect });
  await page.goto(pathFor('connectors', 'ssh'));
  await page.getByRole('tab', { name: 'Default instructions' }).click();
  const editor = page.getByTestId('connector-layered-editor');
  await expect(editor).toBeVisible();
  await expect(page.getByTestId('builtin-updated')).toContainText('The Global default changed');
  await expect(page.getByTestId('layer-versions')).toContainText('v2');
  await page.locator('#layer-Tenant-content').fill('# SSH resources\nAsk before restarting anything.');
  await page.locator('#layer-Tenant-note').fill('stricter');
  await page.getByTestId('layer-save').click();
  const save = requests.find((r) => r.method === 'PUT' && r.path === '/v1.0/api/prompt-templates/connector.ssh.instructions');
  expect(save.body).toMatchObject({ content: '# SSH resources\nAsk before restarting anything.', changeNote: 'stricter' });
  await page.getByTestId('layer-diff').click();
  await expect(page.getByTestId('server-diff-lines')).toContainText('Never restart payments.');
  await page.keyboard.press('Escape');
  await page.getByTestId('layer-reset').click();
  await page.getByRole('dialog').getByRole('button', { name: 'Reset to default' }).click();
  expect(requests.some((r) => r.method === 'POST' && r.path === '/v1.0/api/prompt-templates/connector.ssh.instructions/reset')).toBe(true);
  await page.getByRole('tab', { name: /^Built-in/ }).click();
  await expect(page.getByTestId('layer-builtin')).toContainText('Prefer read-only commands.');
});

test('lists prompt templates by key and layer and opens the layered editor', async ({ page }) => {
  await installMocks(page);
  await login(page, { expect });
  await page.goto(pathFor('promptTemplates'));
  await expect(page.locator('.data-table tbody tr')).toHaveCount(3);
  await expect(page.locator('.data-table')).toContainText('Default updated');
  await page.locator('select[aria-label="Customized"]').selectOption('Tenant');
  await expect(page.locator('.data-table tbody tr')).toHaveCount(1);
  await page.locator('.data-table tbody tr').first().click();
  await expect(page).toHaveURL(/\/dashboard\/prompts\/connector\.ssh\.instructions$/);
  await expect(page.getByTestId('template-layered-editor')).toContainText('connector.ssh.instructions');
});

test('opens a resource, dry runs and executes a tool, and works with its knowledge', async ({ page }) => {
  const requests = [];
  await installMocks(page, { onRequest: (r) => requests.push(r) });
  await login(page, { expect });
  await page.goto(pathFor('resources'));
  await page.locator('.data-table tbody tr').nth(1).click();
  await expect(page).toHaveURL(/\/dashboard\/resources\/res_0002$/);
  await expect(page.getByTestId('resource-overview')).toContainText('Hugo / web-01');
  await page.getByRole('tab', { name: 'Try a tool' }).click();
  await expect(page.getByTestId('try-tool-panel')).toBeVisible();

  await page.getByRole('tab', { name: 'Knowledge' }).click();
  await expect(page.getByTestId('knowledge-context')).toContainText('Database context');
  await page.getByTestId('knowledge-item').nth(1).click();
  await expect(page.getByTestId('knowledge-entry')).toHaveAttribute('data-key', 'public.orders');
  await page.getByTestId('knowledge-history').click();
  await expect(page.getByTestId('knowledge-versions')).toContainText('v1');
  await page.locator('[data-testid="knowledge-entry"] textarea').fill('One row per order; status is paid or open.');
  await page.getByTestId('knowledge-save').click();
  const put = requests.find((r) => r.method === 'PUT' && r.path === '/v1.0/api/resources/res_0002/knowledge/table/public.orders');
  expect(put.body).toEqual({ content: 'One row per order; status is paid or open.' });
  await page.getByTestId('knowledge-load-tables').click();
  await expect(page.getByTestId('knowledge-context')).toContainText('public.customers');

  await page.getByRole('tab', { name: 'Intelligence' }).click();
  await expect(page.getByTestId('quality-score')).toContainText('48');
  await expect(page.getByTestId('intelligence-entities')).toContainText('orders');
  await page.getByRole('tab', { name: 'Relationships' }).click();
  await expect(page.getByTestId('relationship-graph')).toBeVisible();
  await expect(page.locator('.rel-edge.inferred')).toHaveCount(1);
  await expect(page.locator('.rel-edge.declared')).toHaveCount(1);
  expect(requests.filter((r) => r.path === '/v1.0/api/resources/res_0002/invoke').map((r) => r.body.toolName)).toEqual([
    'sql_get_context',
    'sql_get_intelligence',
    'sql_list_relationships'
  ]);

  await page.getByTestId('generate-context-open').click();
  await expect(page.getByTestId('generate-context')).toBeVisible();
  await page.getByTestId('generate-run').click();
  const generate = requests.find((r) => r.method === 'POST' && r.path === '/v1.0/api/tool-invocations');
  expect(generate.body).toMatchObject({
    profileId: 'prf_0001',
    resource: 'web-01',
    toolName: 'sql_generate_context',
    arguments: { scope: 'all', overwrite: false }
  });
});

test('sends the binding alias with a dry run and shows a pending approval after execute', async ({ page }) => {
  const requests = [];
  await installMocks(page, { onRequest: (r) => requests.push(r) });
  await login(page, { expect });
  await page.goto(`${pathFor('resources', 'res_0001')}?tab=tools`);
  // The panel fills the arguments once the profile's bindings and tools load; wait for that before typing.
  await expect(page.locator('#try-tool-name')).toHaveValue('ssh_exec');
  await expect(page.locator('#try-args')).toHaveValue(/command/);
  await page.locator('#try-args').fill('{ "command": "restart nginx" }');
  await page.getByTestId('dry-run').click();
  await expect(page.getByTestId('dry-run-result')).toHaveAttribute('data-decision', 'ApprovalRequired');
  const dry = requests.find((r) => r.path === '/v1.0/api/tool-invocations/dry-run');
  expect(dry.body).toEqual({ profileId: 'prf_0001', resource: 'web-01', toolName: 'ssh_exec', arguments: { command: 'restart nginx' } });
  await page.getByTestId('execute-tool').click();
  await expect(page.getByTestId('pending-approval')).toContainText('apr_0101');
});

test('shows the compiled instructions with a gutter per template layer', async ({ page }) => {
  await installMocks(page);
  await login(page, { expect });
  await page.goto(`${pathFor('profiles', 'prf_0001')}?tab=instructions`);
  await expect(page.getByTestId('compiled-text')).toContainText('Address the user as Mr. Anderson');
  await expect(page.locator('.src-chunk.layer-Tenant')).toHaveCount(1);
  await expect(page.getByTestId('source-map')).toContainText('connector.postgresql.instructions');
  await page.getByTestId('source-map').getByRole('button', { name: 'persona.agent-smith' }).hover();
  await expect(page.locator('.src-chunk.active')).toHaveCount(1);
});

test('keeps the validation history of a model endpoint for the session', async ({ page }) => {
  await installMocks(page);
  await login(page, { expect });
  await page.goto(pathFor('models'));
  await page.locator('.data-table tbody tr').first().locator('[aria-haspopup="menu"]').first().click();
  await page.getByRole('menuitem', { name: 'Validate' }).click();
  await expect(page.getByTestId('result-outcome')).toContainText('Succeeded');
  await page.getByTestId('result-modal').getByRole('button', { name: 'Close', exact: true }).last().click();
  await page.locator('.data-table tbody tr').first().click();
  await expect(page.getByTestId('model-health-history')).toContainText('Succeeded');
});

test('derives analytics from tool invocations and approvals while the analytics routes are missing', async ({ page }) => {
  await installMocks(page, { missing: ['analytics'] });
  await login(page, { expect });
  await page.goto(pathFor('analytics'));
  await expect(page.getByTestId('analytics-derived')).toBeVisible();
  await expect(page.getByTestId('derived-effect')).toContainText('Destructive');
  await expect(page.getByTestId('derived-tool')).toContainText('ssh_exec');
});

test('explains a self-approval refusal instead of calling it already decided', async ({ page }) => {
  await installMocks(page);
  await page.route('**/v1.0/api/approvals/*/approve', (route) =>
    route.fulfill({
      status: 409,
      contentType: 'application/json',
      body: JSON.stringify({ error: 'Conflict', code: 'SelfApprovalNotAllowed', message: 'You cannot approve your own call.' })
    })
  );
  await login(page, { expect });
  await page.goto(pathFor('approvals'));
  await page.locator('.data-table tbody tr').first().locator('[aria-haspopup="menu"]').first().click();
  await page.getByRole('menuitem', { name: 'Approve or reject' }).click();
  await page.getByTestId('approval-approve').click();
  await expect(page.locator('.toast')).toContainText('another person');
});
