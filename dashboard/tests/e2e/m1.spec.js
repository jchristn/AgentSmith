// Coverage for the M1 simulated user session fixes (P9.17, docs/testing/M1_SESSION.md) against mocked APIs.

import { test, expect } from '@playwright/test';
import { installMocks, respond } from './mocks.js';

async function login(page) {
  await page.goto('/');
  await page.locator('#tenantId').fill('default');
  await page.locator('#email').fill('admin@garrison');
  await page.locator('#password').fill('password');
  await page.getByTestId('login-submit').click();
  await expect(page.locator('.dashboard')).toBeVisible();
}

test.use({ actionTimeout: 15000 });

test.beforeEach(async ({ page }) => {
  await installMocks(page);
});

test('M1-01 settings validates ranges before saving and shows server errors per field', async ({ page }) => {
  let puts = 0;
  await page.route(/\/v1\.0\/api\/settings$/, (route) => {
    if (route.request().method() !== 'PUT') return route.fallback();
    puts += 1;
    return route.fulfill({
      status: 400,
      contentType: 'application/json',
      body: JSON.stringify({ error: 'BadRequest', code: 'ValidationFailed', message: 'One or more settings are invalid.', errors: { 'Logging.MinimumSeverity': 'Logging.MinimumSeverity must be between 0 and 7.' } })
    });
  });
  await login(page);
  await page.goto('/dashboard/system?tab=settings');
  const port = page.locator('[id="setting-Rest.Port"]');
  await port.fill('-5');
  // The value is kept as typed (never clamped) and flagged at once.
  await expect(port).toHaveValue('-5');
  await expect(page.getByTestId('setting-error-Rest.Port')).toContainText('1 to 65535');
  await page.getByTestId('save-settings').click();
  expect(puts).toBe(0);
  await expect(page.locator('.toast')).toContainText('Nothing was saved');
  await expect(page.locator('.toast', { hasText: 'saved and applied' })).toHaveCount(0);

  // Human labels instead of raw keys (M1-24), with the path still visible.
  await expect(page.locator('label[for="setting-Rest.Port"]')).toContainText('REST port');

  await port.fill('8701');
  await expect(page.getByTestId('setting-error-Rest.Port')).toHaveCount(0);
  await page.locator('[id="setting-Logging.MinimumSeverity"]').fill('3');
  await page.getByTestId('save-settings').click();
  expect(puts).toBe(1);
  await expect(page.getByTestId('setting-error-Logging.MinimumSeverity')).toContainText('between 0 and 7');
  await expect(page.locator('.toast', { hasText: 'saved and applied' })).toHaveCount(0);
});

test('M1-03 and M1-04 the deployment wizard defaults templates by role and keeps binding kind in step', async ({ page }) => {
  await login(page);
  await page.goto('/dashboard/deployments');
  await page.getByTestId('add-button').first().click();
  await page.locator('#deployments-name').waitFor();
  await page.locator('.modal .stepper-step').nth(2).click();
  // System binds builtin/default-system and Task binds builtin/default-task, never the newest template.
  await expect(page.locator('#ol-0-templateId')).toHaveValue('pt_0001');
  await expect(page.locator('#ol-1-templateId')).toHaveValue('pt_0002');
  // Each role lists templates of its own kind only: template-3 is a System template, so the Task row omits it.
  await expect(page.locator('#ol-1-templateId option', { hasText: 'template-3' })).toHaveCount(0);
  // Switching a row's role follows the new role's built-in.
  await page.locator('#ol-0-role').selectOption('Task');
  await expect(page.locator('#ol-0-templateId')).toHaveValue('pt_0002');

  await page.locator('.modal .stepper-step').nth(1).click();
  await page.locator('#deployments-harnessProfileId').selectOption('hp_0001');
  await page.locator('.modal .stepper-step').nth(3).click();
  await page.locator('.modal .object-list > button').first().click();
  // Kind follows the resource that is selected (a secret here), not the first enum value.
  await expect(page.locator('#ol-0-resourceId')).toHaveValue('sec_0001');
  await expect(page.locator('#ol-0-kind')).toHaveValue('Secret');
  await page.locator('#ol-0-resourceId').selectOption('mcp_0001');
  await expect(page.locator('#ol-0-kind')).toHaveValue('McpServer');
  // The hint names what the chosen profile reads; the mock profile reads no secret.
  await expect(page.locator('.modal .field-hint', { hasText: 'builtin/claude-code' })).toContainText('does not read any secret');
});

