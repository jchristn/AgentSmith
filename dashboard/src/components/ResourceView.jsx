import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import PageHeader from './PageHeader';
import TableFrame from './TableFrame';
import Modal from './Modal';
import ConfirmModal from './ConfirmModal';
import JsonViewerModal from './JsonViewerModal';
import ActionMenu from './ActionMenu';
import FormFields from './FormFields';
import Explainer from './Explainer';
import FieldWarnings, { fieldWarningsOf } from './FieldWarnings';
import { tip } from './Tooltip';
import { EmptyState, ErrorBanner, UnavailableState } from './States';
import { optionalText } from '../utils/tips';
import { PlusIcon, TrashIcon } from './Icons';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';
import { useToast } from '../context/ToastContext';
import useAutoRefresh from '../hooks/useAutoRefresh';
import useDebounce from '../hooks/useDebounce';
import { DEFAULT_PAGE_SIZE, STORAGE_KEYS } from '../utils/constants';
import { buildListParams, sortLocal, SERVER_SORT_KEYS, stripForDuplicate, toLocalInput, toUtcIso } from '../utils/query';
import { errorMessage, errorDetail, isMissingEndpoint, fieldErrorMessages, withListItemErrors } from '../utils/errors';

function savedPageSize(tableId) {
  try {
    const all = JSON.parse(localStorage.getItem(STORAGE_KEYS.pageSize) || '{}');
    return all[tableId] || DEFAULT_PAGE_SIZE;
  } catch {
    return DEFAULT_PAGE_SIZE;
  }
}

function persistPageSize(tableId, size) {
  try {
    const all = JSON.parse(localStorage.getItem(STORAGE_KEYS.pageSize) || '{}');
    all[tableId] = size;
    localStorage.setItem(STORAGE_KEYS.pageSize, JSON.stringify(all));
  } catch {
    /* ignore */
  }
}

/** A free-text filter (for example a run ID) that applies after typing pauses, so each keystroke is not a request. */
function TextFilter({ filter, value, onChange, tipText, t }) {
  const [text, setText] = useState(value || '');
  const debounced = useDebounce(text, 350);
  useEffect(() => setText(value || ''), [value]);
  useEffect(() => {
    if ((debounced || '') !== (value || '')) onChange(debounced.trim());
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debounced]);
  const label = filter.label(t);
  return (
    <label className="filter-text" {...tip(tipText)}>
      <span className="sr-only">{label}</span>
      <input
        type="search"
        className="filter-search"
        value={text}
        placeholder={filter.placeholder ? filter.placeholder(t) : label}
        aria-label={label}
        data-filter={filter.name}
        {...tip(tipText)}
        onChange={(e) => setText(e.target.value)}
      />
    </label>
  );
}

/**
 * Generic record page: header with + Add, search and filter row, server-paginated table with Select Columns,
 * sortable headers, row actions (View, Edit, Duplicate, Delete, View JSON, extras), bulk delete, purpose-built
 * create/edit/view modal from field descriptors, custom confirmations, and loading/empty/error states.
 * Filters are selects (options may be a function of the lookups), date pickers, or free text (type 'text');
 * `toDuplicate(row)` replaces the generic Duplicate stripping for records whose children keep references (pipelines);
 * `search: false` hides the name search for lists the server cannot search. `endpoint` names the list route, shown
 * when the server does not expose it yet (404), together with resources.<id>.purpose.
 */
