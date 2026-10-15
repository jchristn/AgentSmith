// Tooltip coverage for everything generated from configuration (D-030 in Garrison, kept for Hugo): every column,
// field, section, row action, filter, add button, navigation item, and explainer has real English text that explains
// more than its label. The Playwright suite covers the rendered pages.

import { describe, it, expect, beforeAll } from 'vitest';
import i18n, { setActiveLocale } from '../i18n';
import * as resourceConfigs from '../views/resourceConfigs';
import * as hugoConfigs from '../views/hugoConfigs';
import { HUBS, PAGES } from '../config/navConfig';
import { resourceTip, enumTip } from '../utils/tips';

const MIN = 25;
const norm = (s) =>
  String(s || '')
    .toLowerCase()
    .replace(/[^\p{L}\p{N}]+/gu, ' ')
    .trim();

function check(problems, where, tipText, label) {
  if (!tipText) problems.push(`${where}: missing`);
  else if (tipText.length < MIN) problems.push(`${where}: too short (${tipText})`);
  else if (label && norm(tipText) === norm(label)) problems.push(`${where}: restates its label`);
  else if (/tips\./.test(tipText)) problems.push(`${where}: unresolved key (${tipText})`);
}

const configs = [...Object.values(resourceConfigs), ...Object.values(hugoConfigs)].filter((c) => c && typeof c === 'object' && c.id && c.columns);

beforeAll(async () => {
  await setActiveLocale('en');
});

describe('tooltip coverage for configured pages', () => {
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

  it('gives every column, field, and section a useful tooltip', () => {
    const problems = [];
    for (const config of configs) {
      for (const col of config.columns(ctx)) check(problems, `${config.id} column ${col.key}`, col.tip, col.label);
      const blank = config.blank ? config.blank(ctx) : {};
      for (const mode of ['create', 'edit', 'view']) {
        for (const f of config.fields(blank, { ...ctx, mode, options: {} })) {
          if (f.type === 'custom') continue;
          check(problems, `${config.id} field ${f.name}`, f.tip, f.label);
        }
      }
    }
    expect([...new Set(problems)]).toEqual([]);
  });

  it('gives every row action, filter, add button, and explainer real text', () => {
    const problems = [];
    for (const config of configs) {
      if (config.api.create)
        check(problems, `${config.id} add`, t(`tips.resources.${config.id}.add`, { defaultValue: t('tips.actions.add') }), t(`resources.${config.id}.add`));
      if (config.rowActions) {
        for (const status of ['Pending', 'Approved']) {
          for (const a of config.rowActions({ id: 'x', name: 'x', status, enabled: true, toolInvocationId: 'tin_1' }, ctx))
            check(problems, `${config.id} action ${a.label}`, a.tip, a.label);
        }
      }
      for (const f of config.filters || []) check(problems, `${config.id} filter ${f.name}`, t(`tips.filters.${f.name}`), f.label(t));
      if (config.explainer) check(problems, `${config.id} explainer body`, t(`explainers.${config.explainer}.p1`), '');
    }
    expect([...new Set(problems)]).toEqual([]);
  });

  it('gives every hub and tab a navigation tooltip longer than its label', () => {
    const problems = [];
    for (const hub of HUBS) check(problems, `hub ${hub.key}`, t(`tips.nav.hubs.${hub.key}`), t(`nav.hubs.${hub.key}.label`));
    for (const page of PAGES.filter((p) => p.tab)) check(problems, `tab ${page.id}`, t(`tips.nav.tabs.${page.id}`), t(`nav.items.${page.id}.label`));
    expect(problems).toEqual([]);
  });

  it('explains every effect, access mode, decision, status, and health badge', () => {
    const problems = [];
    const groups = {
      effect: ['Read', 'Write', 'Destructive'],
      accessMode: ['ReadOnly', 'ReadWriteWithApproval', 'ReadWrite'],
      approvalStatus: ['Pending', 'Approved', 'Rejected', 'Expired', 'Cancelled'],
      toolDecision: ['Allowed', 'Denied', 'ApprovalRequired', 'Approved', 'Rejected', 'Expired'],
      toolStatus: ['Succeeded', 'Failed', 'TimedOut', 'Cancelled'],
      healthStatus: ['Healthy', 'Degraded', 'Unhealthy', 'Unknown']
    };
    for (const [group, values] of Object.entries(groups))
      for (const v of values) check(problems, `enum ${group}.${v}`, enumTip(t, group, v), t(`enums.${group}.${v}`));
    expect(problems).toEqual([]);
  });

  it('falls back to shared field tips and returns empty text for unknown keys', () => {
    expect(resourceTip(t, 'tenants', 'createdUtc')).toBe(t('tips.common.fields.createdUtc'));
    expect(resourceTip(t, 'tenants', 'noSuchField')).toBe('');
    expect(enumTip(t, 'effect', 'NoSuchEffect')).toBe('');
  });
});
