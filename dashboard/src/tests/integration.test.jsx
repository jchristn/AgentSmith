// Helpers and components wired to the integration routes: the real server shapes (resources, referrers, layered
// templates, compiled-instruction segments, knowledge, relationships, tool outcomes) and the views built on them.

import { describe, it, expect, vi, beforeAll, afterEach } from 'vitest';
import { render, screen, fireEvent, cleanup, waitFor } from '@testing-library/react';
import i18n, { setActiveLocale } from '../i18n';
import AuthContext from '../context/AuthContext';
import { ToastProvider } from '../context/ToastContext';
import ApiClient, { ApiError, pascalCode } from '../utils/api';
import { resourceHealth, settingsToMap, resourceToForm, resourceFieldErrors, referrerRows } from '../utils/resources';
import { contextEntries, structuredResult, relationshipLayout, isSqlConnector, knowledgeSourceTone } from '../utils/knowledge';
import { sourceChunks } from '../components/CompiledInstructionsView';
import CompiledInstructionsView from '../components/CompiledInstructionsView';
import LayeredTemplateEditor, { outdatedLayerOf, initialDraft, contentBelow, ServerDiff } from '../components/LayeredTemplateEditor';
import { RelationshipGraph, IntelligenceView } from '../components/ResourceKnowledge';
import { argumentSkeleton, toolCallBody, toolsForResource } from '../components/TryToolModal';
import { approvalCommand } from '../components/ApprovalCard';
import { recordValidation, validationHistory } from '../components/ModelHealthHistory';
import { approvalStats } from '../views/AnalyticsView';
import { listLayeredTemplates } from '../views/resourceConfigs';
import { profileToForm } from '../views/hugoConfigs';
import { connectorSchema } from '../components/ResourceSettingsPanel';
import { summaryCount } from '../views/HomeView';
import { isMissingEndpoint } from '../utils/errors';

beforeAll(async () => {
  await setActiveLocale('en');
});
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

const t = (key, opts) => i18n.t(key, opts);

function withAuth(ui, apiClient = {}, principal = { isAdmin: true }) {
  return (
    <AuthContext.Provider value={{ apiClient, principal, serverUrl: 'http://127.0.0.1:8800' }}>
      <ToastProvider>{ui}</ToastProvider>
    </AuthContext.Provider>
  );
}

const jsonResponse = (status, body) => ({ ok: status < 400, status, headers: { get: () => null }, text: async () => JSON.stringify(body) });

describe('api client against the integration routes', () => {
  it('enumerates Hugo routes with maxResults and skip and maps fromUtc to since', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(jsonResponse(200, { objects: [{ id: 'tiv_1' }], totalRecords: 30, maxResults: 10, skip: 10 }));
    const client = new ApiClient('http://h', 't');
    const page = await client.listToolInvocations({ pageNumber: 2, pageSize: 10, status: 'Failed', fromUtc: '2026-10-14T00:00:00Z' });
    expect(fetchMock.mock.calls[0][0]).toBe('http://h/v1.0/api/tool-invocations?status=Failed&since=2026-10-14T00%3A00%3A00Z&maxResults=10&skip=10');
    expect(page).toMatchObject({ items: [{ id: 'tiv_1' }], pageNumber: 2, pageSize: 10, totalCount: 30 });
  });

  it('sends tool rules as a bare array, layers as a query, and knowledge root keys as _root', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(200, {}));
    const client = new ApiClient('http://h', 't');
    await client.updateToolRules('agp_1', 'bnd_1', [{ toolName: 'sql_query', rule: 'Deny' }]);
    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual([{ toolName: 'sql_query', rule: 'Deny' }]);
    await client.savePromptLayer('connector.ssh.instructions', 'Global', 'text', 'note');
    expect(fetchMock.mock.calls[1][0]).toBe('http://h/v1.0/api/prompt-templates/connector.ssh.instructions?layer=Global');
    await client.putResourceKnowledge('res_1', 'database', '', 'ctx');
    expect(fetchMock.mock.calls[2][0]).toBe('http://h/v1.0/api/resources/res_1/knowledge/database/_root');
    await client.listResourceKnowledgeVersions('res_1', 'table', 'main.orders');
    expect(fetchMock.mock.calls[3][0]).toBe('http://h/v1.0/api/resources/res_1/knowledge/table/main.orders/versions');
    await client.changeMyPassword({ currentPassword: 'a', newPassword: 'b' });
    expect(fetchMock.mock.calls[4][0]).toBe('http://h/v1.0/api/users/me/password');
  });

  it('keeps 202 approval outcomes as results and reads the error code and snake_case field codes', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(jsonResponse(202, { decision: 'ApprovalRequired', approvalId: 'apr_1' }));
    const client = new ApiClient('http://h', 't');
    await expect(client.invokeTool({ toolName: 'sql_query' })).resolves.toMatchObject({ approvalId: 'apr_1' });
    const err = new ApiError(400, {
      error: 'BadRequest',
      code: 'SettingsInvalid',
      message: 'bad',
      fields: { port: 'too big' },
      fieldCodes: { port: 'out_of_range' }
    });
    expect(err.code).toBe('SettingsInvalid');
    expect(err.fieldCodes).toEqual({ port: 'OutOfRange' });
    expect(pascalCode('not_allowed')).toBe('NotAllowed');
    expect(isMissingEndpoint(new ApiError(404, { error: 'NotFound', code: 'RouteNotFound' }))).toBe(true);
    expect(isMissingEndpoint(new ApiError(404, { error: 'NotFound', code: 'NotFound' }))).toBe(false);
  });
});

