// Enum values are shown as translated labels, never as raw wire values, while forms still submit the wire value.
//
// The checks run in the en-XA pseudo-locale, where every catalog string is wrapped in ⟦ ⟧, so any enum value that
// reaches the screen without going through enums.<group>.<value> shows up as bare text. They cover every column of
// every record page (rendered against the Playwright mock rows) and every select option of every form.

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import i18n, { setActiveLocale } from '../i18n';
import { en, TRANSLATIONS } from '../i18n/resources';
import * as resourceConfigs from '../views/resourceConfigs';
import * as hugoConfigs from '../views/hugoConfigs';
import { ENUMS } from '../utils/constants';
import { respond } from '../../tests/e2e/mocks.js';

const configs = [...Object.values(resourceConfigs), ...Object.values(hugoConfigs)].filter((c) => c && typeof c === 'object' && c.id && c.columns);

/** Collection each record page lists, as served by the mocks. */
const COLLECTIONS = {
  tenants: '/v1.0/api/tenants',
  users: '/v1.0/api/tenants/default/users',
  credentials: '/v1.0/api/tenants/default/credentials',
  roles: '/v1.0/api/tenants/default/roles',
  permissions: '/v1.0/api/tenants/default/permissions',
  userAssignments: '/v1.0/api/tenants/default/user-role-assignments',
  credentialAssignments: '/v1.0/api/tenants/default/credential-scope-assignments',
  promptTemplates: '/v1.0/api/tenants/default/prompt-templates',
  audit: '/v1.0/api/tenants/default/audit',
  profiles: '/v1.0/api/agent-profiles',
  approvals: '/v1.0/api/approvals',
  conversations: '/v1.0/api/conversations',
  resources: '/v1.0/api/resources',
  secrets: '/v1.0/api/secrets',
  models: '/v1.0/api/model-endpoints',
  knowledge: '/v1.0/api/knowledge',
  toolInvocations: '/v1.0/api/tool-invocations',
  sessions: '/v1.0/api/sessions',
  plugins: '/v1.0/api/system/plugins'
};

function rows(id) {
  const body = respond('GET', new URL(`http://127.0.0.1${COLLECTIONS[id]}?maxResults=25&skip=0&pageSize=25&pageNumber=1`));
  return body?.items || body?.objects || [];
}

/** Values whose label is the value itself in every locale (A2A, MCP): showing them bare is not a bug. */
function invariant(group, value) {
  return [en, ...Object.values(TRANSLATIONS)].every((c) => c.enums?.[group]?.[value] === value);
}

const RAW = new Map();
for (const [group, values] of Object.entries(ENUMS)) for (const v of values) if (!invariant(group, v)) RAW.set(v, group);

function fragments(markup) {
  const text = markup
    .replace(/<[^>]+>/g, '\n')
    .replace(/&amp;/g, '&')
    .replace(/&#x27;/g, "'")
    .replace(/&quot;/g, '"');
  return text
    .split(/\n|, | \/ /)
    .map((s) => s.trim())
    .filter(Boolean);
}

beforeAll(async () => {
  await setActiveLocale('en-XA');
});

afterAll(async () => {
  await setActiveLocale('en');
});

describe('enum values render as translated labels', () => {
  const t = (...args) => i18n.t(...args);
  const ctx = {
    t,
    i18n,
    apiClient: {},
    tenantId: 'default',
    principal: { isAdmin: true },
    navigate: () => {},
    addToast: () => {},
    reload: () => {},
    setExtraModal: () => {},
    setJsonValue: () => {},
    lookups: {}
  };

  it('has mock rows for every record page', () => {
    for (const config of configs) expect(rows(config.id).length, config.id).toBeGreaterThan(0);
  });

  it('shows no raw enum value in any table cell', () => {
    const problems = [];
    for (const config of configs) {
      for (const col of config.columns(ctx)) {
        for (const row of rows(config.id)) {
          const out = col.render(row);
          if (out === null || out === undefined) continue;
          const markup = renderToStaticMarkup(<MemoryRouter>{typeof out === 'object' ? out : <>{String(out)}</>}</MemoryRouter>);
          for (const frag of fragments(markup)) if (RAW.has(frag)) problems.push(`${config.id}.${col.key}: "${frag}" (enums.${RAW.get(frag)})`);
        }
      }
    }
    expect([...new Set(problems)]).toEqual([]);
  });

  it('labels every enum option in every form while submitting the wire value', () => {
    const problems = [];
    let checked = 0;
    for (const config of configs) {
      const blank = config.blank ? config.blank(ctx) : {};
      for (const mode of ['create', 'edit', 'view']) {
        for (const f of config.fields(blank, { ...ctx, mode, options: {} })) {
          if (f.type !== 'select' && f.type !== 'multiselect') continue;
          for (const o of f.options || []) {
            const value = typeof o === 'object' ? o.value : o;
            if (!RAW.has(value)) continue;
            checked += 1;
            const label = typeof o === 'object' ? o.label : o;
            if (label === value || !String(label).startsWith('⟦')) problems.push(`${config.id} ${mode} ${f.name}: option ${value} shows "${label}"`);
          }
        }
      }
    }
    expect(checked).toBeGreaterThan(30);
    expect([...new Set(problems)]).toEqual([]);
  });
});
