/** Copy text in secure contexts or through the legacy selection API on HTTP hosts. */
export async function copyToClipboard(value) {
  const text = String(value ?? '');
  if (navigator.clipboard && window.isSecureContext) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      /* The browser may deny clipboard permission; try the selection API. */
    }
  }
  const opener = document.activeElement;
  const selection = window.getSelection();
  const ranges = selection ? Array.from({ length: selection.rangeCount }, (_, i) => selection.getRangeAt(i).cloneRange()) : [];
  const inputSelection =
    opener && typeof opener.selectionStart === 'number'
      ? { start: opener.selectionStart, end: opener.selectionEnd, direction: opener.selectionDirection }
      : null;
  const textarea = document.createElement('textarea');
  textarea.value = text;
  textarea.readOnly = true;
  textarea.style.position = 'fixed';
  textarea.style.opacity = '0';
  textarea.style.pointerEvents = 'none';
  // Keep the selection inside the active dialog so modal focus containment cannot steal it.
  const dialogs = document.querySelectorAll('[role="dialog"][aria-modal="true"]');
  const container = opener?.closest?.('[role="dialog"][aria-modal="true"]') || dialogs[dialogs.length - 1] || document.body;
  try {
    container.appendChild(textarea);
    textarea.focus({ preventScroll: true });
    textarea.select();
    textarea.setSelectionRange(0, text.length);
    return document.execCommand('copy') === true;
  } catch {
    return false;
  } finally {
    textarea.remove();
    if (opener && document.contains(opener)) {
      opener.focus?.({ preventScroll: true });
      if (inputSelection) opener.setSelectionRange?.(inputSelection.start, inputSelection.end, inputSelection.direction);
    }
    if (selection) {
      selection.removeAllRanges();
      ranges.forEach((range) => selection.addRange(range));
    }
  }
}
