// Operator workflows against mocked Hugo APIs (P8.50): secrets with reveal, a resource with the schema form and
// Test Connection, a binding on the profile, chat with a streamed turn and an inline approval, the approvals inbox,
// request history, the API, MCP, and A2A explorers, settings, and the setup wizard.

import { test, expect } from '@playwright/test';
import { installMocks, login } from './mocks.js';
import { pathFor } from '../../src/config/navConfig.js';

test.beforeEach(async ({ page }) => {
  page.on('dialog', (dialog) => {
    throw new Error(`Native dialog used: ${dialog.message()}`);
  });
});

test('creates a secret with kind-specific fields and reveals it with a reason and re-authentication', async ({ page }) => {
  const requests = [];
  await installMocks(page, { onRequest: (r) => requests.push(r) });
  let reveals = 0;
  await page.route('**/v1.0/api/secrets/*/reveal', (route) => {
    reveals += 1;
    if (reveals === 1)
      return route.fulfill({
        status: 403,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'ReauthenticationRequired', message: 'Re-authenticate first.' })
      });
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ values: { password: 'swordfish' } }) });
  });
  await login(page, { expect });
  await page.goto(pathFor('secrets'));
  await expect(page.locator('.data-table tbody tr')).toHaveCount(4);
  await expect(page.locator('.data-table')).toContainText('Expiring soon');
  await page.getByTestId('add-button').click();
  await page.locator('#secrets-name').fill('aws-staging');
  await page.locator('#secrets-kind').selectOption('AccessKeyPair');
  await page.locator('#secret-create-accessKeyId').fill('AKIAEXAMPLE');
  await page.locator('#secret-create-secretAccessKey').fill('very-secret');
  await page.getByTestId('save-button').click();
  const create = requests.find((r) => r.method === 'POST' && r.path === '/v1.0/api/secrets');
  expect(create.body).toMatchObject({ name: 'aws-staging', kind: 'AccessKeyPair', values: { accessKeyId: 'AKIAEXAMPLE', secretAccessKey: 'very-secret' } });

  await page.locator('.data-table tbody tr').first().locator('.action-menu-trigger, [aria-haspopup="menu"]').first().click();
  await page.getByRole('menuitem', { name: 'Reveal' }).click();
  const modal = page.getByTestId('reveal-modal');
  await modal.locator('#reveal-reason').fill('Incident 42');
  await page.getByTestId('reveal-submit').click();
  await modal.locator('#reveal-password').fill('password');
  await page.getByTestId('reauth-submit').click();
  await expect(page.getByTestId('revealed-values')).toContainText('swordfish');
  expect(requests.some((r) => r.path === '/v1.0/token/reauthenticate' && r.body?.password === 'password')).toBe(true);
  // The session survives the 403: the page did not log out.
  await expect(page.locator('.dashboard')).toBeVisible();
});

test('adds a resource from the connector schema, tests the connection, and creates a secret inline', async ({ page }) => {
  const requests = [];
  await installMocks(page, { onRequest: (r) => requests.push(r) });
  await login(page, { expect });
  await page.goto(pathFor('resources'));
  await expect(page.locator('.data-table')).toContainText('web-01');
  await page.getByTestId('add-button').click();
  await page.locator('#resources-name').fill('web-02');
  await page.locator('#resource-connectorType').selectOption('ssh');
  const form = page.getByTestId('schema-form');
  await expect(form.locator('legend')).toHaveText(['Connection', 'Authentication', 'Limits', 'Advanced']);
  await expect(form.locator('#resource-setting-port')).toHaveValue('22');
  await page.getByTestId('save-button').click();
  await expect(page.locator('.field-error').first()).toBeVisible();
  await form.locator('#resource-setting-host').fill('web-02.example.com');
  await form.locator('#resource-setting-username').fill('ops');
  await page.getByTestId('create-secret-privateKey').click();
  await page.locator('#resource-slot-new-name').fill('web-02-key');
  await page.locator('#resource-slot-new-privateKey').fill('-----BEGIN OPENSSH PRIVATE KEY-----\nabc');
  await page.getByTestId('inline-secret').locator('.button-primary').click();
  await page.getByTestId('test-connection').click();
  await expect(page.getByTestId('test-result')).toContainText('Connected');
  const test1 = requests.find((r) => r.path === '/v1.0/api/resources/test');
  expect(test1.body).toMatchObject({ connectorType: 'ssh', settings: { host: 'web-02.example.com', port: 22, username: 'ops' } });
  await page.getByTestId('save-button').click();
  const create = requests.find((r) => r.method === 'POST' && r.path === '/v1.0/api/resources');
  expect(create.body).toMatchObject({
    name: 'web-02',
    connectorType: 'ssh',
    settings: { host: 'web-02.example.com', port: 22, username: 'ops', allowedPaths: ['/var/log'], shell: 'bash', useSudo: false }
  });
  expect(create.body.secretBindings).toEqual([{ slotName: 'privateKey', secretId: expect.any(String), secretFieldName: null }]);
  expect(create.body._connector).toBeUndefined();
});

