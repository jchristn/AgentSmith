// Coverage for run telemetry (P5.43), relationship explainers (P5.45), analytics (P5.46), and chart and table
// export (P5.50) against mocked APIs.

import fs from 'fs';
import { test, expect } from '@playwright/test';
import { installMocks } from './mocks.js';
import { pathFor } from '../../src/config/navConfig.js';

async function login(page) {
  await page.goto('/');
  await page.locator('#tenantId').fill('default');
  await page.locator('#email').fill('admin@garrison');
  await page.locator('#password').fill('password');
  await page.getByTestId('login-submit').click();
  await expect(page.locator('.dashboard')).toBeVisible();
}

test.use({ actionTimeout: 15000, acceptDownloads: true });

test.beforeEach(async ({ page }) => {
  await installMocks(page);
});

// ---------------------------------------------------------------- P5.43 run telemetry

test('runs table shows telemetry columns with defaults and the column picker', async ({ page }) => {
  await login(page);
  await page.goto('/dashboard/runs');
  const headers = page.locator('.data-table thead th');
  await expect(headers.filter({ hasText: 'TTFT' })).toHaveCount(1);
  await expect(headers.filter({ hasText: 'Duration' })).toHaveCount(1);
  await expect(headers.filter({ hasText: 'Input tokens' })).toHaveCount(1);
  await expect(headers.filter({ hasText: 'Streaming' })).toHaveCount(0);
  await expect(page.locator('.data-table tbody tr').first()).toContainText('1.21 s');

  await page.getByTestId('column-picker').click();
  for (const label of ['Streaming', 'Queue time', 'Tokens/s', 'Tool calls', 'Cached tokens', 'Reasoning tokens']) {
    await page.locator('.column-picker-item', { hasText: label }).locator('input').check();
  }
  await page.keyboard.press('Escape');
  await expect(headers.filter({ hasText: 'Streaming' })).toHaveCount(1);
  // run_0003 has no measurable streaming time, so it shows the translated placeholder.
  const row3 = page.locator('.data-table tbody tr', { hasText: 'run_0003' });
  await expect(row3).toContainText('n/a');
  await expect(headers.filter({ hasText: 'TTFT' })).toHaveAttribute('data-tip', /harness process/);
});

test('run detail shows every telemetry field with an explanation', async ({ page }) => {
  await login(page);
  await page.goto('/dashboard/runs/run_0001');
  const panel = page.getByTestId('run-telemetry');
  await expect(panel).toBeVisible();
  for (const key of ['timeToFirstTokenMs', 'timeToFirstTokenExcludingDelegationMs', 'delegationWaitMs', 'streamingMs', 'executionMs', 'queueMs', 'totalMs', 'outputTokensPerSecond', 'inputTokens', 'outputTokens', 'cachedTokens', 'reasoningTokens', 'toolCallCount', 'costUsd', 'firstTokenUtc', 'lastTokenUtc']) {
    const item = panel.locator(`[data-telemetry="${key}"]`);
    await expect(item, key).toBeVisible();
    expect((await item.locator('.detail-item-label').getAttribute('data-tip')).length, key).toBeGreaterThan(40);
  }
  await expect(panel.locator('[data-telemetry="timeToFirstTokenMs"] .detail-item-value')).toHaveText('1.21 s');
  // run_0001 waited on a delegated run before its first token, so the bar has a delegation phase too (M1-08).
  await expect(panel.locator('svg.telemetry-phases rect')).toHaveCount(5);
});

// ---------------------------------------------------------------- P5.45 explainers

