// Capture the real primary journey. Private model URLs are loaded from temporary state and masked in the DOM.
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from '../dashboard/node_modules/@playwright/test/index.mjs';
const [stateFile, phase = 'setup'] = process.argv.slice(2);
const state = JSON.parse(fs.readFileSync(stateFile, 'utf8'));
const out = path.resolve('user-testing/evidence/2026-10-16-primary-real');
fs.mkdirSync(out, { recursive: true });
const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 1440, height: 1050 } });
const base = `http://127.0.0.1:${state.ports.dashboard}`;
async function shot(name) {
  await page.waitForTimeout(500);
  await page.evaluate(({ endpoint, host }) => {
    const scrub = (s) => s.replaceAll(endpoint, '[private model endpoint]').replaceAll(host, '[private model host]').replace(/\u2014/g, ' - ').replace(/(?:access|secret)_[A-Za-z0-9_]+/g, '[redacted test credential]');
    for (const table of document.querySelectorAll('table')) {
      const index = [...table.querySelectorAll('thead th')].findIndex((cell) => cell.textContent.trim().toLowerCase() === 'secret');
      if (index >= 0) for (const row of table.querySelectorAll('tbody tr')) if (row.children[index]) row.children[index].textContent = '[redacted]';
    }
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    while (walker.nextNode()) walker.currentNode.textContent = scrub(walker.currentNode.textContent);
    for (const field of document.querySelectorAll('input, textarea')) if (field.value) field.value = scrub(field.value);
  }, { endpoint: state.model_url, host: new URL(state.model_url).hostname });
  await page.screenshot({ path: path.join(out, `${name}.png`), fullPage: true });
}
await page.goto(base);
await page.locator('#email').fill('anderson@hugo.local');
await page.getByTestId('tenant-found').waitFor();
await page.locator('#password').fill('password');
await page.getByTestId('login-submit').click();
await page.locator('.dashboard').waitFor();
const password = page.getByTestId('password-change');
if (await password.isVisible()) {
  if (phase === 'setup') await shot('01-advisory-password-prompt');
  await page.getByTestId('password-change-skip').click();
}
const wizard = page.getByTestId('setup-wizard');
if (await wizard.isVisible()) await wizard.locator('.modal-close').click();
if (phase === 'setup') {
  await page.goto(base + '/dashboard/secrets');
  await page.locator('.data-table').waitFor();
  await shot('02-four-secret-slots');
  await page.goto(base + '/dashboard/resources');
  await page.locator('.data-table').waitFor();
  await shot('03-four-resources');
  await page.goto(base + `/dashboard/profiles/${state.profile}?tab=bindings`);
  await page.getByTestId('profile-bindings').waitFor();
  await shot('04-four-profile-bindings');
  await page.getByRole('tab', { name: 'Instructions', exact: true }).click();
  await page.getByTestId('compiled-text').waitFor();
  await shot('05-compiled-persona-and-resource-sections');
}
if (phase === 'approve' || phase === 'reject') {
  await page.goto(base + '/dashboard/approvals');
  const row = page.locator('.data-table tbody tr', { hasText: /nginx/ }).first();
  await row.waitFor();
  await row.locator('[aria-haspopup="menu"]').first().click();
  await page.getByRole('menuitem', { name: 'Approve or reject' }).click();
  const card = page.getByTestId('approval-card');
  await card.waitFor();
  await shot(phase === 'approve' ? '07-destructive-nginx-pending-approval' : '09-second-nginx-pending-rejection');
  await card.locator('input[type="text"]').fill(phase === 'approve' ? 'Approved isolated nginx restart for real-model acceptance.' : 'Rejected second restart; leave the service unchanged.');
  await page.getByTestId(`approval-${phase}`).click();
  await page.waitForTimeout(500);
  await shot(phase === 'approve' ? '08-nginx-approved' : '10-nginx-rejected');
}
if (phase === 'afterfix') {
  await page.goto(base + `/dashboard/chat/${state.conversation}`);
  await page.getByTestId('chat-input').waitFor();
  const context = page.getByTestId('chat-drawer');
  await context.waitFor();
  const text = await context.innerText();
  if (!text.includes('136,452') || !text.includes('1,642') || text.includes('272,904')) throw new Error('Corrected persisted token totals were not rendered');
  await shot('17-token-count-after-fix');
  console.log('After-fix token totals verified: 136452 input / 1642 output');
}
if (phase === 'chat' || phase === 'final') {
  await page.goto(base + `/dashboard/chat/${state.conversation}`);
  await page.getByTestId('chat-input').waitFor();
  await shot(phase === 'chat' ? '06-real-model-investigation-with-tool-calls' : '11-real-model-final-chat');
}
if (phase === 'credentials') {
  await page.goto(base + '/dashboard/credentials');
  await page.locator('.data-table').waitFor();
  await shot('15-profile-locked-credential');
}
if (phase === 'final') {
  for (const [slug, name] of [['tool-invocations', '12-invocations-effects-and-durations'], ['requests', '13-request-history'], ['audit', '14-audit'], ['credentials', '15-profile-locked-credential']]) {
    await page.goto(base + '/dashboard/' + slug);
    await page.locator('.data-table').waitFor();
    await shot(name);
  }
}
await browser.close();
console.log(`Captured sanitized ${phase} screenshots`);