test('tries a tool on a resource with a dry run first', async ({ page }) => {
  await installMocks(page);
  await login(page, { expect });
  await page.goto(pathFor('resources'));
  await page.locator('.data-table tbody tr').first().locator('[aria-haspopup="menu"]').first().click();
  await page.getByRole('menuitem', { name: 'Try a tool' }).click();
  const modal = page.getByTestId('try-tool');
  await expect(page.getByTestId('execute-tool')).toBeDisabled();
  await page.getByTestId('dry-run').click();
  await expect(page.getByTestId('dry-run-result')).toContainText('Read');
  await page.getByTestId('execute-tool').click();
  await expect(page.getByTestId('execute-result')).toContainText('up 41 days');
  await expect(modal).toBeVisible();
});

test('composes a profile: binding, instructions, tools, and connect snippets', async ({ page }) => {
  const requests = [];
  await installMocks(page, { onRequest: (r) => requests.push(r) });
  await login(page, { expect });
  await page.goto(pathFor('profiles'));
  await expect(page.getByTestId('relation-diagram')).toBeVisible();
  await page.locator('.data-table tbody tr').first().click();
  await expect(page).toHaveURL(/\/dashboard\/profiles\/prf_0001$/);
  await expect(page.locator('#profile-personaName')).toHaveValue('Agent Smith');
  await page.getByRole('tab', { name: 'Bindings' }).click();
  await expect(page.getByTestId('profile-bindings')).toContainText('web-01');
  await page.getByTestId('add-binding').click();
  await page.locator('#binding-resourceId').selectOption('res_0003');
  await expect(page.locator('#binding-alias')).toHaveValue('assets');
  await page.locator('#binding-accessMode').selectOption('ReadWriteWithApproval');
  await page.getByTestId('binding-save').click();
  const created = requests.find((r) => r.method === 'POST' && /\/bindings$/.test(r.path));
  expect(created.body).toMatchObject({ resourceId: 'res_0003', alias: 'assets', accessMode: 'ReadWriteWithApproval' });
  await page.getByRole('tab', { name: 'Instructions' }).click();
  await expect(page.getByTestId('profile-instructions')).toContainText('Address the user as Mr. Anderson');
  await page.getByRole('tab', { name: 'Tools' }).click();
  await expect(page.getByTestId('profile-tools')).toContainText('sql_query');
  await page.getByRole('tab', { name: 'Connect' }).click();
  await expect(page.getByTestId('snippet-mcp')).toContainText('/mcp/Hugo');
});

test('chats with Agent Smith: streams a turn, shows the tool row, and approves inline', async ({ page }) => {
  const requests = [];
  await installMocks(page, { onRequest: (r) => requests.push(r) });
  await login(page, { expect });
  await page.goto(pathFor('chat', 'cnv_0001'));
  await expect(page.getByTestId('chat-message-assistant').first()).toContainText('14 waiting queries');
  await expect(page.getByTestId('chat-drawer')).toContainText('prod-db');
  await page.getByTestId('chat-input').fill('Restart nginx on web-01');
  await page.getByTestId('chat-input').press('Enter');
  const reply = page.getByTestId('chat-message-assistant').last();
  await expect(reply).toContainText('Mr. Anderson.');
  await expect(reply.getByTestId('tool-row')).toHaveAttribute('data-status', 'awaitingApproval');
  await expect(reply.getByTestId('approval-card')).toContainText('sudo systemctl restart nginx');
  await expect(reply.getByTestId('chat-usage')).toBeVisible();
  await reply.getByTestId('approval-approve').click();
  await expect(reply.getByTestId('approval-card')).toHaveAttribute('data-status', 'Approved');
  expect(requests.some((r) => r.method === 'POST' && r.path === '/v1.0/api/approvals/apr_0099/approve')).toBe(true);
  expect(requests.find((r) => r.method === 'POST' && r.path === '/v1.0/api/conversations/cnv_0001/messages').body).toEqual({
    message: 'Restart nginx on web-01'
  });
});

