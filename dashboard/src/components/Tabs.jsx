import { useCallback, useMemo, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';
import { TAB_PARAM } from '../config/navConfig';
import { tip } from './Tooltip';

/** Arrow, Home, and End key handling shared by both tab strips (roving tabindex). */
function useTabKeys(keys, activeKey, select, listRef) {
  return useCallback(
    (event) => {
      if (keys.length === 0) return;
      const current = keys.indexOf(activeKey);
      const rtl = listRef.current && getComputedStyle(listRef.current).direction === 'rtl';
      const forward = rtl ? 'ArrowLeft' : 'ArrowRight';
      const backward = rtl ? 'ArrowRight' : 'ArrowLeft';
      let next = -1;
      if (event.key === forward) next = (current + 1) % keys.length;
      else if (event.key === backward) next = (current - 1 + keys.length) % keys.length;
      else if (event.key === 'Home') next = 0;
      else if (event.key === 'End') next = keys.length - 1;
      if (next < 0) return;
      event.preventDefault();
      select(keys[next]);
      const el = listRef.current?.querySelector(`[data-tab-key="${keys[next]}"]`);
      if (el) el.focus();
    },
    [keys, activeKey, select, listRef]
  );
}

function TabStrip({ tabs, activeKey, onSelect, label, idPrefix, className = '' }) {
  const listRef = useRef(null);
  const keys = useMemo(() => tabs.map((tab) => tab.id), [tabs]);
  const onKeyDown = useTabKeys(keys, activeKey, onSelect, listRef);
  return (
    <div ref={listRef} className={`tabs ${className}`.trim()} role="tablist" aria-label={label} onKeyDown={onKeyDown}>
      {tabs.map((tab) => {
        const selected = activeKey === tab.id;
        return (
          <button
            key={tab.id}
            type="button"
            role="tab"
            id={idPrefix ? `${idPrefix}-tab-${tab.id}` : undefined}
            aria-controls={idPrefix ? `${idPrefix}-panel-${tab.id}` : undefined}
            aria-selected={selected}
            tabIndex={selected ? 0 : -1}
            className={`tab ${selected ? 'active' : ''}`}
            onClick={() => onSelect(tab.id)}
            {...tip(tab.tip || tab.title)}
            data-tab-key={tab.id}
          >
            {tab.label}
          </button>
        );
      })}
    </div>
  );
}

/** Accessible, locally controlled tab strip. tabs: [{ id, label, tip }]. */
export default function Tabs({ tabs, active, onChange, label }) {
  return <TabStrip tabs={tabs} activeKey={active} onSelect={onChange} label={label} />;
}

/**
 * URL-synced tabbed surface used by hub pages. The active tab lives in the query string (`?tab=`),
 * so bookmarks, deep links, and click-throughs can land on a specific tab. Only the active panel is
 * mounted. Other query parameters survive a tab switch only when they belong to the same tab, so a
 * filter for one tab never leaks into another. An unknown or hidden `?tab=` falls back to the first tab.
 *
 * tabs: [{ id, label, tip?, render: () => ReactNode }] (already filtered for visibility).
 */
export function UrlTabs({ tabs, label, idPrefix = 'hub' }) {
  const [searchParams, setSearchParams] = useSearchParams();
  const requested = searchParams.get(TAB_PARAM);
  const activeKey = tabs.some((tab) => tab.id === requested) ? requested : tabs[0]?.id;

  const select = useCallback(
    (key) => {
      if (key === activeKey) return;
      setSearchParams(new URLSearchParams({ [TAB_PARAM]: key }));
    },
    [activeKey, setSearchParams]
  );

  const activeTab = tabs.find((tab) => tab.id === activeKey);

  return (
    <div className="hub-tabs">
      <TabStrip tabs={tabs} activeKey={activeKey} onSelect={select} label={label} idPrefix={idPrefix} className="hub-tab-strip" />
      {activeTab && (
        <div role="tabpanel" id={`${idPrefix}-panel-${activeTab.id}`} aria-labelledby={`${idPrefix}-tab-${activeTab.id}`} className="hub-tab-panel">
          {activeTab.render()}
        </div>
      )}
    </div>
  );
}
