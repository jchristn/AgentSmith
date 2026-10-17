import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import i18n, { setActiveLocale } from '../i18n';
import AuthContext from '../context/AuthContext';
import AppContext from '../context/AppContext';
import { ToastProvider } from '../context/ToastContext';
import ResourceView from '../components/ResourceView';
import ModelHealthModal from '../components/ModelHealthModal';
import { modelsConfig } from '../views/hugoConfigs';
import { summarizeModelHealth } from '../utils/modelHealth';

beforeAll(async () => {
  await setActiveLocale('en');
});
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

const endpoint = {
  id: 'mdl_health',
  name: 'Local inference',
  kind: 'Inference',
  provider: 'Ollama',
  model: 'test-model',
  baseUrl: 'http://127.0.0.1:11434',
  healthStatus: 'Healthy',
  lastHealthUtc: '2026-10-16T12:00:00Z',
  lastHealthMessage: 'Responded',
  enabled: true,
  supportsTools: true,
  supportsStreaming: true
};
const records = [
  { id: 'check_3', status: 'Healthy', source: 'Validate', latencyMs: 210, message: 'Validation responded', checkedUtc: '2026-10-16T12:00:00Z' },
  { id: 'check_2', status: 'Unhealthy', source: 'Sweep', latencyMs: null, message: 'Connection refused', checkedUtc: '2026-10-16T11:59:00Z' },
  { id: 'check_1', status: 'Healthy', source: 'Sweep', latencyMs: 180, message: 'Sweep responded', checkedUtc: '2026-10-16T11:58:00Z' }
];

function withContext(ui, apiClient) {
  return (
    <MemoryRouter>
      <AuthContext.Provider value={{ apiClient, principal: { isAdmin: true } }}>
        <AppContext.Provider value={{ tenantId: 'default' }}>
          <ToastProvider>{ui}</ToastProvider>
        </AppContext.Provider>
      </AuthContext.Provider>
    </MemoryRouter>
  );
}

function api(history = vi.fn().mockResolvedValue(records)) {
  return {
    listModelEndpoints: vi.fn().mockResolvedValue({ items: [endpoint], totalCount: 1 }),
    listSecrets: vi.fn().mockResolvedValue({ items: [] }),
    getModelEndpointHealthHistory: history,
    updateModelEndpoint: vi.fn().mockResolvedValue(endpoint),
    validateModelEndpoint: vi.fn()
  };
}

const t = (key) => i18n.t(key);

