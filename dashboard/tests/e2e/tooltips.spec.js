// D-030 enforcement: every visible interactive or labeled element has a useful tooltip.
//
// The test signs in (mocked APIs), visits every route and tab, opens every create and edit form (every step),
// row action menus, the column picker, record modals, the setup wizard, and the tour, and fails when a visible
// button, link, input, select, textarea, table header, tab, label, or KPI card has no tooltip, has one shorter
// than 25 characters, or has one that only restates the element's visible text.

import { test, expect } from '@playwright/test';
import { installMocks, respond, OBSERVABILITY_CONFIGURED } from './mocks.js';
import { PAGES, pathFor } from '../../src/config/navConfig.js';

const SELECTOR = 'button, a[href], input:not([type="hidden"]), select, textarea, th, [role="tab"], label, .kpi-card';
const MIN_LENGTH = 25;

const stats = { checks: 0, unique: new Set(), contexts: 0 };
const failures = [];

/** Check every visible matching element on the page (including portaled modals and menus). */
async function audit(page, context) {
  const result = await page.evaluate(
    ({ selector, min }) => {
      const norm = (s) => String(s || '').toLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
      const visible = (el) => {
        if (!el.getClientRects().length) return false;
        const style = getComputedStyle(el);
        if (style.visibility === 'hidden' || style.display === 'none') return false;
        if (el.closest('[aria-hidden="true"], .tooltip')) return false;
        return true;
      };
      const out = [];
      const seen = [];
      for (const el of document.querySelectorAll(selector)) {
        if (!visible(el)) continue;
        const tipText = (el.getAttribute('data-tip') || '').trim();
        const text = (el.innerText || (el.tagName === 'SELECT' ? '' : el.value) || el.getAttribute('aria-label') || '').trim().split('\n')[0];
        const tag = el.tagName.toLowerCase() + (el.getAttribute('role') ? `[role=${el.getAttribute('role')}]` : '') + (el.type && el.tagName === 'INPUT' ? `[type=${el.type}]` : '');
        const id = `${tag} "${text.slice(0, 40)}"${el.id ? ` #${el.id}` : ''}${el.className && typeof el.className === 'string' ? ` .${el.className.split(' ')[0]}` : ''}`;
        seen.push(`${tag}|${tipText}|${text}`);
        if (!tipText) out.push(`${id}: no tooltip`);
        else if (tipText.length < min) out.push(`${id}: tooltip too short ("${tipText}")`);
        else if (text && norm(tipText) === norm(text)) out.push(`${id}: tooltip restates the label`);
        else if (text && norm(tipText).startsWith(norm(text)) && tipText.length - text.length < 15) out.push(`${id}: tooltip barely extends the label ("${tipText}")`);
      }
      return { problems: out, seen };
    },
    { selector: SELECTOR, min: MIN_LENGTH }
  );
  stats.contexts += 1;
  stats.checks += result.seen.length;
  for (const s of result.seen) stats.unique.add(s);
  for (const p of result.problems) failures.push(`[${context}] ${p}`);
}

async function settle(page) {
  await page.waitForLoadState('networkidle');
  await expect(page.locator('.loading-spinner')).toHaveCount(0);
}

async function closeModal(page) {
  const modal = page.locator('.modal').last();
  if (await modal.count()) {
    await modal.locator('.modal-header .icon-button').click();
    await expect(page.locator('.modal')).toHaveCount(0);
  }
}

