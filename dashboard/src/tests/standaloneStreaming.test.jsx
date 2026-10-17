import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { setActiveLocale } from '../i18n';
import StandaloneChatView from '../views/StandaloneChatView';

vi.mock('../context/AuthContext', () => ({
  useAuth: () => ({ isAuthenticated: false, isLoading: false, token: null, principal: null, theme: 'light', toggleTheme: () => {} })
}));

beforeAll(async () => {
  await setActiveLocale('en');
});
beforeEach(() => sessionStorage.clear());
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

function chatTransport() {
  let stream;
  let signal;
  const fetchMock = vi.fn(async (url, options = {}) => {
    const path = new URL(url, 'http://127.0.0.1').pathname;
    if (path.endsWith('/feedback')) return Response.json(JSON.parse(options.body));
    if (path.endsWith('/cancel')) return Response.json({ cancelled: true });
    if (path.endsWith('/messages')) {
      signal = options.signal;
      return new Response(
        new ReadableStream({
          start(controller) {
            stream = controller;
            signal.addEventListener('abort', () => controller.error(new DOMException('Aborted', 'AbortError')), { once: true });
          }
        }),
        { headers: { 'content-type': 'text/event-stream' } }
      );
    }
    if (path.endsWith('/conversations')) return Response.json({ id: 'conv_visitor', sessionToken: 'visitor-capability' });
    return Response.json({ name: 'Support', accessMode: 'Public', authenticationRequired: false });
  });
  vi.stubGlobal('fetch', fetchMock);
  return {
    fetchMock,
    get signal() {
      return signal;
    },
    emit(event, data) {
      stream.enqueue(new TextEncoder().encode(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`));
    },
    close() {
      stream.close();
    }
  };
}

async function startChat() {
  render(
    <MemoryRouter initialEntries={['/chat/link_opaque']}>
      <Routes>
        <Route path="/chat/:linkId" element={<StandaloneChatView />} />
      </Routes>
    </MemoryRouter>
  );
  const input = await screen.findByTestId('chat-input');
  fireEvent.change(input, { target: { value: 'Please explain streaming' } });
  fireEvent.click(screen.getByTestId('chat-send'));
  await screen.findByTestId('chat-stop');
}

describe('standalone streaming over the real API client', () => {
  it('renders partial Markdown before completion, hides internal events, and saves feedback against the final persisted ID', async () => {
    const transport = chatTransport();
    await startChat();
    await waitFor(() => expect(transport.signal).toBeTruthy());
    const [, request] = transport.fetchMock.mock.calls.find(([url]) => url.endsWith('/messages'));
    expect(request.headers.Accept).toBe('text/event-stream');
    expect(request.headers['x-hugo-chat-session']).toBe('visitor-capability');
    expect(request.headers.Authorization).toBeUndefined();
    expect(JSON.parse(request.body)).toEqual({ message: 'Please explain streaming' });
    await act(async () => {
      transport.emit('turn.started', { conversationId: 'conv_visitor' });
      transport.emit('assistant.thinking', { text: 'PRIVATE REASONING' });
      transport.emit('tool.completed', { outputPreview: 'PRIVATE TOOL OUTPUT', toolName: 'internal_tool' });
      transport.emit('assistant.delta', { text: '**Partial answer**' });
    });
    expect((await screen.findByText('Partial answer')).tagName).toBe('STRONG');
    expect(screen.getByTestId('chat-stop')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Helpful response' })).toBeNull();
    expect(document.body.textContent).not.toContain('PRIVATE');
    expect(document.body.textContent).not.toContain('internal_tool');
    await act(async () => {
      transport.emit('assistant.delta', { text: ' with the rest.' });
      transport.emit('turn.completed', { messageId: 'msg_persisted', text: '**Partial answer** with the rest. Final held word.', tokensIn: 10, tokensOut: 7 });
      transport.close();
    });
    expect(await screen.findByText('with the rest.', { exact: false })).toBeTruthy();
    expect(await screen.findByText('Final held word.', { exact: false })).toBeTruthy();
    fireEvent.click(await screen.findByRole('button', { name: 'Helpful response' }));
    await waitFor(() => expect(transport.fetchMock.mock.calls.some(([url]) => url.endsWith('/messages/msg_persisted/feedback'))).toBe(true));
    expect(screen.queryByTestId('chat-stop')).toBeNull();
  });

  it('Stop aborts the open fetch and cancels the same capability-scoped conversation', async () => {
    const transport = chatTransport();
    await startChat();
    await waitFor(() => expect(transport.signal).toBeTruthy());
    await act(async () => transport.emit('assistant.delta', { text: 'Partial before stopping' }));
    await screen.findByText('Partial before stopping');
    fireEvent.click(screen.getByTestId('chat-stop'));
    await waitFor(() => expect(transport.signal.aborted).toBe(true));
    await waitFor(() => expect(screen.queryByTestId('chat-stop')).toBeNull());
    expect(screen.getByText('Partial before stopping')).toBeTruthy();
    const [url, request] = transport.fetchMock.mock.calls.find(([path]) => path.endsWith('/cancel'));
    expect(url).toContain('/chat/link_opaque/conversations/conv_visitor/cancel');
    expect(request.headers['x-hugo-chat-session']).toBe('visitor-capability');
    expect(screen.queryByRole('button', { name: 'Helpful response' })).toBeNull();
  });
});
