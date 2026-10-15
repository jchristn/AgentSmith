// Shared components rendered with React Testing Library (no Storybook): chat panel, tool activity rows, approval
// cards, effect badges, the settings schema form, the prompt editor, the secret slot picker, the reveal flow, and the
// empty and error states.

import { describe, it, expect, vi, beforeAll, afterEach } from 'vitest';
import { render, screen, fireEvent, cleanup, waitFor, act } from '@testing-library/react';
import i18n, { setActiveLocale } from '../i18n';
import AuthContext from '../context/AuthContext';
import { ToastProvider } from '../context/ToastContext';
import ChatPanel from '../components/ChatPanel';
import ToolActivityRow from '../components/ToolActivityRow';
import ApprovalCard from '../components/ApprovalCard';
import { EffectBadge } from '../components/StatusBadge';
import SchemaForm from '../components/SchemaForm';
import PromptEditor from '../components/PromptEditor';
import SecretSlotPicker, { SecretValuesForm } from '../components/SecretField';
import RevealSecretModal, { needsReauth } from '../components/RevealSecretModal';
import { UnavailableState, ErrorPresenter } from '../components/States';
import { validatePasswordChange } from '../components/PasswordChangeModal';
import { ApiError } from '../utils/api';
import { beginTurn, applyChatEvent, initialChatState } from '../utils/chatStream';

