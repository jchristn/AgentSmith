// The primary journey (plan section 3.6) against a live Hugo server, clicked through the dashboard with no mocks:
// sign in as the seeded operator and change the password, run the setup wizard, create a secret, create a SQLite
// resource and test it, bind it to the Hugo profile ReadOnly, read the compiled instructions, dry run and invoke a
// tool from the resource page, see the call in Tool Invocations, switch the binding to ReadWriteWithApproval, trigger
// a write, approve it from Approvals, and check Request History and Audit. Screenshots go to
// test-results/live/screenshots.
//
// Run against a fresh server (the seeded password must still be "password", or set HUGO_OPERATOR_PASSWORD to the
// password chosen by an earlier run):
//   HUGO_DASHBOARD_URL=http://127.0.0.1:3000 npx playwright test --config playwright.live.config.js journey
// Names carry a per-run suffix, so the spec can run again against the same server.

import { test, expect } from '@playwright/test';
import fs from 'fs';
import path from 'path';
import { DatabaseSync } from 'node:sqlite';
import { pathFor } from '../../src/config/navConfig.js';

const EMAIL = process.env.HUGO_OPERATOR_EMAIL || 'anderson@hugo.local';
const SEEDED_PASSWORD = process.env.HUGO_OPERATOR_SEEDED_PASSWORD || 'password';
const NEW_PASSWORD = process.env.HUGO_OPERATOR_PASSWORD || 'Follow-the-white-rabbit-2026';
const RUN = Date.now().toString(36);
const OUT = path.resolve('test-results/live');
const SHOTS = path.join(OUT, 'screenshots');
const DB_FILE = path.join(OUT, `journey-${RUN}.db`);
const SECRET = `journey-secret-${RUN}`;
const RESOURCE = `journey-db-${RUN}`;

test.describe.configure({ mode: 'serial' });
test.setTimeout(240000);

test.beforeAll(() => {
  fs.mkdirSync(SHOTS, { recursive: true });
  const db = new DatabaseSync(DB_FILE);
  db.exec(`
    create table customers(id integer primary key, name text not null, email text, created_at text);
    create table orders(id integer primary key, customer_id integer references customers(id), total real, status text, created_at text);
    create table order_items(id integer primary key, order_id integer, sku text, qty integer);
    insert into customers(name, email, created_at) values ('Thomas Anderson', 'neo@example.com', '2026-01-01'), ('Trinity', 'trinity@example.com', '2026-02-01');
    insert into orders(customer_id, total, status, created_at) values (1, 10.5, 'paid', '2026-03-01'), (2, 99, 'open', '2026-03-02');
    insert into order_items(order_id, sku, qty) values (1, 'RED-PILL', 1), (2, 'BLUE-PILL', 2);
  `);
  db.close();
});

async function shot(page, name) {
  // The tenant event stream keeps a request open, so the page is never network idle for long; wait briefly.
  await page.waitForLoadState('networkidle', { timeout: 3000 }).catch(() => {});
  await page.mouse.move(1279, 719);
  await page.screenshot({ path: path.join(SHOTS, `${name}.png`), fullPage: true });
}

async function signIn(page, password) {
  await page.goto('/');
  await page.locator('#email').fill(EMAIL);
  await expect(page.getByTestId('tenant-found')).toBeVisible();
  await page.locator('#password').fill(password);
  await page.getByTestId('login-submit').click();
  return Promise.race([
    page
      .locator('.dashboard')
      .waitFor({ timeout: 15000 })
      .then(() => true),
    page
      .locator('.login-form .error-banner, .login-error, [role="alert"]')
      .first()
      .waitFor({ timeout: 15000 })
      .then(() => false)
  ]).catch(() => false);
}

/** Open a table row's action menu and choose an item. */
async function rowAction(page, rowText, item) {
  const row = page.locator('.data-table tbody tr', { hasText: rowText }).first();
  await row.locator('[aria-haspopup="menu"]').first().click();
  await page.getByRole('menuitem', { name: item }).click();
}

