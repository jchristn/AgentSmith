// Chart helpers and normalization for GET /v1.0/api/analytics/* (plan section 14.2: bucketed, tenant scoped, with
// server-side gap fill). The dashboard accepts `{ bucketSeconds, buckets: [{ timestampUtc, <metric>: number }] }` and
// also `{ times, series: [{ key, values }] }`, and still fills gaps itself so a sparse answer draws correctly.

/** Chart series colors (theme variables, so light and dark both work). */
export const SERIES_COLORS = ['var(--chart-1)', 'var(--chart-2)', 'var(--chart-3)', 'var(--chart-4)', 'var(--chart-5)', 'var(--chart-6)'];

/** Fixed colors for effects: Read neutral, Write amber, Destructive red (plan section 18.4). */
export const EFFECT_COLORS = { Read: 'var(--chart-neutral)', Write: 'var(--color-warning)', Destructive: 'var(--color-danger)' };

/** Round axis ticks from zero to at least `max`. */
export function niceTicks(max, count = 4) {
  if (!Number.isFinite(max) || max <= 0) return [0, 1, 2, 3, 4].map((v) => v / 4);
  const raw = max / count;
  const mag = 10 ** Math.floor(Math.log10(raw));
  const norm = raw / mag;
  const step = (norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 2.5 ? 2.5 : norm <= 5 ? 5 : 10) * mag;
  return Array.from({ length: count + 1 }, (_, i) => Number((i * step).toPrecision(12)));
}

/** Largest value to fit on a chart: stacked sums per bucket, or the largest single value for lines. */
export function chartMax(series, kind) {
  let max = 0;
  const len = series[0]?.values.length || 0;
  for (let i = 0; i < len; i++) {
    if (kind === 'stacked') {
      max = Math.max(
        max,
        series.reduce((sum, s) => sum + (s.hidden ? 0 : s.values[i] || 0), 0)
      );
    } else {
      for (const s of series) if (!s.hidden && s.values[i] !== null && s.values[i] !== undefined) max = Math.max(max, s.values[i]);
    }
  }
  return max;
}

/** Bucket start times from `fromMs` to `toMs` every `bucketMs` (the client-side gap fill). */
export function bucketTimes(fromMs, toMs, bucketMs) {
  const out = [];
  if (!(bucketMs > 0) || !(toMs > fromMs)) return out;
  const start = Math.floor(fromMs / bucketMs) * bucketMs;
  for (let t = start; t < toMs && out.length < 2000; t += bucketMs) out.push(new Date(t).toISOString());
  return out;
}

/**
 * Normalize an analytics response into { times, series: [{ key, metric, values }] } for the given metrics.
 * Missing buckets become 0 for counters (or null when `nullGaps` is set, for averages and percentiles).
 */
export function normalizeSeries(response, metrics, { fromUtc, toUtc, bucketMinutes, nullGaps = false } = {}) {
  if (Array.isArray(response?.times) && Array.isArray(response?.series)) {
    return {
      times: response.times,
      series: metrics.map((m) => ({
        key: m,
        metric: m,
        values: (response.series.find((s) => s.key === m || s.metric === m)?.values || []).map((v) => (v === undefined ? null : v))
      }))
    };
  }
  const buckets = Array.isArray(response?.buckets) ? response.buckets : Array.isArray(response) ? response : [];
  const bucketMs = (response?.bucketSeconds ? response.bucketSeconds * 1000 : 0) || (bucketMinutes || 0) * 60000;
  const byTime = new Map();
  for (const b of buckets) {
    const ts = b.timestampUtc || b.bucketStartUtc || b.time || b.t;
    if (!ts) continue;
    const ms = Date.parse(ts);
    if (Number.isFinite(ms)) byTime.set(bucketMs ? Math.floor(ms / bucketMs) * bucketMs : ms, b);
  }
  let times;
  if (bucketMs && fromUtc && toUtc) times = bucketTimes(Date.parse(fromUtc), Date.parse(toUtc), bucketMs);
  else times = [...byTime.keys()].sort((a, b) => a - b).map((ms) => new Date(ms).toISOString());
  const series = metrics.map((m) => ({
    key: m,
    metric: m,
    values: times.map((iso) => {
      const ms = Date.parse(iso);
      const b = byTime.get(bucketMs ? Math.floor(ms / bucketMs) * bucketMs : ms);
      const v = b ? Number(b[m]) : NaN;
      if (Number.isFinite(v)) return v;
      return nullGaps ? null : 0;
    })
  }));
  return { times, series };
}

/** Sum of a series' values (nulls skipped). */
export function seriesTotal(series) {
  return (series?.values || []).reduce((s, v) => s + (Number.isFinite(v) ? v : 0), 0);
}