/** Open the add form (every step), the first row's action menu, the edit or view form, and the column picker. */
async function auditResourcePage(page, context) {
  const add = page.getByTestId('add-button').first();
  if (await add.isVisible().catch(() => false)) {
    await add.click();
    await expect(page.locator('.modal')).toBeVisible();
    const steps = page.locator('.modal .stepper-step');
    const stepCount = await steps.count();
    if (stepCount === 0) await audit(page, `${context} create form`);
    for (let i = 0; i < stepCount; i++) {
      await steps.nth(i).click();
      // Fill every list editor on the step so the per-item fields render.
      const addButtons = page.locator('.modal .object-list > button, .modal .kv-editor > button');
      for (let j = 0; j < (await addButtons.count()); j++) await addButtons.nth(j).click();
      await audit(page, `${context} create form step ${i + 1}`);
    }
    await closeModal(page);
  }
  const trigger = page.locator('.data-table .action-menu-trigger').first();
  if (await trigger.isVisible().catch(() => false)) {
    await trigger.click();
    await expect(page.locator('.action-menu-dropdown')).toBeVisible();
    await audit(page, `${context} row actions`);
    const edit = page.locator('.action-menu-dropdown [role="menuitem"]', { hasText: /^(Edit|View)$/ }).first();
    if (await edit.count()) {
      await edit.click();
      const modal = page.locator('.modal');
      if (await modal.isVisible().catch(() => false)) {
        const steps = page.locator('.modal .stepper-step');
        const stepCount = await steps.count();
        if (stepCount === 0) await audit(page, `${context} edit or view form`);
        for (let i = 0; i < stepCount; i++) {
          await steps.nth(i).click();
          await audit(page, `${context} edit form step ${i + 1}`);
        }
        await closeModal(page);
      } else {
        await settle(page);
        await audit(page, `${context} detail page`);
        await page.goBack();
        await settle(page);
      }
    } else {
      await page.mouse.click(5, 5);
    }
  }
  const picker = page.getByTestId('column-picker').first();
  if (await picker.isVisible().catch(() => false)) {
    await picker.click();
    await expect(page.locator('.column-picker')).toBeVisible();
    await audit(page, `${context} column picker`);
    await page.keyboard.press('Escape');
  }
}

async function clickTabs(page, selector, context) {
  const tabs = page.locator(selector);
  const n = await tabs.count();
  for (let i = 0; i < n; i++) {
    await tabs.nth(i).click();
    await settle(page);
    await audit(page, `${context} tab ${i + 1}`);
    if (await page.getByTestId('add-button').first().isVisible().catch(() => false)) await auditResourcePage(page, `${context} tab ${i + 1}`);
  }
}

test.use({ actionTimeout: 15000 });

test.beforeEach(async ({ page }) => {
  await installMocks(page);
  // Configure every observability service so the audit also covers the Home cards and the Grafana deep links (P13.15).
  await page.route(/\/v1\.0\/api\/server-info$/, (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ ...respond('GET', new URL(route.request().url())), observability: OBSERVABILITY_CONFIGURED }) })
  );
  await page.route(/\/v1\.0\/api\/observability\/status$/, (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(['grafana', 'prometheus', 'loki', 'tempo'].map((service, i) => ({ service, configured: true, reachable: i !== 2, version: i === 2 ? null : '1.0.0', checkedUtc: '2026-10-09T12:00:00Z' }))) })
  );
});

