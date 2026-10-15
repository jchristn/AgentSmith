import { createContext, useContext, useEffect, useRef } from 'react';

/**
 * The tenant event stream, provided by the dashboard shell. `mode` is 'connecting', 'live' (SSE connected),
 * 'polling' (fallback), or 'stopped'. `on(event, fn)` subscribes to an event name ('*' for every event) and returns
 * an unsubscribe function. Polling ticks are delivered as the synthetic event 'poll'.
 */
const EventsContext = createContext({ mode: 'stopped', on: () => () => {} });

export const EventsProvider = EventsContext.Provider;

/** The event stream context. */
export function useEvents() {
  return useContext(EventsContext);
}

/**
 * Run `handler(data, evt)` for each named event (and on polling ticks when `includePoll` is set). The latest
 * handler is always used, so callers do not need to memoize it.
 */
export function useServerEvent(names, handler, { includePoll = false } = {}) {
  const { on } = useEvents();
  const ref = useRef(handler);
  ref.current = handler;
  const key = [].concat(names).join('|');
  useEffect(() => {
    const list = key.split('|').filter(Boolean);
    if (includePoll) list.push('poll');
    const offs = list.map((name) => on(name, (data, evt) => ref.current?.(data, evt)));
    return () => offs.forEach((off) => off());
  }, [on, key, includePoll]);
}

export default EventsContext;
