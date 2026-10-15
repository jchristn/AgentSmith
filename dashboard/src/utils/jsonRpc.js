// JSON-RPC helpers for the MCP and A2A explorers: request building, response parsing (plain JSON or a Server-Sent
// Events body, as MCP streamable HTTP and A2A streaming use), and conversion of a tool's JSON Schema into the
// setting descriptors SchemaForm renders.

import { parseSseEvent } from './api';

let nextId = 1;

/** A JSON-RPC 2.0 request with a fresh numeric id (or a notification when `notification` is set). */
export function rpcRequest(method, params, { notification = false } = {}) {
  const msg = { jsonrpc: '2.0', method };
  if (params !== undefined) msg.params = params;
  if (!notification) msg.id = nextId++;
  return msg;
}

/**
 * Read a fetch Response that carries JSON-RPC: a JSON body (one message or a batch), or an SSE stream of messages.
 * Calls onMessage for every message as it arrives (streaming) and resolves to the list of messages.
 */
export async function readRpcResponse(response, onMessage) {
  const type = response.headers?.get?.('content-type') || '';
  const messages = [];
  const push = (m) => {
    if (m === null || m === undefined) return;
    if (Array.isArray(m)) m.forEach(push);
    else {
      messages.push(m);
      onMessage?.(m);
    }
  };
  if (type.includes('text/event-stream') && response.body?.getReader) {
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';
    for (;;) {
      const { value, done } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true }).replace(/\r\n/g, '\n');
      const frames = buffer.split('\n\n');
      buffer = frames.pop() || '';
      for (const frame of frames) push(parseSseEvent(frame)?.data);
    }
    if (buffer.trim()) push(parseSseEvent(buffer)?.data);
    return messages;
  }
  const text = await response.text();
  if (!text) return messages;
  try {
    push(JSON.parse(text));
  } catch {
    // An SSE body without the content type still parses frame by frame.
    for (const frame of text.replace(/\r\n/g, '\n').split('\n\n')) push(parseSseEvent(frame)?.data);
  }
  return messages;
}

/** The message answering request `id`, or the last message. */
export function responseFor(messages, id) {
  return messages.find((m) => m && m.id === id) || messages[messages.length - 1] || null;
}

/** Group tools by family: the part of the name before the first underscore (hugo_, sql_, ssh_, and so on). */
export function groupToolsByFamily(tools) {
  const groups = new Map();
  for (const tool of tools || []) {
    const name = String(tool?.name || '');
    const family = name.includes('__') ? name.split('__')[0] : name.includes('_') ? name.split('_')[0] : name;
    if (!groups.has(family)) groups.set(family, []);
    groups.get(family).push(tool);
  }
  return [...groups.entries()].map(([family, list]) => ({ family, tools: list }));
}

/** Convert a JSON Schema object (a tool's inputSchema) into SchemaForm setting descriptors. */
export function jsonSchemaToDescriptors(schema) {
  const props = schema?.properties || {};
  const required = new Set(schema?.required || []);
  return Object.entries(props).map(([key, p]) => {
    const type = Array.isArray(p.type) ? p.type.find((x) => x !== 'null') : p.type;
    let kind = 'Text';
    let allowedValues = [];
    if (Array.isArray(p.enum)) {
      kind = 'Enum';
      allowedValues = p.enum.map(String);
    } else if (type === 'string') kind = 'String';
    else if (type === 'integer' || type === 'number') kind = 'Integer';
    else if (type === 'boolean') kind = 'Boolean';
    else if (type === 'array' && (p.items?.type === 'string' || !p.items)) kind = 'StringList';
    return {
      key,
      label: p.title || key,
      help: p.description || '',
      kind,
      required: required.has(key),
      defaultValue: p.default !== undefined ? (typeof p.default === 'object' ? JSON.stringify(p.default) : String(p.default)) : null,
      minimum: p.minimum ?? null,
      maximum: p.maximum ?? null,
      allowedValues,
      pattern: p.pattern || null,
      group: 'General',
      jsonType: type || 'object'
    };
  });
}

/**
 * Tool call arguments from SchemaForm values: numbers stay numbers (integers or decimals), Text fields whose schema
 * type is an object or array are parsed as JSON. Throws SyntaxError with the field key when JSON does not parse.
 */
export function argumentsFromValues(descriptors, values) {
  const out = {};
  for (const d of descriptors) {
    const v = values?.[d.key];
    if (v === undefined || v === null || v === '' || (Array.isArray(v) && v.length === 0)) continue;
    if (d.kind === 'Integer') out[d.key] = Number(v);
    else if (d.kind === 'Boolean') out[d.key] = !!v;
    else if (d.kind === 'StringList') out[d.key] = v.map((s) => String(s).trim()).filter(Boolean);
    else if (d.kind === 'Text' && d.jsonType !== 'string') {
      try {
        out[d.key] = JSON.parse(v);
      } catch {
        throw new SyntaxError(d.key);
      }
    } else out[d.key] = String(v);
  }
  return out;
}
