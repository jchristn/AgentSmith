import { test, expect } from '@playwright/test';
import { installMocks, login } from './mocks.js';
import { pathFor } from '../../src/config/navConfig.js';

async function runningConversation(page) {
  const state = { running: true, cancelled: false };
  await installMocks(page);
  await page.route('**/v1.0/api/conversations/cnv_0001', (route) => route.fulfill({ json: {
    id: 'cnv_0001', agentProfileId: 'prf_0001', title: 'Background task', turnInProgress: state.running
  } }));
  await page.route('**/v1.0/api/conversations/cnv_0001/messages**', (route) => route.fulfill({ json: {
    objects: [{ id: 'msg_bg_user', role: 'User', content: 'Work while I navigate.' },
      ...(!state.running ? [{ id: 'msg_bg_answer', role: 'Assistant', content: state.cancelled ? 'The turn was cancelled.' : 'The background task finished.' }] : [])],
    totalRecords: state.running ? 1 : 2
  } }));
  await page.route('**/v1.0/api/conversations/cnv_0001/cancel', (route) => {
    state.cancelled = true;
    state.running = false;
    return route.fulfill({ json: { cancelled: true } });
  });
  await login(page, { expect });
  return state;
}

test('returning to a running conversation refreshes its persisted result without sending a duplicate turn', async ({ page }) => {
  const state = await runningConversation(page);
  await page.goto(pathFor('chat', 'cnv_0001'));
  await expect(page.getByTestId('chat-stop')).toBeVisible();
  state.running = false;
  await expect(page.getByText('The background task finished.', { exact: true })).toBeVisible();
  await expect(page.getByTestId('chat-stop')).toHaveCount(0);
  await expect(page.getByTestId('chat-input')).toBeEnabled();
});

test('Stop explicitly cancels a background conversation after returning to it', async ({ page }) => {
  await runningConversation(page);
  await page.goto(pathFor('chat', 'cnv_0001'));
  await page.getByTestId('chat-stop').click();
  await expect(page.getByText('The turn was cancelled.', { exact: true })).toBeVisible();
  await expect(page.getByTestId('chat-stop')).toHaveCount(0);
});
