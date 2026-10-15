// Pure logic behind the shared components: connector settings schemas, secret kinds, the chat stream reducer, the
// event stream client, template rendering, JSON-RPC helpers, and analytics report series.

import { describe, it, expect, vi } from 'vitest';
import { normalizeSchema, groupSchema, initialValues, validateSchemaValues, toSubmitValues, kindOf, schemaSummary } from '../utils/schemaForm';
import { secretFieldsFor, validateSecretValues, secretValuesBody, describeSecretFile, secretsForSlot, SECRET_FIELDS } from '../utils/secrets';
import { initialChatState, beginTurn, applyChatEvent, endTurn, fromStoredMessages, messageText, pendingApprovals } from '../utils/chatStream';
import { createEventStream } from '../utils/eventStream';
import { extractVariables, renderTemplateLocal, insertAtCursor } from '../utils/templates';
import { rpcRequest, readRpcResponse, responseFor, groupToolsByFamily, jsonSchemaToDescriptors, argumentsFromValues } from '../utils/jsonRpc';
import { reportSeries, analyticsQuery, chartMax, niceTicks } from '../utils/analytics';
import { registerModal, isModalOpen } from '../utils/modalState';
import { ENUMS } from '../utils/constants';

// A settings schema as Hugo.Connectors.Abstractions serializes it (camelCase, string enums).
const SSH_SCHEMA = {
  settings: [
    {
      key: 'host',
      label: 'Host',
      help: 'Hostname or IP',
      kind: 'String',
      required: true,
      defaultValue: null,
      minimum: null,
      maximum: null,
      allowedValues: [],
      pattern: '^[A-Za-z0-9.-]+$',
      group: 'Connection',
      showInSummary: true
    },
    {
      key: 'port',
      label: 'Port',
      help: '',
      kind: 'Integer',
      required: true,
      defaultValue: '22',
      minimum: 1,
      maximum: 65535,
      allowedValues: [],
      pattern: null,
      group: 'Connection',
      showInSummary: true
    },
    { key: 'username', label: 'User', kind: 'String', required: true, group: 'Authentication' },
    { key: 'strictHostKey', label: 'Verify host key', kind: 'Boolean', defaultValue: 'true', group: 'Authentication' },
    { key: 'allowedPaths', label: 'Allowed paths', kind: 'StringList', defaultValue: '/var/log,/etc', group: 'Limits' },
    { key: 'shell', label: 'Shell', kind: 'Enum', allowedValues: ['bash', 'sh'], defaultValue: 'bash', group: 'Advanced' },
    { key: 'banner', label: 'Banner', kind: 'Text', maximum: 10 }
  ]
};

describe('connector settings schemas', () => {
  const settings = normalizeSchema(SSH_SCHEMA);
  it('normalizes descriptors and groups them in declared order', () => {
    expect(settings.map((s) => s.key)).toEqual(['host', 'port', 'username', 'strictHostKey', 'allowedPaths', 'shell', 'banner']);
    expect(groupSchema(settings).map((g) => g.name)).toEqual(['Connection', 'Authentication', 'Limits', 'Advanced', 'General']);
    expect(kindOf({ kind: 2 })).toBe('Integer');
    expect(kindOf({ kind: 'stringlist' })).toBe('StringList');
    expect(kindOf({})).toBe('String');
    expect(normalizeSchema(null)).toEqual([]);
    for (const k of ENUMS.settingValueKind) expect(kindOf({ kind: k })).toBe(k);
  });
  it('fills defaults with the right types and keeps stored values', () => {
    const v = initialValues(settings, { host: 'web-01' });
    expect(v).toMatchObject({ host: 'web-01', port: 22, username: '', strictHostKey: true, allowedPaths: ['/var/log', '/etc'], shell: 'bash', banner: '' });
  });
  it('validates required, integer, range, enum, pattern, and length', () => {
    const errors = validateSchemaValues(settings, { host: 'bad host!', port: 70000, username: '', shell: 'zsh', allowedPaths: [], banner: 'x'.repeat(11) });
    expect(errors).toEqual({
      host: { code: 'PatternMismatch', params: { pattern: '^[A-Za-z0-9.-]+$' } },
      port: { code: 'OutOfRange', params: { minimum: 1, maximum: 65535 } },
      username: { code: 'Required', params: {} },
      shell: { code: 'NotAllowedValue', params: { values: 'bash, sh' } },
      banner: { code: 'TooLong', params: { maximum: 10 } }
    });
    expect(validateSchemaValues(settings, { host: 'h', port: 1.5, username: 'u' }).port.code).toBe('NotAnInteger');
    expect(validateSchemaValues(settings, initialValues(settings, { host: 'h', username: 'root' }))).toEqual({});
  });
  it('converts to typed request values and summarizes', () => {
    const values = { host: 'web-01', port: '2222', username: 'root', strictHostKey: false, allowedPaths: [' /a ', ''], shell: 'sh', banner: '' };
    expect(toSubmitValues(settings, values)).toEqual({ host: 'web-01', port: 2222, username: 'root', strictHostKey: false, allowedPaths: ['/a'], shell: 'sh' });
    expect(schemaSummary(settings, values)).toBe('web-01 2222');
  });
});

