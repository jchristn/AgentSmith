import { cloneElement, isValidElement, useEffect, useRef, useState, useLayoutEffect } from 'react';
import { createPortal } from 'react-dom';

// Shared, accessible tooltips (D-030).
//
// Any element with a `data-tip` attribute gets a tooltip. One TooltipLayer, mounted once at the app root,
// listens for pointer hover and keyboard focus anywhere in the document, waits a short delay, and renders the
// text in a portal on document.body so it is never clipped by a scrolling table or a modal. While the tooltip
// belongs to an element, the element's aria-describedby points at it, so screen readers announce the text when
// the element is focused. Escape hides it. Placement prefers above the element, flips below when there is no
// room, and is clamped to the viewport, which keeps it correct in RTL layouts as well.
//
// Use the `Tooltip` wrapper or `tip()` helper instead of the native `title` attribute so every tooltip looks and
// behaves the same in light and dark themes.

export const TOOLTIP_ID = 'garrison-tooltip';
const HOVER_DELAY_MS = 400;
const FOCUS_DELAY_MS = 150;
const GAP = 8;
const MARGIN = 8;

/** Props that attach a tooltip to any element: `<button {...tip(text)}>`. Empty text adds nothing. */
export function tip(text) {
  return text ? { 'data-tip': text } : {};
}

/** Wrap one element so it carries a tooltip. `<Tooltip text="..."><button /></Tooltip>`. */
export function Tooltip({ text, children }) {
  if (!text || !isValidElement(children)) return children ?? null;
  return cloneElement(children, { 'data-tip': text });
}

function tipTarget(node) {
  if (!node || typeof node.closest !== 'function') return null;
  return node.closest('[data-tip]');
}

function addDescribedBy(el) {
  const current = (el.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean);
  if (!current.includes(TOOLTIP_ID)) el.setAttribute('aria-describedby', [...current, TOOLTIP_ID].join(' '));
}

function removeDescribedBy(el) {
  if (!el || !el.getAttribute) return;
  const rest = (el.getAttribute('aria-describedby') || '').split(/\s+/).filter((id) => id && id !== TOOLTIP_ID);
  if (rest.length) el.setAttribute('aria-describedby', rest.join(' '));
  else el.removeAttribute('aria-describedby');
}

/** The single tooltip renderer. Mount once, near the root of the app. */
export function TooltipLayer() {
  const [state, setState] = useState({ target: null, text: '', visible: false });
  const [pos, setPos] = useState({ top: -9999, left: -9999, placement: 'top' });
  const timer = useRef(null);
  const current = useRef(null);
  const bubble = useRef(null);

  useEffect(() => {
    const clear = () => {
      if (timer.current) clearTimeout(timer.current);
      timer.current = null;
    };
    const hide = () => {
      clear();
      if (current.current) removeDescribedBy(current.current);
      current.current = null;
      setState((s) => (s.target || s.visible ? { target: null, text: '', visible: false } : s));
    };
    const show = (el, delay) => {
      const text = el.getAttribute('data-tip');
      if (!text) return;
      clear();
      if (current.current && current.current !== el) removeDescribedBy(current.current);
      current.current = el;
      addDescribedBy(el);
      setState({ target: el, text, visible: false });
      timer.current = setTimeout(() => {
        if (current.current === el && el.isConnected) setState({ target: el, text: el.getAttribute('data-tip') || text, visible: true });
      }, delay);
    };

    const onOver = (e) => {
      if (e.pointerType === 'touch') return;
      const el = tipTarget(e.target);
      if (el === current.current) return;
      if (!el) {
        if (current.current && !current.current.contains(document.activeElement)) hide();
        return;
      }
      show(el, HOVER_DELAY_MS);
    };
    const onOut = (e) => {
      const el = current.current;
      if (!el) return;
      const next = e.relatedTarget;
      if (next && el.contains(next)) return;
      if (el.contains(document.activeElement) && document.activeElement !== document.body) return;
      if (!tipTarget(next)) hide();
    };
    const onFocusIn = (e) => {
      const el = tipTarget(e.target);
      if (!el) return;
      let visibleFocus = true;
      try {
        visibleFocus = e.target.matches(':focus-visible');
      } catch {
        visibleFocus = true;
      }
      if (el !== current.current) show(el, visibleFocus ? FOCUS_DELAY_MS : HOVER_DELAY_MS);
    };
    const onFocusOut = (e) => {
      if (current.current && (!e.relatedTarget || !current.current.contains(e.relatedTarget))) hide();
    };
    const onKey = (e) => {
      if (e.key === 'Escape') hide();
    };
    const onScroll = () => {
      if (current.current) setState((s) => ({ ...s }));
    };

    document.addEventListener('pointerover', onOver, true);
    document.addEventListener('pointerout', onOut, true);
    document.addEventListener('focusin', onFocusIn, true);
    document.addEventListener('focusout', onFocusOut, true);
    document.addEventListener('keydown', onKey, true);
    document.addEventListener('pointerdown', hide, true);
    window.addEventListener('scroll', onScroll, true);
    window.addEventListener('resize', hide);
    return () => {
      clear();
      document.removeEventListener('pointerover', onOver, true);
      document.removeEventListener('pointerout', onOut, true);
      document.removeEventListener('focusin', onFocusIn, true);
      document.removeEventListener('focusout', onFocusOut, true);
      document.removeEventListener('keydown', onKey, true);
      document.removeEventListener('pointerdown', hide, true);
      window.removeEventListener('scroll', onScroll, true);
      window.removeEventListener('resize', hide);
    };
  }, []);

  useLayoutEffect(() => {
    const el = state.target;
    const node = bubble.current;
    if (!el || !node || !state.visible) return;
    if (!el.isConnected) {
      setState({ target: null, text: '', visible: false });
      return;
    }
    const r = el.getBoundingClientRect();
    const b = node.getBoundingClientRect();
    let placement = 'top';
    let top = r.top - b.height - GAP;
    if (top < MARGIN) {
      placement = 'bottom';
      top = r.bottom + GAP;
    }
    top = Math.max(MARGIN, Math.min(top, window.innerHeight - b.height - MARGIN));
    let left = r.left + r.width / 2 - b.width / 2;
    left = Math.max(MARGIN, Math.min(left, window.innerWidth - b.width - MARGIN));
    setPos((p) => (p.top === top && p.left === left && p.placement === placement ? p : { top, left, placement }));
  }, [state]);

  return createPortal(
    <div
      ref={bubble}
      id={TOOLTIP_ID}
      role="tooltip"
      className={`tooltip tooltip-${pos.placement} ${state.visible ? 'is-visible' : ''}`}
      style={state.visible ? { top: pos.top, left: pos.left } : undefined}
      aria-hidden={state.text ? undefined : 'true'}
    >
      {state.text}
    </div>,
    document.body
  );
}

export default Tooltip;