test('the primary journey through the dashboard against a live server', async ({ page }) => {
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  page.on('dialog', (dialog) => {
    throw new Error(`Native dialog used: ${dialog.message()}`);
  });

  // 1. Sign in as Mr. Anderson; the seeded account must change its password first.
  await page.goto('/');
  await shot(page, '01-login');
  let signedIn = await signIn(page, SEEDED_PASSWORD);
  if (signedIn) {
    const dialog = page.getByTestId('password-change');
    if (await dialog.isVisible().catch(() => false)) {
      await shot(page, '02-password-change');
      await dialog.locator('#pwd-currentPassword').fill(SEEDED_PASSWORD);
      await dialog.locator('#pwd-newPassword').fill(NEW_PASSWORD);
      await dialog.locator('#pwd-confirmPassword').fill(NEW_PASSWORD);
      await dialog.locator('.button-primary').click();
      await expect(dialog).toHaveCount(0);
    }
  } else {
    signedIn = await signIn(page, NEW_PASSWORD);
  }
  expect(signedIn, 'signed in').toBe(true);
  await expect(page.getByTestId('password-change')).toHaveCount(0);

  // 2. The setup wizard: an inference endpoint (it becomes the Hugo profile's model), then finish.
  const wizard = page.getByTestId('setup-wizard');
  if (!(await wizard.isVisible({ timeout: 8000 }).catch(() => false))) {
    await page.locator('.sidebar-utility').first().click();
  }
  await expect(wizard).toBeVisible();
  await shot(page, '03-setup-wizard');
  await wizard.getByTestId('wizard-primary').click();
  await wizard.locator('#wiz-inference-name').fill(`local-llm-${RUN}`);
  await wizard.locator('#wiz-inference-model').fill('llama3.1');
  await shot(page, '04-setup-wizard-inference');
  await wizard.getByTestId('wizard-primary').click();
  await expect(page.locator('.toast').first()).toBeVisible();
  for (let i = 0; i < 3; i++) await wizard.getByTestId('wizard-skip-step').click();
  await wizard.getByTestId('wizard-primary').click();
  await expect(wizard).toHaveCount(0);

  // 3. A secret, with kind-specific fields.
  await page.goto(pathFor('secrets'));
  await page.getByTestId('add-button').click();
  await page.locator('#secrets-name').fill(SECRET);
  await page.locator('#secrets-kind').selectOption('Password');
  await page.locator('#secret-create-username').fill('reporting');
  await page.locator('#secret-create-password').fill('there-is-no-spoon');
  await shot(page, '05-secret-form');
  await page.getByTestId('save-button').click();
  await expect(page.locator('.data-table')).toContainText(SECRET);
  await shot(page, '06-secrets');

  // 4. A SQLite resource from the connector schema, tested before saving.
  await page.goto(pathFor('resources'));
  await page.getByTestId('add-button').click();
  await page.locator('#resources-name').fill(RESOURCE);
  await page.locator('#resources-environment').fill('dev');
  await page.getByRole('radio', { name: /SQLite/ }).check();
  await page.locator('#resource-setting-filename').fill(DB_FILE);
  await page.getByTestId('test-connection').click();
  await expect(page.getByTestId('test-result')).toHaveClass(/success-banner/);
  await expect(page.getByTestId('test-result')).toContainText('SQLite');
  await shot(page, '07-resource-form-tested');
  await page.getByTestId('save-button').click();
  await expect(page.locator('.data-table')).toContainText(RESOURCE);

  // 5. The resource page: Test Connection on the saved resource.
  await page.locator('.data-table tbody tr', { hasText: RESOURCE }).first().click();
  await expect(page).toHaveURL(/\/dashboard\/resources\/res_/);
  const resourceUrl = page.url();
  await page.getByTestId('resource-test').click();
  await expect(page.getByTestId('resource-test-result')).toHaveClass(/success-banner/);
  await shot(page, '08-resource-overview');

  // 6. Bind it to the Hugo profile, ReadOnly.
  await page.goto(pathFor('profiles'));
  await page.locator('.data-table tbody tr', { hasText: 'Hugo' }).first().click();
  await expect(page).toHaveURL(/\/dashboard\/profiles\/agp_/);
  const profileUrl = page.url();
  await page.getByRole('tab', { name: 'Bindings' }).click();
  await page.getByTestId('add-binding').click();
  const resourceOption = page.locator('#binding-resourceId option', { hasText: RESOURCE });
  await page.locator('#binding-resourceId').selectOption(await resourceOption.getAttribute('value'));
  await expect(page.locator('#binding-alias')).toHaveValue(RESOURCE);
  await page.locator('#binding-accessMode').selectOption('ReadOnly');
  await page.getByTestId('binding-save').click();
  await expect(page.getByTestId('profile-bindings')).toContainText(RESOURCE);
  await shot(page, '09-profile-bindings');

  // 7. The compiled instructions: Agent Smith's persona, addressed to Mr. Anderson, with the resource section.
  await page.getByRole('tab', { name: 'Instructions' }).click();
  const compiled = page.getByTestId('compiled-text');
  await expect(compiled).toContainText('Agent Smith');
  await expect(compiled).toContainText('Mr. Anderson');
  await expect(compiled).toContainText(RESOURCE);
  await expect(page.getByTestId('source-map')).toContainText('connector.sqlite.instructions');
  await shot(page, '10-compiled-instructions');

  // 8. Dry run, then invoke a read tool from the resource page.
  await page.goto(`${resourceUrl}?tab=tools`);
  const panel = page.getByTestId('try-tool-panel');
  await expect(panel).toContainText(RESOURCE);
  await page.locator('#try-tool-name').selectOption('sql_query');
  await page.locator('#try-args').fill('{ "sql": "select count(*) as n from orders" }');
  await page.getByTestId('dry-run').click();
  await expect(page.getByTestId('dry-run-result')).toHaveAttribute('data-decision', 'Allowed');
  await page.getByTestId('execute-tool').click();
  await expect(page.getByTestId('execute-result')).toContainText('| n |');
  await shot(page, '11-try-tool-read');

  // 9. The call in Tool Invocations.
  await page.goto(pathFor('toolInvocations'));
  await expect(page.locator('.data-table')).toContainText('sql_query');
  await shot(page, '12-tool-invocations');

  // 10. Switch the binding to ReadWriteWithApproval.
  await page.goto(`${profileUrl}?tab=bindings`);
  await page.locator('.data-table tbody tr', { hasText: RESOURCE }).first().click();
  await page.locator('#binding-accessMode').selectOption('ReadWriteWithApproval');
  await page.getByTestId('binding-save').click();
  await expect(page.getByTestId('binding-modal')).toHaveCount(0);
  await expect(page.getByTestId('profile-bindings')).toContainText('Approval');

  // 11. A write now waits for approval.
  await page.goto(`${resourceUrl}?tab=tools`);
  await page.locator('#try-tool-name').selectOption('sql_query');
  await page.locator('#try-args').fill(`{ "sql": "insert into customers(name) values ('Morpheus')" }`);
  await page.getByTestId('dry-run').click();
  await expect(page.getByTestId('dry-run-result')).toHaveAttribute('data-decision', 'ApprovalRequired');
  await page.getByTestId('execute-tool').click();
  await expect(page.getByTestId('pending-approval')).toBeVisible();
  await shot(page, '13-try-tool-write-pending');

  // 12. Approve it from the Approvals inbox.
  await page.goto(pathFor('approvals'));
  await expect(page.locator('.data-table')).toContainText('Morpheus');
  await shot(page, '14-approvals-pending');
  await rowAction(page, 'Morpheus', 'Approve or reject');
  const card = page.getByTestId('approval-card');
  await expect(card).toContainText('insert into customers');
  await card.locator('input[type="text"]').fill('Approved from the live journey.');
  await page.getByTestId('approval-approve').click();
  await expect(page.locator('.toast-success').first()).toBeVisible();
  await page.locator('select[aria-label="Status"]').selectOption('Approved');
  await expect(page.locator('.data-table')).toContainText('Morpheus');
  await shot(page, '15-approvals-approved');

  // The approved write ran: the invocation is Approved and Succeeded, and the row exists.
  await page.goto(pathFor('toolInvocations'));
  await expect(page.locator('.data-table tbody tr').first()).toContainText('Approved');
  await expect(page.locator('.data-table tbody tr').first()).toContainText('Succeeded');
  await page.goto(`${resourceUrl}?tab=tools`);
  await page.locator('#try-tool-name').selectOption('sql_query');
  await page.locator('#try-args').fill(`{ "sql": "select name from customers where name = 'Morpheus'" }`);
  await page.getByTestId('dry-run').click();
  await page.getByTestId('execute-tool').click();
  await expect(page.getByTestId('execute-result')).toContainText('Morpheus');

  // 13. Knowledge: intelligence and relationships from the crawl.
  await page.goto(`${resourceUrl}?tab=knowledge`);
  await expect(page.getByTestId('knowledge-context')).toContainText('Database context');
  await page.getByRole('tab', { name: 'Intelligence' }).click();
  await expect(page.getByTestId('intelligence-entities')).toContainText('orders');
  await shot(page, '16-knowledge-intelligence');
  await page.getByRole('tab', { name: 'Relationships' }).click();
  await expect(page.getByTestId('relationship-graph')).toBeVisible();
  await expect(page.locator('.rel-edge.declared')).toHaveCount(1);
  await expect(page.locator('.rel-edge.inferred')).toHaveCount(1);
  await shot(page, '17-knowledge-relationships');
  await page.getByRole('tab', { name: 'Context' }).click();
  await page.getByTestId('knowledge-item').first().click();
  await page.locator('[data-testid="knowledge-entry"] textarea').fill('Shop database for the journey. Orders belong to customers; status is paid or open.');
  await page.getByTestId('knowledge-save').click();
  await expect(page.getByTestId('knowledge-entry')).toContainText('v1');
  await shot(page, '18-knowledge-context');

  // 14. Connector default instructions, layered (Tenant override of the SQLite instructions).
  await page.goto(pathFor('connectors', 'sqlite'));
  await page.getByRole('tab', { name: 'Default instructions' }).click();
  await expect(page.getByTestId('connector-layered-editor')).toContainText('connector.sqlite.instructions');
  await shot(page, '19-connector-instructions');
  await page.goto(pathFor('promptTemplates'));
  await expect(page.locator('.data-table')).toContainText('connector.sqlite.instructions');
  await shot(page, '20-prompt-templates');

  // 15. Request History and Audit show what happened.
  await page.goto(pathFor('requests'));
  await page.locator('#filter-method').selectOption('POST');
  await page.locator('#filter-pathContains').fill('tool-invocations');
  await page.getByRole('button', { name: 'Apply' }).click();
  await expect(page.locator('.data-table')).toContainText('/v1.0/api/tool-invocations/dry-run');
  await shot(page, '21-request-history');
  await page.goto(pathFor('audit'));
  await expect(page.locator('.data-table')).toContainText(/Approval decided|Binding changed|Resource changed/);
  await shot(page, '22-audit');
  await page.goto(pathFor('home'));
  await expect(page.getByTestId('kpi-tool-calls')).not.toContainText('-');
  await shot(page, '23-home');
  // Analytics: the analytics routes when the server has them, otherwise figures derived from tool invocations and
  // approvals (the approval decided above counts).
  await page.goto(pathFor('analytics'));
  await expect(page.locator('[data-testid="analytics-kpis"], [data-testid="analytics-derived"]').first()).toBeVisible();
  await shot(page, '24-analytics');
  await page.goto(pathFor('sessions'));
  await expect(page.locator('.data-table')).toContainText(/Bearer token|Password sign-in/);
  await shot(page, '25-sessions');

  expect(errors).toEqual([]);
});