describe('resource shapes', () => {
  it('maps settings rows, health, and setting errors', () => {
    expect(resourceHealth({ lastTestResult: true })).toBe('Healthy');
    expect(resourceHealth({ lastTestResult: false })).toBe('Unhealthy');
    expect(resourceHealth({})).toBe('Unknown');
    expect(
      settingsToMap([
        { key: 'port', valueKind: 'Integer', value: '22' },
        { key: 'sudo', valueKind: 'Boolean', value: 'true' },
        { key: 'paths', valueKind: 'StringList', value: '/var/log\n/tmp' }
      ])
    ).toEqual({ port: 22, sudo: true, paths: ['/var/log', '/tmp'] });
    const form = resourceToForm({
      name: 'db',
      settings: [{ key: 'filename', valueKind: 'String', value: '/x.db' }],
      secretBindings: [{ slotName: 'pw', secretId: 'sec_1' }]
    });
    expect(form.settings).toEqual({ filename: '/x.db' });
    expect(form.secretBindings.pw.secretId).toBe('sec_1');
    expect(resourceFieldErrors({ commandTimeoutSeconds: 'range', name: 'taken' }, { code: 'SettingsInvalid' })).toEqual({
      'settings.commandTimeoutSeconds': 'range',
      name: 'taken'
    });
    expect(connectorSchema({ settings: [{ key: 'a' }] })).toEqual([{ key: 'a' }]);
  });

  it('reads both referrer shapes', () => {
    expect(referrerRows([{ agentProfileId: 'agp_1', agentProfileName: 'Hugo', bindingId: 'bnd_1', alias: 'db', accessMode: 'ReadOnly' }])[0]).toMatchObject({
      type: 'bindings',
      name: 'Hugo / db',
      accessMode: 'ReadOnly'
    });
    expect(referrerRows([{ referrerType: 'ModelEndpoint', referrerId: 'mep_1', referrerName: 'llm' }])[0]).toMatchObject({
      type: 'modelEndpoints',
      name: 'llm'
    });
  });

  it('maps the profile exposure flags both ways', () => {
    expect(profileToForm({ exposeA2A: false, exposeOpenAI: true })).toMatchObject({ exposeA2a: false, exposeOpenai: true, inferenceEndpointId: '' });
  });
});

