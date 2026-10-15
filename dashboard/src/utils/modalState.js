// Tracks how many modals are open, so auto-refresh pauses while the operator reads or edits in a dialog
// (DASHBOARD_STYLE_AND_USABILITY.md: stop or pause auto-refresh while modals are open).

let openCount = 0;

/** Record a modal opening; returns the function that records it closing. */
export function registerModal() {
  openCount += 1;
  let closed = false;
  return () => {
    if (closed) return;
    closed = true;
    openCount = Math.max(0, openCount - 1);
  };
}

/** True while any modal is open. */
export function isModalOpen() {
  return openCount > 0;
}
