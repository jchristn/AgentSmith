// Direction helpers for portaled, viewport-positioned popups (menus, pickers). Their coordinates are physical
// (fixed-position left/top), so the inline-end alignment of the trigger has to be resolved per direction here.

/** True when the document is laid out right to left. */
export function isRtl() {
  return typeof document !== 'undefined' && document.documentElement.dir === 'rtl';
}

/**
 * Left coordinate for a popup of `width` that lines up with the inline-end edge of `rect` (the right edge in LTR,
 * the left edge in RTL), clamped to stay `margin` pixels inside the viewport.
 */
export function popupLeft(rect, width, margin = 8) {
  const left = isRtl() ? rect.left : rect.right - width;
  return Math.max(margin, Math.min(left, window.innerWidth - width - margin));
}

/** Left coordinate for a popup of `width` that lines up with the inline-start edge of `rect`, clamped to the viewport. */
export function popupStartLeft(rect, width, margin = 8) {
  const left = isRtl() ? rect.right - width : rect.left;
  return Math.max(margin, Math.min(left, window.innerWidth - width - margin));
}
