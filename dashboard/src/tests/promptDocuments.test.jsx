import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { setActiveLocale } from '../i18n';
import PromptDocumentEditor, { readPromptDocumentFile } from '../components/PromptDocumentEditor';
import ProfileDocuments from '../components/ProfileDocuments';
import MarkdownView from '../components/MarkdownView';
import ApiClient from '../utils/api';
beforeAll(async () => setActiveLocale('en'));
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});
const metadata = {
  id: 'pdc_test',
  name: 'schema.md',
  description: 'Read before analyzing logs',
  visibility: 'Private',
  active: true,
  version: 3,
  currentRevision: 2
};
const input = (id, value) => fireEvent.change(document.querySelector(`#${id}`), { target: { value } });
function client() {
  return {
    getPromptDocument: vi.fn().mockResolvedValue(metadata),
    getPromptDocumentReferrers: vi.fn().mockResolvedValue({ items: [] }),
    getPromptDocumentRevision: vi.fn().mockResolvedValue({ revision: 2, markdown: '# Schema\n{{literal}}' }),
    listPromptDocumentRevisions: vi.fn().mockResolvedValue({ items: [{ revision: 2 }], totalCount: 1 }),
    createPromptDocument: vi.fn().mockResolvedValue(metadata),
    updatePromptDocument: vi.fn().mockResolvedValue(metadata),
    listPromptDocuments: vi.fn().mockResolvedValue({ items: [metadata], totalCount: 1 }),
    listProfileDocuments: vi.fn().mockResolvedValue({ documentSetVersion: 7, items: [] }),
    attachProfileDocument: vi.fn().mockResolvedValue({}),
    previewProfileDocuments: vi.fn().mockResolvedValue({ compiledText: 'Reference catalog', manifest: [], segments: [], issues: [], estimatedTokens: 6 })
  };
}
describe('managed prompt documents', () => {
  it('imports exact UTF-8 and rejects malformed, binary, and oversized input', async () => {
    const file = (bytes, name = 'schema.md') => ({ name, size: bytes.length, arrayBuffer: async () => new Uint8Array(bytes).buffer });
    expect(await readPromptDocumentFile(file([...new TextEncoder().encode('\uFEFF# 日本語\r\n  literal {{x}}  \n')]))).toBe('# 日本語\n  literal {{x}}  \n');
    await expect(readPromptDocumentFile(file([255]))).rejects.toThrow();
    await expect(readPromptDocumentFile(file([0]))).rejects.toThrow('invalidFile');
    await expect(readPromptDocumentFile(file([65], 'file.bin'))).rejects.toThrow('invalidFile');
    await expect(readPromptDocumentFile({ name: 'big.md', size: 1048577 })).rejects.toThrow('oversizedFile');
  });
  it('creates literal Markdown with explicit visibility', async () => {
    const api = client();
    const onSaved = vi.fn();
    render(<PromptDocumentEditor apiClient={api} document={{}} onClose={() => {}} onSaved={onSaved} />);
    input('document-name', 'schema.md');
    input('document-description', 'Read logs');
    input('document-markdown', '# Schema\n{{tenant.secret}}');
    expect(document.querySelector('.markdown-body').textContent).toContain('{{tenant.secret}}');
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() =>
      expect(api.createPromptDocument).toHaveBeenCalledWith({
        name: 'schema.md',
        description: 'Read logs',
        markdown: '# Schema\n{{tenant.secret}}',
        visibility: 'Private',
        active: true
      })
    );
    expect(onSaved).toHaveBeenCalledWith(metadata);
    expect(api.updatePromptDocument).not.toHaveBeenCalled();
  });
  it('keeps a draft after version conflict and sends the loaded version', async () => {
    const api = client();
    api.updatePromptDocument.mockRejectedValue({ status: 409, message: 'Stale document' });
    render(<PromptDocumentEditor apiClient={api} document={metadata} onClose={() => {}} onSaved={() => {}} />);
    await waitFor(() => expect(document.querySelector('#document-markdown')?.value).toContain('Schema'));
    input('document-markdown', '# Unsaved work');
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await screen.findByRole('button', { name: 'Compare latest' });
    expect(document.querySelector('#document-markdown').value).toBe('# Unsaved work');
    expect(api.updatePromptDocument).toHaveBeenCalledWith('pdc_test', expect.objectContaining({ expectedVersion: 3, markdown: '# Unsaved work' }));
  });
  it('blocks embedded image requests and script markup', () => {
    render(<MarkdownView blockImages text={'# Safe\n![tracking](https://example.com/image.png)\n<script>alert(1)</script>'} />);
    expect(screen.getByRole('heading', { name: 'Safe' })).toBeTruthy();
    expect(document.querySelector('img')).toBeNull();
    expect(document.querySelector('script')).toBeNull();
  });
  it('attaches Reference by default with attachment-set concurrency', async () => {
    const api = client();
    render(
      <MemoryRouter>
        <ProfileDocuments apiClient={api} profileId="agp_test" />
      </MemoryRouter>
    );
    await waitFor(() => expect(screen.getByRole('button', { name: 'Attach document' }).disabled).toBe(false));
    fireEvent.click(screen.getByRole('button', { name: 'Attach document' }));
    await waitFor(() => expect(document.querySelector('#attachment-documentId option[value="pdc_test"]')).toBeTruthy());
    input('attachment-documentId', 'pdc_test');
    expect(document.querySelector('#attachment-deliveryMode').value).toBe('Reference');
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() =>
      expect(api.attachProfileDocument).toHaveBeenCalledWith(
        'agp_test',
        expect.objectContaining({ documentId: 'pdc_test', deliveryMode: 'Reference', expectedDocumentSetVersion: 7, allowPublicAccess: false })
      )
    );
  });
  it('previews Public using only the saved publishing credential', async () => {
    const api = client();
    render(
      <MemoryRouter>
        <ProfileDocuments apiClient={api} profileId="agp_test" />
      </MemoryRouter>
    );
    input('document-audience', 'Public');
    fireEvent.click(screen.getByRole('button', { name: 'Preview' }));
    await waitFor(() => expect(api.previewProfileDocuments).toHaveBeenCalledWith('agp_test', 'Public'));
    expect(await screen.findByText('Reference catalog')).toBeTruthy();
  });
  it('encodes transport IDs and preserves detach version tokens', async () => {
    const fetch = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async () => new Response('{}', { status: 200, headers: { 'content-type': 'application/json' } }));
    const api = new ApiClient('http://127.0.0.1:8800', 'fake-test-token');
    await api.detachProfileDocument('a/b', 'pda_one', 4, 7);
    expect(fetch.mock.calls[0][0]).toContain('/agent-profiles/a%2Fb/documents/pda_one?expectedVersion=4&expectedDocumentSetVersion=7');
    await api.previewProfileDocuments('agp_test', 'Public');
    expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual({ audience: 'Public' });
  });
});
