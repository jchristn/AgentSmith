import { initialChatState, beginTurn, applyChatEvent, endTurn } from './chatStream';

/** Only local standalone chat URLs may survive a login redirect. */
export function chatReturnPath(value) {
  return typeof value === 'string' && /^\/chat\/[A-Za-z0-9_-]+$/.test(value) ? value : null;
}

/** Standalone transcripts expose text and usage only, including restored messages. */
export function safeChatMessages(messages = []) {
  return messages.map((message) => ({ ...message, thinking: '', parts: (message.parts || []).filter((part) => part.kind === 'text') }));
}

export function standaloneChatReducer(state, action) {
  switch (action.type) {
    case 'reset':
      return initialChatState(action.messages || []);
    case 'begin':
      return beginTurn(state, action.text);
    case 'final':
      return {
        ...state,
        messages: state.messages.map((message, index) =>
          index === state.messages.length - 1 && message.role === 'assistant' ? { ...message, parts: [{ kind: 'text', text: action.text }] } : message
        )
      };
    case 'event':
      return applyChatEvent(state, action.evt);
    case 'end':
      return endTurn(state, action.options);
    default:
      return state;
  }
}