describe('separate model editing and health inspection', () => {
  it('opens the real endpoint editor without health requests, then opens health from its row action', async () => {
    const client = api();
    const { container } = render(withContext(<ResourceView config={modelsConfig} />, client));
    await screen.findByText(endpoint.name);
    fireEvent.click(container.querySelector('.data-table tbody tr'));
    const editor = screen.getByRole('dialog');
    expect(within(editor).queryByTestId('model-health-history')).toBeNull();
    expect(editor.querySelector('input')).not.toBeNull();
    expect(client.getModelEndpointHealthHistory).not.toHaveBeenCalled();
    fireEvent.click(within(editor).getByRole('button', { name: t('common.actions.save'), exact: true }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(client.updateModelEndpoint).toHaveBeenCalledTimes(1);
    fireEvent.click(container.querySelector('.data-table tbody tr [aria-haspopup="menu"]'));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Health', exact: true }));
    const health = await screen.findByTestId('model-health-modal');
    await waitFor(() => expect(client.getModelEndpointHealthHistory).toHaveBeenCalledWith(endpoint.id, 50));
    expect(health.querySelector('input,select,textarea')).toBeNull();
    expect(within(health).queryByRole('button', { name: t('common.actions.save') })).toBeNull();
    expect(client.updateModelEndpoint).toHaveBeenCalledTimes(1);
    expect(client.validateModelEndpoint).not.toHaveBeenCalled();
  });

  it('opens only the read-only health modal when the health badge is clicked', async () => {
    const client = api();
    render(withContext(<ResourceView config={modelsConfig} />, client));
    const badge = await screen.findByRole('button', { name: i18n.t('resources.models.actions.healthTitle', { name: endpoint.name }) });
    fireEvent.click(badge);
    await screen.findByRole('list', { name: t('resources.models.health.chartLabel') });
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    expect(screen.getByRole('dialog')).toBe(screen.getByTestId('model-health-modal'));
    expect(screen.queryByRole('button', { name: t('common.actions.save'), exact: true })).toBeNull();
    expect(client.listSecrets).not.toHaveBeenCalled();
    expect(client.updateModelEndpoint).not.toHaveBeenCalled();
  });

  it('shows a bounded, read-only history with all source and failure details available on demand', async () => {
    const client = api();
    const onClose = vi.fn();
    render(withContext(<ModelHealthModal endpoint={endpoint} onClose={onClose} />, client));
    const modal = screen.getByTestId('model-health-modal');
    await screen.findByRole('list', { name: t('resources.models.health.chartLabel') });
    expect(modal.className).toContain('modal-large');
    expect(modal.textContent).toContain(endpoint.name);
    expect(modal.querySelector('details').open).toBe(false);
    fireEvent.click(modal.querySelector('summary'));
    const checks = within(modal).getAllByTestId('model-health-record');
    expect(checks).toHaveLength(3);
    expect(checks[0].textContent).toContain('Validation');
    expect(checks[1].textContent).toContain('Connection refused');
    expect(checks[1].textContent).toContain('Health sweep');
    fireEvent.click(within(modal).getAllByRole('button', { name: t('common.actions.close'), exact: true })[0]);
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('recovers from an initial history failure using retry without validating or modifying the endpoint', async () => {
    const history = vi.fn().mockRejectedValueOnce(new Error('Health history unavailable')).mockResolvedValueOnce(records);
    const client = api(history);
    render(withContext(<ModelHealthModal endpoint={endpoint} onClose={vi.fn()} />, client));
    await screen.findByText('Health history unavailable');
    fireEvent.click(screen.getByRole('button', { name: t('common.actions.retry') }));
    await screen.findByRole('list', { name: t('resources.models.health.chartLabel') });
    expect(history).toHaveBeenCalledTimes(2);
    expect(client.validateModelEndpoint).not.toHaveBeenCalled();
    expect(client.updateModelEndpoint).not.toHaveBeenCalled();
  });

  it('refreshes an empty history to new server checks', async () => {
    const history = vi.fn().mockResolvedValueOnce([]).mockResolvedValueOnce(records);
    render(withContext(<ModelHealthModal endpoint={endpoint} onClose={vi.fn()} />, api(history)));
    await screen.findByText(t('resources.models.history.empty'));
    fireEvent.click(screen.getByRole('button', { name: /refresh/i }));
    await screen.findByRole('list', { name: t('resources.models.health.chartLabel') });
    expect(history).toHaveBeenCalledTimes(2);
    expect(screen.getAllByTestId('model-health-record')).toHaveLength(3);
  });

  it('keeps the new endpoint history when the old endpoint request resolves last', async () => {
    let resolveOld;
    let resolveNew;
    const oldRequest = new Promise((resolve) => {
      resolveOld = resolve;
    });
    const newRequest = new Promise((resolve) => {
      resolveNew = resolve;
    });
    const history = vi.fn((id) => (id === endpoint.id ? oldRequest : newRequest));
    const client = api(history);
    const onClose = vi.fn();
    const { rerender } = render(withContext(<ModelHealthModal endpoint={endpoint} onClose={onClose} />, client));
    await waitFor(() => expect(history).toHaveBeenCalledWith(endpoint.id, 50));
    const nextEndpoint = { ...endpoint, id: 'mdl_new', name: 'New endpoint' };
    rerender(withContext(<ModelHealthModal endpoint={nextEndpoint} onClose={onClose} />, client));
    await waitFor(() => expect(history).toHaveBeenCalledWith(nextEndpoint.id, 50));
    await act(async () => {
      resolveNew([{ ...records[0], message: 'New endpoint responded' }]);
    });
    expect(screen.getAllByTestId('model-health-record')).toHaveLength(1);
    expect(screen.getByTestId('model-health-record').textContent).toContain('New endpoint responded');
    await act(async () => {
      resolveOld([{ ...records[1], message: 'Stale old endpoint failure' }]);
    });
    expect(screen.getAllByTestId('model-health-record')).toHaveLength(1);
    expect(screen.getByTestId('model-health-record').textContent).toContain('New endpoint responded');
    expect(screen.queryByText('Stale old endpoint failure')).toBeNull();
    expect(screen.getByRole('dialog').textContent).toContain(nextEndpoint.name);
  });

  it('reports observed-check success and streaks from unsorted mixed states, with chronological chart order', () => {
    const sample = [
      { id: 'failed-latest', status: 'Unhealthy', checkedUtc: '2026-10-16T12:00:00Z', message: 'Latest failure' },
      { id: 'healthy-first', status: 'Healthy', checkedUtc: '2026-10-01T12:00:00Z' },
      { id: 'unknown', status: 'Unknown', checkedUtc: '2026-10-16T11:58:00Z' },
      { id: 'failed-previous', status: 'Unhealthy', checkedUtc: '2026-10-16T11:59:00Z', message: 'Previous failure' },
      { id: 'healthy-second', status: 'Healthy', checkedUtc: '2026-10-15T12:00:00Z' }
    ];
    const originalOrder = sample.map((check) => check.id);
    const summary = summarizeModelHealth(sample);
    // Two of five sampled checks succeeded; long gaps between checks do not imply uptime.
    expect(summary.successRate).toBe(0.4);
    expect(summary.total).toBe(5);
    expect(summary.healthy).toBe(2);
    expect(summary.status).toBe('Unhealthy');
    expect(summary.consecutiveUnhealthy).toBe(2);
    expect(summary.consecutiveHealthy).toBe(0);
    expect(summary.lastError).toBe('Latest failure');
    expect(summary.oldestFirst.map((check) => check.id)).toEqual(['healthy-first', 'healthy-second', 'unknown', 'failed-previous', 'failed-latest']);
    expect(summary.newestFirst[0].id).toBe('failed-latest');
    expect(sample.map((check) => check.id)).toEqual(originalOrder);
  });
});
