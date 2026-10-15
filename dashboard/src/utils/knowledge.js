// Helpers for resource knowledge (plan D-038): durable, tenant-scoped notes per resource that connectors read and
// write. The SQL connectors keep database context at scope `database` (the root entry, key _root) and table context
// at scope `table` (key schema.table); any connector may use other scopes.

/** The key the server stores and returns for a scope's root entry. Inputs accept it or an empty key. */
export const ROOT_KEY = '_root';

/** Whether a key names a scope's root entry (_root, or empty from older data and new-entry forms). */
export const isRootKey = (key) => !key || key === ROOT_KEY;

/** A key in its canonical form: _root for the root entry, otherwise unchanged. */
export const normalizeKey = (key) => (isRootKey(key) ? ROOT_KEY : key);

/** What to call an entry in lists and headings: the database context, the scope for a root entry, or the key. */
export function entryLabel(t, entry) {
  if (entry?.scope === 'database' && isRootKey(entry?.key)) return t('views.knowledge.databaseContext');
  return isRootKey(entry?.key) ? entry?.scope : entry.key;
}

/** Source tones: operator edits are the most trusted, generated text needs review, agent notes are unverified. */
export function knowledgeSourceTone(source) {
  switch (source) {
    case 'Operator':
      return 'info';
    case 'Generated':
      return 'success';
    case 'Agent':
      return 'warning';
    default:
      return 'neutral';
  }
}

/** The structured JSON a tool returned (result.structuredJson), parsed; null when absent or invalid. */
export function structuredResult(outcome) {
  const raw = outcome?.result?.structuredJson;
  if (!raw) return null;
  if (typeof raw === 'object') return raw;
  try {
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

/** A stable identity for an entry: scope plus key (the root entry is one identity whether its key is _root or empty). */
export const entryId = (scope, key) => `${scope}::${normalizeKey(key)}`;

/**
 * The entries to list in the context editor: every stored entry, plus a placeholder per table that has no context
 * yet (from sql_get_context with includeEmpty), plus the database entry even when it is missing (SQL resources).
 * Root keys are normalized to _root, so a stored root entry never gets a duplicate placeholder. Sorted: database
 * first, then tables by key, then other scopes (each scope's root entry first).
 */
export function contextEntries(stored, { isSql = false, tablesWithoutContext = [], extra = [] } = {}) {
  const map = new Map();
  for (const e of stored || []) map.set(entryId(e.scope, e.key), { ...e, key: normalizeKey(e.key), exists: true });
  if (isSql && !map.has(entryId('database', ROOT_KEY))) map.set(entryId('database', ROOT_KEY), { scope: 'database', key: ROOT_KEY, exists: false });
  for (const table of tablesWithoutContext || []) {
    const id = entryId('table', table);
    if (!map.has(id)) map.set(id, { scope: 'table', key: table, exists: false });
  }
  for (const e of extra || []) {
    const id = entryId(e.scope, e.key);
    if (!map.has(id)) map.set(id, { scope: e.scope, key: normalizeKey(e.key), exists: false });
  }
  const rank = (e) => (e.scope === 'database' ? 0 : e.scope === 'table' ? 1 : 2);
  // Within a scope the root entry comes first.
  const keyRank = (e) => (isRootKey(e.key) ? '' : e.key);
  return [...map.values()].sort((a, b) => rank(a) - rank(b) || a.scope.localeCompare(b.scope) || keyRank(a).localeCompare(keyRank(b)));
}

/** Whether a connector is one of the SQL family (it has the knowledge tools this page drives). */
export function isSqlConnector(connector) {
  if (!connector) return false;
  if (connector.family === 'sql') return true;
  return (connector.tools || []).some((t) => t.name === 'sql_get_intelligence');
}

/** A table name as the graph and lists show it: bare when the schema is the resource's default. */
export function tableLabel(schema, table, defaultSchema) {
  return !schema || schema === defaultSchema ? table : `${schema}.${table}`;
}

/**
 * Lay the tables of a relationship list on a circle for the SVG graph. Returns { nodes: [{ id, label, x, y }],
 * edges: [{ from, to, declared, confidence, label }] } inside a width by height box.
 */
export function relationshipLayout(relationships, { width = 640, height = 360, defaultSchema } = {}) {
  const ids = [];
  const add = (schema, table) => {
    const id = `${schema || ''}.${table}`;
    if (!ids.includes(id)) ids.push(id);
    return id;
  };
  const edges = (relationships || []).map((r) => ({
    from: add(r.fromSchema, r.fromTable),
    to: add(r.toSchema, r.toTable),
    declared: !!r.isDeclared,
    confidence: typeof r.confidence === 'number' ? r.confidence : null,
    label: `${r.fromColumn} -> ${r.toColumn}`
  }));
  const cx = width / 2;
  const cy = height / 2;
  const radius = Math.max(40, Math.min(width, height) / 2 - 60);
  const nodes = ids.map((id, i) => {
    const angle = ids.length === 1 ? 0 : (2 * Math.PI * i) / ids.length - Math.PI / 2;
    const [schema, ...rest] = id.split('.');
    const table = rest.join('.');
    return {
      id,
      label: tableLabel(schema, table, defaultSchema),
      x: ids.length === 1 ? cx : Math.round(cx + radius * Math.cos(angle)),
      y: ids.length === 1 ? cy : Math.round(cy + radius * Math.sin(angle))
    };
  });
  return { nodes, edges };
}
