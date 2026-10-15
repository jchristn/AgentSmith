import { describe, it, expect, afterEach } from 'vitest';
import { normalizeBuckets } from '../utils/chart';
import { REQUEST_RANGES, requestRangeToParams, PAGE_SIZE_OPTIONS, LARGE_PAGE_SIZE_OPTIONS, DEFAULT_PAGE_SIZE, defaultServerUrl, STORAGE_KEYS, GITHUB_URL, APP_VERSION, effectTone } from '../utils/constants';
import { diffLines } from '../utils/diff';
import { buildListParams, sortLocal, toUtcIso, stripForDuplicate } from '../utils/query';
import { flattenOpenApiSpec, resolvePath, buildUrl, isDestructive } from '../utils/openApi';
import { allowed, roleKey } from '../utils/principal';
import { pageBySlug, pathFor, PAGES, HUBS, NAV_SECTIONS, LEGACY_REDIRECTS, hubForPath, detailPageBySlug } from '../config/navConfig';
import { mcpConfigSnippet, claudeCliSnippet, a2aCardUrl, openAiBaseUrl } from '../utils/snippets';
import { environmentOf } from '../components/Sidebar';
import { isLocalServer } from '../components/Login';
import { resolveRoute } from '../components/Dashboard';
import { isMissingEndpoint } from '../utils/errors';
import pkg from '../../package.json';

describe('normalizeBuckets', () => {
  const now = new Date('2026-10-09T12:34:56Z');
  it.each([
    ['hour', 60],
    ['day', 96],
    ['week', 84],
    ['month', 120]
  ])('returns exactly the bucket count for %s', (range, count) => {
    expect(REQUEST_RANGES[range].bucketCount).toBe(count);
    expect(normalizeBuckets([], range, now)).toHaveLength(count);
  });
  it('builds query parameters for a range', () => {
    expect(JSON.stringify(requestRangeToParams('day', now))).toContain('2026');
  });
});

describe('constants', () => {
  afterEach(() => {
    window.HUGO_CONFIG = { apiUrl: '' };
  });
  it('uses 10, 25, 50, and 100 rows, with 250, 500, and 1000 for Request History', () => {
    expect(PAGE_SIZE_OPTIONS).toEqual([10, 25, 50, 100]);
    expect(LARGE_PAGE_SIZE_OPTIONS).toEqual([10, 25, 50, 100, 250, 500, 1000]);
    expect(DEFAULT_PAGE_SIZE).toBe(25);
  });
  it('prefers window.HUGO_CONFIG.apiUrl, then the page origin', () => {
    window.HUGO_CONFIG = { apiUrl: 'http://127.0.0.1:9999/' };
    expect(defaultServerUrl()).toContain('127.0.0.1:9999');
    window.HUGO_CONFIG = { apiUrl: '' };
    expect(defaultServerUrl()).toBe(window.location.origin);
  });
  it('uses hugo storage keys, the AgentSmith repository, and the mirrored version', () => {
    for (const [name, key] of Object.entries(STORAGE_KEYS)) expect(key, name).toMatch(/^hugo[._]/);
    expect(STORAGE_KEYS.locale).toBe('hugo.locale');
    expect(GITHUB_URL).toBe('https://github.com/jchristn/AgentSmith');
    expect(APP_VERSION).toBe(pkg.version);
    expect(pkg.version).toBe('0.1.0-alpha');
    expect(pkg.name).toBe('hugo-dashboard');
  });
  it('maps effects to tones', () => {
    expect(effectTone('Read')).toBe('neutral');
    expect(effectTone('Write')).toBe('warning');
    expect(effectTone('Destructive')).toBe('danger');
  });
});

describe('diffLines', () => {
  it('marks added, removed, and unchanged lines', () => {
    expect(diffLines('a\nb\nc', 'a\nc\nd')).toEqual([
      { type: 'same', text: 'a' },
      { type: 'del', text: 'b' },
      { type: 'same', text: 'c' },
      { type: 'add', text: 'd' }
    ]);
  });
});