test('M1-05 home shows a setup note instead of unconfigured observability services', async ({ page }) => {
  await login(page);
  const card = page.getByTestId('observability');
  await expect(card).toBeVisible();
  await expect(page.getByTestId('observability-setup')).toContainText('Set up observability');
  await expect(card).not.toContainText('localhost');
  await expect(card).not.toContainText('admin / admin');
});

test('M1-05 home links only the observability services the server reports', async ({ page }) => {
  await page.route(/\/v1\.0\/api\/server-info$/, async (route) => {
    const body = respond('GET', new URL(route.request().url()));
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ ...body, observability: { grafanaUrl: 'http://localhost:3000' } }) });
  });
  await login(page);
  const card = page.getByTestId('observability');
  await expect(card.locator('[data-service]')).toHaveCount(1);
  await expect(card.locator('[data-service="grafana"]')).toContainText('http://127.0.0.1:3000');
});

test('M1-06 deleting an agent names the deployments deleted with it', async ({ page }) => {
  await login(page);
  await page.goto('/dashboard/agents');
  const row = page.locator('.data-table tbody tr', { hasText: 'echo-agent' });
  await row.locator('.action-menu-trigger, [aria-haspopup="menu"]').first().click();
  await page.getByRole('menuitem', { name: 'Delete' }).click();
  await expect(page.locator('.modal')).toContainText('and its 2 deployments');
  await expect(page.getByTestId('delete-affected').locator('li')).toHaveText(['deployment-1', 'deployment-2']);
});

/** The mocks answer every template ID with the built-in pt_0001; give pt_0004 its own custom record. */
async function customTemplate(page) {
  await page.route(/\/v1\.0\/api\/.*\/prompt-templates\/pt_0004$/, (route) =>
    route.request().method() === 'GET'
      ? route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ id: 'pt_0004', tenantId: 'default', name: 'terse-system', kind: 'System', content: 'Be terse.', currentVersion: 1, isBuiltIn: false, active: true }) })
      : route.fallback()
  );
}

test('M1-07 and M1-32 cloning asks for a name and built-in templates are clearly read-only', async ({ page }) => {
  await customTemplate(page);
  let cloneBody = null;
  await page.route(/\/prompt-templates\/[^/]+\/clone$/, async (route) => {
    cloneBody = route.request().postDataJSON();
    return route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({ id: 'pt_0004', name: cloneBody.name }) });
  });
  await login(page);
  await page.goto('/dashboard/prompt-templates/pt_0001');
  await expect(page.getByTestId('builtin-readonly')).toBeVisible();
  await expect(page.locator('#tpl-name')).toBeDisabled();
  await expect(page.locator('#tpl-description')).toBeDisabled();
  await page.getByTestId('builtin-readonly').getByRole('button').click();
  const name = page.locator('#clone-name');
  await expect(name).toHaveValue('default-system-custom');
  await name.fill('builtin/x');
  await page.getByTestId('clone-confirm').click();
  await expect(page.locator('.modal .field-error')).toContainText('reserved');
  await name.fill('terse-system');
  await page.getByTestId('clone-confirm').click();
  await expect(page).toHaveURL(/prompt-templates\/pt_0004/);
  expect(cloneBody).toEqual({ name: 'terse-system' });
  // A custom template's name can be changed.
  await expect(page.locator('#tpl-name')).toBeEnabled();
});

test('M1-21 template delete lists the deployments that bind it', async ({ page }) => {
  await customTemplate(page);
  await login(page);
  await page.goto('/dashboard/prompt-templates/pt_0004');
  await page.getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(page.locator('.modal')).toContainText('No deployment binds it');
});