describe('secret kinds', () => {
  it('defines fields for every secret kind', () => {
    for (const kind of ENUMS.secretKind) expect(SECRET_FIELDS[kind], kind).toBeTruthy();
    expect(secretFieldsFor('AccessKeyPair').map((f) => f.name)).toEqual(['accessKeyId', 'secretAccessKey', 'sessionToken']);
    expect(secretFieldsFor('Nope').map((f) => f.name)).toEqual(['value']);
  });
  it('requires every required field on create, and any one field on replace', () => {
    expect(validateSecretValues('Password', { username: 'u' })).toEqual({ password: 'required' });
    expect(validateSecretValues('Password', { password: 'p' })).toEqual({});
    expect(validateSecretValues('PrivateKey', {}, { partial: true })).toEqual({ _form: 'noValues' });
    expect(validateSecretValues('PrivateKey', { passphrase: 'x' }, { partial: true })).toEqual({});
  });
  it('sends only filled fields, keeping whitespace in keys', () => {
    expect(secretValuesBody('PrivateKey', { privateKey: 'KEY\n', passphrase: '' })).toEqual({ privateKey: 'KEY\n' });
  });
  it('describes imported files without exposing key material', () => {
    expect(describeSecretFile('privateKey', '-----BEGIN OPENSSH PRIVATE KEY-----\nabc')).toEqual({ kind: 'pem', type: 'OPENSSH PRIVATE KEY' });
    expect(describeSecretFile('json', '{"project_id":"p","client_email":"a@b","private_key":"x"}')).toEqual({
      kind: 'serviceAccount',
      projectId: 'p',
      clientEmail: 'a@b'
    });
    expect(describeSecretFile('json', '{')).toEqual({ kind: 'invalidJson' });
    expect(
      describeSecretFile('kubeconfig', 'apiVersion: v1\ncontexts:\n- context:\n    cluster: c\n  name: prod\n- name: dev\ncurrent-context: prod\n').contexts
    ).toEqual(['prod', 'dev']);
  });
  it('filters secrets by the kinds a slot accepts', () => {
    const secrets = [
      { id: 'a', kind: 'Password' },
      { id: 'b', kind: 'PrivateKey' }
    ];
    expect(secretsForSlot(secrets, { acceptedKinds: ['PrivateKey'] }).map((s) => s.id)).toEqual(['b']);
    expect(secretsForSlot(secrets, { acceptedKinds: [] })).toHaveLength(2);
  });
});

