import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { setActiveLocale } from '../i18n';
import FeedbackView from '../views/FeedbackView';
import { ToastProvider } from '../context/ToastContext';

beforeAll(async () => {
  await setActiveLocale('en');
});
afterEach(cleanup);
const row = {
  id: 'msg_1',
  agentProfileId: 'agp_1',
  agentName: 'First agent',
  feedbackRating: 'Negative',
  feedbackComment: 'Needs citations',
  requestText: 'Find the **user**',
  responseText: '## Result\n\nFound a user.',
  feedbackUpdatedUtc: '2026-10-17T12:00:00Z',
  inputTokens: 0,
  outputTokens: 12,
  timeToFirstTokenMs: 0,
  durationMs: 1000,
  conversationId: 'conv_1',
  turnId: 'turn_1',
  toolCallCount: 2
};
function api(overrides = {}) {
  return {
    listProfiles: vi.fn(async () => ({
      items: [
        { id: 'agp_1', name: 'First agent' },
        { id: 'agp_2', name: 'Second agent' }
      ],
      totalCount: 2
    })),
    listFeedback: vi.fn(async () => ({ items: [row], totalCount: 26 })),
    getFeedback: vi.fn(async () => row),
    ...overrides
  };
}
function mount(client, path = '/activity/feedback') {
  render(
    <MemoryRouter initialEntries={[path]}>
      <ToastProvider>
        <FeedbackView apiClient={client} />
      </ToastProvider>
    </MemoryRouter>
  );
}

describe('feedback review', () => {
  it('requires an agent, scopes requests, pages results and renders Markdown plus zero-valued telemetry', async () => {
    const client = api();
    mount(client);
    const select = await screen.findByRole('combobox', { name: 'Agent' });
    await waitFor(() => expect(select.disabled).toBe(false));
    expect(client.listFeedback).not.toHaveBeenCalled();
    fireEvent.change(select, { target: { value: 'agp_1' } });
    await screen.findByText('Needs citations');
    expect(client.listFeedback).toHaveBeenLastCalledWith({ agentProfileId: 'agp_1', pageNumber: 1, pageSize: 25 });
    fireEvent.click(screen.getByRole('button', { name: 'Next page' }));
    await waitFor(() => expect(client.listFeedback).toHaveBeenLastCalledWith({ agentProfileId: 'agp_1', pageNumber: 2, pageSize: 25 }));
    await screen.findByText('Needs citations');
    fireEvent.click(screen.getByRole('button', { name: 'View details' }));
    const dialog = await screen.findByRole('dialog');
    await within(dialog).findByRole('heading', { name: 'Result' });
    expect(dialog.querySelector('strong').textContent).toBe('user');
    expect(dialog.classList.contains('modal-wide')).toBe(true);
    for (const id of ['msg_1', 'agp_1', 'conv_1', 'turn_1']) {
      const code = within(dialog).getByText(id);
      expect(code.tagName).toBe('CODE');
      expect(within(code.closest('.copyable-id')).getByRole('button', { name: 'Copy' })).toBeTruthy();
    }
    expect(within(dialog).getAllByText('0').length).toBeGreaterThan(0);
  });

  it('ignores stale results after switching agents and restores a URL selection', async () => {
    let resolveFirst;
    const client = api({
      listFeedback: vi.fn(({ agentProfileId }) =>
        agentProfileId === 'agp_1'
          ? new Promise((resolve) => {
              resolveFirst = resolve;
            })
          : Promise.resolve({ items: [{ ...row, id: 'msg_2', feedbackComment: 'Second feedback' }], totalCount: 1 })
      )
    });
    mount(client, '/activity/feedback?agentProfileId=agp_1');
    const select = await screen.findByRole('combobox', { name: 'Agent' });
    await waitFor(() => expect(select.disabled).toBe(false));
    fireEvent.change(select, { target: { value: 'agp_2' } });
    await screen.findByText('Second feedback');
    resolveFirst({ items: [row], totalCount: 1 });
    await waitFor(() => expect(screen.queryByText('Needs citations')).toBeNull());
    expect(select.value).toBe('agp_2');
  });

  it('shows scoped loading failures and retries without falling back to stale details', async () => {
    const client = api({ getFeedback: vi.fn().mockRejectedValueOnce(new Error('detail offline')).mockResolvedValue(row) });
    mount(client, '/activity/feedback?agentProfileId=agp_1');
    await screen.findByText('Needs citations');
    fireEvent.click(screen.getByRole('button', { name: 'View details' }));
    const dialog = await screen.findByRole('dialog');
    await within(dialog).findByRole('alert');
    expect(within(dialog).queryByText('Found a user.')).toBeNull();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Retry' }));
    await within(dialog).findByRole('heading', { name: 'Result' });
  });
});
