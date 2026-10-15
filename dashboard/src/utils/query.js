// Shared query helpers for paginated, sorted, and filtered list requests.

/**
 * Build list parameters for the API client from table state.
 * @param {{pageNumber:number,pageSize:number,sortKey?:string,sortDir?:string,search?:string,filters?:object}} state
 * @returns {object}
 */
export function buildListParams({ pageNumber = 1, pageSize = 25, sortKey, sortDir, search, filters } = {}) {
  const params = { pageNumber, pageSize };
  if (sortKey === 'name') params.ordering = sortDir === 'desc' ? 'NameDescending' : 'NameAscending';
  else if (sortKey === 'createdUtc') params.ordering = sortDir === 'asc' ? 'CreatedAscending' : 'CreatedDescending';
  if (search) params.search = search;
  for (const [k, v] of Object.entries(filters || {})) {
    if (v !== undefined && v !== null && v !== '') params[k] = v;
  }
  return params;
}

/** Keys the server can sort by. Other columns sort the current page only and are marked as such. */
export const SERVER_SORT_KEYS = ['name', 'createdUtc'];

/**
 * Sort a page locally (used only for columns the server cannot sort).
 * @param {Array} items
 * @param {string} key
 * @param {'asc'|'desc'} dir
 */
export function sortLocal(items, key, dir) {
  if (!key) return items;
  const sorted = [...items].sort((a, b) => {
    const va = a?.[key];
    const vb = b?.[key];
    if (va === vb) return 0;
    if (va === undefined || va === null) return 1;
    if (vb === undefined || vb === null) return -1;
    if (typeof va === 'number' && typeof vb === 'number') return va - vb;
    return String(va).localeCompare(String(vb));
  });
  return dir === 'desc' ? sorted.reverse() : sorted;
}

/** datetime-local value to UTC ISO, or undefined. */
export function toUtcIso(local) {
  if (!local) return undefined;
  const d = new Date(local);
  return Number.isNaN(d.getTime()) ? undefined : d.toISOString();
}

/** UTC ISO to a datetime-local input value. */
export function toLocalInput(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  const pad = (n) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** Remove server-generated fields so a record can be duplicated. */
export function stripForDuplicate(record, extraKeys = []) {
  const out = { ...(record || {}) };
  for (const k of ['id', 'createdUtc', 'lastUpdateUtc', 'isProtected', 'isBuiltIn', 'builtInKey', 'currentVersion', 'configVersion', ...extraKeys]) delete out[k];
  if (typeof out.name === 'string') out.name = `${out.name}-copy`;
  if (typeof out.tenantId !== 'undefined') delete out.tenantId;
  const stripChildIds = (v) => (Array.isArray(v) ? v.map((c) => (c && typeof c === 'object' ? stripChild(c) : c)) : v);
  const stripChild = (c) => {
    const o = { ...c };
    delete o.id;
    for (const k of Object.keys(o)) if (/Id$/.test(k) && k !== 'resourceId' && k !== 'templateId' && k !== 'toolCallId' && k !== 'skillId') delete o[k];
    return o;
  };
  for (const k of Object.keys(out)) out[k] = stripChildIds(out[k]);
  return out;
}