test('starts a new conversation on the first message and keeps drafts', async ({ page }) => {
  await installMocks(page);
  await login(page, { expect });
  await page.goto(pathFor('chat'));
  await page.getByTestId('chat-input').fill('draft text');
  await page.goto(pathFor('home'));
  await page.goto(pathFor('chat'));
  await expect(page.getByTestId('chat-input')).toHaveValue('draft text');
  await page.getByTestId('chat-send').click();
  await expect(page).toHaveURL(/\/dashboard\/chat\/cnv_0042$/);
});

test('decides approvals in the inbox and rejects in bulk', async ({ page }) => {
  const requests = [];
  await installMocks(page, { onRequest: (r) => requests.push(r) });
  await login(page, { expect });
  await page.goto(pathFor('approvals'));
  await expect(page.locator('.data-table tbody tr')).toHaveCount(2);
  await expect(page.locator('.data-table')).toContainText('Destructive');
  await page.locator('.data-table tbody tr').first().locator('[aria-haspopup="menu"]').first().click();
  await page.getByRole('menuitem', { name: 'Approve or reject' }).click();
  await page.getByTestId('approval-decide').getByTestId('approval-reject').click();
  expect(requests.some((r) => r.path === '/v1.0/api/approvals/apr_0001/reject')).toBe(true);
  await expect(page.getByTestId('approval-decide')).toHaveCount(0);
  await page.locator('.data-table thead input[type="checkbox"]').check();
  await page.getByRole('button', { name: 'Reject selected' }).click();
  await expect.poll(() => requests.filter((r) => /\/reject$/.test(r.path)).length).toBe(3);
});

test('inspects request history in the details modal', async ({ page }) => {
  await installMocks(page);
  await login(page, { expect });
  await page.goto(pathFor('requests'));
  await expect(page.locator('.data-table tbody tr').first()).toBeVisible();
  await expect(page.locator('select').filter({ hasText: '1000' }).first()).toBeVisible();
  await page.locator('.data-table tbody tr').first().click();
  await expect(page.locator('.modal')).toContainText('/v1.0/api/resources');
  await page.keyboard.press('Escape');
  await expect(page.locator('.modal')).toHaveCount(0);
});

test('API Explorer reads the live OpenAPI document and confirms destructive calls', async ({ page }) => {
  await installMocks(page);
  await login(page, { expect });
  await page.goto(pathFor('explorer'));
  const operations = page.getByRole('combobox', { name: 'Operations' });
  await expect(operations).toContainText('List resources');
  await operations.selectOption({ label: 'DELETE /v1.0/api/resources/{id}: Delete resource' });
  await page.getByTestId('explorer-execute').click();
  await expect(page.locator('.modal')).toBeVisible();
  await page.locator('.modal .button-secondary').click();
  await operations.selectOption({ label: 'GET /v1.0/api/resources: List resources' });
  await page.getByTestId('explorer-execute').click();
  await expect(page.locator('.modal')).toHaveCount(0);
  await expect(page.locator('body')).toContainText('web-01');
});

test('MCP Explorer initializes, lists tools by family, and calls one with a generated form', async ({ page }) => {
  await installMocks(page);
  await login(page, { expect });
  await page.goto(pathFor('mcpExplorer'));
  await page.getByTestId('mcp-connect').click();
  await expect(page.locator('body')).toContainText('You are Agent Smith');
  await page.locator('[data-tool="sql_query"]').click();
  await page.locator('#mcp-arg-sql').fill('select 1');
  await page.getByTestId('mcp-call').click();
  await expect(page.getByTestId('mcp-tool')).toContainText('called sql_query');
  await expect(page.getByTestId('mcp-log')).toBeVisible();
});