test('every interactive and labeled element has a useful tooltip', async ({ page }) => {
  test.setTimeout(600000);
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto('/');
  await audit(page, 'login, password tab');
  await page.getByRole('tab', { name: 'Access key' }).click();
  await audit(page, 'login, access key tab');
  await page.getByRole('tab', { name: 'Email and password' }).click();
  await page.locator('#tenantId').fill('default');
  await page.locator('#email').fill('admin@garrison');
  await page.locator('#password').fill('password');
  await page.getByTestId('login-submit').click();
  await expect(page.locator('.dashboard')).toBeVisible();

  for (const p of PAGES) {
    await page.goto(pathFor(p.id));
    await settle(page);
    const context = `${p.hub}${p.tab ? `/${p.tab}` : ''}`;
    await audit(page, context);
    if (p.id === 'roles' || p.id === 'assignments' || p.id === 'manifests') await clickTabs(page, '.hub-tab-panel [role="tablist"]:not(.hub-tab-strip) [role="tab"]', context);
    else await auditResourcePage(page, context);
  }

  // Explainers collapsed to their reopen buttons.
  await page.goto(pathFor('agents'));
  await page.getByTestId('explainer-dismiss-agents').click();
  await audit(page, 'agents with explainer dismissed');
  await page.getByTestId('explainer-open-agents').click();

  // Detail pages and their tabs.
  for (const [path, context] of [
    ['/dashboard/runs/run_0001', 'run detail'],
    ['/dashboard/agents/agt_0001', 'agent detail'],
    ['/dashboard/conversations/cnv_0001', 'conversation detail'],
    ['/dashboard/conversations/cnv_0002', 'busy conversation detail'],
    ['/dashboard/runs/run_0042', 'conversation turn run detail'],
    ['/dashboard/invoke?agentId=agt_0001&conversationId=cnv_0001', 'invoke continuing a conversation'],
    ['/dashboard/prompt-templates/pt_0004', 'template editor'],
    ['/dashboard/prompt-templates/new', 'new template'],
    // P12.9: a pipeline run awaiting review (review panel) and a failed one (Retry, Delete), and a stage run.
    ['/dashboard/pipeline-runs/plr_0001', 'pipeline run awaiting review'],
    ['/dashboard/pipeline-runs/plr_0003', 'failed pipeline run']
  ]) {
    await page.goto(path);
    await settle(page);
    await audit(page, context);
    await clickTabs(page, '.dashboard-content [role="tablist"]:not(.hub-tab-strip):not(.range-group) [role="tab"]', context);
  }
  await page.goto('/dashboard/prompt-templates/pt_0004');
  await settle(page);
  await page.getByRole('tab', { name: /Versions/ }).click();
  await page.getByRole('button', { name: 'Diff' }).first().click();
  await audit(page, 'template diff modal');
  await closeModal(page);

  // Analytics: grouped, custom range, and an opened data table.
  await page.goto('/dashboard/analytics?groupBy=agent');
  await settle(page);
  await audit(page, 'analytics grouped');
  await page.goto('/dashboard/analytics?groupBy=harnessModel&model=gpt-oss%3A20b');
  await settle(page);
  await audit(page, 'analytics by harness and model');
  await page.getByTestId('export-ttft').click();
  await audit(page, 'analytics export menu');
  await page.keyboard.press('Escape');
  await page.goto('/dashboard/analytics?range=custom&from=2026-10-08T00%3A00%3A00.000Z&to=2026-10-09T00%3A00%3A00.000Z');
  await settle(page);
  await page.locator('[data-testid="analytics-chart-outcomes"] summary').click();
  await audit(page, 'analytics custom range with data table');

  // Record modals opened from row actions.
  const modals = [
    ['agents?tab=deployments', 'Compile preview', 'compile preview'],
    ['configuration?tab=mcp-servers', 'Test connection', 'MCP test'],
    ['access?tab=users', 'Effective permissions', 'effective permissions'],
    ['configuration?tab=secrets', 'Set value', 'secret value'],
    ['configuration?tab=secrets', 'View JSON', 'JSON viewer'],
    // P12.9: start and preview a pipeline, and decide a stage review.
    ['agents?tab=pipelines', 'Start', 'pipeline start'],
    ['agents?tab=pipelines', 'Preview prompts', 'pipeline preview'],
    ['approvals?tab=pipeline-reviews', 'Request changes', 'stage review decide']
  ];
  for (const [path, item, context] of modals) {
    await page.goto(`/dashboard/${path}`);
    await settle(page);
    await page.locator('.data-table .action-menu-trigger').first().click();
    await page.locator('.action-menu-dropdown [role="menuitem"]', { hasText: item }).click();
    await expect(page.locator('.modal')).toBeVisible();
    await settle(page);
    await audit(page, context);
    await closeModal(page);
  }
  // The pipeline preview with rendered stage prompts and a render failure.
  await page.goto('/dashboard/agents?tab=pipelines');
  await settle(page);
  await page.locator('.data-table .action-menu-trigger').first().click();
  await page.locator('.action-menu-dropdown [role="menuitem"]', { hasText: 'Preview prompts' }).click();
  await page.getByTestId('pipeline-preview-render').click();
  await expect(page.getByTestId('pipeline-preview-result')).toBeVisible();
  await audit(page, 'pipeline preview rendered');
  await closeModal(page);

  await page.goto('/dashboard/configuration?tab=secrets');
  await settle(page);
  await page.locator('.data-table .action-menu-trigger').first().click();
  await page.locator('.action-menu-dropdown [role="menuitem"]', { hasText: 'Delete' }).click();
  await audit(page, 'delete confirmation');
  await page.locator('.modal').getByRole('button', { name: 'Cancel' }).click();

  await page.goto('/dashboard/monitoring?tab=requests');
  await settle(page);
  await page.locator('.data-table tbody tr').first().click();
  await expect(page.locator('.modal')).toContainText('Request details');
  await settle(page);
  await audit(page, 'request details');
  await closeModal(page);

  await page.goto('/dashboard/monitoring?tab=explorer');
  await settle(page);
  const select = page.locator('.explorer-op-select');
  await select.selectOption(await select.locator('option', { hasText: 'Create agent' }).getAttribute('value'));
  await audit(page, 'API Explorer with an operation');
  await page.getByTestId('explorer-execute').click();
  await audit(page, 'API Explorer with a response');

  await page.goto('/dashboard/invoke');
  await settle(page);
  await page.locator('#invoke-message').fill('hello');
  await page.getByTestId('invoke-send').click();
  await expect(page.locator('.invoke-run-head')).toBeVisible();
  await audit(page, 'invoke with a run');
  // P5.52: the streamed Preview (Markdown links, tool chips, reasoning, timing) and the Response tab.
  await expect(page.getByTestId('invoke-preview').locator('.markdown-body a').first()).toBeVisible();
  await page.getByTestId('stream-reasoning').locator('summary').click();
  await audit(page, 'invoke preview streamed');
  await page.getByRole('tab', { name: 'Response' }).click();
  await expect(page.getByTestId('invoke-response')).toBeVisible();
  await audit(page, 'invoke response');
  await page.getByTestId('invoke-result').getByTestId('markdown-raw-toggle').click();
  await audit(page, 'invoke response raw');

  // Setup wizard, every step.
  await page.goto('/dashboard/home');
  await settle(page);
  await page.locator('.sidebar-utility').first().click();
  await expect(page.locator('.wizard-steps')).toBeVisible();
  await audit(page, 'wizard welcome');
  await page.getByRole('button', { name: 'Start' }).click();
  await audit(page, 'wizard credential');
  await page.getByRole('button', { name: 'Skip this step' }).click();
  await audit(page, 'wizard manifest');
  await page.locator('.modal-footer').getByRole('button', { name: 'Apply' }).click();
  await expect(page.locator('.wizard-steps li.active')).toContainText('Test run');
  await audit(page, 'wizard test run');
  await closeModal(page);

  // Guided tour, every stop.
  await page.locator('.sidebar-utility').nth(1).click();
  for (let i = 0; i < 6; i++) {
    await audit(page, `tour step ${i + 1}`);
    const next = page.locator('.tour-card').getByRole('button', { name: /^(Next|Done)$/ });
    await next.click();
  }

  console.log(`Tooltip audit: ${stats.checks} element checks across ${stats.contexts} screens (${stats.unique.size} distinct elements).`);
  expect([...new Set(failures)]).toEqual([]);
  expect(stats.unique.size).toBeGreaterThan(400);
});