describe('chat stream reducer (D-022 events)', () => {
  const ev = (event, data) => ({ event, data });
  it('builds a streamed turn with interleaved text, tool rows, and an approval', () => {
    let s = beginTurn(initialChatState(), 'Why is checkout slow?', 1000);
    expect(s.streaming).toBe(true);
    s = applyChatEvent(s, ev('turn.started', { messageId: 'msg_2' }));
    s = applyChatEvent(s, ev('assistant.thinking', { text: 'Check logs.' }), 1100);
    s = applyChatEvent(
      s,
      ev('tool.proposed', { invocationId: 't1', toolName: 'papertrail_search', resourceAlias: 'papertrail', effect: 'Read', arguments: { q: 'checkout' } })
    );
    s = applyChatEvent(s, ev('tool.started', { invocationId: 't1' }));
    s = applyChatEvent(s, ev('tool.completed', { invocationId: 't1', status: 'Succeeded', durationMs: 120, outputPreview: '3 errors' }));
    s = applyChatEvent(s, ev('assistant.delta', { text: 'Mr. Anderson, ' }), 1200);
    s = applyChatEvent(s, ev('assistant.delta', { delta: 'the database is busy.' }));
    s = applyChatEvent(s, ev('tool.proposed', { invocationId: 't2', toolName: 'ssh_exec', resourceAlias: 'web-01', effect: 'Destructive' }));
    s = applyChatEvent(
      s,
      ev('approval.required', {
        approvalId: 'apr_1',
        invocationId: 't2',
        summary: 'Restart nginx on web-01',
        effect: 'Destructive',
        command: 'sudo systemctl restart nginx'
      })
    );
    expect(pendingApprovals(s).map((a) => a.id)).toEqual(['apr_1']);
    s = applyChatEvent(s, ev('approval.resolved', { approvalId: 'apr_1', status: 'Approved', decidedBy: 'anderson' }));
    s = applyChatEvent(s, ev('tool.completed', { invocationId: 't2', status: 'Succeeded', durationMs: 900 }));
    s = applyChatEvent(s, ev('turn.completed', { inputTokens: 100, outputTokens: 20 }), 2000);
    const reply = s.messages[1];
    expect(s.streaming).toBe(false);
    expect(reply.id).toBe('msg_2');
    expect(reply.thinking).toBe('Check logs.');
    expect(reply.parts.map((p) => p.kind)).toEqual(['tool', 'text', 'tool', 'approval']);
    expect(reply.parts[0].tool).toMatchObject({ status: 'Succeeded', durationMs: 120, outputPreview: '3 errors' });
    expect(reply.parts[2].tool).toMatchObject({ status: 'Succeeded', approvalId: 'apr_1' });
    expect(reply.parts[3].approval).toMatchObject({ status: 'Approved', decidedBy: 'anderson' });
    expect(messageText(reply)).toBe('Mr. Anderson, the database is busy.');
    expect(reply.usage).toMatchObject({ inputTokens: 100, outputTokens: 20, timeToFirstTokenMs: 100, durationMs: 1000 });
    expect(pendingApprovals(s)).toEqual([]);
  });
  it('records stream errors and stops', () => {
    let s = beginTurn(initialChatState(), 'hi');
    s = applyChatEvent(s, ev('error', { code: 'NoInferenceEndpoint', message: 'No model' }));
    expect(s.streaming).toBe(false);
    expect(s.error).toEqual({ code: 'NoInferenceEndpoint', message: 'No model' });
    expect(s.messages[1].status).toBe('error');
  });
  it('closes an open turn when stopped and ignores unknown events', () => {
    let s = beginTurn(initialChatState(), 'hi');
    s = applyChatEvent(s, ev('something.else', {}));
    s = endTurn(s, { stopped: true });
    expect(s.messages[1].status).toBe('stopped');
    expect(endTurn(s).streaming).toBe(false);
  });
  it('accepts a non-streaming JSON reply', () => {
    let s = beginTurn(initialChatState(), 'hi');
    s = applyChatEvent(s, ev('message', { content: 'Hello', toolInvocations: [{ id: 't', toolName: 'hugo_whoami', status: 'Succeeded' }] }));
    expect(messageText(s.messages[1])).toBe('Hello');
    expect(s.messages[1].parts[0].tool.toolName).toBe('hugo_whoami');
  });
  it('converts stored messages', () => {
    const msgs = fromStoredMessages([
      { id: 'm1', role: 'User', content: 'hi' },
      { id: 'm2', role: 'Assistant', content: 'hello', thinkingContent: 't', outputTokens: 3 },
      { id: 'm3', role: 'Tool', content: 'x' }
    ]);
    expect(msgs.map((m) => m.role)).toEqual(['user', 'assistant']);
    expect(msgs[1].usage.outputTokens).toBe(3);
  });
});

