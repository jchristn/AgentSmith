import { REQUEST_RANGES } from './constants';

/**
 * Normalize server buckets onto the exact grid for a range preset (Last Hour 60, Last Day 96, Last Week 84,
 * Last Month 120). Missing buckets become zero-count buckets; the grid ends at `now`.
 * @param {Array} raw Server buckets with bucketStartUtc, successCount, failureCount, averageDurationMs.
 * @param {string} rangeId hour | day | week | month.
 * @param {Date} now End of the window.
 * @returns {Array} Exactly bucketCount buckets.
 */
export function normalizeBuckets(raw, rangeId, now = new Date()) {
  const range = REQUEST_RANGES[rangeId] || REQUEST_RANGES.day;
  const sizeMs = range.bucketMinutes * 60 * 1000;
  const end = Math.ceil(now.getTime() / sizeMs) * sizeMs;
  const start = end - range.bucketCount * sizeMs;
  const byIndex = new Map();
  for (const b of raw || []) {
    const ts = new Date(b.bucketStartUtc).getTime();
    if (Number.isNaN(ts)) continue;
    const idx = Math.floor((ts - start) / sizeMs);
    if (idx < 0 || idx >= range.bucketCount) continue;
    const prev = byIndex.get(idx);
    const success = Number(b.successCount || 0) + (prev?.successCount || 0);
    const failure = Number(b.failureCount || 0) + (prev?.failureCount || 0);
    byIndex.set(idx, { successCount: success, failureCount: failure, averageDurationMs: Number(b.averageDurationMs || 0) });
  }
  const out = [];
  for (let i = 0; i < range.bucketCount; i++) {
    const s = start + i * sizeMs;
    const v = byIndex.get(i) || { successCount: 0, failureCount: 0, averageDurationMs: 0 };
    out.push({ bucketStartUtc: new Date(s).toISOString(), bucketEndUtc: new Date(s + sizeMs).toISOString(), ...v });
  }
  return out;
}
