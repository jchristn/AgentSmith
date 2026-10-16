import { beforeAll, afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { setActiveLocale } from '../i18n';
import AuthContext from '../context/AuthContext';
import AppContext from '../context/AppContext';
import { ToastProvider } from '../context/ToastContext';
import ProfileDetailView from '../views/ProfileDetailView';

beforeAll(async () => setActiveLocale('en'));
afterEach(cleanup);

const resources = [
  { id: 'res_mysql', name: 'jurat-mysql', connectorType: 'mysql' },
  { id: 'res_other', name: 'other-db', connectorType: 'postgresql' }
];

function openProfile(bindings = [], availableResources = resources) {
  const api = {
    getProfile: vi.fn().mockResolvedValue({ id: 'agp_1', name: 'Hugo', visibility: 'Tenant' }),
    listBindings: vi.fn().mockImplementation(async () => ({ items: bindings })),
    listModelEndpoints: vi.fn().mockResolvedValue({ items: [] }),
    listResources: vi.fn().mockResolvedValue({ items: availableResources }),
    getProfileTools: vi.fn().mockResolvedValue([]),
    createBinding: vi.fn().mockImplementation(async (_, body) => {
      bindings.push({ id: 'bnd_new', ...body });
    }),
    updateBinding: vi.fn().mockResolvedValue({})
  };
  render(
    <MemoryRouter initialEntries={['/dashboard/profiles/agp_1?tab=bindings']}>
      <AuthContext.Provider value={{ apiClient: api, principal: { isAdmin: true }, serverUrl: 'http://127.0.0.1:8800' }}>
        <AppContext.Provider value={{ tenantId: 'default' }}>
          <ToastProvider><ProfileDetailView recordId="agp_1" /></ToastProvider>
        </AppContext.Provider>
      </AuthContext.Provider>
    </MemoryRouter>
  );
  return api;
}

describe('profile resource bindings', () => {
  it('shows no resource selected until chosen and saves the first existing resource directly', async () => {
    const api = openProfile();
    await screen.findByTestId('add-binding');
    await waitFor(() => expect(api.listResources).toHaveBeenCalled());
    fireEvent.click(screen.getByTestId('add-binding'));
    const select = document.getElementById('binding-resourceId');
    await waitFor(() => expect(select.options).toHaveLength(3));
    expect(select.value).toBe('');
    expect(select.selectedOptions[0].textContent).toBe('None');
    fireEvent.change(select, { target: { value: 'res_mysql' } });
    expect(document.getElementById('binding-alias').value).toBe('jurat-mysql');
    fireEvent.click(screen.getByTestId('binding-save'));
    await waitFor(() => expect(api.createBinding).toHaveBeenCalledWith('agp_1', expect.objectContaining({ resourceId: 'res_mysql', alias: 'jurat-mysql' })));
    expect(api.updateBinding).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.queryByTestId('binding-modal')).toBeNull());
  });

  it.each([['listed', resources], ['not listed', [resources[0]]]])('keeps an existing binding resource fixed when it is %s', async (_, available) => {
    const binding = { id: 'bnd_existing', resourceId: 'res_other', resourceName: 'other-db', alias: 'existing', accessMode: 'ReadWrite', instructionMode: 'Inherit', enabled: true };
    const api = openProfile([binding], available);
    const cell = await screen.findByText('existing');
    fireEvent.click(cell.closest('tr').querySelector('[aria-haspopup="menu"]'));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Edit' }));
    const select = document.getElementById('binding-resourceId');
    expect(select.disabled).toBe(true);
    expect(select.value).toBe('res_other');
    expect(select.selectedOptions[0].textContent).toContain('other-db');
    fireEvent.change(document.getElementById('binding-alias'), { target: { value: 'renamed' } });
    fireEvent.click(screen.getByTestId('binding-save'));
    await waitFor(() => expect(api.updateBinding).toHaveBeenCalledWith('agp_1', 'bnd_existing', expect.objectContaining({ alias: 'renamed', accessMode: 'ReadWrite' })));
    expect(api.updateBinding.mock.calls[0][2]).not.toHaveProperty('resourceId');
    expect(api.createBinding).not.toHaveBeenCalled();
  });
});