test('A2A Explorer fetches the card, sends a message, and lists tasks', async ({ page }) => {
  await installMocks(page);
  await login(page, { expect });
  await page.goto(pathFor('a2aExplorer'));
  await page.getByTestId('a2a-card').click();
  await expect(page.getByTestId('a2a-card-body')).toContainText('Agent Smith');
  await page.locator('#a2a-message').fill('status report');
  await page.getByTestId('a2a-send').click();
  await expect(page.locator('body')).toContainText('Agent Smith received: status report');
  await page.getByTestId('a2a-tasks').click();
  await expect(page.getByTestId('a2a-task-list')).toContainText('task_1');
});

test('settings mark environment overrides read-only and restart behind a confirmation', async ({ page }) => {
  const requests = [];
  await installMocks(page, { onRequest: (r) => requests.push(r) });
  await login(page, { expect });
  await page.goto(pathFor('settings'));
  await expect(page.locator('#setting-Rest\\.Hostname')).toBeDisabled();
  await expect(page.locator('body')).toContainText('Restart required');
  await page.getByTestId('restart-server').click();
  await page.locator('.modal .button-danger').click();
  await expect.poll(() => requests.some((r) => r.path === '/v1.0/api/system/restart')).toBe(true);
  await page.getByRole('tab', { name: 'Server Info' }).click();
  await expect(page.locator('body')).toContainText('http://127.0.0.1:8800/mcp');
});

test('the setup wizard opens for a fresh tenant and walks the Hugo steps', async ({ page }) => {
  const requests = [];
  await installMocks(page, { onRequest: (r) => requests.push(r) });
  await page.route(/\/v1\.0\/api\/(model-endpoints|resources)\?/, (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ items: [], pageNumber: 1, pageSize: 1, totalCount: 0, totalPages: 0 })
    })
  );
  await login(page, { expect });
  const wizard = page.getByTestId('setup-wizard');
  await expect(wizard).toBeVisible();
  await page.getByTestId('wizard-primary').click();
  await wizard.locator('#wiz-inference-model').fill('llama3.1');
  await page.getByTestId('wizard-primary').click();
  expect(requests.find((r) => r.method === 'POST' && r.path === '/v1.0/api/model-endpoints').body).toMatchObject({
    kind: 'Inference',
    provider: 'Ollama',
    model: 'llama3.1'
  });
  await page.getByTestId('wizard-skip-step').click();
  await expect(wizard.locator('[data-step="resource"]')).toBeVisible();
  await page.getByTestId('wizard-skip-step').click();
  await page.getByTestId('wizard-skip-step').click();
  await expect(wizard).toContainText('mcpServers');
  await wizard.getByRole('button', { name: 'Skip setup' }).click();
  await page.reload();
  await expect(page.getByTestId('setup-wizard')).toHaveCount(0);
});

test('connectors catalog filters by category and opens a connector', async ({ page }) => {
  await installMocks(page);
  await login(page, { expect });
  await page.goto(pathFor('connectors'));
  await expect(page.locator('.connector-card')).toHaveCount(4);
  await page.locator('.connector-filters select').selectOption('Database');
  await expect(page.locator('.connector-card')).toHaveCount(1);
  await page.locator('.connector-filters select').selectOption('');
  await page.locator('[data-connector="ssh"]').click();
  await page.getByRole('tab', { name: 'Tools' }).click();
  await expect(page.getByTestId('connector-tools')).toContainText('ssh_exec');
  await page.getByRole('tab', { name: 'Settings schema' }).click();
  await expect(page.getByTestId('connector-settings')).toContainText('hostKeyFingerprint');
});

test('tool invocations show the redacted detail with a trace link', async ({ page }) => {
  await installMocks(page);
  await login(page, { expect });
  await page.goto(pathFor('toolInvocations'));
  await page.locator('.data-table tbody tr').first().click();
  const detail = page.getByTestId('tool-invocation-detail');
  await expect(detail).toContainText('[redacted:password]');
  await expect(detail.locator('a[href*="/explore"]')).toHaveCount(1);
});