describe('tools and approvals', () => {
  const tool = {
    name: 'sql_query',
    inputSchemaJson: JSON.stringify({
      required: ['resource', 'sql', 'maxRows'],
      properties: { resource: { enum: ['db'] }, sql: { type: 'string' }, maxRows: { type: 'integer' } }
    })
  };
  it('builds arguments and the request body', () => {
    expect(argumentSkeleton(tool)).toEqual({ sql: '', maxRows: 0 });
    expect(toolCallBody('agp_1', 'db', 'sql_query', { resource: 'x', sql: 'select 1' })).toEqual({
      profileId: 'agp_1',
      resource: 'db',
      toolName: 'sql_query',
      arguments: { sql: 'select 1' }
    });
    expect(
      toolsForResource({ tools: [tool, { name: 'ssh_exec', inputSchemaJson: '{"properties":{"resource":{"enum":["web"]}}}' }] }, ['db']).map((x) => x.name)
    ).toEqual(['sql_query']);
  });
  it('shows the primary argument of an approval', () => {
    expect(approvalCommand({ argumentsRedacted: '{"sql": "delete from t"}' })).toBe('delete from t');
    expect(approvalCommand({ command: 'uptime' })).toBe('uptime');
    expect(approvalCommand({ argumentsRedacted: '{"key": "a"}' })).toContain('"key"');
  });
  it('computes approval statistics and summary counts', () => {
    const stats = approvalStats(
      [
        { status: 'Approved', createdUtc: '2026-10-14T00:00:00Z', decidedUtc: '2026-10-14T00:01:00Z' },
        { status: 'Rejected', createdUtc: '2026-10-14T00:00:00Z', decidedUtc: '2026-10-14T00:03:00Z' },
        { status: 'Pending', createdUtc: '2026-10-13T00:00:00Z' }
      ],
      '2026-10-13T12:00:00Z'
    );
    expect(stats).toMatchObject({ total: 2, medianTurnaroundMs: 60000, approvalRate: 0.5 });
    expect(
      summaryCount(
        [
          { status: 'Failed', count: 2 },
          { status: 'TimedOut', count: 1 },
          { status: 'Succeeded', count: 5 }
        ],
        'Failed',
        'TimedOut'
      )
    ).toBe(3);
  });
  it('records model validations for the session', () => {
    recordValidation('mep_1', { success: false, error: 'Connection refused', latencyMs: 4 });
    expect(validationHistory('mep_1')[0]).toMatchObject({ success: false, error: 'Connection refused', latencyMs: 4 });
  });
});

describe('layered templates', () => {
  const view = {
    key: 'connector.ssh.instructions',
    activeLayer: 'Global',
    builtInContent: 'built-in',
    global: { exists: true, content: 'global', version: 2, customized: true, defaultUpdatedSinceCustomized: true },
    tenant: { exists: false, content: 'old tenant', version: 1 }
  };
  it('picks the draft, the layer below, and the outdated layer', () => {
    expect(initialDraft(view, 'Tenant')).toBe('global');
    expect(initialDraft(view, 'Global')).toBe('global');
    expect(contentBelow(view, 'Global')).toBe('built-in');
    expect(outdatedLayerOf(view)).toBe('Global');
    expect(outdatedLayerOf({ ...view, global: { ...view.global, defaultUpdatedSinceCustomized: false } })).toBeNull();
  });
  it('filters and pages the catalog in the browser', async () => {
    const client = { listPromptLayers: async () => [view, { key: 'persona.agent-smith', activeLayer: 'BuiltIn', tenant: { exists: true, customized: true } }] };
    expect((await listLayeredTemplates(client, { customized: 'Tenant' })).items.map((v) => v.key)).toEqual(['persona.agent-smith']);
    expect((await listLayeredTemplates(client, { search: 'ssh', pageSize: 1 })).totalCount).toBe(1);
  });
  it('saves the Tenant layer and keeps Global read-only for tenant administrators', async () => {
    const apiClient = {
      getPromptLayers: vi.fn(async () => view),
      listPromptLayerVersions: vi.fn(async () => []),
      savePromptLayer: vi.fn(async () => ({ ...view, tenant: { exists: true, content: 'mine', version: 1, customized: true } })),
      renderPromptContent: vi.fn(async () => ({ rendered: 'ok', success: true }))
    };
    render(withAuth(<LayeredTemplateEditor templateKey="connector.ssh.instructions" />, apiClient, { isAdmin: false, isTenantAdmin: true }));
    await waitFor(() => expect(screen.getByTestId('layer-Tenant')).toBeTruthy());
    fireEvent.change(document.querySelector('#layer-Tenant-content'), { target: { value: 'mine' } });
    fireEvent.click(screen.getByTestId('layer-save'));
    await waitFor(() => expect(apiClient.savePromptLayer).toHaveBeenCalledWith('connector.ssh.instructions', 'Tenant', 'mine', ''));
    fireEvent.click(screen.getByRole('tab', { name: new RegExp(t('enums.promptLayer.Global')) }));
    expect(screen.getByText(t('components.layeredEditor.globalReadOnly'))).toBeTruthy();
    expect(screen.queryByTestId('layer-save')).toBeNull();
  });
  it('renders the server line diff', () => {
    render(
      withAuth(
        <ServerDiff
          lines={[
            { op: 'same', text: 'a' },
            { op: 'remove', text: 'b' },
            { op: 'add', text: 'c' }
          ]}
          beforeLabel="Global"
          afterLabel="Tenant"
        />
      )
    );
    expect(document.querySelectorAll('.diff-del').length).toBeGreaterThan(1);
    expect(document.querySelector('.diff-line.diff-add').textContent).toContain('c');
  });
});

