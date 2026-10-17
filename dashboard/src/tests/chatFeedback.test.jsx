import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { setActiveLocale } from '../i18n';
import ChatMessageFeedback from '../components/ChatMessageFeedback';
import ChatPanel from '../components/ChatPanel';
import { fromStoredMessages } from '../utils/chatStream';

beforeAll(async () => {
  await setActiveLocale('en');
});
afterEach(cleanup);

describe('response feedback', () => {
  it('saves ratings against the assistant message and allows changing or clearing a rating', async () => {
    const save = vi.fn(async (_id, value) => value);
    render(<ChatMessageFeedback messageId="msg_123" onFeedback={save} />);
    fireEvent.click(screen.getByRole('button', { name: 'Helpful response' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Helpful response' }).getAttribute('aria-pressed')).toBe('true'));
    expect(save).toHaveBeenLastCalledWith('msg_123', { rating: 'Positive', comment: '' });
    fireEvent.click(screen.getByRole('button', { name: 'Unhelpful response' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Unhelpful response' }).getAttribute('aria-pressed')).toBe('true'));
    fireEvent.click(screen.getByRole('button', { name: 'Unhelpful response' }));
    await waitFor(() => expect(save).toHaveBeenLastCalledWith('msg_123', { rating: null, comment: '' }));
  });

  it('saves comment-only feedback and retains a failed draft for retry', async () => {
    const save = vi
      .fn()
      .mockRejectedValueOnce(new Error('offline'))
      .mockImplementation(async (_id, value) => value);
    render(<ChatMessageFeedback messageId="msg_123" onFeedback={save} />);
    fireEvent.click(screen.getByRole('button', { name: 'Add a comment' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Your feedback' }), { target: { value: 'Please cite the table name.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save feedback' }));
    await screen.findByRole('alert');
    expect(screen.getByRole('textbox', { name: 'Your feedback' }).value).toBe('Please cite the table name.');
    fireEvent.click(screen.getByRole('button', { name: 'Save feedback' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(save).toHaveBeenLastCalledWith('msg_123', { rating: null, comment: 'Please cite the table name.' });
    expect(screen.getByText('Please cite the table name.')).toBeTruthy();
  });

  it('shows stored feedback to operators without exposing visitor editing controls', () => {
    const messages = fromStoredMessages([
      { id: 'msg_1', role: 'Assistant', content: 'Answer', feedbackRating: 'Negative', feedbackComment: 'Missing details' }
    ]);
    render(<ChatPanel messages={messages} />);
    expect(screen.getByText('Missing details')).toBeTruthy();
    expect(screen.getByText('Response feedback: Not helpful')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Helpful response' })).toBeNull();
  });

  it('only renders editable feedback on completed assistant messages', () => {
    render(
      <ChatPanel
        onFeedback={vi.fn()}
        messages={[
          { id: 'u', role: 'user', parts: [{ kind: 'text', text: 'Question' }], status: 'done' },
          { id: 'a', role: 'assistant', parts: [], status: 'streaming' }
        ]}
      />
    );
    expect(screen.queryByRole('button', { name: 'Helpful response' })).toBeNull();
  });
});
