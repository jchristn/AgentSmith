import { createPortal } from 'react-dom';

/**
 * Mouse-following tooltip rendered via portal so it escapes any overflow:hidden
 * ancestor. `pos` is { x, y } in client coordinates; `content` is JSX.
 */
export default function ChartTooltip({ pos, content }) {
  if (!pos || !content) return null;
  const left = Math.min(pos.x + 14, window.innerWidth - 292);
  const top = Math.max(pos.y - 60, 8);
  return createPortal(
    <div className="chart-tooltip" style={{ left, top }}>
      {content}
    </div>,
    document.body
  );
}