describe('event stream client', () => {
  function fakeTimers() {
    const timers = [];
    return {
      setTimer: (fn, ms) => {
        timers.push({ fn, ms });
        return timers.length;
      },
      clearTimer: () => {},
      fire: (ms) => timers.filter((x) => x.ms === ms).forEach((x) => x.fn()),
      timers
    };
  }
  it('dispatches named events while live', () => {
    let handlers;
    const modes = [];
    const s = createEventStream({ connect: (h) => ((handlers = h), { abort: vi.fn() }), onMode: (m) => modes.push(m) });
    const seen = [];
    s.on('approval.created', (d) => seen.push(d.id));
    s.on('*', (d, e) => seen.push(e.event));
    s.start();
    handlers.onOpen();
    handlers.onEvent({ event: 'approval.created', data: { id: 'apr_1' } });
    handlers.onEvent({ event: 'ping', data: null });
    expect(seen).toEqual(['apr_1', 'approval.created']);
    expect(modes).toEqual(['connecting', 'live']);
    s.stop();
    expect(s.mode).toBe('stopped');
  });
  it('falls back to polling for good when the server has no event stream', () => {
    const t = fakeTimers();
    const poll = vi.fn();
    let handlers;
    let connects = 0;
    const s = createEventStream({
      connect: (h) => ((handlers = h), connects++, { abort() {} }),
      poll,
      pollMs: 100,
      setTimer: t.setTimer,
      clearTimer: t.clearTimer
    });
    s.start();
    handlers.onEnd({ status: 404 });
    expect(s.mode).toBe('polling');
    expect(poll).toHaveBeenCalledTimes(1);
    t.fire(100);
    expect(poll).toHaveBeenCalledTimes(2);
    expect(connects).toBe(1);
    expect(t.timers.some((x) => x.ms !== 100)).toBe(false);
  });
  it('retries a dropped stream with backoff while polling', () => {
    const t = fakeTimers();
    let connects = 0;
    let handlers;
    const s = createEventStream({
      connect: (h) => ((handlers = h), connects++, { abort() {} }),
      poll: () => {},
      pollMs: 100,
      setTimer: t.setTimer,
      clearTimer: t.clearTimer
    });
    s.start();
    handlers.onEnd({ status: 0 });
    const retry = t.timers.find((x) => x.ms === 2000);
    expect(retry).toBeTruthy();
    retry.fn();
    expect(connects).toBe(2);
  });
});

describe('templates', () => {
  it('extracts variables', () => {
    expect(
      extractVariables('Hi {{user.name}} {{x | default: "y"}} {{#if task}}{{task}}{{else}}none{{/if}} {{#each items}}{{this.a}}{{/each}} {{> footer}}')
    ).toEqual(['items', 'task', 'user.name', 'x']);
    expect(extractVariables('{{{{raw}}}}{{notAVar}}{{{{/raw}}}}{{real}}')).toEqual(['real']);
  });
  it('renders substitutions, defaults, conditionals, loops, includes, and raw blocks locally', () => {
    const ctx = { persona: { name: 'Agent Smith' }, list: [{ a: 1 }, { a: 2 }], on: true };
    expect(renderTemplateLocal('I am {{persona.name}}.', ctx).rendered).toBe('I am Agent Smith.');
    expect(renderTemplateLocal('{{missing | default: "x"}}', ctx).rendered).toBe('x');
    expect(renderTemplateLocal('{{#if on}}yes{{else}}no{{/if}}{{#unless on}}!{{/unless}}', ctx).rendered).toBe('yes');
    expect(renderTemplateLocal('{{#each list}}[{{this.a}}]{{/each}}', ctx).rendered).toBe('[1][2]');
    expect(renderTemplateLocal('{{> safety}}', ctx).rendered).toBe('[include: safety]');
    expect(renderTemplateLocal('{{{{raw}}}}{{persona.name}}{{{{/raw}}}}', ctx).rendered).toBe('{{persona.name}}');
    expect(renderTemplateLocal('{{nope}} and {{persona.name}}', ctx).missingVariables).toEqual(['nope']);
  });
  it('inserts at the cursor', () => {
    expect(insertAtCursor('ab', '{{x}}', 1, 1)).toEqual({ text: 'a{{x}}b', caret: 6 });
    expect(insertAtCursor('abc', 'Z', 0, 3)).toEqual({ text: 'Z', caret: 1 });
  });
});