beforeAll(async () => {
  await setActiveLocale('en');
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

const t = (key, opts) => i18n.t(key, opts);

function withAuth(ui, apiClient = {}) {
  return (
    <AuthContext.Provider value={{ apiClient, principal: { isAdmin: true }, serverUrl: 'http://127.0.0.1:8800' }}>
      <ToastProvider>{ui}</ToastProvider>
    </AuthContext.Provider>
  );
}

describe('ChatPanel', () => {
  function transcript() {
    let s = beginTurn(initialChatState(), 'Restart nginx');
    s = applyChatEvent(s, { event: 'tool.proposed', data: { invocationId: 't1', toolName: 'ssh_exec', resourceAlias: 'web-01', effect: 'Destructive', arguments: { command: 'systemctl restart nginx' } } });
    s = applyChatEvent(s, { event: 'approval.required', data: { approvalId: 'apr_1', invocationId: 't1', summary: 'Restart nginx on web-01', effect: 'Destructive', command: 'sudo systemctl restart nginx' } });
    s = applyChatEvent(s, { event: 'assistant.delta', data: { text: 'Waiting for your approval, **Mr. Anderson**.' } });
    return s;
  }

  it('renders the transcript with tool rows, an approval card, and Markdown', () => {
    const s = transcript();
    render(<ChatPanel messages={s.messages} streaming={s.streaming} draft="" />);
    expect(screen.getByText('Restart nginx')).toBeTruthy();
    expect(screen.getByTestId('tool-row').getAttribute('data-status')).toBe('awaitingApproval');
    expect(screen.getByTestId('approval-card').textContent).toContain('sudo systemctl restart nginx');
    expect(document.querySelector('.markdown-body strong').textContent).toBe('Mr. Anderson');
    expect(screen.getByTestId('chat-stop')).toBeTruthy();
  });

  it('sends on Enter, keeps Shift+Enter for new lines, and stops while streaming', () => {
    const onSend = vi.fn();
    const onDraftChange = vi.fn();
    const { rerender } = render(<ChatPanel messages={[]} draft="Why is checkout slow?" onSend={onSend} onDraftChange={onDraftChange} />);
    const input = screen.getByTestId('chat-input');
    fireEvent.keyDown(input, { key: 'Enter', shiftKey: true });
    expect(onSend).not.toHaveBeenCalled();
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(onSend).toHaveBeenCalledWith('Why is checkout slow?');
    const onStop = vi.fn();
    rerender(<ChatPanel messages={[]} draft="" streaming onStop={onStop} />);
    fireEvent.click(screen.getByTestId('chat-stop'));
    expect(onStop).toHaveBeenCalled();
  });

  it('disables the composer with a reason', () => {
    render(<ChatPanel messages={[]} draft="x" disabled disabledReason="No model" />);
    expect(screen.getByText('No model')).toBeTruthy();
    expect(screen.getByTestId('chat-send').disabled).toBe(true);
  });

  it('passes approval decisions with the note', () => {
    const s = transcript();
    const onApprove = vi.fn();
    render(<ChatPanel messages={s.messages} draft="" onApprove={onApprove} onReject={() => {}} />);
    fireEvent.change(screen.getByPlaceholderText(t('components.approvalCard.notePlaceholder')), { target: { value: 'ok to restart' } });
    fireEvent.click(screen.getByTestId('approval-approve'));
    expect(onApprove).toHaveBeenCalledWith('apr_1', 'ok to restart');
  });
});

describe('ToolActivityRow and badges', () => {
  it('shows tool, resource, effect, status, and duration, and expands to arguments and output', () => {
    render(<ToolActivityRow tool={{ id: 't', toolName: 'sql_query', resourceAlias: 'prod-db', effect: 'Read', status: 'Succeeded', durationMs: 42, arguments: { sql: 'select 1' }, outputPreview: '1 row' }} />);
    expect(screen.getByText('sql_query')).toBeTruthy();
    expect(screen.getByText('prod-db')).toBeTruthy();
    expect(screen.queryByText('1 row')).toBeNull();
    fireEvent.click(screen.getByRole('button', { expanded: false }));
    expect(screen.getByText('1 row')).toBeTruthy();
    expect(document.body.textContent).toContain('select 1');
  });
  it('shows errors instead of output', () => {
    render(<ToolActivityRow defaultOpen tool={{ id: 't', toolName: 'ssh_exec', effect: 'Write', status: 'Failed', error: 'Permission denied' }} />);
    expect(screen.getByText('Permission denied')).toBeTruthy();
  });
  it('labels effects with an icon and text, not color alone', () => {
    for (const effect of ['Read', 'Write', 'Destructive']) {
      const { container, unmount } = render(<EffectBadge effect={effect} />);
      const badge = container.querySelector('.effect-badge');
      expect(badge.querySelector('svg')).toBeTruthy();
      expect(badge.textContent).toBe(t(`enums.effect.${effect}`));
      unmount();
    }
    expect(render(<EffectBadge effect="Destructive" />).container.querySelector('.pill-danger')).toBeTruthy();
  });
});

describe('ApprovalCard', () => {
  const approval = { id: 'apr_1', summary: 'Drop table orders', effect: 'Destructive', effectReason: 'DROP statement', command: 'DROP TABLE orders', resourceAlias: 'prod-db', status: 'Pending' };
  it('approves and rejects with a note', () => {
    const onApprove = vi.fn();
    const onReject = vi.fn();
    render(<ApprovalCard approval={approval} onApprove={onApprove} onReject={onReject} />);
    expect(screen.getByText('DROP TABLE orders')).toBeTruthy();
    expect(document.body.textContent).toContain('DROP statement');
    fireEvent.click(screen.getByTestId('approval-reject'));
    expect(onReject).toHaveBeenCalledWith('');
    expect(onApprove).not.toHaveBeenCalled();
  });
  it('shows the outcome once decided', () => {
    render(<ApprovalCard approval={{ ...approval, status: 'Rejected', decidedBy: 'anderson', note: 'no' }} onApprove={() => {}} />);
    expect(screen.queryByTestId('approval-approve')).toBeNull();
    expect(screen.getByTestId('approval-outcome').textContent).toContain('anderson');
  });
});

describe('SchemaForm', () => {
  const schema = {
    settings: [
      { key: 'host', label: 'Host', help: 'Hostname', kind: 'String', required: true, group: 'Connection' },
      { key: 'port', label: 'Port', kind: 'Integer', minimum: 1, maximum: 65535, group: 'Connection' },
      { key: 'tls', label: 'Use TLS', kind: 'Boolean', group: 'Security' },
      { key: 'mode', label: 'Mode', kind: 'Enum', allowedValues: ['a', 'b'], required: true, group: 'Security' },
      { key: 'paths', label: 'Paths', kind: 'StringList' },
      { key: 'notes', label: 'Notes', kind: 'Text' }
    ]
  };
  it('renders one control per kind in group fieldsets and reports changes', () => {
    const onChange = vi.fn();
    render(<SchemaForm schema={schema} values={{ host: '', port: 22, tls: false, mode: 'a', paths: [], notes: '' }} onChange={onChange} errors={{ host: 'Required' }} />);
    expect([...document.querySelectorAll('legend')].map((l) => l.textContent)).toEqual(['Connection', 'Security', 'General']);
    expect(document.querySelector('#setting-port').getAttribute('type')).toBe('number');
    expect(document.querySelector('#setting-port').getAttribute('max')).toBe('65535');
    expect(document.querySelector('#setting-tls').getAttribute('type')).toBe('checkbox');
    expect(document.querySelector('#setting-mode').tagName).toBe('SELECT');
    expect(document.querySelector('#setting-notes').tagName).toBe('TEXTAREA');
    expect(screen.getByText('Required')).toBeTruthy();
    fireEvent.change(document.querySelector('#setting-host'), { target: { value: 'web-01' } });
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ host: 'web-01', port: 22 }));
  });
  it('says when a connector has no settings', () => {
    render(<SchemaForm schema={{ settings: [] }} values={{}} />);
    expect(screen.getByText(t('components.schemaForm.noSettings'))).toBeTruthy();
  });
});

