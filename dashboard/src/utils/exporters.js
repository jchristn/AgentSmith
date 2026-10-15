// Chart and table export (P5.50). CSV and JSON carry the exact data behind a chart; SVG and PNG carry the
// rendered chart with its title, legend, filters, and time range. Numbers in CSV and JSON are written with
// JavaScript's locale-independent formatting (dot decimals, no grouping) and timestamps as ISO 8601 UTC.

/** Make a value safe for a file name segment. */
export function slug(value) {
  return String(value ?? '')
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[^\w.-]+/g, '-')
    .replace(/-+/g, '-')
    .replace(/^-|-$/g, '')
    .toLowerCase()
    .slice(0, 60);
}

/**
 * File name: garrison-<chart>-<range>[-<filter values>].<ext>. Filters with empty values are skipped, so the
 * name says exactly which slice of data the file holds.
 */
export function exportFileName(chart, range, filters, ext) {
  const parts = ['garrison', slug(chart), slug(range)];
  for (const [k, v] of Object.entries(filters || {})) if (v !== undefined && v !== null && v !== '') parts.push(`${slug(k)}-${slug(v)}`);
  return `${parts.filter(Boolean).join('_')}.${ext}`;
}

function csvCell(value) {
  if (value === null || value === undefined) return '';
  let s;
  if (typeof value === 'number') s = Number.isFinite(value) ? String(value) : '';
  else if (value instanceof Date) s = value.toISOString();
  else if (typeof value === 'boolean') s = value ? 'true' : 'false';
  else s = String(value);
  return /[",\r\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
}

/** RFC 4180 CSV from a header array and row arrays, with CRLF line endings. */
export function toCsv(header, rows) {
  return [header, ...rows].map((row) => row.map(csvCell).join(',')).join('\r\n') + '\r\n';
}

/**
 * CSV for a time series chart: one row per bucket, first column bucket_start_utc (ISO 8601), then one column
 * per series in legend order. Missing values (null) are empty cells, not zero.
 */
export function seriesCsv(times, series) {
  const header = ['bucket_start_utc', ...series.map((s) => s.label)];
  const rows = times.map((iso, i) => [new Date(iso).toISOString(), ...series.map((s) => (s.values[i] === undefined ? null : s.values[i]))]);
  return toCsv(header, rows);
}

/** JSON document for a time series chart, including the context needed to interpret it. */
export function seriesJson({ chart, title, range, filters, times, series, unit }) {
  return JSON.stringify(
    {
      chart,
      title,
      unit: unit || null,
      fromUtc: range?.fromUtc || (times[0] ? new Date(times[0]).toISOString() : null),
      toUtc: range?.toUtc || null,
      filters: filters || {},
      exportedUtc: new Date().toISOString(),
      series: series.map((s) => ({ key: s.key, label: s.label })),
      buckets: times.map((iso, i) => ({ startUtc: new Date(iso).toISOString(), values: Object.fromEntries(series.map((s) => [s.key, s.values[i] ?? null])) }))
    },
    null,
    2
  );
}

/** Trigger a browser download of a Blob. */
export function downloadBlob(name, blob) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  a.rel = 'noopener';
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export function downloadText(name, text, type) {
  downloadBlob(name, new Blob([text], { type }));
}

const STYLE_PROPS = ['fill', 'stroke', 'stroke-width', 'stroke-dasharray', 'opacity', 'font-size', 'font-family', 'font-weight'];

/** Copy computed paint and font styles onto a cloned SVG so it renders the same outside the page. */
function inlineStyles(source, target) {
  const srcEls = [source, ...source.querySelectorAll('*')];
  const dstEls = [target, ...target.querySelectorAll('*')];
  srcEls.forEach((el, i) => {
    const cs = getComputedStyle(el);
    const parts = STYLE_PROPS.map((p) => `${p}:${cs.getPropertyValue(p)}`).join(';');
    dstEls[i].setAttribute('style', parts);
  });
}

function esc(text) {
  return String(text ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);
}

/**
 * A standalone SVG document for a rendered chart: a header with the title and subtitle lines (time range and
 * filters), a legend row, then the chart itself with styles inlined and theme colors resolved.
 */
export function buildChartSvg(svgEl, { title, lines = [], legend = [] }) {
  const rect = svgEl.getBoundingClientRect();
  const width = Math.max(320, Math.round(rect.width || Number(svgEl.getAttribute('width')) || 800));
  const chartH = Math.round(rect.height || Number(svgEl.getAttribute('height')) || 400);
  const root = getComputedStyle(document.body);
  const bg = root.getPropertyValue('--color-surface').trim() || '#ffffff';
  const fg = root.getPropertyValue('--color-text').trim() || '#0f172a';
  const muted = root.getPropertyValue('--color-text-secondary').trim() || '#64748b';
  const font = root.getPropertyValue('font-family') || 'sans-serif';
  const headerH = 28 + lines.length * 18 + (legend.length ? 26 : 0) + 8;
  const clone = svgEl.cloneNode(true);
  inlineStyles(svgEl, clone);
  clone.setAttribute('x', '0');
  clone.setAttribute('y', String(headerH));
  clone.setAttribute('width', String(width));
  clone.setAttribute('height', String(chartH));
  let x = 16;
  const legendItems = legend
    .map((item) => {
      const color = resolveColor(item.color);
      const out = `<rect x="${x}" y="${headerH - 26}" width="10" height="10" rx="2" fill="${esc(color)}"/><text x="${x + 14}" y="${headerH - 17}" font-size="12" fill="${esc(muted)}">${esc(item.label)}</text>`;
      x += 24 + String(item.label).length * 7;
      return out;
    })
    .join('');
  const text = lines.map((line, i) => `<text x="16" y="${44 + i * 18}" font-size="12" fill="${esc(muted)}">${esc(line)}</text>`).join('');
  return `<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${headerH + chartH}" viewBox="0 0 ${width} ${headerH + chartH}" font-family="${esc(font)}">
<rect width="100%" height="100%" fill="${esc(bg)}"/>
<text x="16" y="24" font-size="16" font-weight="600" fill="${esc(fg)}">${esc(title)}</text>
${text}${legendItems}
${new XMLSerializer().serializeToString(clone)}
</svg>`;
}

/** Resolve a CSS color that may be a var(--name) reference to its current value. */
export function resolveColor(color) {
  const m = /^var\((--[\w-]+)\)$/.exec(String(color || '').trim());
  if (!m) return color;
  return getComputedStyle(document.body).getPropertyValue(m[1]).trim() || color;
}

/** Render SVG markup to a PNG Blob at the given scale (2x by default). */
export function svgToPng(svgText, scale = 2) {
  return new Promise((resolve, reject) => {
    const img = new Image();
    const url = URL.createObjectURL(new Blob([svgText], { type: 'image/svg+xml;charset=utf-8' }));
    img.onload = () => {
      try {
        const canvas = document.createElement('canvas');
        canvas.width = Math.round(img.width * scale);
        canvas.height = Math.round(img.height * scale);
        const ctx = canvas.getContext('2d');
        ctx.scale(scale, scale);
        ctx.drawImage(img, 0, 0);
        URL.revokeObjectURL(url);
        canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('PNG encoding failed'))), 'image/png');
      } catch (e) {
        reject(e);
      }
    };
    img.onerror = () => {
      URL.revokeObjectURL(url);
      reject(new Error('SVG could not be rendered'));
    };
    img.src = url;
  });
}

/**
 * CSV for a table page: the visible columns except actions, using each column's `csv(row)` when it has one,
 * else the raw record value for the column key (objects and arrays become JSON).
 */
export function tableCsv(columns, items) {
  const cols = columns.filter((c) => c.key !== 'actions');
  const value = (col, row) => {
    if (col.csv) return col.csv(row);
    const v = row?.[col.key];
    if (v === null || v === undefined) return null;
    return typeof v === 'object' ? JSON.stringify(v) : v;
  };
  return toCsv(
    cols.map((c) => c.label),
    items.map((row) => cols.map((c) => value(c, row)))
  );
}
