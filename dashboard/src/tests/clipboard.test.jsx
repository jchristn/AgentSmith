import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { copyToClipboard } from '../utils/clipboard';
import CopyableId from '../components/CopyableId';
import FormFields from '../components/FormFields';
import Modal from '../components/Modal';
import { setActiveLocale } from '../i18n';

const originalClipboard = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
const originalCommand = Object.getOwnPropertyDescriptor(document, 'execCommand');
beforeAll(async () => setActiveLocale('en'));
beforeEach(() => window.getSelection().removeAllRanges());
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  if (originalClipboard) Object.defineProperty(navigator, 'clipboard', originalClipboard);
  else delete navigator.clipboard;
  if (originalCommand) Object.defineProperty(document, 'execCommand', originalCommand);
  else delete document.execCommand;
});

function clipboardEnvironment(secure, writeText) {
  vi.stubGlobal('isSecureContext', secure);
  Object.defineProperty(navigator, 'clipboard', { configurable: true, value: writeText ? { writeText } : undefined });
}

describe('global clipboard control', () => {
  it('uses the secure clipboard when the browser marks the origin trustworthy', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    clipboardEnvironment(true, writeText);
    expect(await copyToClipboard('agp_full_id')).toBe(true);
    expect(writeText).toHaveBeenCalledWith('agp_full_id');
  });

  it('copies from an HTTP-host modal, keeps the temporary selection inside it, and restores input focus and selection', async () => {
    clipboardEnvironment(false);
    render(
      <Modal open title="Details">
        <input aria-label="Notes" defaultValue="Existing notes" />
        <CopyableId value="agp_identifier" />
      </Modal>
    );
    const input = screen.getByRole('textbox', { name: 'Notes' });
    input.focus();
    input.setSelectionRange(2, 6, 'backward');
    const command = vi.fn(() => {
      const temporary = document.activeElement;
      expect(temporary.tagName).toBe('TEXTAREA');
      expect(temporary.closest('[role="dialog"]')).toBe(screen.getByRole('dialog'));
      expect(temporary.value).toBe('agp_identifier');
      expect(temporary.selectionStart).toBe(0);
      expect(temporary.selectionEnd).toBe(14);
      return true;
    });
    Object.defineProperty(document, 'execCommand', { configurable: true, value: command });
    expect(await copyToClipboard('agp_identifier')).toBe(true);
    expect(command).toHaveBeenCalledWith('copy');
    expect(document.activeElement).toBe(input);
    expect([input.selectionStart, input.selectionEnd, input.selectionDirection]).toEqual([2, 6, 'backward']);
    expect(screen.getByRole('dialog').querySelector('textarea')).toBeNull();
  });

  it('falls back after secure clipboard rejection and copies the full untruncated ID', async () => {
    const writeText = vi.fn().mockRejectedValue(new Error('Permission denied'));
    clipboardEnvironment(true, writeText);
    const value = 'agp_this_identifier_is_longer_than_the_display_limit';
    const command = vi.fn(() => document.activeElement.value === value);
    Object.defineProperty(document, 'execCommand', { configurable: true, value: command });
    render(
      <Modal open title="Details">
        <CopyableId value={value} max={12} />
      </Modal>
    );
    const button = within(screen.getByRole('dialog')).getByRole('button', { name: 'Copy' });
    button.focus();
    fireEvent.click(button);
    await waitFor(() => expect(button.getAttribute('aria-label')).toBe('Copied'));
    expect(writeText).toHaveBeenCalledWith(value);
    expect(command).toHaveBeenCalledWith('copy');
    expect(document.activeElement).toBe(button);
  });

  it.each([false, 'throws'])('reports a failed legacy copy (%s) without a false success state or a leaked textarea', async (outcome) => {
    clipboardEnvironment(false);
    Object.defineProperty(document, 'execCommand', {
      configurable: true,
      value: vi.fn(() => {
        if (outcome === 'throws') throw new Error('Blocked');
        return outcome;
      })
    });
    render(
      <Modal open title="Details">
        <CopyableId value="conv_identifier" />
      </Modal>
    );
    const button = within(screen.getByRole('dialog')).getByRole('button', { name: 'Copy' });
    button.focus();
    expect(await copyToClipboard('conv_identifier')).toBe(false);
    expect(button.getAttribute('aria-label')).toBe('Copy');
    expect(document.activeElement).toBe(button);
    expect(document.querySelector('textarea')).toBeNull();
  });

  it('restores the selected page text after a successful legacy copy', async () => {
    clipboardEnvironment(false);
    render(<p>Select this existing text</p>);
    const text = screen.getByText('Select this existing text').firstChild;
    const range = document.createRange();
    range.setStart(text, 7);
    range.setEnd(text, 11);
    window.getSelection().addRange(range);
    Object.defineProperty(document, 'execCommand', { configurable: true, value: vi.fn(() => true) });
    expect(await copyToClipboard('res_other')).toBe(true);
    expect(window.getSelection().toString()).toBe('this');
  });

  it('gives generic modal identifier fields a copy control even when the field is read-only', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    clipboardEnvironment(true, writeText);
    render(
      <Modal open title="Record">
        <FormFields fields={[{ name: 'resourceId', label: 'Resource ID', readOnly: true }]} data={{ resourceId: 'res_full_identifier' }} onChange={vi.fn()} />
      </Modal>
    );
    const input = screen.getByRole('textbox', { name: 'Resource ID' });
    expect(input.disabled).toBe(true);
    expect(input.closest('.field-identifier')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Copy' }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith('res_full_identifier'));
  });
});