describe('compiled instructions source map', () => {
  const text = 'persona\n\nsection: connector text';
  const segments = [
    { key: 'instructions.root', layer: 'Global', start: 0, length: text.length, depth: 0 },
    { key: 'persona.agent-smith', layer: 'BuiltIn', start: 0, length: 7, depth: 1 },
    { key: 'connector.ssh.instructions', layer: 'Tenant', version: 3, bindingAlias: 'web', start: 18, length: 14, depth: 2 }
  ];
  it('assigns each chunk to its deepest segment', () => {
    const chunks = sourceChunks(text, segments);
    expect(chunks.map((c) => c.segment.key)).toEqual(['persona.agent-smith', 'instructions.root', 'connector.ssh.instructions']);
    expect(chunks.map((c) => c.text).join('')).toBe(text);
  });
  it('renders a gutter per layer and the source map', () => {
    render(withAuth(<CompiledInstructionsView compiled={{ text, segments, tokenEstimate: 10, bindingCount: 1, warnings: ['w1'] }} />));
    expect(document.querySelector('.src-chunk.layer-Tenant').textContent).toBe('connector text');
    expect(screen.getByTestId('source-map').textContent).toContain('connector.ssh.instructions');
    expect(screen.getByTestId('compiled-warnings').textContent).toContain('w1');
  });
});

describe('resource knowledge', () => {
  it('lists stored entries, the database entry, and tables without context', () => {
    const entries = contextEntries([{ scope: 'table', key: 'main.orders', source: 'Agent' }], {
      isSql: true,
      tablesWithoutContext: ['main.customers', 'main.orders'],
      extra: [{ scope: 'notes', key: 'ops' }]
    });
    expect(entries.map((e) => `${e.scope}:${e.key}:${e.exists}`)).toEqual([
      'database::false',
      'table:main.customers:false',
      'table:main.orders:true',
      'notes:ops:false'
    ]);
    expect(structuredResult({ result: { structuredJson: '{"a":1}' } })).toEqual({ a: 1 });
    expect(structuredResult({ result: { structuredJson: 'nope' } })).toBeNull();
    expect(isSqlConnector({ family: 'sql' })).toBe(true);
    expect(knowledgeSourceTone('Agent')).toBe('warning');
  });
  const rels = [
    {
      fromSchema: 'main',
      fromTable: 'orders',
      fromColumn: 'customer_id',
      toSchema: 'main',
      toTable: 'customers',
      toColumn: 'id',
      isDeclared: true,
      confidence: 1
    },
    { fromSchema: 'main', fromTable: 'items', fromColumn: 'order_id', toSchema: 'main', toTable: 'orders', toColumn: 'id', isDeclared: false, confidence: 0.9 }
  ];
  it('lays out tables and edges', () => {
    const { nodes, edges } = relationshipLayout(rels, { defaultSchema: 'main' });
    expect(nodes.map((n) => n.label)).toEqual(['orders', 'customers', 'items']);
    expect(edges.filter((e) => !e.declared)).toHaveLength(1);
  });
  it('draws declared edges solid and inferred edges dashed with confidence', () => {
    render(withAuth(<RelationshipGraph relationships={rels} defaultSchema="main" />));
    expect(document.querySelectorAll('.rel-edge.declared')).toHaveLength(1);
    expect(document.querySelector('.rel-edge.inferred .rel-confidence').textContent).toContain('90');
  });
  it('renders the intelligence quality, entities, and ambiguities', () => {
    render(
      withAuth(
        <IntelligenceView
          data={{
            schema: 'main',
            domain: {
              summary: 'Crawled 2 tables.',
              entities: [{ schemaName: 'main', tableName: 'orders', role: 'core entity', rowEstimate: 5, keyColumns: ['id'], hasContext: true }]
            },
            ambiguities: [{ term: 'revenue', question: 'Which column?', candidates: ['main.orders.total'] }],
            contextQuality: {
              score: 48,
              label: 'Needs context',
              totalTables: 2,
              tablesWithContext: 1,
              signals: [{ key: 's', severity: 'warning', message: 'Only 1 of 2.' }]
            }
          }}
        />
      )
    );
    expect(screen.getByTestId('quality-score').textContent).toContain('48');
    expect(screen.getByTestId('intelligence-entities').textContent).toContain('orders');
    expect(screen.getByTestId('intelligence-ambiguities').textContent).toContain('revenue');
  });
});
