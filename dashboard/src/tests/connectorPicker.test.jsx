import { useState } from 'react';
import { beforeAll, afterEach, describe, it, expect, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import i18n, { setActiveLocale } from '../i18n';
import AuthContext from '../context/AuthContext';
import FormFields from '../components/FormFields';
import { resourcesConfig } from '../views/hugoConfigs';
import { resourceConnectorBody } from '../components/ResourceSettingsPanel';

beforeAll(async () => setActiveLocale('en'));
afterEach(cleanup);
const connectors = [
  {
    type: 'ssh',
    displayName: 'SSH host',
    category: 'Compute',
    iconKey: 'terminal',
    settings: [{ key: 'host', label: 'Host', kind: 'String', required: true }],
    secretSlots: [{ name: 'password', label: 'Password', required: true, acceptedKinds: ['Password'] }]
  },
  {
    type: 'sqlite',
    displayName: 'SQLite',
    category: 'Database',
    iconKey: 'database',
    settings: [{ key: 'filename', label: 'Filename', kind: 'String', required: true, defaultValue: ':memory:' }],
    secretSlots: []
  }
];
function Form({ initial = {}, mode = 'create', errors = {} }) {
  const [data, setData] = useState({ ...resourcesConfig.blank(), ...initial });
  return (
    <>
      <FormFields fields={resourcesConfig.fields(data, { t: i18n.t.bind(i18n), mode })} data={data} onChange={setData} errors={errors} idPrefix="resources" />
      <output data-testid="resource-state">{JSON.stringify(data)}</output>
    </>
  );
}
function openForm(props = {}, connectorItems = connectors, secretItems = [{ id: 'sec_password', name: 'Saved password', kind: 'Password' }]) {
  const client = {
    listConnectors: vi.fn().mockResolvedValue({ items: connectorItems }),
    listSecrets: vi.fn().mockResolvedValue({ items: secretItems }),
    testResourceSettings: vi.fn().mockResolvedValue({ success: true, message: 'Connected' })
  };
  const result = render(
    <AuthContext.Provider value={{ apiClient: client }}>
      <Form {...props} />
    </AuthContext.Provider>
  );
  return { ...result, client };
}
const state = () => JSON.parse(screen.getByTestId('resource-state').textContent);

describe('visible resource connector picker', () => {
  const cryptography = {
    type: 'cryptography',
    displayName: 'Cryptography',
    category: 'Other',
    settings: [],
    secretSlots: [
      { name: 'encryptionKey', label: 'AES key bundle', acceptedKinds: ['AesKey', 'Generic'] },
      { name: 'initializationVector', label: 'Initialization vector', acceptedKinds: ['Generic'] }
    ]
  };
  it('shows only the AES bundle picker for a new cryptography resource', async () => {
    openForm({ initial: { connectorType: 'cryptography' } }, [cryptography]);
    await waitFor(() => expect(document.querySelector('#resource-slot-encryptionKey')).toBeTruthy());
    expect(document.querySelector('#resource-slot-initializationVector')).toBeNull();
    expect(screen.queryByTestId('create-secret-initializationVector')).toBeNull();
  });
  it('keeps an existing separate IV binding editable and includes it in saved settings', async () => {
    const secretBindings = {
      encryptionKey: { secretId: 'sec_bundle' },
      initializationVector: { secretId: 'sec_iv' }
    };
    openForm(
      { mode: 'edit', initial: { connectorType: 'cryptography', secretBindings } },
      [cryptography],
      [
        { id: 'sec_bundle', name: 'AES bundle', kind: 'AesKey' },
        { id: 'sec_iv', name: 'Legacy IV', kind: 'Generic' }
      ]
    );
    await waitFor(() => expect(document.querySelector('#resource-slot-initializationVector')?.value).toBe('sec_iv'));
    expect(resourceConnectorBody(state(), cryptography).secretBindings).toEqual([
      { slotName: 'encryptionKey', secretId: 'sec_bundle', secretFieldName: null },
      { slotName: 'initializationVector', secretId: 'sec_iv', secretFieldName: null }
    ]);
  });
  it('puts the searchable radio cards before general metadata and keeps the metadata IDs', async () => {
    const { container } = openForm();
    const ssh = await screen.findByRole('radio', { name: 'SSH host' });
    const name = container.querySelector('#resources-name');
    expect(ssh.compareDocumentPosition(name) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(screen.getAllByRole('radio')).toHaveLength(2);
    expect(container.querySelector('select#resource-connectorType')).toBeNull();
    fireEvent.change(screen.getByRole('searchbox', { name: i18n.t('views.connectors.search') }), { target: { value: 'SQLite' } });
    expect(screen.queryByRole('radio', { name: 'SSH host' })).toBeNull();
    fireEvent.click(screen.getByRole('radio', { name: 'SQLite' }));
    expect(state().connectorType).toBe('sqlite');
  });

  it('preserves populated settings and secrets when the selected connector is chosen again', async () => {
    openForm({ initial: { connectorType: 'ssh', settings: { host: 'web-01' }, secretBindings: { password: { secretId: 'sec_password' } } } });
    const ssh = await screen.findByRole('radio', { name: 'SSH host' });
    fireEvent.click(ssh);
    expect(state().settings).toEqual({ host: 'web-01' });
    expect(state().secretBindings).toEqual({ password: { secretId: 'sec_password' } });
  });

  it('resets incompatible settings and secret references on a different connector selection', async () => {
    openForm({ initial: { name: 'checkout', connectorType: 'ssh', settings: { host: 'web-01' }, secretBindings: { password: { secretId: 'sec_password' } } } });
    fireEvent.click(await screen.findByRole('radio', { name: 'SQLite' }));
    await waitFor(() => expect(state().connectorType).toBe('sqlite'));
    expect(state().settings).not.toHaveProperty('host');
    expect(state().secretBindings).toEqual({});
    expect(state().name).toBe('checkout');
  });

  it('requires a connector selection and associates its validation error with the picker', async () => {
    const errors = resourcesConfig.validate({ ...resourcesConfig.blank(), name: 'checkout' }, i18n.t.bind(i18n));
    expect(errors.connectorType).toBe(i18n.t('common.errors.required'));
    openForm({ errors });
    await screen.findByRole('radio', { name: 'SSH host' });
    const picker = screen.getByTestId('connector-picker');
    expect(picker.textContent).toContain(errors.connectorType);
    expect(document.getElementById(picker.getAttribute('aria-describedby')).textContent).toBe(errors.connectorType);
  });

  it('keeps an existing connector locked while retaining editable metadata and settings', async () => {
    const { container } = openForm({ mode: 'edit', initial: { connectorType: 'ssh', settings: { host: 'web-01' } } });
    await waitFor(() => expect(state()._connector?.type).toBe('ssh'));
    expect(screen.queryByRole('radio')).toBeNull();
    expect(container.querySelector('#resources-name').disabled).toBe(false);
    fireEvent.change(container.querySelector('#resources-name'), { target: { value: 'renamed-host' } });
    expect(state().name).toBe('renamed-host');
    expect(state().connectorType).toBe('ssh');
    expect(state().settings.host).toBe('web-01');
  });
});