test('explainers can be dismissed and reopened, and the choice persists', async ({ page }) => {
  await login(page);
  await page.goto('/dashboard/agents');
  await expect(page.getByTestId('explainer-agents')).toContainText('An agent is the identity callers use');
  await expect(page.getByTestId('relation-diagram')).toBeVisible();
  await page.getByTestId('explainer-dismiss-agents').click();
  await expect(page.getByTestId('explainer-agents')).toHaveCount(0);
  await page.reload();
  await expect(page.getByTestId('explainer-open-agents')).toBeVisible();
  await page.getByTestId('explainer-open-agents').click();
  await expect(page.getByTestId('explainer-agents')).toBeVisible();
  for (const [path, topic] of [
    ['/dashboard/agents?tab=deployments', 'deployments'],
    ['/dashboard/configuration', 'harnessProfiles'],
    ['/dashboard/configuration?tab=prompt-templates', 'promptTemplates'],
    ['/dashboard/configuration?tab=secrets', 'secrets'],
    ['/dashboard/configuration?tab=mcp-servers', 'mcpServers'],
    ['/dashboard/configuration?tab=workspaces', 'workspaces'],
    ['/dashboard/configuration?tab=manifests', 'manifests'],
    ['/dashboard/fleet', 'nodes'],
    ['/dashboard/fleet?tab=node-pools', 'nodePools'],
    ['/dashboard/access?tab=users', 'users'],
    ['/dashboard/access?tab=credentials', 'credentials'],
    ['/dashboard/access?tab=roles', 'roles'],
    ['/dashboard/access?tab=assignments', 'assignments'],
    ['/dashboard/runs', 'runs'],
    ['/dashboard/runs?tab=queue', 'queue']
  ]) {
    await page.goto(path);
    await expect(page.getByTestId(`explainer-${topic}`), topic).toBeVisible();
  }
});

test('agent detail lists its deployments with weights and traffic shares', async ({ page }) => {
  await login(page);
  await page.goto('/dashboard/agents/agt_0001');
  const table = page.getByTestId('agent-deployments');
  await expect(table.locator('tbody tr')).toHaveCount(2);
  await expect(table.locator('tr[data-deployment="deployment-1"] [data-cell="weight"]')).toHaveText('300');
  await expect(table.locator('tr[data-deployment="deployment-1"] [data-cell="share"]')).toContainText('75%');
  await expect(table.locator('tr[data-deployment="deployment-2"] [data-cell="share"]')).toContainText('25%');
  await expect(page.getByTestId('explainer-agentDetail')).toBeVisible();
});

// ---------------------------------------------------------------- P5.46 analytics

test('analytics renders totals, every chart, and the harness by model breakdown', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await login(page);
  await page.locator('.sidebar-item[data-nav="analytics"]').click();
  await expect(page).toHaveURL(/\/dashboard\/analytics$/);
  await expect(page.getByTestId('analytics-card-runs').locator('.kpi-value')).not.toHaveText('');
  for (const id of ['outcomes', 'ttft', 'duration', 'queue', 'tokens', 'throughput', 'cost', 'toolCalls', 'usageTokens', 'usageCost']) {
    await expect(page.getByTestId(`analytics-chart-${id}`).locator('svg[role="img"]'), id).toBeVisible();
  }
  const svg = await page.getByTestId('analytics-chart-ttft').locator('svg[role="img"]').boundingBox();
  const content = await page.locator('.dashboard-content').boundingBox();
  expect(svg.width).toBeGreaterThan(content.width * 0.85);
  expect(svg.height).toBeGreaterThanOrEqual(380);
  await expect(page.getByTestId('analytics-breakdown').locator('tbody tr')).toHaveCount(3);
  await expect(page.getByTestId('analytics-breakdown')).toContainText('Unknown model');

  // Hover shows every series value for the bucket.
  await page.getByTestId('analytics-chart-ttft').locator('rect[data-bucket="3"]').hover();
  await expect(page.getByTestId('chart-tooltip')).toContainText('Median (p50)');
});

