// Client for the tenant event stream (GET /v1.0/api/events, Server-Sent Events) with a polling fallback.
//
// The stream carries approval, tool invocation, health, lockdown, and restart notices. When the server does not
// expose the stream (404) or the connection keeps failing, the client switches to polling: it calls `poll()` every
// `pollMs` and lets subscribers refresh themselves. Subscribers never need to know which mode is active, but the
// shell shows it (Live or Polling) in the topbar.

import { EVENT_POLL_MS } from './constants';

const MAX_RETRIES = 3;

/**
 * Create an event stream client.
 * @param {object} options
 * @param {(handlers: object) => AbortController} options.connect Opens the stream (ApiClient.streamEvents).
 * @param {() => void} [options.poll] Called on every polling tick while the stream is down.
 * @param {(mode: 'connecting'|'live'|'polling'|'stopped') => void} [options.onMode] Mode changes.
 * @param {number} [options.pollMs] Polling interval in milliseconds.
 * @param {(fn: Function, ms: number) => any} [options.setTimer] Timer factory (tests inject a fake).
 * @param {(handle: any) => void} [options.clearTimer]
 */
export function createEventStream({ connect, poll, onMode, pollMs = EVENT_POLL_MS, setTimer = setTimeout, clearTimer = clearTimeout }) {
  const listeners = new Map();
  let controller = null;
  let pollTimer = null;
  let retryTimer = null;
  let retries = 0;
  let mode = 'stopped';
  let stopped = true;

  const setMode = (next) => {
    if (mode === next) return;
    mode = next;
    onMode?.(next);
  };

  const emit = (evt) => {
    for (const fn of listeners.get(evt.event) || []) fn(evt.data, evt);
    for (const fn of listeners.get('*') || []) fn(evt.data, evt);
  };

  const schedulePoll = () => {
    clearTimer(pollTimer);
    pollTimer = setTimer(() => {
      if (stopped) return;
      poll?.();
      schedulePoll();
    }, pollMs);
  };

  const open = () => {
    if (stopped) return;
    setMode('connecting');
    controller = connect({
      onOpen: () => {
        retries = 0;
        clearTimer(pollTimer);
        setMode('live');
      },
      onEvent: (evt) => {
        if (evt && evt.event !== 'ping' && evt.event !== 'heartbeat') emit(evt);
      },
      onEnd: (error, info) => {
        controller = null;
        if (stopped || info?.aborted) return;
        // 404, 401, and 403 mean the stream is not available to this caller: poll for good.
        const permanent = error && [401, 403, 404, 405, 501].includes(error.status);
        retries += 1;
        setMode('polling');
        poll?.();
        schedulePoll();
        if (!permanent && retries <= MAX_RETRIES) {
          clearTimer(retryTimer);
          retryTimer = setTimer(open, Math.min(30000, 1000 * 2 ** retries));
        }
      }
    });
  };

  return {
    /** Start the stream (idempotent). */
    start() {
      if (!stopped) return;
      stopped = false;
      retries = 0;
      open();
    },
    /** Stop streaming and polling. */
    stop() {
      stopped = true;
      clearTimer(pollTimer);
      clearTimer(retryTimer);
      controller?.abort();
      controller = null;
      setMode('stopped');
    },
    /** Subscribe to an event name ('*' for all). Returns an unsubscribe function. */
    on(event, fn) {
      if (!listeners.has(event)) listeners.set(event, new Set());
      listeners.get(event).add(fn);
      return () => listeners.get(event)?.delete(fn);
    },
    /** Current mode. */
    get mode() {
      return mode;
    }
  };
}
