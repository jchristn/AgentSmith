// Chat transcript state and the reducer that applies the turn stream (plan decision D-022) to it.
//
// The server streams named Server-Sent Events while a turn runs:
//   turn.started        { turnId?, messageId?, conversationId? }
//   assistant.delta     { text | delta }
//   assistant.thinking  { text | delta }
//   tool.proposed       { invocationId | id, toolName, resourceAlias | resource, effect, arguments }
//   tool.started        { invocationId | id }
//   tool.completed      { invocationId | id, status, durationMs, outputPreview, outputTruncated, error }
//   approval.required   { approvalId | id, invocationId, summary, effect, effectReason, command, resourceAlias, expiresUtc }
//   approval.resolved   { approvalId | id, status, decidedBy, note }
//   turn.completed      { messageId?, inputTokens, outputTokens, timeToFirstTokenMs, durationMs, finishReason }
//   error               { code | error, message }
// The reducer is pure, so it is unit tested without a server; the Chat page feeds it events as they arrive.

/** Empty transcript. */
export function initialChatState(messages = []) {
  return { messages, streaming: false, error: null, startedAt: null, firstTokenAt: null };
}

/** Convert stored messages (GET conversations/{id}/messages) into transcript messages. */
export function fromStoredMessages(items = []) {
  return items
    .filter((m) => m && (m.role === 'User' || m.role === 'Assistant' || m.role === 'user' || m.role === 'assistant'))
    .map((m) => {
      const role = String(m.role).toLowerCase();
      const parts = [];
      for (const tool of m.toolInvocations || m.tools || []) parts.push({ kind: 'tool', tool: normalizeTool(tool) });
      if (m.content) parts.push({ kind: 'text', text: m.content });
      return {
        id: m.id || `${role}-${m.sequence ?? Math.random()}`,
        role,
        parts,
        thinking: m.thinkingContent || '',
        status: 'done',
        usage: {
          inputTokens: m.inputTokens ?? null,
          outputTokens: m.outputTokens ?? null,
          timeToFirstTokenMs: m.timeToFirstTokenMs ?? null,
          durationMs: m.durationMs ?? null,
          finishReason: m.finishReason || null
        },
        createdUtc: m.createdUtc || null
      };
    });
}

function normalizeTool(data = {}) {
  return {
    id: data.invocationId || data.id || data.toolCallId || null,
    toolName: data.toolName || data.name || data.tool || '',
    resourceAlias: data.resourceAlias || data.resource || data.alias || '',
    effect: data.effect || null,
    effectReason: data.effectReason || '',
    status: data.status || 'proposed',
    durationMs: data.durationMs ?? null,
    arguments: data.arguments ?? data.argumentsRedacted ?? data.args ?? null,
    outputPreview: data.outputPreview ?? data.output ?? null,
    outputTruncated: !!data.outputTruncated,
    error: data.error || data.errorMessage || null,
    approvalId: data.approvalId || null
  };
}

function normalizeApproval(data = {}) {
  return {
    id: data.approvalId || data.id || null,
    invocationId: data.invocationId || data.toolInvocationId || null,
    summary: data.summary || '',
    effect: data.effect || null,
    effectReason: data.effectReason || '',
    command: data.command || data.statement || data.detail || '',
    resourceAlias: data.resourceAlias || data.resource || '',
    toolName: data.toolName || '',
    status: data.status || 'Pending',
    expiresUtc: data.expiresUtc || null,
    note: data.note || null,
    decidedBy: data.decidedBy || null
  };
}

function textOf(data) {
  if (typeof data === 'string') return data;
  return data?.text ?? data?.delta ?? data?.content ?? '';
}

/** The assistant message being streamed (the last message, when it is an assistant message that is still open). */
function updateCurrent(state, fn) {
  const messages = [...state.messages];
  let index = messages.length - 1;
  if (index < 0 || messages[index].role !== 'assistant' || messages[index].status !== 'streaming') {
    messages.push({ id: `assistant-${messages.length}`, role: 'assistant', parts: [], thinking: '', status: 'streaming', usage: {} });
    index = messages.length - 1;
  }
  messages[index] = fn({ ...messages[index], parts: [...messages[index].parts] });
  return { ...state, messages };
}

function updateTool(message, id, patch) {
  const parts = message.parts.map((p) => (p.kind === 'tool' && p.tool.id === id ? { ...p, tool: { ...p.tool, ...patch } } : p));
  return { ...message, parts };
}

/** Add the operator's message and open the assistant reply (called when Send is pressed). */
export function beginTurn(state, text, now = Date.now()) {
  const messages = [
    ...state.messages,
    { id: `user-${state.messages.length}`, role: 'user', parts: [{ kind: 'text', text }], thinking: '', status: 'done', usage: {} }
  ];
  messages.push({ id: `assistant-${messages.length}`, role: 'assistant', parts: [], thinking: '', status: 'streaming', usage: {} });
  return { ...state, messages, streaming: true, error: null, startedAt: now, firstTokenAt: null };
}

