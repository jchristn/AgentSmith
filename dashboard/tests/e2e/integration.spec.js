// The pages wired to the integration routes (P8.33, P8.34, P8.35, P8.38, P8.53) against mocks with the server's real
// shapes: layered default instructions on a connector, the layered Prompt Templates list and editor, the resource
// detail page with Try a Tool and the Knowledge tab, the compiled-instructions source map, model validation history,
// analytics from the report routes, server-side list filters, the credential owner bypass, and the approval error codes.

import { test, expect } from '@playwright/test';
import { installMocks, login, PRINCIPAL } from './mocks.js';
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
  // The stored root entry (key _root) is the database context itself: no placeholder row next to it.
  await expect(page.getByTestId('knowledge-item')).toHaveCount(2);
  await expect(page.getByTestId('knowledge-item').first()).not.toContainText('_root');
  await expect(page.getByTestId('knowledge-entry')).toHaveAttribute('data-key', '_root');
  await page.locator('[data-testid="knowledge-entry"] textarea').fill('Shop database.');
  await page.getByTestId('knowledge-save').click();
  await expect.poll(() => requests.some((r) => r.method === 'PUT' && r.path === '/v1.0/api/resources/res_0002/knowledge/database/_root')).toBe(true);
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

test('shows the health history of a model endpoint from the server', async ({ page }) => {
  const requests = [];
  await installMocks(page, { onRequest: (r) => requests.push(r) });
  await login(page, { expect });
  await page.goto(pathFor('models'));
  await page.locator('.data-table tbody tr').first().locator('[aria-haspopup="menu"]').first().click();
  await page.getByRole('menuitem', { name: 'Validate' }).click();
  // The result is laid out (outcome, model, latency, reply), not dumped as JSON.
  await expect(page.getByTestId('model-validation-outcome')).toContainText('The model responded.');
  await expect(page.getByTestId('model-validation').locator('.kv-grid')).toBeVisible();
  await page.getByTestId('result-modal').getByRole('button', { name: 'Close', exact: true }).last().click();
  await page.locator('.data-table tbody tr').first().click();
  const editor = page.getByRole('dialog');
  await expect(editor).toBeVisible();
  await expect(editor.getByTestId('model-health-history')).toHaveCount(0);
  expect(requests.filter((r) => r.method === 'GET' && r.path.endsWith('/health-history'))).toHaveLength(0);
  await editor.getByRole('button', { name: 'Close', exact: true }).first().click();
  await page.locator('.data-table tbody tr').first().locator('[aria-haspopup="menu"]').first().click();
  await page.getByRole('menuitem', { name: 'Health', exact: true }).click();
  const modal = page.getByTestId('model-health-modal');
  await expect(modal).toBeVisible();
  await expect(modal.locator('input, select, textarea')).toHaveCount(0);
  const history = modal.getByTestId('model-health-history');
  await expect(history.locator('.model-health-chart')).toBeVisible();
  await history.locator('summary').click();
  await expect(history.getByTestId('model-health-record')).toHaveCount(3);
  await expect(history.getByTestId('model-health-record').first()).toContainText('Validation');
  await expect(history.getByTestId('model-health-record').nth(1)).toContainText('Health sweep');
  await expect(history.getByTestId('model-health-record').nth(1)).toContainText('Connection refused');
  expect(requests.some((r) => r.method === 'GET' && r.path === '/v1.0/api/model-endpoints/mdl_0001/health-history')).toBe(true);
});

test('shows the provider message when listing a model endpoint models fails with 502', async ({ page }) => {
  await installMocks(page);
  await page.route('**/v1.0/api/model-endpoints/*/models', (route) =>
    route.fulfill({
      status: 502,
      contentType: 'application/json',
      body: JSON.stringify({
        error: 'BadGateway',
        code: 'ProviderUnreachable',
        message: 'The Ollama provider could not list models: connection refused',
        requestId: 'r1'
      })
    })
  );
  await login(page, { expect });
  await page.goto(pathFor('models'));
  await page.locator('.data-table tbody tr').first().locator('[aria-haspopup="menu"]').first().click();
  await page.getByRole('menuitem', { name: 'List provider models' }).click();
  await expect(page.getByTestId('result-modal')).toContainText('The Ollama provider could not list models: connection refused');
});

test('draws analytics from the analytics report routes', async ({ page }) => {
  const requests = [];
  await installMocks(page, { onRequest: (r) => requests.push(r) });
  await login(page, { expect });
  await page.goto(pathFor('analytics'));
  await expect(page.getByTestId('analytics-kpis')).toContainText('42');
  await expect(page.getByTestId('analytics-tools')).toContainText('ssh_exec');
  await expect(page.getByTestId('analytics-models')).toContainText('local-llm');
  await expect(page.getByTestId('chart-tokens')).toBeVisible();
  for (const kind of ['overview', 'tokens', 'tools', 'models']) expect(requests.some((r) => r.path === `/v1.0/api/analytics/${kind}`)).toBe(true);
});

test('filters lists on the server with query parameters', async ({ page }) => {
  const urls = [];
  await installMocks(page);
  page.on('request', (req) => urls.push(new URL(req.url())));
  await login(page, { expect });
  await page.goto(`${pathFor('resources')}${pathFor('resources').includes('?') ? '&' : '?'}health=Unhealthy`);
  await expect(page.locator('.data-table tbody tr')).toHaveCount(1);
  await expect(page.locator('.data-table tbody')).toContainText('prod-db');
  expect(urls.some((u) => u.pathname === '/v1.0/api/resources' && u.searchParams.get('health') === 'Unhealthy')).toBe(true);
  await page.goto(pathFor('sessions'));
  await page.locator('select[aria-label="Principal type"]').selectOption('Credential');
  await expect(page.locator('.data-table tbody tr')).toHaveCount(1);
  await expect(page.locator('.data-table tbody')).toContainText('claude-code-1');
  expect(urls.some((u) => u.pathname === '/v1.0/api/sessions' && u.searchParams.get('principalType') === 'Credential')).toBe(true);
});

test('offers the credential owner bypass to administrators with a warning', async ({ page }) => {
  await installMocks(page);
  await login(page, { expect });
  await page.goto(pathFor('credentials'));
  await expect(page.locator('.data-table tbody')).toContainText('Owner bypass');
  await page.getByTestId('add-button').first().click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toContainText('A credential without a role gets no access');
  await expect(dialog.getByTestId('owner-bypass-warning')).toHaveCount(0);
  await dialog.getByLabel("Carry the owner's administrator bypass").check();
  await expect(dialog.getByTestId('owner-bypass-warning')).toBeVisible();
});

test('hides the credential owner bypass from principals that are not administrators', async ({ page }) => {
  await installMocks(page);
  await page.route('**/v1.0/token/details', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ ...PRINCIPAL, isAdmin: false, isTenantAdmin: false })
    })
  );
  await login(page, { expect });
  await page.goto(pathFor('credentials'));
  await page.getByTestId('add-button').first().click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await expect(page.getByRole('dialog').getByLabel("Carry the owner's administrator bypass")).toHaveCount(0);
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
