import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { DownloadIcon } from './Icons';
import { tip } from './Tooltip';
import { useToast } from '../context/ToastContext';
import { downloadText, downloadBlob, svgToPng } from '../utils/exporters';
import { popupLeft } from '../utils/direction';

const TYPES = { csv: 'text/csv;charset=utf-8', json: 'application/json', svg: 'image/svg+xml' };

/**
 * Export menu for a chart or table (P5.50). `fileName(ext)` names the file; `csv()`, `json()`, and `svg()`
 * return the file content on demand; PNG is rendered from `svg()` at 2x. Formats without a getter are hidden.
 */
export default function ExportMenu({ title, fileName, csv, json, svg, testId }) {
  const { t } = useTranslation();
  const { addToast } = useToast();
  const [open, setOpen] = useState(false);
  const [pos, setPos] = useState({ top: 0, left: 0 });
  const triggerRef = useRef(null);
  const menuRef = useRef(null);
  const formats = [csv && 'csv', svg && 'png', svg && 'svg', json && 'json'].filter(Boolean);

  useLayoutEffect(() => {
    if (!open || !triggerRef.current) return;
    const r = triggerRef.current.getBoundingClientRect();
    const w = 200;
    const h = formats.length * 34 + 8;
    const top = window.innerHeight - r.bottom < h + 8 ? r.top - h - 4 : r.bottom + 4;
    setPos({ top, left: popupLeft(r, w) });
  }, [open, formats.length]);

  useEffect(() => {
    if (!open) return undefined;
    const close = (e) => {
      if (triggerRef.current?.contains(e.target) || menuRef.current?.contains(e.target)) return;
      setOpen(false);
    };
    const onKey = (e) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  const run = async (format) => {
    setOpen(false);
    try {
      if (format === 'csv') downloadText(fileName('csv'), csv(), TYPES.csv);
      else if (format === 'json') downloadText(fileName('json'), json(), TYPES.json);
      else if (format === 'svg') downloadText(fileName('svg'), svg(), TYPES.svg);
      else if (format === 'png') downloadBlob(fileName('png'), await svgToPng(svg(), 2));
    } catch (e) {
      addToast({ type: 'error', message: t('components.export.failed', { message: e?.message || '' }) });
    }
  };

  if (formats.length === 0) return null;

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        className="button-ghost button-sm export-trigger"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
        data-testid={testId}
        {...tip(t('tips.export.menu', { name: title }))}
      >
        <DownloadIcon size={14} /> {t('components.export.label')}
      </button>
      {open &&
        createPortal(
          <div ref={menuRef} className="action-menu-dropdown" role="menu" style={{ top: pos.top, left: pos.left, minWidth: 200 }}>
            {formats.map((f) => (
              <button key={f} type="button" role="menuitem" className="action-menu-item" onClick={() => run(f)} data-export={f} {...tip(t(`tips.export.${f}`))}>
                {t(`components.export.${f}`)}
              </button>
            ))}
          </div>,
          document.body
        )}
    </>
  );
}
