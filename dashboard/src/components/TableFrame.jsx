import { useTranslation } from 'react-i18next';
import TablePagination from './TablePagination';
import DataTable from './DataTable';
import ColumnPicker from './ColumnPicker';
import useColumnPrefs from '../hooks/useColumnPrefs';
import { tip } from './Tooltip';
import ExportMenu from './ExportMenu';
import { exportFileName, tableCsv } from '../utils/exporters';

/**
 * Above-table control bar (pagination, page size, refresh, auto-refresh, Select Columns) plus the table, a bulk
 * action bar when rows are selected, and an optional filter row slot.
 */
export default function TableFrame({
  tableId,
  columns,
  items,
  loading = false,
  emptyMessage,
  onRowClick,
  rowId,
  sortKey,
  sortDir,
  onSort,
  totalRecords,
  pageNumber,
  pageSize,
  onPageChange,
  onPageSizeChange,
  onRefresh,
  leftSlot,
  rightSlot,
  filterSlot,
  autoRefreshSeconds,
  onAutoRefreshChange,
  selectable = false,
  selected = [],
  onSelectChange,
  bulkActions = null,
  pageSizeOptions
}) {
  const { t } = useTranslation();
  const { hidden, toggle, visibleColumns } = useColumnPrefs(tableId || 'default', columns);

  return (
    <div className="table-frame">
      <div className="table-toolbar">
        <TablePagination
          totalRecords={totalRecords}
          pageNumber={pageNumber}
          pageSize={pageSize}
          onPageChange={onPageChange}
          onPageSizeChange={onPageSizeChange}
          onRefresh={onRefresh}
          disabled={loading}
          leftSlot={leftSlot}
          rightSlot={
            <>
              {rightSlot}
              {items && items.length > 0 && (
                <ExportMenu
                  title={t('components.export.tableTitle', { table: tableId })}
                  testId={`export-table-${tableId}`}
                  fileName={(ext) => exportFileName(tableId || 'table', `page-${pageNumber || 1}`, {}, ext)}
                  csv={() => tableCsv(visibleColumns, items)}
                />
              )}
              <ColumnPicker columns={columns} hidden={hidden} onToggle={toggle} />
            </>
          }
          autoRefreshSeconds={autoRefreshSeconds}
          onAutoRefreshChange={onAutoRefreshChange}
          pageSizeOptions={pageSizeOptions}
        />
      </div>
      {filterSlot && <div className="table-filter-row">{filterSlot}</div>}
      {selectable && selected.length > 0 && (
        <div className="bulk-bar" role="region" aria-label={t('components.table.bulkActions')}>
          <span>{t('components.table.selected', { count: selected.length })}</span>
          <button type="button" className="button-secondary button-sm" onClick={() => onSelectChange?.([])} {...tip(t('tips.table.clearSelection'))}>
            {t('components.table.clearSelection')}
          </button>
          {bulkActions}
        </div>
      )}
      <DataTable
        columns={visibleColumns}
        items={items}
        loading={loading}
        emptyMessage={emptyMessage}
        onRowClick={onRowClick}
        rowId={rowId}
        sortKey={sortKey}
        sortDir={sortDir}
        onSort={onSort}
        selectable={selectable}
        selected={selected}
        onSelectChange={onSelectChange}
      />
    </div>
  );
}