/** Apply one stream event. `now` is injectable for tests (time to first token). */
export function applyChatEvent(state, evt, now = Date.now()) {
  const name = evt?.event;
  const data = evt?.data ?? {};
  switch (name) {
    case 'turn.started':
      return updateCurrent({ ...state, streaming: true }, (m) => ({ ...m, id: data.messageId || m.id, turnId: data.turnId || null }));
    case 'assistant.delta': {
      const text = textOf(data);
      if (!text) return state;
      const next = updateCurrent(state, (m) => {
        const last = m.parts[m.parts.length - 1];
        if (last && last.kind === 'text') m.parts[m.parts.length - 1] = { ...last, text: last.text + text };
        else m.parts.push({ kind: 'text', text });
        return m;
      });
      return state.firstTokenAt ? next : { ...next, firstTokenAt: now };
    }
    case 'assistant.thinking': {
      const text = textOf(data);
      const next = updateCurrent(state, (m) => ({ ...m, thinking: (m.thinking || '') + text }));
      return state.firstTokenAt ? next : { ...next, firstTokenAt: now };
    }
    case 'tool.proposed': {
      const tool = normalizeTool({ ...data, status: 'proposed' });
      return updateCurrent(state, (m) => {
        if (tool.id && m.parts.some((p) => p.kind === 'tool' && p.tool.id === tool.id)) return updateTool(m, tool.id, tool);
        m.parts.push({ kind: 'tool', tool });
        return m;
      });
    }
    case 'tool.started': {
      const id = data.invocationId || data.id;
      return updateCurrent(state, (m) => {
        if (!m.parts.some((p) => p.kind === 'tool' && p.tool.id === id)) {
          m.parts.push({ kind: 'tool', tool: normalizeTool({ ...data, status: 'running' }) });
          return m;
        }
        return updateTool(m, id, { status: 'running' });
      });
    }
    case 'tool.completed': {
      const tool = normalizeTool({ ...data, status: data.status || 'Succeeded' });
      return updateCurrent(state, (m) => {
        if (!m.parts.some((p) => p.kind === 'tool' && p.tool.id === tool.id)) {
          m.parts.push({ kind: 'tool', tool });
          return m;
        }
        const patch = {
          status: tool.status,
          durationMs: tool.durationMs,
          outputPreview: tool.outputPreview,
          outputTruncated: tool.outputTruncated,
          error: tool.error
        };
        return updateTool(m, tool.id, patch);
      });
    }
    case 'approval.required': {
      const approval = normalizeApproval(data);
      return updateCurrent(state, (m) => {
        m.parts.push({ kind: 'approval', approval });
        return approval.invocationId ? updateTool(m, approval.invocationId, { status: 'awaitingApproval', approvalId: approval.id }) : m;
      });
    }
    case 'approval.resolved': {
      const resolved = normalizeApproval(data);
      const messages = state.messages.map((m) => ({
        ...m,
        parts: m.parts.map((p) =>
          p.kind === 'approval' && p.approval.id === resolved.id
            ? { ...p, approval: { ...p.approval, status: resolved.status, note: resolved.note, decidedBy: resolved.decidedBy } }
            : p
        )
      }));
      return { ...state, messages };
    }
    case 'turn.completed': {
      const next = updateCurrent(state, (m) => ({
        ...m,
        id: data.messageId || m.id,
        status: 'done',
        usage: {
          inputTokens: data.inputTokens ?? null,
          outputTokens: data.outputTokens ?? null,
          timeToFirstTokenMs: data.timeToFirstTokenMs ?? (state.firstTokenAt && state.startedAt ? state.firstTokenAt - state.startedAt : null),
          durationMs: data.durationMs ?? (state.startedAt ? now - state.startedAt : null),
          finishReason: data.finishReason || null
        }
      }));
      return { ...next, streaming: false };
    }
    case 'error': {
      const error = { code: data.code || data.error || 'StreamError', message: data.message || textOf(data) || '' };
      const next = updateCurrent(state, (m) => ({ ...m, status: 'error', error }));
      return { ...next, streaming: false, error };
    }
    case 'message': {
      // A non-streaming JSON reply: { message | content, toolInvocations, ... }.
      const content = data?.message?.content ?? data?.content ?? (typeof data === 'string' ? data : '');
      const next = updateCurrent(state, (m) => {
        for (const tool of data?.toolInvocations || []) m.parts.push({ kind: 'tool', tool: normalizeTool(tool) });
        if (content) m.parts.push({ kind: 'text', text: content });
        return { ...m, status: 'done', usage: { inputTokens: data?.inputTokens ?? null, outputTokens: data?.outputTokens ?? null } };
      });
      return { ...next, streaming: false };
    }
    default:
      return state;
  }
}

/** Close the open assistant message after the stream ended or was stopped. */
export function endTurn(state, { stopped = false, error = null } = {}) {
  const last = state.messages[state.messages.length - 1];
  if (!last || last.role !== 'assistant' || last.status !== 'streaming') return { ...state, streaming: false, error: error || state.error };
  const messages = [...state.messages];
  messages[messages.length - 1] = { ...last, status: error ? 'error' : stopped ? 'stopped' : 'done', error: error || last.error };
  return { ...state, messages, streaming: false, error: error || state.error };
}

/** Plain text of a message (its text parts joined). */
export function messageText(message) {
  return (message?.parts || [])
    .filter((p) => p.kind === 'text')
    .map((p) => p.text)
    .join('');
}

/** Pending approvals across the transcript (for the context drawer). */
export function pendingApprovals(state) {
  return state.messages.flatMap((m) => m.parts.filter((p) => p.kind === 'approval' && p.approval.status === 'Pending').map((p) => p.approval));
}