describe('PromptEditor', () => {
  it('inserts variables at the cursor and previews locally when no renderer is given', async () => {
    let value = 'Hello ';
    const onChange = vi.fn((v) => (value = v));
    const { rerender } = render(<PromptEditor value={value} onChange={onChange} />);
    const area = screen.getByLabelText(t('components.promptEditor.content'), { selector: 'textarea' });
    area.setSelectionRange(6, 6);
    fireEvent.click(document.querySelector('[data-variable="persona.name"]'));
    expect(onChange).toHaveBeenCalledWith('Hello {{persona.name}}');
    rerender(<PromptEditor value={value} onChange={onChange} />);
    expect(screen.getByTestId('used-variables').textContent).toContain('persona.name');
    await waitFor(() => expect(document.body.textContent).toContain('Hello Agent Smith'), { timeout: 2000 });
  });
  it('shows a diff against the baseline', () => {
    render(<PromptEditor value={'a\nc'} baseline={{ label: 'Built-in', text: 'a\nb' }} />);
    fireEvent.click(screen.getByRole('tab', { name: t('components.promptEditor.diff') }));
    expect(document.querySelector('.diff-del').textContent).toContain('Built-in');
  });
});

describe('secret fields', () => {
  it('renders the fields of a secret kind', () => {
    render(<SecretValuesForm kind="AccessKeyPair" values={{}} onChange={() => {}} />);
    expect(document.querySelector('#secret-accessKeyId')).toBeTruthy();
    expect(document.querySelector('#secret-secretAccessKey').getAttribute('type')).toBe('password');
  });
  it('lists only accepted kinds and creates a secret inline', async () => {
    const onChange = vi.fn();
    const createSecret = vi.fn().mockResolvedValue({ id: 'sec_new', kind: 'PrivateKey' });
    render(withAuth(<SecretSlotPicker slot={{ name: 'privateKey', label: 'SSH key', acceptedKinds: ['PrivateKey'], required: true }} secrets={[{ id: 'sec_pw', name: 'pw', kind: 'Password' }, { id: 'sec_key', name: 'key', kind: 'PrivateKey' }]} onChange={onChange} createSecret={createSecret} />));
    const options = [...document.querySelectorAll('#slot-privateKey option')].map((o) => o.value);
    expect(options).toEqual(['', 'sec_key']);
    fireEvent.click(screen.getByTestId('create-secret-privateKey'));
    fireEvent.change(document.querySelector('#slot-new-name'), { target: { value: 'web-01-key' } });
    fireEvent.change(document.querySelector('#slot-new-privateKey'), { target: { value: '-----BEGIN OPENSSH PRIVATE KEY-----' } });
    await act(async () => fireEvent.click(screen.getByText(t('components.secretField.saveInline'))));
    expect(createSecret).toHaveBeenCalledWith({ name: 'web-01-key', kind: 'PrivateKey', values: { privateKey: '-----BEGIN OPENSSH PRIVATE KEY-----' } });
    expect(onChange).toHaveBeenCalledWith({ secretId: 'sec_new', secretFieldName: null });
  });
});