describe('JSON-RPC helpers', () => {
  it('builds requests and notifications', () => {
    const r = rpcRequest('tools/list', {});
    expect(r).toMatchObject({ jsonrpc: '2.0', method: 'tools/list' });
    expect(typeof r.id).toBe('number');
    expect(rpcRequest('notifications/initialized', undefined, { notification: true })).toEqual({ jsonrpc: '2.0', method: 'notifications/initialized' });
  });
  it('reads JSON and SSE responses', async () => {
    const json = await readRpcResponse(
      new Response(JSON.stringify({ jsonrpc: '2.0', id: 1, result: { ok: true } }), { headers: { 'content-type': 'application/json' } })
    );
    expect(responseFor(json, 1).result).toEqual({ ok: true });
    const sse = await readRpcResponse(
      new Response('event: message\ndata: {"jsonrpc":"2.0","id":2,"result":{"x":1}}\n\n', { headers: { 'content-type': 'text/event-stream' } })
    );
    expect(responseFor(sse, 2).result).toEqual({ x: 1 });
  });
  it('groups tools by family and converts input schemas to form descriptors', () => {
    expect(
      groupToolsByFamily([{ name: 'sql_query' }, { name: 'hugo_whoami' }, { name: 'sql_list_tables' }, { name: 'gh__search' }]).map((g) => [
        g.family,
        g.tools.length
      ])
    ).toEqual([
      ['sql', 2],
      ['hugo', 1],
      ['gh', 1]
    ]);
    const d = jsonSchemaToDescriptors({
      type: 'object',
      required: ['resource', 'sql'],
      properties: {
        resource: { type: 'string', enum: ['prod-db'] },
        sql: { type: 'string', description: 'Query' },
        limit: { type: 'integer', minimum: 1 },
        params: { type: 'object' },
        tags: { type: 'array', items: { type: 'string' } },
        dry: { type: 'boolean', default: false }
      }
    });
    expect(d.map((x) => [x.key, x.kind, x.required])).toEqual([
      ['resource', 'Enum', true],
      ['sql', 'String', true],
      ['limit', 'Integer', false],
      ['params', 'Text', false],
      ['tags', 'StringList', false],
      ['dry', 'Boolean', false]
    ]);
    expect(argumentsFromValues(d, { resource: 'prod-db', sql: 'select 1', limit: '5', params: '{"a":1}', tags: ['x'], dry: true })).toEqual({
      resource: 'prod-db',
      sql: 'select 1',
      limit: 5,
      params: { a: 1 },
      tags: ['x'],
      dry: true
    });
    expect(() => argumentsFromValues(d, { params: '{' })).toThrow(SyntaxError);
  });
});

describe('analytics helpers', () => {
  it('reads series from the server-filled report buckets', () => {
    const report = {
      buckets: [
        { startUtc: '2026-01-01T00:00:00Z', inputTokens: 0, avgTurnDurationMs: null },
        { startUtc: '2026-01-01T01:00:00Z', inputTokens: 5, avgTurnDurationMs: 1200 }
      ]
    };
    const out = reportSeries(report, ['inputTokens', 'outputTokens']);
    expect(out.times).toEqual(['2026-01-01T00:00:00Z', '2026-01-01T01:00:00Z']);
    expect(out.series[0].values).toEqual([0, 5]);
    expect(out.series[1].values).toEqual([0, 0]);
    expect(reportSeries(report, ['avgTurnDurationMs'], { nullGaps: true }).series[0].values).toEqual([null, 1200]);
    expect(reportSeries(null, ['a'])).toEqual({ times: [], series: [{ key: 'a', metric: 'a', values: [] }] });
    expect(analyticsQuery('hour', { fromUtc: 'f', toUtc: 't' }).interval).toBe('minute');
  });
  it('computes chart maxima and ticks', () => {
    expect(chartMax([{ values: [1, 2] }, { values: [3, 1] }], 'stacked')).toBe(4);
    expect(chartMax([{ values: [1, 2] }, { values: [3, null] }], 'line')).toBe(3);
    expect(niceTicks(9)).toEqual([0, 2.5, 5, 7.5, 10]);
  });
});

describe('modal state', () => {
  it('tracks open modals so auto-refresh can pause', () => {
    expect(isModalOpen()).toBe(false);
    const close = registerModal();
    expect(isModalOpen()).toBe(true);
    close();
    close();
    expect(isModalOpen()).toBe(false);
  });
});
