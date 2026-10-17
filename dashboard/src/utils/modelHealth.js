/** Normalize health states without treating an unrecognized state as a failed check. */
export function modelHealthStatus(value) {
  return value === 'Healthy' || value === 'Unhealthy' ? value : 'Unknown';
}

/** Statistics describe observed checks, never time-based availability between checks. */
export function summarizeModelHealth(history = []) {
  const oldestFirst = [...history].sort((a, b) => (Date.parse(a.checkedUtc) || 0) - (Date.parse(b.checkedUtc) || 0));
  const newestFirst = [...oldestFirst].reverse();
  const latest = newestFirst[0];
  const status = modelHealthStatus(latest?.status);
  const lastHealthy = newestFirst.find((item) => modelHealthStatus(item.status) === 'Healthy');
  const lastUnhealthy = newestFirst.find((item) => modelHealthStatus(item.status) === 'Unhealthy');
  const healthy = oldestFirst.filter((item) => modelHealthStatus(item.status) === 'Healthy').length;
  let consecutive = 0;
  for (const item of newestFirst) {
    if (modelHealthStatus(item.status) !== status) break;
    consecutive++;
  }
  const firstTime = Date.parse(oldestFirst[0]?.checkedUtc);
  const lastTime = Date.parse(latest?.checkedUtc);
  return {
    oldestFirst,
    newestFirst,
    total: oldestFirst.length,
    status,
    healthy,
    successRate: oldestFirst.length ? healthy / oldestFirst.length : null,
    spanMs: Number.isFinite(firstTime) && Number.isFinite(lastTime) ? Math.max(0, lastTime - firstTime) : null,
    consecutiveHealthy: status === 'Healthy' ? consecutive : 0,
    consecutiveUnhealthy: status === 'Unhealthy' ? consecutive : 0,
    firstCheck: oldestFirst[0]?.checkedUtc,
    lastCheck: latest?.checkedUtc,
    lastHealthy: lastHealthy?.checkedUtc,
    lastUnhealthy: lastUnhealthy?.checkedUtc,
    lastError: lastUnhealthy?.message || null
  };
}