describe('query helpers', () => {
  it('maps server-sortable keys to ordering and drops empty filters', () => {
    expect(buildListParams({ pageNumber: 2, pageSize: 50, sortKey: 'name', sortDir: 'desc', search: 'x', filters: { a: '', b: 'v' } })).toEqual({ pageNumber: 2, pageSize: 50, ordering: 'NameDescending', search: 'x', b: 'v' });
  });
  it('sorts locally with nulls last', () => {
    expect(sortLocal([{ v: 2 }, { v: null }, { v: 1 }], 'v', 'asc').map((x) => x.v)).toEqual([1, 2, null]);
  });
  it('converts local input to UTC ISO', () => {
    expect(toUtcIso('')).toBeUndefined();
    expect(toUtcIso('2026-01-01T00:00')).toMatch(/Z$/);
  });
  it('strips server fields for duplication', () => {
    expect(stripForDuplicate({ id: 'x', name: 'n', createdUtc: 'c', tenantId: 't' })).toEqual({ name: 'n-copy' });
  });
});

describe('openApi helpers', () => {
  const spec = { paths: { '/v1.0/api/resources/{id}': { get: { tags: ['Resources'], summary: 'Read' }, delete: { tags: ['Resources'], summary: 'Delete' } } } };
  it('flattens operations and flags destructive ones', () => {
    const ops = flattenOpenApiSpec(spec);
    expect(ops.map((o) => o.method).sort()).toEqual(['DELETE', 'GET']);
    expect(isDestructive(ops.find((o) => o.method === 'DELETE'))).toBe(true);
  });
  it('resolves paths and builds URLs', () => {
    expect(resolvePath('/r/{id}', { id: 'res 1' })).toBe('/r/res%201');
    expect(buildUrl('http://h/', '/p', { a: 1, b: '' })).toBe('http://h/p?a=1');
  });
});

describe('navigation (plan section 18.2)', () => {
  it('groups the sidebar into the seven plan groups in order', () => {
    expect(NAV_SECTIONS.map((s) => s.key)).toEqual(['start', 'agents', 'connect', 'configure', 'observe', 'develop', 'administer']);
    const items = Object.fromEntries(NAV_SECTIONS.map((s) => [s.key, s.items.map((h) => h.key)]));
    expect(items.start).toEqual(['home', 'chat']);
    expect(items.agents).toEqual(['profiles', 'approvals', 'conversations']);
    expect(items.connect).toEqual(['resources', 'secrets', 'connectors', 'models']);
    expect(items.configure).toEqual(['prompts', 'knowledge']);
    expect(items.observe).toEqual(['toolInvocations', 'requests', 'analytics', 'audit']);
    expect(items.develop).toEqual(['explorer', 'mcpExplorer', 'a2aExplorer']);
    expect(items.administer).toEqual(['tenants', 'users', 'credentials', 'rbac', 'sessions', 'settings', 'plugins']);
  });
  it('uses the FRONTEND_ARCHITECTURE paths and the plan route inventory', () => {
    expect(pathFor('home')).toBe('/dashboard/home');
    expect(pathFor('requests')).toBe('/dashboard/requests');
    expect(pathFor('explorer')).toBe('/dashboard/explorer');
    expect(pathFor('chat', 'cnv_1')).toBe('/dashboard/chat/cnv_1');
    expect(pathFor('profiles', 'prf_1')).toBe('/dashboard/profiles/prf_1');
    expect(pathFor('promptTemplates')).toBe('/dashboard/prompts');
    expect(pathFor('toolInvocations', null, { status: 'Failed' })).toBe('/dashboard/tool-invocations?status=Failed');
    expect(pathFor('roles')).toBe('/dashboard/rbac');
    expect(pathFor('assignments')).toBe('/dashboard/rbac?tab=assignments');
    expect(pathFor('serverInfo')).toBe('/dashboard/settings?tab=server-info');
    for (const p of PAGES) expect(pathFor(p.id)).toMatch(/^\/dashboard\/[a-z0-9-]+(\?tab=[a-z-]+)?$/);
  });
  it('resolves routes, detail routes, and tab redirects', () => {
    expect(resolveRoute('home').kind).toBe('page');
    expect(resolveRoute('rbac').kind).toBe('hub');
    expect(resolveRoute('chat', 'cnv_1').kind).toBe('detail');
    expect(resolveRoute('profiles', 'prf_1').kind).toBe('detail');
    expect(resolveRoute('connectors', 'ssh').kind).toBe('detail');
    expect(resolveRoute('assignments')).toEqual({ kind: 'redirect', to: '/dashboard/rbac', tab: 'assignments' });
    expect(resolveRoute('nowhere').to).toBe('/dashboard/home');
    expect(detailPageBySlug('prompts').id).toBe('promptTemplates');
    expect(hubForPath('/dashboard/profiles/prf_1').key).toBe('profiles');
    expect(hubForPath('/dashboard/nowhere')).toBeNull();
    expect(HUBS.length).toBe(25);
    for (const r of LEGACY_REDIRECTS) expect(HUBS.some((h) => h.slug === r.from), r.from).toBe(false);
  });
  it('gates administrator pages', () => {
    const admin = { isAdmin: true };
    const user = { isAdmin: false, isTenantAdmin: false };
    expect(roleKey(admin)).toBe('roles.systemAdmin');
    expect(roleKey({ principalType: 'Credential' })).toBe('roles.credential');
    expect(allowed(pageBySlug('tenants').requires, admin)).toBe(true);
    expect(allowed(pageBySlug('tenants').requires, user)).toBe(false);
    expect(allowed(pageBySlug('plugins').requires, { isTenantAdmin: true })).toBe(false);
    expect(allowed(pageBySlug('users').requires, { isTenantAdmin: true })).toBe(true);
    expect(allowed(pageBySlug('chat').requires, user)).toBe(true);
  });
});