test('analytics filters, group by, and custom range are synced to the URL', async ({ page }) => {
  await login(page);
  await page.goto('/dashboard/analytics');
  await expect(page.locator('#analytics-model option')).toHaveCount(4);
  const agentRequest = page.waitForRequest((r) => r.url().includes('/analytics/runs?') && r.url().includes('agentId=agt_0001'));
  await page.locator('#analytics-agentId').selectOption('agt_0001');
  await agentRequest;
  await expect(page).toHaveURL(/agentId=agt_0001/);
  await page.locator('#analytics-model').selectOption('gpt-oss:20b');
  await expect(page).toHaveURL(/model=gpt-oss%3A20b/);
  await page.locator('#analytics-groupBy').selectOption('harness');
  await expect(page).toHaveURL(/groupBy=harness/);
  await expect(page.getByTestId('analytics-chart-ttft').locator('.chart-legend-toggle')).toHaveCount(2);
  // Harness legends show display names, never enum names (M1-24).
  await expect(page.getByTestId('analytics-chart-ttft').locator('.chart-legend')).toContainText('Claude Code');
  await expect(page.getByTestId('analytics-chart-ttft').locator('.chart-legend')).not.toContainText('ClaudeCode');
  await page.getByTestId('analytics-chart-ttft').locator('.chart-legend-toggle').first().click();
  await expect(page.getByTestId('analytics-chart-ttft').locator('.chart-legend-toggle').first()).toHaveAttribute('aria-pressed', 'false');
  await page.getByRole('button', { name: 'Custom', exact: true }).click();
  await expect(page).toHaveURL(/range=custom&from=.*&to=/);
  await expect(page.locator('#analytics-from')).toBeVisible();
  await page.reload();
  await expect(page.locator('#analytics-agentId')).toHaveValue('agt_0001');
  await expect(page.locator('#analytics-groupBy')).toHaveValue('harness');
  await expect(page.locator('#analytics-from')).toBeVisible();
});

test('analytics handles truncated, empty, error, and unavailable responses', async ({ page }) => {
  await login(page);
  const base = (over) => ({ fromUtc: '2026-10-08T12:00:00Z', toUtc: '2026-10-09T12:00:00Z', interval: 'hour', bucketSeconds: 3600, truncated: false, totals: { runs: 0 }, series: [{ key: 'all', label: 'All runs', buckets: [] }], ...over });
  let mode = 'truncated';
  await page.route(/\/analytics\/runs\?/, (route) => {
    if (mode === 'error') return route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ code: 'InternalError', message: 'boom' }) });
    if (mode === 'missing') return route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ code: 'RouteNotFound', message: 'No route' }) });
    if (mode === 'empty') return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(base()) });
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(base({ truncated: true, totals: { runs: 5, succeeded: 5 }, series: [{ key: 'all', label: 'All runs', buckets: [{ startUtc: '2026-10-08T12:00:00Z', runs: 5, succeeded: 5 }] }] })) });
  });
  await page.goto('/dashboard/analytics');
  await expect(page.getByTestId('analytics-truncated')).toBeVisible();
  mode = 'empty';
  await page.goto('/dashboard/analytics?range=week');
  await expect(page.getByTestId('analytics-empty')).toBeVisible();
  mode = 'error';
  await page.goto('/dashboard/analytics?range=hour');
  await expect(page.locator('.error-banner')).toBeVisible();
  mode = 'missing';
  await page.goto('/dashboard/analytics?range=month');
  await expect(page.getByText('Analytics is not available on this server yet')).toBeVisible();
});

// ---------------------------------------------------------------- P5.50 chart and table export

async function exportAs(page, triggerTestId, format) {
  const download = page.waitForEvent('download');
  await page.getByTestId(triggerTestId).click();
  await page.locator(`.action-menu-dropdown [data-export="${format}"]`).click();
  const d = await download;
  const path = await d.path();
  const body = fs.readFileSync(path);
  return { name: d.suggestedFilename(), body };
}

