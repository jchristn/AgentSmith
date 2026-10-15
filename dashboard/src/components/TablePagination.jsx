import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { formatNumber } from '../i18n/formatters';
import { PAGE_SIZE_OPTIONS } from '../utils/constants';
import AutoRefreshSelect from './AutoRefreshSelect';
import { tip } from './Tooltip';
import { ChevronLeftIcon, ChevronRightIcon, ChevronsLeftIcon, ChevronsRightIcon, RefreshIcon } from './Icons';

/**
 * Above-table control bar: total count, visible range, page size, first/prev/jump/next/last, and refresh.
 * `totalRecords` is the full (server) count. Tables offer 10, 25, 50, and 100 rows; Request History passes
 * `pageSizeOptions` with 250, 500, and 1000 as well (plan section 18.1).
 */
export default function TablePagination({
  totalRecords = 0,
  pageNumber = 1,
  pageSize = 25,
  onPageChange,
  onPageSizeChange,
  onRefresh,
  disabled = false,
  leftSlot = null,
  rightSlot = null,
  autoRefreshSeconds = null,
  onAutoRefreshChange = null,
  pageSizeOptions = PAGE_SIZE_OPTIONS
}) {
  const { t } = useTranslation();
  const totalPages = Math.max(1, Math.ceil(totalRecords / pageSize));
  const [jump, setJump] = useState(String(pageNumber));

  useEffect(() => setJump(String(pageNumber)), [pageNumber]);

  const canPrev = !disabled && pageNumber > 1;
  const canNext = !disabled && pageNumber < totalPages;
  const go = (p) => onPageChange?.(Math.max(1, Math.min(totalPages, p)));

  const commitJump = () => {
    const n = parseInt(jump, 10);
    if (!Number.isNaN(n)) go(n);
    else setJump(String(pageNumber));
  };

  const from = totalRecords === 0 ? 0 : (pageNumber - 1) * pageSize + 1;
  const to = Math.min(totalRecords, pageNumber * pageSize);

  return (
    <div className="table-pagination" role="group" aria-label={t('components.table.pageSize')}>
      <div className="table-pagination-summary">
        <span>
          <strong>{formatNumber(totalRecords)}</strong> {t('components.table.records', { count: totalRecords })}
        </span>
        {totalRecords > 0 && <span className="text-muted">{t('components.table.showing', { from, to })}</span>}
        {leftSlot}
      </div>

      <div className="table-pagination-controls">
        {rightSlot}
        {onAutoRefreshChange && <AutoRefreshSelect value={autoRefreshSeconds ?? 0} onChange={onAutoRefreshChange} />}
        {onPageSizeChange && (
          <label className="table-pagination-size" {...tip(t('tips.table.pageSize'))}>
            <span>{t('components.table.pageSize')}</span>
            <select value={pageSize} disabled={disabled} {...tip(t('tips.table.pageSize'))} onChange={(e) => onPageSizeChange(Number(e.target.value))}>
              {pageSizeOptions.map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </label>
        )}
        <button
          type="button"
          className="table-pagination-btn"
          disabled={!canPrev}
          onClick={() => go(1)}
          aria-label={t('components.table.first')}
          {...tip(t('tips.table.first'))}
        >
          <ChevronsLeftIcon />
        </button>
        <button
          type="button"
          className="table-pagination-btn"
          disabled={!canPrev}
          onClick={() => go(pageNumber - 1)}
          aria-label={t('components.table.prev')}
          {...tip(t('tips.table.prev'))}
        >
          <ChevronLeftIcon />
        </button>
        <div className="table-pagination-jump">
          <input
            inputMode="numeric"
            value={jump}
            disabled={disabled}
            onChange={(e) => setJump(e.target.value.replace(/[^0-9]/g, ''))}
            onBlur={commitJump}
            onKeyDown={(e) => e.key === 'Enter' && commitJump()}
            aria-label={t('components.table.jump')}
            {...tip(t('tips.table.jump', { total: totalPages }))}
          />
          <span>{t('components.table.pageOf', { page: pageNumber, total: totalPages })}</span>
        </div>
        <button
          type="button"
          className="table-pagination-btn"
          disabled={!canNext}
          onClick={() => go(pageNumber + 1)}
          aria-label={t('components.table.next')}
          {...tip(t('tips.table.next'))}
        >
          <ChevronRightIcon />
        </button>
        <button
          type="button"
          className="table-pagination-btn"
          disabled={!canNext}
          onClick={() => go(totalPages)}
          aria-label={t('components.table.last')}
          {...tip(t('tips.table.last'))}
        >
          <ChevronsRightIcon />
        </button>
        {onRefresh && (
          <button
            type="button"
            className="table-pagination-btn"
            disabled={disabled}
            onClick={onRefresh}
            aria-label={t('components.table.refresh')}
            {...tip(t('tips.table.refresh'))}
          >
            <RefreshIcon />
          </button>
        )}
      </div>
    </div>
  );
}