export default function ResourceView({ config }) {
  const { t, i18n } = useTranslation();
  const { apiClient, principal } = useAuth();
  const { tenantId } = useApp();
  const { addToast, startProgress, updateProgress, endProgress } = useToast();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const tableId = config.id;
  const ctx = useMemo(() => ({ apiClient, principal, tenantId, t, i18n, navigate, addToast }), [apiClient, principal, tenantId, t, i18n, navigate, addToast]);

  const [items, setItems] = useState([]);
  const [page, setPage] = useState({ pageNumber: 1, pageSize: savedPageSize(tableId), totalCount: 0 });
  const [sort, setSort] = useState({ key: config.defaultSort || 'createdUtc', dir: config.defaultSortDir || 'desc' });
  // ?search= preselects the name search, so other pages can link to one record (for example an approval's deployment).
  const [search, setSearch] = useState(() => searchParams.get('search') || '');
  const debouncedSearch = useDebounce(search, 350);
  const [filters, setFilters] = useState(() => {
    const initial = { ...(config.defaultFilters || {}) };
    for (const key of config.urlFilters || []) {
      const v = searchParams.get(key);
      if (v) initial[key] = v;
    }
    return initial;
  });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [editing, setEditing] = useState(null);
  const [fieldErrors, setFieldErrors] = useState({});
  const [saveWarnings, setSaveWarnings] = useState([]);
  const [deleteTarget, setDeleteTarget] = useState(null);
  const [bulkOpen, setBulkOpen] = useState(false);
  const [selected, setSelected] = useState([]);
  const [jsonValue, setJsonValue] = useState(null);
  const [busy, setBusy] = useState(false);
  const [autoRefreshSeconds, setAutoRefreshSeconds] = useState(config.autoRefresh ?? 0);
  const [extraModal, setExtraModal] = useState(null);
  const [options, setOptions] = useState({});
  const [step, setStep] = useState(0);
  const [lookups, setLookups] = useState({});
  const [deletePreview, setDeletePreview] = useState(null);
  // The last list response without its rows, for pages that show envelope fields such as respondingNodeId.
  const [listInfo, setListInfo] = useState(null);
  const requestSeq = useRef(0);

  // Names for ID columns (M1-20). Reloaded with the list so new records resolve too.
  const loadLookups = useCallback(() => {
    if (!apiClient || !config.loadLookups) return;
    config
      .loadLookups({ apiClient, tenantId })
      .then(setLookups)
      .catch(() => setLookups({}));
  }, [apiClient, tenantId, config]);
  useEffect(loadLookups, [loadLookups]);

  const load = useCallback(
    (pageNumber = 1, pageSize = page.pageSize) => {
      if (!apiClient) return;
      const seq = ++requestSeq.current;
      setLoading(true);
      setError(null);
      const params = buildListParams({
        pageNumber,
        pageSize,
        sortKey: SERVER_SORT_KEYS.includes(sort.key) ? sort.key : undefined,
        sortDir: sort.dir,
        search: debouncedSearch,
        filters
      });
      config.api
        .list(apiClient, tenantId, params)
        .then((r) => {
          if (seq !== requestSeq.current) return;
          setItems(r.items || []);
          setListInfo(r);
          setPage({ pageNumber: r.pageNumber || pageNumber, pageSize, totalCount: r.totalCount ?? (r.items || []).length });
          setSelected([]);
        })
        .catch((e) => seq === requestSeq.current && setError(e))
        .finally(() => seq === requestSeq.current && setLoading(false));
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [apiClient, tenantId, sort.key, sort.dir, debouncedSearch, filters, config]
  );

  useEffect(() => {
    load(1);
  }, [load]);

  useAutoRefresh(() => !editing && !jsonValue && load(page.pageNumber, page.pageSize), autoRefreshSeconds);

  const displayItems = SERVER_SORT_KEYS.includes(sort.key) ? items : sortLocal(items, sort.key, sort.dir);
  const canManage = !config.canManage || config.canManage(ctx);
  const isEditable = (row) => canManage && (config.isEditable ? config.isEditable(row) : !row?.isBuiltIn);
  const canDelete = (row) => canManage && (config.canDelete ? config.canDelete(row) : !row?.isProtected && !row?.isBuiltIn);

  const open = (mode, data) => {
    setFieldErrors({});
    setStep(0);
    setEditing({ mode, data });
    if (config.loadOptions) {
      config
        .loadOptions(ctx)
        .then((opts) => {
          setOptions(opts);
          // Defaults that depend on the pick lists (for example built-in prompt templates) apply to new records only.
          if (config.withOptions && mode === 'create') setEditing((e) => (e && e.mode === 'create' ? { ...e, data: config.withOptions(e.data, opts) } : e));
        })
        .catch(() => setOptions({}));
    }
  };
  const openRow = (row) => {
    if (config.detailPath) navigate(config.detailPath(row));
    else open(isEditable(row) && config.api.update ? 'edit' : 'view', config.toForm ? config.toForm(row) : { ...row });
  };

  const save = async () => {
    setBusy(true);
    setFieldErrors({});
    try {
      const d = editing.data;
      const validation = config.validate ? config.validate(d, t) : null;
      if (validation && Object.keys(validation).length > 0) {
        setFieldErrors(validation);
        return;
      }
      const body = config.toBody ? config.toBody(d, editing.mode) : d;
      let result;
      if (editing.mode === 'edit') result = await config.api.update(apiClient, tenantId, d.id, body);
      else result = await config.api.create(apiClient, tenantId, body);
      addToast({ type: 'success', message: t(editing.mode === 'edit' ? 'common.toasts.saved' : 'common.toasts.created') });
      // I-08: values the server stored differently (clamped) stay listed on the page until dismissed.
      setSaveWarnings(fieldWarningsOf(result));
      setEditing(null);
      if (config.afterCreate && editing.mode !== 'edit') config.afterCreate(result, { ...ctx, setExtraModal });
      load(page.pageNumber);
    } catch (e) {
      const messages = fieldErrorMessages(t, e);
      const perField = withListItemErrors(t, config.mapErrors ? config.mapErrors(messages, e) : messages);
      if (perField) setFieldErrors(perField);
      addToast({ type: 'error', message: `${errorMessage(t, e)} ${errorDetail(t, e)}`.trim() });
    } finally {
      setBusy(false);
    }
  };

  // Say what a delete takes with it before the operator confirms (M1-06, M1-21).
  const askDelete = (row) => {
    setDeleteTarget(row);
    setDeletePreview(null);
    if (config.deletePreview) {
      config
        .deletePreview(row, ctx)
        .then((p) => setDeletePreview(p ? { id: row.id, ...p } : null))
        .catch(() => setDeletePreview(null));
    }
  };

  const doDelete = async () => {
    try {
      await config.api.remove(apiClient, tenantId, deleteTarget.id);
      addToast({ type: 'success', message: t('common.toasts.deleted') });
      setDeleteTarget(null);
      load(page.pageNumber);
      loadLookups();
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
      setDeleteTarget(null);
    }
  };

  const doBulkDelete = async () => {
    let failed = 0;
    let done = 0;
    setBulkOpen(false);
    const progressId = startProgress(t('components.progress.deleting', { count: selected.length }), selected.length);
    for (const id of selected) {
      try {
        await config.api.remove(apiClient, tenantId, id);
      } catch {
        failed += 1;
      }
      done += 1;
      updateProgress(progressId, done);
    }
    endProgress(progressId);
    setBulkOpen(false);
    addToast({ type: failed ? 'warning' : 'success', message: t('common.toasts.bulkDeleted', { count: selected.length - failed, failed }) });
    load(page.pageNumber);
  };

  const columns = [
    ...config.columns({ ...ctx, lookups }),
    {
      key: 'actions',
      label: t('common.generic.actions'),
      tip: t('tips.table.actionsColumn'),
      headerClass: 'right',
      cellClass: 'actions-cell',
      pinned: true,
      render: (r) => (
        <ActionMenu
          label={r.name || r.email || r.id}
          items={[
            {
              label: t('common.actions.view'),
              tip: t(config.detailPath ? 'tips.actions.openDetail' : 'tips.actions.view'),
              onClick: () => (config.detailPath ? navigate(config.detailPath(r)) : open('view', config.toForm ? config.toForm(r) : { ...r }))
            },
            {
              label: t('common.actions.edit'),
              tip: t('tips.actions.edit'),
              hidden: !config.api.update || !isEditable(r),
              onClick: () => open('edit', config.toForm ? config.toForm(r) : { ...r })
            },
            {
              label: t('common.actions.duplicate'),
              tip: t('tips.actions.duplicate'),
              hidden: !canManage || config.canDuplicate === false || !config.api.create,
              onClick: () =>
                open(
                  'create',
                  config.toDuplicate ? config.toDuplicate(r) : stripForDuplicate(config.toForm ? config.toForm(r) : r, config.duplicateStrip || [])
                )
            },
            ...((config.rowActions && config.rowActions(r, { ...ctx, reload: () => load(page.pageNumber), setExtraModal, setJsonValue })) || []),
            { label: t('common.actions.viewJson'), tip: t('tips.actions.viewJson'), onClick: () => setJsonValue(r) },
            {
              label: t('common.actions.delete'),
              tip: t('tips.actions.delete'),
              danger: true,
              hidden: !config.api.remove || !canDelete(r),
              onClick: () => askDelete(r)
            }
          ]}
        />
      )
    }
  ];

  const prefix = `resources.${config.id}`;
  const intro = config.intro ? config.intro({ ...ctx, listInfo }) : null;
  const allFields = editing ? config.fields(editing.data, { ...ctx, mode: editing.mode, options }) : [];
  const steps = config.steps || null;
  const fields = steps ? allFields.filter((f) => (f.step || 0) === step) : allFields;
  const missing = isMissingEndpoint(error);
  const noData = !loading && !error && items.length === 0 && !debouncedSearch && Object.values(filters).every((v) => !v);

  const filterTip = (f) => (typeof f.tip === 'function' ? f.tip(t) : f.tip) || t(`tips.filters.${f.name}`);
  // Options may depend on the lookups (for example agent IDs with their names).
  const filterOptions = (f) => (typeof f.options === 'function' ? f.options(lookups, filters) : f.options || []);
  const defaults = config.defaultFilters || {};
  const filtersChanged = [...new Set([...Object.keys(filters), ...Object.keys(defaults)])].some((k) => (filters[k] || '') !== (defaults[k] || ''));
  const addTip = t(`tips.resources.${config.id}.add`, { defaultValue: t('tips.actions.add') });

  const filterSlot = (
    <div className="filter-inline">
      {config.search !== false && (
        <input
          type="search"
          className="filter-search"
          value={search}
          placeholder={t('components.filters.searchName')}
          aria-label={t('components.filters.searchName')}
          {...tip(t('tips.table.search'))}
          onChange={(e) => setSearch(e.target.value)}
        />
      )}
      {(config.filters || []).map((f) =>
        f.type === 'text' ? (
          <TextFilter
            key={f.name}
            filter={f}
            value={filters[f.name]}
            t={t}
            tipText={filterTip(f)}
            onChange={(v) => setFilters((prev) => ({ ...prev, [f.name]: v }))}
          />
        ) : f.type === 'datetime' ? (
          <label key={f.name} className="filter-date" {...tip(filterTip(f))}>
            <span>{f.label(t)}</span>
            <input
              {...tip(filterTip(f))}
              type="datetime-local"
              value={toLocalInput(filters[f.name])}
              onChange={(e) => setFilters((prev) => ({ ...prev, [f.name]: toUtcIso(e.target.value) || '' }))}
            />
          </label>
        ) : (
          <label key={f.name} className="filter-select" {...tip(filterTip(f))}>
            <span className="sr-only">{f.label(t)}</span>
            <select
              {...tip(filterTip(f))}
              aria-label={f.label(t)}
              value={filters[f.name] || ''}
              onChange={(e) => setFilters((prev) => ({ ...prev, [f.name]: e.target.value }))}
            >
              <option value="">
                {f.label(t)}: {t('common.generic.all')}
              </option>
              {filterOptions(f).map((o) => (
                <option key={o} value={o}>
                  {f.optionLabel ? f.optionLabel(o, t, lookups) : t(`enums.${f.enumGroup || f.name}.${o}`, { defaultValue: o })}
                </option>
              ))}
            </select>
          </label>
        )
      )}
      {(search || filtersChanged) && (
        <button
          type="button"
          className="button-ghost button-sm"
          {...tip(t('tips.table.clearFilters'))}
          onClick={() => {
            setSearch('');
            setFilters(config.defaultFilters || {});
          }}
        >
          {t('components.filters.clear')}
        </button>
      )}
    </div>
  );

  return (
    <div>
      <PageHeader
        title={t(`${prefix}.title`)}
        subtitle={t(`${prefix}.subtitle`)}
        actions={
          <>
            {config.headerActions && config.headerActions({ ...ctx, reload: () => load(page.pageNumber), setExtraModal })}
            {config.api.create && canManage && !missing && (
              <button
                type="button"
                className="button-primary"
                onClick={() => open('create', config.blank ? config.blank(ctx) : {})}
                data-testid="add-button"
                {...tip(addTip)}
              >
                <PlusIcon /> {t(`${prefix}.add`)}
              </button>
            )}
          </>
        }
      />

      <FieldWarnings warnings={saveWarnings} onDismiss={() => setSaveWarnings([])} testId="save-warnings" />

      {config.explainer && <Explainer topic={config.explainer} diagram={config.explainerDiagram ? config.explainerDiagram(ctx) : null} />}
      {intro && <div className="page-intro">{intro}</div>}

      {error && !missing && <ErrorBanner message={`${errorMessage(t, error)} ${errorDetail(t, error)}`.trim()} onRetry={() => load(page.pageNumber)} />}

      {missing ? (
        <div className="table-frame">
          <UnavailableState
            feature={t(`${prefix}.title`)}
            purpose={optionalText(t, `${prefix}.purpose`)}
            endpoint={config.endpoint}
            onRetry={() => load(page.pageNumber)}
          />
        </div>
      ) : noData ? (
        <div className="table-frame">
          <EmptyState
            title={t(`${prefix}.emptyTitle`)}
            body={t(`${prefix}.emptyBody`)}
            action={
              config.api.create &&
              canManage && (
                <button type="button" className="button-primary" onClick={() => open('create', config.blank ? config.blank(ctx) : {})} {...tip(addTip)}>
                  <PlusIcon /> {t(`${prefix}.add`)}
                </button>
              )
            }
          />
        </div>
      ) : (
        <TableFrame
          tableId={tableId}
          columns={columns}
          items={displayItems}
          loading={loading}
          emptyMessage={t('components.table.noMatches')}
          rowId={(r) => r.id}
          onRowClick={openRow}
          sortKey={sort.key}
          sortDir={sort.dir}
          onSort={(key) => setSort((s) => ({ key, dir: s.key === key && s.dir === 'asc' ? 'desc' : 'asc' }))}
          totalRecords={page.totalCount}
          pageNumber={page.pageNumber}
          pageSize={page.pageSize}
          onPageChange={(p) => load(p)}
          onPageSizeChange={(s) => {
            persistPageSize(tableId, s);
            setPage((prev) => ({ ...prev, pageSize: s }));
            load(1, s);
          }}
          onRefresh={() => load(page.pageNumber)}
          autoRefreshSeconds={autoRefreshSeconds}
          onAutoRefreshChange={setAutoRefreshSeconds}
          filterSlot={filterSlot}
          selectable={!!config.api.remove || !!config.bulkActions}
          selected={selected}
          onSelectChange={setSelected}
          bulkActions={
            config.bulkActions ? (
              config.bulkActions(selected, { ...ctx, reload: () => load(page.pageNumber), setExtraModal, clearSelection: () => setSelected([]) })
            ) : (
              <button type="button" className="button-danger button-sm" onClick={() => setBulkOpen(true)} {...tip(t('tips.table.bulkDelete'))}>
                <TrashIcon size={14} /> {t('components.table.bulkDelete')}
              </button>
            )
          }
        />
      )}

      <Modal
        open={!!editing}
        onClose={() => !busy && setEditing(null)}
        size={config.formSize || 'large'}
        title={editing ? t(`${prefix}.${editing.mode === 'create' ? 'create' : editing.mode === 'edit' ? 'edit' : 'view'}`) : ''}
        headerMeta={editing?.data?.id ? <code className="cell-id">{editing.data.id}</code> : null}
        footer={
          editing?.mode === 'view' ? (
            <>
              <button type="button" className="button-secondary" onClick={() => setJsonValue(editing.data)} {...tip(t('tips.actions.viewJson'))}>
                {t('common.actions.viewJson')}
              </button>
              {config.api.update && isEditable(editing.data) && (
                <button
                  type="button"
                  className="button-secondary"
                  onClick={() => setEditing((e) => ({ ...e, mode: 'edit' }))}
                  {...tip(t('tips.actions.switchToEdit'))}
                >
                  {t('common.actions.edit')}
                </button>
              )}
              <button type="button" className="button-primary" onClick={() => setEditing(null)} {...tip(t('tips.modal.close'))}>
                {t('common.actions.close')}
              </button>
            </>
          ) : (
            <>
              <button type="button" className="button-secondary" onClick={() => setEditing(null)} disabled={busy} {...tip(t('tips.modal.cancelForm'))}>
                {t('common.actions.cancel')}
              </button>
              {steps && step > 0 && (
                <button type="button" className="button-secondary" onClick={() => setStep((v) => v - 1)} {...tip(t('tips.modal.back'))}>
                  {t('common.actions.back')}
                </button>
              )}
              {steps && step < steps.length - 1 && (
                <button type="button" className="button-secondary" onClick={() => setStep((v) => v + 1)} {...tip(t('tips.modal.next'))}>
                  {t('common.actions.next')}
                </button>
              )}
              <button
                type="button"
                className="button-primary"
                onClick={save}
                disabled={busy}
                data-testid="save-button"
                {...tip(t(editing?.mode === 'edit' ? 'tips.modal.saveEdit' : 'tips.modal.saveCreate'))}
              >
                {busy ? t('common.generic.saving') : t('common.actions.save')}
              </button>
            </>
          )
        }
      >
        {editing && (
          <>
            {Object.keys(fieldErrors).length > 0 && <div className="error-banner">{t('common.errors.fixFields')}</div>}
            {editing.mode === 'view' && config.viewExtra && config.viewExtra(editing.data, ctx)}
            {editing.mode !== 'create' && config.formNotice && config.formNotice(editing.data, { ...ctx, options })}
            {steps && (
              <div className="stepper" role="tablist" aria-label={t('components.form.steps')}>
                {steps.map((s, i) => (
                  <button
                    key={s}
                    type="button"
                    role="tab"
                    aria-selected={step === i}
                    className={`stepper-step ${step === i ? 'active' : ''}`}
                    onClick={() => setStep(i)}
                    {...tip(t(`tips.resources.${config.id}.steps.${s}`))}
                  >
                    <span className="stepper-index">{i + 1}</span> {t(`${prefix}.steps.${s}`)}
                  </button>
                ))}
              </div>
            )}
            <FormFields
              fields={fields}
              data={editing.data}
              errors={fieldErrors}
              disabled={editing.mode === 'view'}
              onChange={(d) => setEditing((e) => ({ ...e, data: d }))}
              idPrefix={config.id}
            />
          </>
        )}
      </Modal>

      <ConfirmModal
        open={!!deleteTarget}
        danger
        title={t(`${prefix}.deleteTitle`)}
        message={
          deletePreview && deletePreview.id === deleteTarget?.id && deletePreview.message
            ? deletePreview.message
            : t('common.confirm.deleteNamed', { name: deleteTarget?.name || deleteTarget?.email || deleteTarget?.id })
        }
        details={deletePreview && deletePreview.id === deleteTarget?.id ? deletePreview.details : null}
        confirmLabel={t('common.actions.delete')}
        confirmTip={t('tips.confirm.delete')}
        onConfirm={doDelete}
        onCancel={() => setDeleteTarget(null)}
      />
      <ConfirmModal
        open={bulkOpen}
        danger
        title={t('components.table.bulkDelete')}
        message={t('common.confirm.bulkDelete', { count: selected.length })}
        confirmLabel={t('common.actions.delete')}
        confirmTip={t('tips.confirm.bulkDelete')}
        onConfirm={doBulkDelete}
        onCancel={() => setBulkOpen(false)}
      />
      <JsonViewerModal open={!!jsonValue} value={jsonValue} onClose={() => setJsonValue(null)} />
      {extraModal}
    </div>
  );
}