test('M1-08 run detail explains delegation wait and analytics can exclude it', async ({ page }) => {
  await login(page);
  await page.goto('/dashboard/runs/run_0001');
  await expect(page.getByTestId('delegation-note')).toContainText('800 ms');
  await expect(page.locator('[data-telemetry="timeToFirstTokenExcludingDelegationMs"] .detail-item-value')).toHaveText('410 ms');

  await page.goto('/dashboard/analytics');
  await expect(page.getByTestId('analytics-card-ttftExcludingDelegation')).toBeVisible();
  await expect(page.getByTestId('analytics-card-delegationWait')).toBeVisible();
  await expect(page.getByTestId('analytics-chart-ttft').locator('.chart-legend')).toContainText('Excluding delegation (avg)');
  await page.getByTestId('analytics-ttft-excluding').click();
  await expect(page.getByTestId('analytics-ttft-excluding')).toBeChecked();
  await expect(page).toHaveURL(/ttft=excluding/);
  await expect(page.getByTestId('analytics-chart-ttft').locator('.chart-legend')).toContainText('Excluding delegation (p95)');
});

test('M1-33 the analytics range control is a one-line group of buttons', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await login(page);
  await page.goto('/dashboard/analytics');
  const group = page.getByRole('group', { name: 'Range' });
  await expect(group.getByRole('button')).toHaveCount(5);
  await expect(group.getByRole('button', { name: 'Last day' })).toHaveAttribute('aria-pressed', 'true');
  const heights = await group.getByRole('button').evaluateAll((els) => els.map((e) => e.getBoundingClientRect().height));
  const lineHeight = Math.min(...heights);
  for (const h of heights) expect(h).toBeLessThan(lineHeight * 1.5);
});

test('M1-14 manifest dry run shows field changes in a diff table', async ({ page }) => {
  await login(page);
  await page.goto('/dashboard/configuration?tab=manifests');
  await page.getByTestId('manifest-dryrun').click();
  const changes = page.getByTestId('manifest-changes');
  await expect(changes).toBeVisible();
  await expect(changes.locator('tbody tr').first()).toContainText('timeoutSeconds');
  await expect(changes.locator('tbody tr').first()).toContainText('300');
  await expect(changes.locator('tbody tr').first()).toContainText('120');
  await expect(page.getByTestId('manifest-results')).toContainText('Would be: Updated');
  // I-08: a clamped value is listed under its document.
  const warnings = page.getByTestId('manifest-warnings');
  await expect(warnings).toHaveCount(1);
  await expect(warnings).toContainText('maxAttempts');
  await expect(warnings).toContainText('The value 99 for maxAttempts is outside the allowed range 1 to 10; 10 was stored.');
  await expect(page.locator('tr[data-item="echo-agent-default"]')).toContainText('1 warning');
});

test('I-08 saving a deployment lists clamped values until dismissed', async ({ page }) => {
  await page.route(/\/v1\.0\/api\/tenants\/[^/]+\/deployments\/dep_0002$/, (route) => {
    if (route.request().method() !== 'PUT') return route.fallback();
    const body = route.request().postDataJSON();
    const warnings = [{ field: 'maxAttempts', code: 'ValueClamped', given: '99', stored: '10', message: 'maxAttempts 99 is outside the allowed range 1 to 10; 10 was stored.' }];
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ ...body, id: 'dep_0002', maxAttempts: 10, warnings }) });
  });
  await login(page);
  await page.goto('/dashboard/deployments');
  await page.locator('.data-table tbody tr', { hasText: 'deployment-2' }).getByText('deployment-2', { exact: true }).click();
  await expect(page.locator('.modal')).toBeVisible();
  await page.getByTestId('save-button').click();
  const notice = page.getByTestId('save-warnings');
  await expect(notice).toBeVisible();
  await expect(notice).toContainText('Saved with 1 adjusted value');
  await expect(notice.locator('li[data-field="maxAttempts"]')).toContainText('outside the allowed range 1 to 10');
  await page.getByTestId('save-warnings-dismiss').click();
  await expect(notice).toHaveCount(0);
});

test('G-29 the worker node list shows whether each node is containerized', async ({ page }) => {
  await login(page);
  await page.goto('/dashboard/nodes');
  await expect(page.locator('.data-table tbody tr', { hasText: 'garrison-worker-0' }).getByTestId('node-containerized')).toHaveText('Yes');
  await expect(page.locator('.data-table tbody tr', { hasText: 'garrison-worker-1' }).getByTestId('node-containerized')).toHaveText('No');
});

