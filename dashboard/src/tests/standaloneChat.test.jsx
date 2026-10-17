import { describe, expect, it } from 'vitest';
import { chatReturnPath, safeChatMessages } from '../utils/standaloneChat';
describe('standalone chat boundary', () => {
  it('only accepts local opaque chat paths after login', () => {
    expect(chatReturnPath('/chat/opaque_123-abc')).toBe('/chat/opaque_123-abc');
    for (const path of [
      'https://example.com/chat/abc',
      '//example.com/chat/a',
      '/dashboard/home',
      '/chat/a?next=evil',
      '/chat/../admin',
      '/chat/a/b',
      '/chat/%2F%2Fevil',
      null
    ])
      expect(chatReturnPath(path)).toBeNull();
  });
  it('removes internal traces without mutating the restored transcript', () => {
    const messages = [
      {
        role: 'assistant',
        thinking: 'private reasoning',
        parts: [
          { kind: 'tool', tool: { outputPreview: 'secret result' } },
          { kind: 'approval', approval: {} },
          { kind: 'text', text: 'Safe answer' }
        ],
        usage: { outputTokens: 3 }
      }
    ];
    expect(safeChatMessages(messages)).toEqual([{ ...messages[0], thinking: '', parts: [{ kind: 'text', text: 'Safe answer' }] }]);
    expect(messages[0].thinking).toBe('private reasoning');
    expect(messages[0].parts).toHaveLength(3);
  });
});