describe('secret reveal flow (P8.32)', () => {
  it('asks for a reason, re-authenticates when required, shows values, and hides them after the timeout', async () => {
    const apiClient = {
      revealSecret: vi.fn().mockRejectedValueOnce(new ApiError(403, { error: 'ReauthenticationRequired', message: 'Re-authenticate first' })).mockResolvedValueOnce({ values: { password: 'swordfish' } }),
      reauthenticate: vi.fn().mockResolvedValue({})
    };
    render(withAuth(<RevealSecretModal secret={{ id: 'sec_1', name: 'db' }} onClose={() => {}} visibleSeconds={2} />, apiClient));
    fireEvent.click(screen.getByTestId('reveal-submit'));
    expect(await screen.findByText(t('views.secrets.reveal.reasonRequired'))).toBeTruthy();
    fireEvent.change(document.querySelector('#reveal-reason'), { target: { value: 'incident 42' } });
    await act(async () => fireEvent.click(screen.getByTestId('reveal-submit')));
    expect(apiClient.revealSecret).toHaveBeenCalledWith('sec_1', 'incident 42');
    fireEvent.change(await screen.findByLabelText(t('views.secrets.reveal.password'), { exact: false, selector: 'input' }), { target: { value: 'pw' } });
    await act(async () => fireEvent.click(screen.getByTestId('reauth-submit')));
    expect(apiClient.reauthenticate).toHaveBeenCalledWith('pw');
    expect(await screen.findByText('swordfish')).toBeTruthy();
    await waitFor(() => expect(screen.getByTestId('reveal-hidden')).toBeTruthy(), { timeout: 4000 });
    expect(screen.queryByText('swordfish')).toBeNull();
  });
  it('recognizes re-authentication errors', () => {
    expect(needsReauth({ status: 403, code: 'ReauthenticationRequired' })).toBe(true);
    expect(needsReauth({ status: 401, code: 'Http401' })).toBe(true);
    expect(needsReauth({ status: 403, code: 'AuthorizationDenied', message: 'no' })).toBe(false);
  });
});

describe('states and forms', () => {
  it('explains a capability the server does not expose yet, without saying coming soon', () => {
    render(<UnavailableState feature="Resources" purpose="Connections to your systems." endpoint="/v1.0/api/resources" />);
    const text = screen.getByTestId('unavailable-state').textContent;
    expect(text).toContain('Resources');
    expect(text).toContain('Connections to your systems.');
    expect(text).toContain('/v1.0/api/resources');
    expect(text.toLowerCase()).not.toContain('coming soon');
  });
  it('presents an error with status, message, and request ID', () => {
    render(<ErrorPresenter error={new ApiError(500, { error: 'InternalError', message: 'boom', requestId: 'req_9' })} onRetry={() => {}} />);
    const text = screen.getByTestId('error-presenter').textContent;
    expect(text).toContain('500');
    expect(text).toContain('req_9');
  });
  it('validates a forced password change', () => {
    expect(validatePasswordChange({ currentPassword: '', newPassword: 'short', confirmPassword: 'x' })).toEqual({ currentPassword: 'common.errors.required', newPassword: 'components.passwordChange.tooShort', confirmPassword: 'components.passwordChange.mismatch' });
    expect(validatePasswordChange({ currentPassword: 'password', newPassword: 'password', confirmPassword: 'password' }).newPassword).toBe('components.passwordChange.sameAsCurrent');
    expect(validatePasswordChange({ currentPassword: 'password', newPassword: 'a-long-passphrase', confirmPassword: 'a-long-passphrase' })).toEqual({});
  });
});
