import { useState, useRef, useLayoutEffect, useEffect, useCallback } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { MoreVerticalIcon } from './Icons';
import { tip } from './Tooltip';
import { popupLeft } from '../utils/direction';

/**
 * Three-dot row action menu. Portaled to document.body so it is never clipped by
 * table overflow. `items`: [{ label, tip, onClick, danger?, hidden? }]. `label` names the record for the trigger tooltip.
 */
export default function ActionMenu({ items = [], label = '' }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [pos, setPos] = useState({ top: 0, left: 0 });
  const triggerRef = useRef(null);
  const menuRef = useRef(null);

  const visible = items.filter((it) => !it.hidden);

  const place = useCallback(() => {
    if (!triggerRef.current) return false;
    const rect = triggerRef.current.getBoundingClientRect();
    if (rect.bottom < 0 || rect.top > window.innerHeight || !triggerRef.current.isConnected) return false;
    const menuH = visible.length * 34 + 8;
    const menuW = 180;
    let top = rect.bottom + 4;
    if (window.innerHeight - rect.bottom < menuH + 8) top = rect.top - menuH - 4;
    const left = popupLeft(rect, menuW);
    setPos((p) => (p.top === top && p.left === left ? p : { top, left }));
    return true;
  }, [visible.length]);

  useLayoutEffect(() => {
    if (open) place();
  }, [open, place]);

  useEffect(() => {
    if (!open) return undefined;
    const close = (e) => {
      if (triggerRef.current?.contains(e.target) || menuRef.current?.contains(e.target)) return;
      setOpen(false);
    };
    const closeNow = () => setOpen(false);
    // Follow the trigger when something scrolls (for example a table refreshing underneath); close only when the
    // trigger leaves the viewport.
    const onScroll = (e) => {
      if (menuRef.current?.contains(e.target)) return;
      if (!place()) setOpen(false);
    };
    const onKey = (e) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', onKey);
    window.addEventListener('scroll', onScroll, true);
    window.addEventListener('resize', closeNow);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', onKey);
      window.removeEventListener('scroll', onScroll, true);
      window.removeEventListener('resize', closeNow);
    };
  }, [open, place]);

  if (visible.length === 0) return null;

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        className="action-menu-trigger"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={t('common.generic.actions')}
        {...tip(label ? t('tips.table.actionsFor', { name: label }) : t('tips.table.actionsMenu'))}
        onClick={(e) => {
          e.stopPropagation();
          setOpen((v) => !v);
        }}
      >
        <MoreVerticalIcon />
      </button>
      {open &&
        createPortal(
          <div
            ref={menuRef}
            className="action-menu-dropdown"
            role="menu"
            style={{ top: pos.top, left: pos.left }}
            onClick={(e) => e.stopPropagation()}
          >
            {visible.map((it, i) => (
              <button
                key={i}
                type="button"
                role="menuitem"
                className={`action-menu-item ${it.danger ? 'danger' : ''}`}
                {...tip(it.tip)}
                onClick={(e) => {
                  e.stopPropagation();
                  setOpen(false);
                  it.onClick?.();
                }}
              >
                {it.label}
              </button>
            ))}
          </div>,
          document.body
        )}
    </>
  );
}
