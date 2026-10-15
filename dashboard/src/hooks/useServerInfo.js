import { useEffect, useMemo, useState } from 'react';
import { useAuth } from '../context/AuthContext';
import { grafanaConfig } from '../utils/grafana';

// One server-info request per API client, shared by every component that needs it (deep links on several pages).
const cache = new WeakMap();

function load(apiClient) {
  if (!cache.has(apiClient)) {
    cache.set(
      apiClient,
      apiClient.getServerInfo().catch(() => {
        cache.delete(apiClient);
        return null;
      })
    );
  }
  return cache.get(apiClient);
}

/** Server info (GET /server-info), or null while loading or when it failed. */
export default function useServerInfo() {
  const { apiClient } = useAuth();
  const [info, setInfo] = useState(null);
  useEffect(() => {
    if (!apiClient) return undefined;
    let cancelled = false;
    load(apiClient).then((i) => {
      if (!cancelled) setInfo(i);
    });
    return () => {
      cancelled = true;
    };
  }, [apiClient]);
  return info;
}

/** Grafana link configuration from server info, or null when Grafana is not configured (P13.15). */
export function useGrafana() {
  const info = useServerInfo();
  return useMemo(() => grafanaConfig(info), [info]);
}