test('M1-20, M1-22, and M1-23 run detail shows names, template links, and why retry is unavailable', async ({ page }) => {
  await page.route(/\/agents\/agt_0001$/, (route) =>
    route.request().method() === 'GET' ? route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ code: 'NotFound', message: 'Agent agt_0001 not found.' }) }) : route.fallback()
  );
  await login(page);
  await page.goto('/dashboard/runs/run_0001');
  await expect(page.locator('.run-summary')).toContainText('deployment-1');
  await expect(page.getByTestId('run-template-link').first()).toHaveAttribute('href', /prompt-templates\/tpl_builtin_default_task/);
  await expect(page.getByTestId('agent-deleted')).toContainText('no longer exists');
  // The transcript line Garrison wrote is translated through its pattern; the code label is not needed then.
  await expect(page.locator('.timeline')).toContainText('Workspace prepared at /work/run_0001.');
  await expect(page.getByTestId('run-retry')).toBeDisabled();
});

test('M1-20 deployments list shows agent and harness profile names', async ({ page }) => {
  await login(page);
  await page.goto('/dashboard/deployments');
  const first = page.locator('.data-table tbody tr').first();
  await expect(first).toContainText('echo-agent');
  await expect(first).toContainText('builtin/claude-code');
  await expect(first).not.toContainText('hp_0001');
});

test('M1-27 a secret expiry in the past is refused before saving', async ({ page }) => {
  await login(page);
  await page.goto('/dashboard/configuration?tab=secrets');
  await page.getByTestId('add-button').first().click();
  await expect(page.locator('#secrets-expiresUtc')).toHaveAttribute('min', /\d{4}-\d{2}-\d{2}T/);
  await page.locator('#secrets-name').fill('old-key');
  await page.locator('#secrets-value').fill('abc');
  await page.locator('#secrets-expiresUtc').fill('2020-01-01T00:00');
  await page.getByTestId('save-button').click();
  await expect(page.locator('.modal .field-error')).toContainText('in the future');
});

test('M1-24 transcript lines written by Garrison are translated', async ({ page }) => {
  await page.route(/\/runs\/[^/]+\/events(\?|$)/, (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ runId: 'run_0001', status: 'Succeeded', events: [{ id: 'rev_1', runId: 'run_0001', sequence: 1, eventType: 'Lifecycle', text: 'Workspace prepared at /work/run_0001.', timestampUtc: '2026-10-09T12:00:00Z' }], lastSequence: 1, hasMore: false })
    })
  );
  await page.goto('/?lang=de');
  await page.locator('#tenantId').fill('default');
  await page.locator('#email').fill('admin@garrison');
  await page.locator('#password').fill('password');
  await page.getByTestId('login-submit').click();
  await expect(page.locator('.dashboard')).toBeVisible();
  await page.goto('/dashboard/runs/run_0001');
  await expect(page.locator('.timeline')).toContainText('Arbeitsbereich vorbereitet unter /work/run_0001.');
});

test('P9.17 settings field codes from a 400 are translated with the field range', async ({ page }) => {
  await page.route(/\/v1\.0\/api\/settings$/, (route) =>
    route.request().method() === 'PUT'
      ? route.fulfill({ status: 400, contentType: 'application/json', body: JSON.stringify({ code: 'ValidationFailed', message: 'One or more settings are invalid.', errors: { 'Scheduling.LeaseDurationMs': 'raw server text' }, fields: { 'Scheduling.LeaseDurationMs': 'raw server text' }, fieldCodes: { 'Scheduling.LeaseDurationMs': 'OutOfRange' } }) })
      : route.fallback()
  );
  await page.goto('/?lang=de');
  await page.locator('#tenantId').fill('default');
  await page.locator('#email').fill('admin@garrison');
  await page.locator('#password').fill('password');
  await page.getByTestId('login-submit').click();
  await expect(page.locator('.dashboard')).toBeVisible();
  await page.goto('/dashboard/system?tab=settings');
  await page.locator('[id="setting-Scheduling.LeaseDurationMs"]').fill('6000');
  await page.getByTestId('save-settings').click();
  await expect(page.getByTestId('setting-error-Scheduling.LeaseDurationMs')).toContainText('5000');
  await expect(page.getByTestId('setting-error-Scheduling.LeaseDurationMs')).not.toContainText('raw server text');
});