test('tooltips show on hover and keyboard focus, are described, portaled, and dismissible', async ({ page }) => {
  await page.goto('/');
  const submit = page.getByTestId('login-submit');
  await submit.hover();
  const bubble = page.locator('#garrison-tooltip');
  await expect(bubble).toHaveClass(/is-visible/);
  await expect(bubble).toHaveText(/Check the credentials/);
  await expect(submit).toHaveAttribute('aria-describedby', /garrison-tooltip/);
  expect(await bubble.evaluate((el) => el.parentElement === document.body)).toBe(true);
  const box = await bubble.boundingBox();
  const vp = page.viewportSize();
  expect(box.x).toBeGreaterThanOrEqual(0);
  expect(box.x + box.width).toBeLessThanOrEqual(vp.width);
  await page.keyboard.press('Escape');
  await expect(bubble).not.toHaveClass(/is-visible/);

  await page.mouse.move(0, 0);
  await page.locator('#serverUrl').focus();
  await page.keyboard.press('Tab');
  await expect(bubble).toHaveClass(/is-visible/);
  await expect(page.locator(':focus')).toHaveAttribute('aria-describedby', /garrison-tooltip/);

  // Dark theme and RTL: the bubble still renders inside the viewport.
  await page.locator('.login-lang .icon-button').click();
  await page.locator('.login-lang select').selectOption('zz');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await page.getByTestId('login-submit').hover();
  await expect(bubble).toHaveClass(/is-visible/);
  const rtlBox = await bubble.boundingBox();
  expect(rtlBox.x).toBeGreaterThanOrEqual(0);
  expect(rtlBox.x + rtlBox.width).toBeLessThanOrEqual(vp.width);
});
