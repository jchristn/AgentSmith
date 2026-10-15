// Grafana deep links. Every URL is built from what the server info reports in `observability` (grafanaUrl,
// datasource UIDs, dashboard UIDs), never from guessed hosts: without a Grafana URL there are no links.
// Formats target Grafana 10 and 11 (Explore with `schemaVersion=1` and `panes`, dashboards at /d/<uid>).
// Datasource UIDs and the dashboard set follow plan section 21 (folder Hugo).

import { loopbackUrl } from './observability';

/** Datasource UIDs provisioned with the bundled Grafana (plan section 21), used when server info omits a field. */
export const DEFAULT_DATASOURCES = Object.freeze({ prometheus: 'prometheus', loki: 'loki', tempo: 'tempo' });

/** Dashboard UIDs in the Hugo folder. */
export const DEFAULT_DASHBOARDS = Object.freeze({
  overview: 'hugo-overview',
  http: 'hugo-http',
  agent: 'hugo-agent',
  tools: 'hugo-tools',
  models: 'hugo-models',
  protocols: 'hugo-protocols',
  security: 'hugo-security'
});

/** The Hugo folder's dashboards in display order. Each key is a field of server info `observability.dashboards`. */
export const DASHBOARD_SECTIONS = Object.freeze(['overview', 'http', 'agent', 'tools', 'models', 'protocols', 'security']);

/** Margin added before and after a record's time range in Explore links, in milliseconds (5 minutes). */
export const RANGE_MARGIN_MS = 5 * 60 * 1000;

function validUrl(url) {
  return typeof url === 'string' && /^https?:\/\/\S+$/i.test(url.trim());
}

function uid(map, defaults, key) {
  const value = map && typeof map === 'object' && key in map ? map[key] : defaults[key];
  if (value === null || value === undefined) return defaults[key];
  return typeof value === 'string' && value.trim() ? value.trim() : null;
}

/**
 * Grafana link configuration from a server info response, or null when Grafana is not configured.
 * @returns {{ url: string, datasources: object, dashboards: object } | null}
 */
export function grafanaConfig(info) {
  const obs = info && typeof info === 'object' && info.observability && typeof info.observability === 'object' ? info.observability : null;
  if (!obs || !validUrl(obs.grafanaUrl)) return null;
  const url = loopbackUrl(obs.grafanaUrl.trim()).replace(/\/+$/, '');
  const datasources = {};
  for (const k of Object.keys(DEFAULT_DATASOURCES)) datasources[k] = uid(obs.datasources, DEFAULT_DATASOURCES, k);
  const dashboards = {};
  for (const k of Object.keys(DEFAULT_DASHBOARDS)) dashboards[k] = uid(obs.dashboards, DEFAULT_DASHBOARDS, k);
  return { url, datasources, dashboards };
}

function epochMs(value) {
  if (value === null || value === undefined || value === '') return null;
  const ms = typeof value === 'number' ? value : Date.parse(value);
  return Number.isFinite(ms) ? ms : null;
}

/** Explore time range around a record (createdUtc to completedUtc or now), widened by the margin, as epoch-ms strings. */
export function recordTimeRange(record, now = Date.now()) {
  const start = epochMs(record?.createdUtc) ?? epochMs(record?.startedUtc) ?? now - 60 * 60 * 1000;
  const duration = Number(record?.durationMs);
  const end = epochMs(record?.completedUtc) ?? (epochMs(record?.createdUtc) !== null && Number.isFinite(duration) ? start + duration : now);
  return { from: String(start - RANGE_MARGIN_MS), to: String(Math.max(end, start) + RANGE_MARGIN_MS) };
}

/** Grafana Explore URL with one pane: `/explore?schemaVersion=1&orgId=1&panes=<json>`. */
export function exploreUrl(config, datasourceUid, queries, range) {
  if (!config?.url || !datasourceUid) return null;
  const panes = { gx: { datasource: datasourceUid, queries, range } };
  return `${config.url}/explore?schemaVersion=1&orgId=1&panes=${encodeURIComponent(JSON.stringify(panes))}`;
}

/** Explore link to a record's trace in Tempo (a tool invocation, a chat turn), or null without Grafana or a trace ID. */
export function traceUrl(config, record, now = Date.now()) {
  const traceId = typeof record?.traceId === 'string' ? record.traceId.trim() : '';
  const ds = config?.datasources?.tempo;
  if (!traceId || !ds) return null;
  return exploreUrl(config, ds, [{ refId: 'A', datasource: { type: 'tempo', uid: ds }, queryType: 'traceql', query: traceId }], recordTimeRange(record, now));
}

/**
 * Dashboard link: `/d/<uid>?orgId=1` plus optional `from`, `to` (epoch ms or ISO strings) and `vars` (`var-<name>`).
 * @param {object} config grafanaConfig() result
 * @param {string} key dashboard key (see DASHBOARD_SECTIONS)
 */
export function dashboardUrl(config, key, { from, to, vars } = {}) {
  const dash = config?.dashboards?.[key];
  if (!config?.url || !dash) return null;
  const params = new URLSearchParams({ orgId: '1' });
  const f = epochMs(from);
  const tt = epochMs(to);
  if (f !== null) params.set('from', String(f));
  if (tt !== null) params.set('to', String(tt));
  for (const [name, value] of Object.entries(vars || {})) {
    if (value !== null && value !== undefined && value !== '') params.append(`var-${name}`, String(value));
  }
  return `${config.url}/d/${encodeURIComponent(dash)}?${params.toString()}`;
}

/**
 * One link per dashboard for the Home observability section, in DASHBOARD_SECTIONS order. Empty when Grafana is not
 * configured or server info reports no dashboards; dashboards whose UID is blank are skipped.
 * @returns {{ key: string, uid: string, url: string }[]}
 */
export function sectionDashboardLinks(info) {
  const config = grafanaConfig(info);
  const reported = info?.observability?.dashboards;
  if (!config || !reported || typeof reported !== 'object') return [];
  const links = [];
  for (const key of DASHBOARD_SECTIONS) {
    const url = dashboardUrl(config, key);
    if (url) links.push({ key, uid: config.dashboards[key], url });
  }
  return links;
}