test('analytics charts export CSV, PNG, SVG, and JSON with descriptive names', async ({ page }) => {
  await login(page);
  await page.goto('/dashboard/analytics?range=week&agentId=agt_0001&groupBy=none');
  await expect(page.getByTestId('analytics-chart-ttft').locator('svg[role="img"]')).toBeVisible();

  const csv = await exportAs(page, 'export-ttft', 'csv');
  expect(csv.name).toBe('garrison_analytics-ttft_week_agentid-agt_0001.csv');
  const lines = csv.body.toString('utf8').trim().split('\r\n');
  expect(lines[0]).toBe('bucket_start_utc,Average,Median (p50),p95,Excluding delegation (avg)');
  expect(lines.length).toBe(25);
  expect(lines[1]).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z,\d+(\.\d+)?,/);

  const json = await exportAs(page, 'export-ttft', 'json');
  expect(json.name).toBe('garrison_analytics-ttft_week_agentid-agt_0001.json');
  const doc = JSON.parse(json.body.toString('utf8'));
  expect(doc.buckets).toHaveLength(24);
  expect(doc.filters.agentId).toBe('agt_0001');

  const svg = await exportAs(page, 'export-ttft', 'svg');
  expect(svg.name).toMatch(/\.svg$/);
  const svgText = svg.body.toString('utf8');
  expect(svgText).toContain('Time to first token');
  expect(svgText).toContain('Agent: echo-agent');
  expect(svgText).toContain('Median (p50)');

  const png = await exportAs(page, 'export-ttft', 'png');
  expect(png.name).toMatch(/\.png$/);
  expect(png.body.subarray(1, 4).toString('ascii')).toBe('PNG');
  expect(png.body.readUInt32BE(16)).toBeGreaterThan(1500);

  const breakdown = await exportAs(page, 'export-breakdown', 'csv');
  expect(breakdown.name).toBe('garrison_analytics-harness-model-breakdown_week_agentid-agt_0001.csv');
  expect(breakdown.body.toString('utf8').split('\r\n')[0]).toBe('harness,model,runs,input_tokens,output_tokens,cached_tokens,reasoning_tokens,cost_usd,avg_time_to_first_token_ms');
});

test('home, request history, run detail, and tables export their data', async ({ page }) => {
  await login(page);
  const home = await exportAs(page, 'export-api-request-activity', 'csv');
  expect(home.name).toMatch(/^garrison_api-request-activity_day.*\.csv$/);
  expect(home.body.toString('utf8').split('\r\n')[0]).toBe('bucket_start_utc,bucket_end_utc,success,failure,total,average_duration_ms');
  const homePng = await exportAs(page, 'export-api-request-activity', 'png');
  expect(homePng.body.length).toBeGreaterThan(1000);

  await page.goto('/dashboard/monitoring?tab=requests');
  const requests = await exportAs(page, 'export-request-history-activity', 'csv');
  expect(requests.name).toMatch(/^garrison_request-history-activity_day/);
  expect(requests.body.toString('utf8').split('\r\n')[0]).toContain('bucket_start_utc');

  await page.goto('/dashboard/runs/run_0001');
  const phases = await exportAs(page, 'export-run-phases', 'csv');
  expect(phases.name).toBe('garrison_run-phases_run_0001_agent-echo-agent.csv');
  expect(phases.body.toString('utf8').split('\r\n')[0]).toBe('phase,duration_ms,share');
  const phasesSvg = await exportAs(page, 'export-run-phases', 'svg');
  expect(phasesSvg.body.toString('utf8')).toContain('<svg');
  const transcript = await exportAs(page, 'export-transcript', 'csv');
  expect(transcript.name).toBe('garrison_run-transcript_run_0001.csv');
  expect(transcript.body.toString('utf8').split('\r\n')[0]).toBe('sequence,timestamp_utc,event_type,tool_name,success,input_tokens,output_tokens,text');

  await page.goto('/dashboard/runs');
  const table = await exportAs(page, 'export-table-runs', 'csv');
  expect(table.name).toBe('garrison_runs_page-1.csv');
  const header = table.body.toString('utf8').split('\r\n')[0];
  expect(header).toContain('Run,Agent,Status');
  expect(header).toContain('TTFT');
  expect(table.body.toString('utf8').split('\r\n')[1]).toMatch(/^run_0001,echo-agent,/);
});

// ---------------------------------------------------------------- G-15 blocked runs

