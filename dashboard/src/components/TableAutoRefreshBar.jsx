import { useTranslation } from 'react-i18next';
import AutoRefreshSelect from './AutoRefreshSelect';
import { RefreshIcon } from './Icons';
import { tip } from './Tooltip';
import { formatNumber } from '../i18n/formatters';

/**
 * Above-table control bar for non-paginated tables: total count on the left,
 * auto-refresh selector + manual refresh on the right. Keeps every table page
 * consistent even when it does not use server pagination.
 */
export default function TableAutoRefreshBar({
  totalRecords,
  autoRefreshSeconds,
  onAutoRefreshChange,
  onRefresh,
  disabled = false,
  leftSlot = null
}) {
  const { t } = useTranslation();
  return (
    <div className="table-toolbar">
      <div className="table-pagination-summary">
        {typeof totalRecords === 'number' && (
          <span>
            <strong>{formatNumber(totalRecords)}</strong>{' '}
            {t('components.table.records', { count: totalRecords })}
          </span>
        )}
        {leftSlot}
      </div>
      <div className="table-pagination-controls">
        {onAutoRefreshChange && (
          <AutoRefreshSelect value={autoRefreshSeconds ?? 0} onChange={onAutoRefreshChange} />
        )}
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
