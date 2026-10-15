// Chart helpers for GET /v1.0/api/analytics/{overview,tokens,tools,models} (plan section 14.2): reports bucketed by
// minute, hour, or day, tenant scoped, with every bucket in the window filled by the server
// (src/Hugo.Core/Responses/AnalyticsReport.cs).

/** Chart series colors (theme variables, so light and dark both work). */
export const SERIES_COLORS = ['var(--chart-1)', 'var(--chart-2)', 'var(--chart-3)', 'var(--chart-4)', 'var(--chart-5)', 'var(--chart-6)'];

/** Analytics interval (minute, hour, day) per chart range preset, chosen to stay well under the server's bucket limit. */
export const ANALYTICS_INTERVALS = { hour: 'minute', day: 'hour', week: 'hour', month: 'day' };

/** Seconds per analytics interval. */
export const INTERVAL_SECONDS = { minute: 60, hour: 3600, day: 86400 };

/** The analytics query for a range preset and its window: { fromUtc, toUtc, interval }. */
export function analyticsQuery(rangeId, range) {
  return { fromUtc: range?.fromUtc, toUtc: range?.toUtc, interval: ANALYTICS_INTERVALS[rangeId] || 'hour' };
}

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

/**
 * Chart series from an analytics report's buckets ([{ startUtc, <metric>: number }]): { times, series: [{ key,
 * metric, values }] }. The server fills every bucket in the window, so the times are the buckets' start times. A
 * missing value is 0 for counters, or null when `nullGaps` is set (averages, where an empty bucket has no value).
 */
export function reportSeries(report, metrics, { nullGaps = false } = {}) {
  const buckets = Array.isArray(report?.buckets) ? report.buckets : [];
  return {
    times: buckets.map((b) => b.startUtc),
    series: metrics.map((m) => ({
      key: m,
      metric: m,
      values: buckets.map((b) => {
        const v = b?.[m];
        if (v === null || v === undefined || !Number.isFinite(Number(v))) return nullGaps ? null : 0;
        return Number(v);
      })
    }))
  };
}

/** Sum of a series' values (nulls skipped). */
export function seriesTotal(series) {
  return (series?.values || []).reduce((s, v) => s + (Number.isFinite(v) ? v : 0), 0);
}