test('blocked queued runs show a warning badge, an explanation, and a Home banner', async ({ page }) => {
  await login(page);

  // Home: overview.blockedRuns drives a banner that links to the queued runs.
  const banner = page.getByTestId('blocked-banner');
  await expect(banner).toContainText('2 queued runs cannot start');
  expect((await banner.getAttribute('data-tip')).length).toBeGreaterThan(40);
  await banner.getByRole('button', { name: 'Show queued runs' }).click();
  await expect(page).toHaveURL(/\/dashboard\/runs\?status=Queued$/);

  // Runs table: run_0003 is queued with blockedReason NoCapableWorker.
  await page.goto('/dashboard/runs');
  const row = page.locator('.data-table tbody tr', { hasText: 'run_0003' });
  const badge = row.locator('.blocked-badge .pill');
  await expect(badge).toHaveText(/No capable worker/);
  await expect(badge).toHaveAttribute('data-tip', /Fleet page/);
  await expect(page.locator('.data-table tbody tr', { hasText: 'run_0009' }).locator('.blocked-badge')).toHaveText(/No ready worker/);
  await expect(page.locator('.data-table tbody tr', { hasText: 'run_0001' }).locator('.blocked-badge')).toHaveCount(0);

  // Run detail: the notice explains the reason and links to the Fleet page.
  await page.goto('/dashboard/runs/run_0003');
  const notice = page.getByTestId('blocked-notice');
  await expect(notice).toContainText('Waiting for a worker');
  await expect(notice).toContainText('none reports the Claude Code harness as available');
  await expect(notice).toContainText('Runtime.FailBlockedAfterSeconds');
  await notice.getByRole('button', { name: 'Open Fleet' }).click();
  await expect(page).toHaveURL(/\/dashboard\/fleet$/);

  // Queue tab: blocked counts per agent and harness.
  await page.goto('/dashboard/queue');
  await expect(page.getByTestId('blocked-banner')).toBeVisible();
  await expect(page.locator('[data-cell="blocked"]').first()).toContainText('No capable worker');

  // A run that failed because it stayed blocked shows the new outcome label.
  await page.goto('/dashboard/runs/run_0007');
  await expect(page.locator('.run-summary')).toContainText('No capable worker');
  await expect(page.getByTestId('blocked-notice')).toHaveCount(0);
});

// ---------------------------------------------------------------- G-16 enum labels

test('detail pages, modals, and forms show translated enum labels, never raw values', async ({ page }) => {
  const { ENUMS } = await import('../../src/utils/constants.js');
  await login(page);
  await page.locator('.topbar .topbar-lang select').selectOption('zz');
  // In the pseudo-locale every label is wrapped in brackets, so a raw enum value stands out as bare text.
  const invariant = new Set(['A2A', 'S3', 'Stdout', 'Stderr']);
  const raw = new Set(Object.values(ENUMS).flat().filter((v) => !invariant.has(v)));
  const scan = async (where) => {
    const texts = await page.evaluate(() =>
      [...document.querySelectorAll('.pill, td, option, .detail-item-value, .checkbox-chip span, .inline-meta, .plain-list li span')]
        .filter((el) => el.getClientRects().length)
        .flatMap((el) => el.innerText.split(/\n|, | \/ /))
        .map((s) => s.replace(/^[\s⚠]+|\s+$/g, ''))
    );
    return texts.filter((s) => raw.has(s)).map((s) => `${where}: ${s}`);
  };
  const problems = [];
  for (const path of [pathFor('home'), pathFor('runs', 'run_0003'), pathFor('agents', 'agt_0002'), pathFor('queue'), pathFor('serverInfo'), pathFor('audit'), pathFor('harnessProfiles'), pathFor('nodes')]) {
    await page.goto(path);
    await page.waitForLoadState('networkidle');
    problems.push(...(await scan(path)));
  }
  await page.goto(pathFor('roles'));
  await page.locator('[data-tab-key="permissions"]').click();
  await page.waitForLoadState('networkidle');
  problems.push(...(await scan('permissions')));
  // Forms: the agent form (Delegation) and the deployment Resources step (binding Kind) from the G-16 report.
  await page.goto('/dashboard/agents');
  await page.getByTestId('add-button').first().click();
  problems.push(...(await scan('agent form')));
  await page.locator('.modal .modal-header .icon-button').click();
  await page.goto('/dashboard/deployments');
  await page.getByTestId('add-button').first().click();
  const steps = page.locator('.modal .stepper-step');
  for (let i = 0; i < (await steps.count()); i++) {
    await steps.nth(i).click();
    const addButtons = page.locator('.modal .object-list > button');
    for (let j = 0; j < (await addButtons.count()); j++) await addButtons.nth(j).click();
    problems.push(...(await scan(`deployment form step ${i}`)));
    if (i === 3) await expect(page.locator('.modal select[id$="-kind"]').first().locator('option[value="McpServer"]')).toHaveText(/^⟦/);
  }
  expect(problems).toEqual([]);
});
