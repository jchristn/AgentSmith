// Observability cards and Grafana deep-link builders (plan section 21: folder Hugo, datasources prometheus, tempo,
// and loki).

import { describe, it, expect } from 'vitest';
import {
  grafanaConfig,
  exploreUrl,
  traceUrl,
  recordTimeRange,
  dashboardUrl,
  sectionDashboardLinks,
  DASHBOARD_SECTIONS,
  DEFAULT_DASHBOARDS,
  DEFAULT_DATASOURCES,
  RANGE_MARGIN_MS
} from '../utils/grafana';
import { observabilityCards, reachabilityState, loopbackUrl } from '../utils/observability';

const INFO = {
  observability: {
    grafanaUrl: 'http://localhost:3000/',
    prometheusUrl: 'https://prom.example.com',
    lokiUrl: null,
    tempoUrl: 'http://127.0.0.1:3200',
    datasources: { ...DEFAULT_DATASOURCES },
    dashboards: { ...DEFAULT_DASHBOARDS }
  }
};
const CALL = { id: 'tin_abc', traceId: '4bf92f3577b34da6a3ce929d0e0e4736', createdUtc: '2026-10-09T12:00:00.000Z', durationMs: 60000 };
const T0 = Date.parse(CALL.createdUtc);

function pane(url) {
  const u = new URL(url);
  expect(u.pathname).toBe('/explore');
  const panes = JSON.parse(u.searchParams.get('panes'));
  return panes.gx;
}

describe('grafanaConfig', () => {
  it('is null without a Grafana URL', () => {
    expect(grafanaConfig(null)).toBeNull();
    expect(grafanaConfig({ observability: { grafanaUrl: 'not a url' } })).toBeNull();
  });
  it('rewrites localhost to 127.0.0.1 and uses the plan datasource UIDs', () => {
    const c = grafanaConfig(INFO);
    expect(c.url).toBe('http://127.0.0.1:3000');
    expect(c.datasources).toEqual({ prometheus: 'prometheus', loki: 'loki', tempo: 'tempo' });
    expect(c.dashboards.overview).toBe('hugo-overview');
  });
  it('keeps server-reported UIDs and drops blank ones', () => {
    const custom = grafanaConfig({ observability: { grafanaUrl: 'https://g.example.com', datasources: { tempo: 'my-tempo' }, dashboards: { http: '' } } });
    expect(custom.datasources.tempo).toBe('my-tempo');
    expect(custom.dashboards.http).toBeNull();
    expect(dashboardUrl(custom, 'http')).toBeNull();
  });
});

describe('Explore and dashboard links', () => {
  const c = grafanaConfig(INFO);
  it('pads a record time range by five minutes on each side', () => {
    expect(recordTimeRange(CALL)).toEqual({ from: String(T0 - RANGE_MARGIN_MS), to: String(T0 + 60000 + RANGE_MARGIN_MS) });
  });
  it('builds a Tempo trace link for a tool invocation', () => {
    const p = pane(traceUrl(c, CALL));
    expect(p.datasource).toBe('tempo');
    expect(p.queries[0]).toMatchObject({ queryType: 'traceql', query: CALL.traceId });
    expect(traceUrl(c, { ...CALL, traceId: '' })).toBeNull();
    expect(traceUrl(null, CALL)).toBeNull();
  });
  it('builds dashboard links with time ranges and variables', () => {
    expect(dashboardUrl(c, 'overview')).toBe('http://127.0.0.1:3000/d/hugo-overview?orgId=1');
    expect(dashboardUrl(c, 'tools', { from: CALL.createdUtc, vars: { connector: 'ssh' } })).toBe(
      `http://127.0.0.1:3000/d/hugo-tools?orgId=1&from=${T0}&var-connector=ssh`
    );
    expect(dashboardUrl(c, 'nope')).toBeNull();
    expect(exploreUrl(null, 'x', [], {})).toBeNull();
  });
  it('lists the seven Hugo dashboards in order', () => {
    expect(DASHBOARD_SECTIONS).toEqual(['overview', 'http', 'agent', 'tools', 'models', 'protocols', 'security']);
    for (const key of DASHBOARD_SECTIONS) expect(DEFAULT_DASHBOARDS[key]).toMatch(/^hugo-[a-z-]+$/);
    expect(sectionDashboardLinks(INFO).map((l) => l.key)).toEqual(DASHBOARD_SECTIONS);
    expect(sectionDashboardLinks(null)).toEqual([]);
  });
});

describe('observability cards', () => {
  const status = [
    { service: 'grafana', configured: true, reachable: true, version: '11.2.0' },
    { service: 'prometheus', configured: true, reachable: false, version: null },
    { service: 'tempo', configured: true, reachable: true, version: '2.6.1' }
  ];
  it('shows one card per configured service with state and version', () => {
    expect(observabilityCards(INFO, status)).toEqual([
      { id: 'grafana', url: 'http://127.0.0.1:3000/', state: 'reachable', version: '11.2.0' },
      { id: 'prometheus', url: 'https://prom.example.com', state: 'unreachable', version: null },
      { id: 'tempo', url: 'http://127.0.0.1:3200', state: 'reachable', version: '2.6.1' }
    ]);
  });
  it('reports checking while loading and unknown when the status call failed', () => {
    expect(reachabilityState(undefined, 'grafana')).toBe('checking');
    expect(reachabilityState(null, 'grafana')).toBe('unknown');
  });
  it('never shows localhost', () => {
    expect(loopbackUrl('http://localhost:3000/x')).toBe('http://127.0.0.1:3000/x');
  });
});
