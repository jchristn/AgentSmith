import { useEffect, useRef, useId } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { XIcon } from './Icons';
import { tip } from './Tooltip';
import { registerModal } from '../utils/modalState';

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Base modal. Renders via portal. Closes on ESC and backdrop click (unless `blocking`), locks body scroll, traps
 * focus inside the dialog, returns focus to the opener on close, and pauses auto-refresh while open.
 * `size` is 'small' | 'medium' | 'large' | 'xlarge' | 'wide' | 'full'.
 */
export default function Modal({ open, onClose, title, headerMeta, children, footer, size = 'medium', blocking = false, testId }) {
  const { t } = useTranslation();
  const dialogRef = useRef(null);
  const titleId = useId();
  const closeRef = useRef(onClose);
  closeRef.current = onClose;

  useEffect(() => {
    if (!open) return undefined;
    const unregister = registerModal();
    const opener = document.activeElement;
    const onKey = (e) => {
      if (e.key === 'Escape' && !blocking) {
        e.stopPropagation();
        closeRef.current?.();
        return;
      }
      if (e.key !== 'Tab' || !dialogRef.current) return;
      const nodes = [...dialogRef.current.querySelectorAll(FOCUSABLE)].filter((n) => n.offsetParent !== null || n === document.activeElement);
      if (nodes.length === 0) return;
      const first = nodes[0];
      const last = nodes[nodes.length - 1];
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    };
    document.addEventListener('keydown', onKey);
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    // Focus the first field (or the dialog) so keyboard users start inside it.
    const focusTimer = setTimeout(() => {
      const node = dialogRef.current;
      if (!node || node.contains(document.activeElement)) return;
      const field = node.querySelector('.modal-body input:not([disabled]), .modal-body select:not([disabled]), .modal-body textarea:not([disabled])');
      (field || node).focus?.();
    }, 0);
    return () => {
      clearTimeout(focusTimer);
      document.removeEventListener('keydown', onKey);
      document.body.style.overflow = prevOverflow;
      unregister();
      if (opener && typeof opener.focus === 'function' && document.contains(opener)) opener.focus();
    };
  }, [open, blocking]);

  if (!open) return null;

  const sizeClass = size === 'medium' ? '' : `modal-${size}`;

  return createPortal(
    <div className="modal-backdrop" onClick={blocking ? undefined : onClose}>
      <div
        ref={dialogRef}
        className={`modal ${sizeClass}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        onClick={(e) => e.stopPropagation()}
        data-testid={testId}
      >
        <div className="modal-header">
          <div>
            <div className="modal-title" id={titleId}>
              {title}
            </div>
            {headerMeta && <div className="modal-header-meta">{headerMeta}</div>}
          </div>
          {!blocking && (
            <button type="button" className="icon-button" onClick={onClose} aria-label={t('common.actions.close')} {...tip(t('tips.modal.dismiss'))}>
              <XIcon />
            </button>
          )}
        </div>
        <div className="modal-body">{children}</div>
        {footer && <div className="modal-footer">{footer}</div>}
      </div>
    </div>,
    document.body
  );
}