describe('connection snippets', () => {
  it('builds an MCP config with access key headers and no secret in the text', () => {
    const text = mcpConfigSnippet({ serverUrl: 'http://127.0.0.1:8800/', accessKey: 'access_x', profile: 'Hugo' });
    const parsed = JSON.parse(text);
    expect(parsed.mcpServers.hugo).toEqual({ type: 'http', url: 'http://127.0.0.1:8800/mcp/Hugo', headers: { 'x-access-key': 'access_x', 'x-secret-key': '${HUGO_SECRET_KEY}' } });
    expect(JSON.parse(mcpConfigSnippet({})).mcpServers.hugo.headers.Authorization).toBe('Bearer ${HUGO_TOKEN}');
  });
  it('builds CLI, A2A, and OpenAI URLs', () => {
    expect(claudeCliSnippet({ serverUrl: 'http://h:8800' })).toContain('claude mcp add --transport http hugo http://h:8800/mcp');
    expect(a2aCardUrl({ serverUrl: 'http://h:8800', tenantId: 'default', profile: 'Hugo' })).toBe('http://h:8800/v1.0/a2a/default/Hugo/.well-known/agent-card.json');
    expect(openAiBaseUrl({ serverUrl: 'http://h:8800/' })).toBe('http://h:8800/v1.0/openai/v1');
  });
});

describe('environment and errors', () => {
  it('detects local servers by loopback address', () => {
    expect(environmentOf('http://127.0.0.1:8800')).toBe('local');
    expect(environmentOf('https://hugo.example.com')).toBe('remote');
    expect(isLocalServer('http://127.0.0.1:8800')).toBe(true);
    expect(isLocalServer('https://hugo.example.com')).toBe(false);
  });
  it('treats 404, 405, and 501 as a missing capability, but not a record miss', () => {
    expect(isMissingEndpoint({ status: 404, code: 'NotFound' })).toBe(true);
    expect(isMissingEndpoint({ status: 405 })).toBe(true);
    expect(isMissingEndpoint({ status: 404, code: 'RecordNotFound' })).toBe(false);
    expect(isMissingEndpoint({ status: 500 })).toBe(false);
  });
});

describe('settings contract', () => {
  it('maps the server field metadata onto the editor', async () => {
    const { normalizeSettings, settingsUpdateBody } = await import('../utils/settings');
    const [port, ssl, db, pwd] = normalizeSettings({
      fields: [
        { path: 'Rest.Port', type: 'int', value: '8800', minimum: 1, maximum: 65535, source: 'File', applyMode: 'RestartRequired', editable: true },
        { path: 'Rest.Ssl', type: 'bool', value: 'false', source: 'File', editable: true },
        { path: 'Database.Type', type: 'enum', value: 'Sqlite', allowedValues: ['Sqlite', 'Postgresql'], source: 'File', editable: true },
        { path: 'Database.Password', type: 'secret', value: '********', isSecret: true, isSet: true, source: 'Environment', editable: true }
      ]
    });
    expect(port).toMatchObject({ type: 'integer', value: 8800, min: 1, max: 65535 });
    expect(ssl).toMatchObject({ type: 'boolean', value: false });
    expect(db).toMatchObject({ type: 'enum', options: ['Sqlite', 'Postgresql'] });
    expect(pwd).toMatchObject({ isSecret: true, editable: false });
    expect(settingsUpdateBody({ 'Rest.Port': 8801 })).toEqual({ values: [{ path: 'Rest.Port', value: '8801' }] });
  });
});
