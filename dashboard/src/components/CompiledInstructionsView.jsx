import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import CopyButton from './CopyButton';
import { EnumBadge } from './StatusBadge';
import { tip } from './Tooltip';
import { formatNumber } from '../i18n/formatters';

/**
 * Split compiled text into chunks owned by the deepest source-map segment covering them (plan section 10.4: the
 * compiler's segments nest, for example instructions.root > section.resource > connector.ssh.instructions).
 * Returns [{ start, end, text, segment }] in order; `segment` is null for text no segment covers.
 */
export function sourceChunks(text, segments) {
  const value = text || '';
  const list = (segments || []).filter((s) => Number.isFinite(s?.start) && Number.isFinite(s?.length) && s.length > 0);
  const cuts = new Set([0, value.length]);
  for (const s of list) {
    cuts.add(Math.max(0, Math.min(value.length, s.start)));
    cuts.add(Math.max(0, Math.min(value.length, s.start + s.length)));
  }
  const points = [...cuts].sort((a, b) => a - b);
  const chunks = [];
  for (let i = 0; i < points.length - 1; i++) {
    const start = points[i];
    const end = points[i + 1];
    if (end <= start) continue;
    let owner = null;
    for (const s of list) {
      if (s.start <= start && s.start + s.length >= end && (!owner || (s.depth ?? 0) >= (owner.depth ?? 0))) owner = s;
    }
    const last = chunks[chunks.length - 1];
    if (last && last.segment === owner) {
      last.end = end;
      last.text = value.slice(last.start, end);
    } else chunks.push({ start, end, text: value.slice(start, end), segment: owner });
  }
  return chunks;
}

/** A stable identity for a segment (the same key can appear once per binding). */
const segmentId = (s) => (s ? `${s.key}|${s.bindingAlias || ''}|${s.start}` : '');

/**
 * Compiled instructions with their source map (P8.24): the text with a colored gutter per layer (Built-in, Global,
 * Tenant) showing which template produced each part, and a list of the segments that highlights its text on hover or
 * focus. Also shows the token estimate, binding count, and compiler warnings.
 */
export default function CompiledInstructionsView({ compiled }) {
  const { t, i18n } = useTranslation();
  const [active, setActive] = useState(null);
  const text = compiled?.text || compiled?.instructions || '';
  const segments = useMemo(() => compiled?.segments || compiled?.sections || [], [compiled]);
  const chunks = useMemo(() => sourceChunks(text, segments), [text, segments]);
  const ordered = useMemo(() => [...segments].sort((a, b) => (a.start ?? 0) - (b.start ?? 0) || (a.depth ?? 0) - (b.depth ?? 0)), [segments]);

  return (
    <div className="instructions-layout" data-testid="compiled-instructions">
      <div className="compiled-main">
        <div className="inline-meta compiled-meta">
          {compiled?.tokenEstimate != null && (
            <span className="text-muted">
              {t('views.profile.instructions.tokens', { count: compiled.tokenEstimate, value: formatNumber(compiled.tokenEstimate, {}, i18n.language) })}
            </span>
          )}
          {compiled?.bindingCount != null && <span className="text-muted">{t('views.profile.instructions.bindings', { count: compiled.bindingCount })}</span>}
          <CopyButton value={text} tipText={t('tips.views.profile.instructions.copy')} />
        </div>
        {(compiled?.warnings || []).length > 0 && (
          <ul className="warning-banner plain-list" data-testid="compiled-warnings">
            {compiled.warnings.map((w) => (
              <li key={w}>{w}</li>
            ))}
          </ul>
        )}
        <div className="compiled-text monospace" data-testid="compiled-text">
          {chunks.map((c) => (
            <div
              key={c.start}
              className={`src-chunk ${c.segment ? `layer-${c.segment.layer}` : 'layer-none'} ${active && c.segment && segmentId(c.segment) === active ? 'active' : ''}`}
              data-layer={c.segment?.layer || ''}
              data-key={c.segment?.key || ''}
              {...(c.segment
                ? tip(
                    t('views.profile.instructions.chunkTip', {
                      key: c.segment.key,
                      layer: t(`enums.promptLayer.${c.segment.layer}`),
                      version: c.segment.version
                    })
                  )
                : {})}
            >
              {c.text}
            </div>
          ))}
        </div>
      </div>
      {ordered.length > 0 && (
        <aside className="source-map" aria-label={t('views.profile.instructions.sourceMap')}>
          <h3 className="section-title">{t('views.profile.instructions.sourceMap')}</h3>
          <ol className="plain-list bare-list" data-testid="source-map">
            {ordered.map((s) => (
              <li key={segmentId(s)} className={`source-map-item depth-${Math.min(s.depth ?? 0, 3)}`}>
                <button
                  type="button"
                  className={`button-ghost button-sm source-map-button layer-${s.layer}`}
                  onMouseEnter={() => setActive(segmentId(s))}
                  onMouseLeave={() => setActive(null)}
                  onFocus={() => setActive(segmentId(s))}
                  onBlur={() => setActive(null)}
                  {...tip(t('tips.views.profile.instructions.section'))}
                >
                  <code>{s.key}</code>
                </button>{' '}
                <EnumBadge group="promptLayer" value={s.layer} tone={s.layer === 'Tenant' ? 'warning' : s.layer === 'Global' ? 'info' : 'neutral'} />{' '}
                {s.version ? <span className="text-muted">v{s.version}</span> : null}
                {s.bindingAlias && <span className="text-muted"> {t('views.profile.instructions.forBinding', { alias: s.bindingAlias })}</span>}
              </li>
            ))}
          </ol>
        </aside>
      )}
    </div>
  );
}
