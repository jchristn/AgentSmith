import { useState, useCallback } from 'react';
import { STORAGE_KEYS } from '../utils/constants';

function readAll() {
  try {
    return JSON.parse(localStorage.getItem(STORAGE_KEYS.columns) || '{}') || {};
  } catch {
    return {};
  }
}

/**
 * Resolve the hidden column keys for a table from a saved preference. Saved preferences are
 * `{ hidden, known }`: columns the user has never seen (not in `known`) fall back to their default, so a
 * column added in a later release (for example new run telemetry columns) honors `defaultHidden`.
 * The older array format is read as the hidden list, with every default-hidden column added.
 */
export function resolveHidden(saved, columns) {
  const defaults = columns.filter((c) => c.defaultHidden && !c.pinned).map((c) => c.key);
  if (Array.isArray(saved)) return [...new Set([...saved, ...defaults])];
  if (saved && Array.isArray(saved.hidden)) {
    const known = new Set(saved.known || []);
    const fresh = defaults.filter((k) => !known.has(k));
    return [...new Set([...saved.hidden, ...fresh])];
  }
  return defaults;
}

/**
 * Per-table column visibility persisted in localStorage. Pinned columns are always visible.
 * @param {string} tableId Stable table identifier.
 * @param {Array<{key:string,pinned?:boolean,defaultHidden?:boolean}>} columns Column definitions.
 */
export default function useColumnPrefs(tableId, columns) {
  const [hidden, setHidden] = useState(() => resolveHidden(readAll()[tableId], columns));

  const toggle = useCallback(
    (key) => {
      setHidden((prev) => {
        const next = prev.includes(key) ? prev.filter((k) => k !== key) : [...prev, key];
        const all = readAll();
        all[tableId] = { hidden: next, known: columns.map((c) => c.key) };
        localStorage.setItem(STORAGE_KEYS.columns, JSON.stringify(all));
        return next;
      });
    },
    [tableId, columns]
  );

  const visibleColumns = columns.filter((c) => c.pinned || !hidden.includes(c.key));
  return { hidden, toggle, visibleColumns };
}
