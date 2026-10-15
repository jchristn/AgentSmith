import { useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import ChartTooltip from './ChartTooltip';
import ExportMenu from './ExportMenu';
import { exportFileName, seriesCsv, seriesJson, buildChartSvg } from '../utils/exporters';
import { tip } from './Tooltip';
import useMeasuredWidth from '../hooks/useMeasuredWidth';
import { niceTicks, chartMax } from '../utils/analytics';
import { formatDateTime } from '../i18n/formatters';

// Hand-rolled SVG time series chart in the style of ActivityChart: drawn at the measured container width in CSS
// pixels, full width and tall, with a recessive grid, one y-axis, a hover crosshair and portaled tooltip that
// lists every series for the bucket, a legend whose items toggle series, and a data table for keyboard and
// screen reader users. `kind` is 'stacked' (bars that add up) or 'line' (values that do not, such as averages).

const TALL_H = 400;
const SHORT_H = 280;
const NARROW = 720;
const PAD_TOP = 16;
const PAD_RIGHT = 20;
const PAD_BOTTOM = 40;
const PAD_LEFT = 72;
const MAX_X_LABELS = 8;

function labelIndices(count) {
  if (count <= MAX_X_LABELS) return new Set(Array.from({ length: count }, (_, i) => i));
  const set = new Set();
  for (let i = 0; i < MAX_X_LABELS; i++) set.add(Math.round((i * (count - 1)) / (MAX_X_LABELS - 1)));
  return set;
}

function axisTime(iso, bucketSeconds, spanMs, locale) {
  const opts = bucketSeconds && bucketSeconds >= 86400 ? { month: 'short', day: 'numeric' } : spanMs > 26 * 3600 * 1000 ? { month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit' } : { hour: '2-digit', minute: '2-digit' };
  return formatDateTime(iso, locale, opts);
}

/** Split a line's points at nulls so gaps stay visible. */
function segments(points) {
  const out = [];
  let current = [];
  for (const p of points) {
    if (p) current.push(p);
    else if (current.length) {
      out.push(current);
      current = [];
    }
  }
  if (current.length) out.push(current);
  return out;
}

export default function TimeSeriesChart({ id, title, help, titleTip, kind = 'line', times = [], series = [], format, bucketSeconds, legendTip, testId, exportInfo }) {
  const { t, i18n } = useTranslation();
  const bodyRef = useRef(null);
  const svgRef = useRef(null);
  const W = useMeasuredWidth(bodyRef);
  const H = W >= NARROW ? TALL_H : SHORT_H;
  const plotW = Math.max(1, W - PAD_LEFT - PAD_RIGHT);
  const plotH = H - PAD_TOP - PAD_BOTTOM;
  const [hiddenKeys, setHiddenKeys] = useState([]);
  const [hover, setHover] = useState(null);

  const visible = useMemo(() => series.map((s) => ({ ...s, hidden: hiddenKeys.includes(s.key) })), [series, hiddenKeys]);
  const ticks = niceTicks(chartMax(visible, kind));
  const axisMax = ticks[ticks.length - 1] || 1;
  const n = times.length;
  const colW = n > 0 ? plotW / n : plotW;
  const barW = Math.max(2, Math.min(40, colW * 0.7));
  const y = (v) => PAD_TOP + plotH - (Math.max(0, v) / axisMax) * plotH;
  const xCenter = (i) => PAD_LEFT + i * colW + colW / 2;
  const spanMs = n > 1 ? new Date(times[n - 1]).getTime() - new Date(times[0]).getTime() : 0;
  const labels = labelIndices(n);
  const lng = i18n.language;
  const hasData = visible.some((s) => s.values.some((v) => v !== null && v !== undefined && v !== 0));
  const name = (s) => s.label || t(`views.analytics.metrics.${s.metric}`);

  const toggle = (key) => setHiddenKeys((prev) => (prev.includes(key) ? prev.filter((k) => k !== key) : [...prev, key]));

  const shown = visible.filter((s) => !s.hidden);
  const labeled = series.map((s) => ({ ...s, label: name(s) }));
  const hoveredRows =
    hover !== null
      ? shown.map((s) => ({ key: s.key, label: name(s), color: s.color, value: s.values[hover.index] }))
      : [];

  return (
    <section className="chart-card" aria-labelledby={`chart-${id}-title`} data-testid={testId}>
      <div className="chart-header">
        <div>
          <h2 id={`chart-${id}-title`} className="chart-title" {...tip(titleTip)}>
            {title}
          </h2>
          {help && <div className="analytics-chart-help">{help}</div>}
        </div>
        {exportInfo && n > 0 && (
          <div className="chart-controls">
            <ExportMenu
              title={title}
              testId={`export-${id}`}
              fileName={(ext) => exportFileName(exportInfo.name || id, exportInfo.range, exportInfo.filters, ext)}
              csv={() => seriesCsv(times, labeled)}
              json={() => seriesJson({ chart: exportInfo.name || id, title, range: exportInfo.window, filters: exportInfo.filters, times, series: labeled, unit: exportInfo.unit })}
              svg={() => (svgRef.current ? buildChartSvg(svgRef.current, { title, lines: exportInfo.lines || [], legend: shown.map((s) => ({ label: name(s), color: s.color })) }) : '')}
            />
          </div>
        )}
      </div>
      {series.length > 1 && (
        <div className="chart-legend" role="group" aria-label={t('views.analytics.legend', { chart: title })}>
          {visible.map((s) => (
            <button key={s.key} type="button" className="chart-legend-toggle" aria-pressed={!s.hidden} onClick={() => toggle(s.key)} {...tip(legendTip ? legendTip(s) : '')} data-series={s.key}>
              <span className="chart-swatch" style={{ background: s.color }} aria-hidden="true" />
              {name(s)}
            </button>
          ))}
        </div>
      )}
      <div className="chart-body" ref={bodyRef}>
        {n === 0 || !hasData ? (
          <div className="chart-empty" style={{ minHeight: Math.round(H / 2) }}>
            {t('views.analytics.noDataInChart')}
          </div>
        ) : (
          <div className="chart-canvas">
            <svg ref={svgRef} viewBox={`0 0 ${W} ${H}`} width={W} height={H} role="img" aria-label={title}>
              {ticks.map((tick) => (
                <g key={tick}>
                  <line className="chart-grid-line" x1={PAD_LEFT} x2={W - PAD_RIGHT} y1={y(tick)} y2={y(tick)} strokeDasharray={tick === 0 ? '0' : '4 4'} />
                  <text className="chart-svg-text" x={PAD_LEFT - 8} y={y(tick) + 4} textAnchor="end">
                    {format(tick)}
                  </text>
                </g>
              ))}

              {kind === 'stacked' &&
                times.map((_, i) => {
                  let base = 0;
                  return (
                    <g key={`b${i}`}>
                      {shown.map((s) => {
                        const v = s.values[i] || 0;
                        if (v <= 0) return null;
                        const top = y(base + v);
                        const bottom = y(base);
                        base += v;
                        const h = Math.max(1, bottom - top - (base > v ? 2 : 0));
                        return <rect key={s.key} x={xCenter(i) - barW / 2} y={top} width={barW} height={h} rx={2} fill={s.color} opacity={hover && hover.index !== i ? 0.75 : 1} />;
                      })}
                    </g>
                  );
                })}

              {kind === 'line' &&
                shown.map((s) => {
                  const points = s.values.map((v, i) => (v === null || v === undefined ? null : [xCenter(i), y(v)]));
                  return (
                    <g key={s.key}>
                      {segments(points).map((seg, k) =>
                        seg.length === 1 ? (
                          <circle key={k} cx={seg[0][0]} cy={seg[0][1]} r={3} fill={s.color} />
                        ) : (
                          <path key={k} d={seg.map((p, j) => `${j ? 'L' : 'M'}${p[0].toFixed(1)},${p[1].toFixed(1)}`).join(' ')} fill="none" stroke={s.color} strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />
                        )
                      )}
                      {hover && points[hover.index] && <circle cx={points[hover.index][0]} cy={points[hover.index][1]} r={4} fill={s.color} stroke="var(--color-surface)" strokeWidth={2} />}
                    </g>
                  );
                })}

              {hover && <line className="chart-crosshair" x1={xCenter(hover.index)} x2={xCenter(hover.index)} y1={PAD_TOP} y2={PAD_TOP + plotH} />}

              {times.map((iso, i) => (
                <rect
                  key={`h${i}`}
                  x={PAD_LEFT + i * colW}
                  y={PAD_TOP}
                  width={colW}
                  height={plotH}
                  fill="transparent"
                  onMouseMove={(e) => setHover({ index: i, x: e.clientX, y: e.clientY })}
                  onMouseLeave={() => setHover(null)}
                  data-bucket={i}
                />
              ))}

              {times.map((iso, i) =>
                labels.has(i) ? (
                  <text key={`x${i}`} className="chart-svg-text" x={xCenter(i)} y={H - PAD_BOTTOM + 20} textAnchor="middle">
                    {axisTime(iso, bucketSeconds, spanMs, lng)}
                  </text>
                ) : null
              )}
            </svg>
            {hover && (
              <ChartTooltip
                pos={{ x: hover.x, y: hover.y }}
                content={
                  <div data-testid="chart-tooltip">
                    <div className="chart-tooltip-title">{formatDateTime(times[hover.index], lng)}</div>
                    {hoveredRows.map((r) => (
                      <div className="chart-tooltip-row" key={r.key}>
                        <span>
                          <span className="chart-swatch" style={{ background: r.color }} aria-hidden="true" /> {r.label}
                        </span>
                        <span>{r.value === null || r.value === undefined ? t('common.generic.notAvailable') : format(r.value)}</span>
                      </div>
                    ))}
                    {kind === 'stacked' && hoveredRows.length > 1 && (
                      <div className="chart-tooltip-row">
                        <strong>{t('components.chart.total')}</strong>
                        <strong>{format(hoveredRows.reduce((sum, r) => sum + (r.value || 0), 0))}</strong>
                      </div>
                    )}
                  </div>
                }
              />
            )}
          </div>
        )}
      </div>
      {n > 0 && hasData && (
        <details className="details-section">
          <summary {...tip(t('tips.analytics.dataTable'))}>{t('views.analytics.dataTable')}</summary>
          <div className="table-scroll">
            <table className="data-table">
              <thead>
                <tr>
                  <th scope="col" {...tip(t('tips.analytics.bucketStart'))}>
                    {t('views.analytics.bucketStart')}
                  </th>
                  {visible.map((s) => (
                    <th scope="col" key={s.key} className="right" {...tip(legendTip ? legendTip(s) : '')}>
                      {name(s)}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {times.map((iso, i) => (
                  <tr key={iso}>
                    <td className="nowrap">{formatDateTime(iso, lng)}</td>
                    {visible.map((s) => (
                      <td key={s.key} className="right">
                        {s.values[i] === null || s.values[i] === undefined ? t('common.generic.notAvailable') : format(s.values[i])}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </details>
      )}
    </section>
  );
}
