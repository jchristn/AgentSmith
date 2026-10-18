import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import PageHeader from '../components/PageHeader';
import TableFrame from '../components/TableFrame';
import PromptDocumentEditor from '../components/PromptDocumentEditor';
import ConfirmModal from '../components/ConfirmModal';
import JsonViewerModal from '../components/JsonViewerModal';
import CopyableId from '../components/CopyableId';
import { ErrorPresenter } from '../components/States';
import { formatNumber } from '../i18n/formatters';
import { tip } from '../components/Tooltip';

/** Reusable tenant document library, with server-side enumeration and versioned edits. */
export default function PromptDocumentsView({ apiClient: supplied }) {
  const auth = useAuth();
  const apiClient = supplied || auth.apiClient;
  const { t, i18n } = useTranslation();
  const k = (name) => t(`documents.${name}`);
  const [result, setResult] = useState({ items: [], totalCount: 0 });
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [search, setSearch] = useState('');
  const [visibility, setVisibility] = useState('');
  const [active, setActive] = useState('');
  const [sort, setSort] = useState({ key: 'name', dir: 'asc' });
  const [refresh, setRefresh] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [editor, setEditor] = useState(null);
  const [deleting, setDeleting] = useState(null);
  const [json, setJson] = useState(null);
  const reload = useCallback(() => setRefresh((v) => v + 1), []);
  useEffect(() => {
    let current = true;
    setLoading(true);
    setError(null);
    apiClient
      .listPromptDocuments({
        pageNumber: page,
        pageSize,
        search,
        visibility: visibility || undefined,
        active: active || undefined,
        ordering: sort.dir === 'asc' ? 'NameAscending' : 'NameDescending'
      })
      .then((r) => current && setResult(r))
      .catch((e) => current && setError(e))
      .finally(() => current && setLoading(false));
    return () => {
      current = false;
    };
  }, [apiClient, page, pageSize, search, visibility, active, sort, refresh]);
  const columns = [
    { key: 'name', label: k('name'), sortable: true, pinned: true },
    { key: 'description', label: k('description') },
    { key: 'currentRevision', label: k('revision'), render: (r) => formatNumber(r.currentRevision, {}, i18n.language) },
    { key: 'visibility', label: k('visibility'), render: (r) => t(`enums.visibility.${r.visibility}`) },
    { key: 'ownerUserId', label: k('owner'), render: (r) => <CopyableId value={r.ownerUserId} /> },
    { key: 'active', label: k('active'), render: (r) => t(r.active ? 'common.boolean.yes' : 'common.boolean.no') },
    { key: 'contentBytes', label: k('size'), render: (r) => formatNumber(r.contentBytes || 0, {}, i18n.language) },
    { key: 'referrerCount', label: k('referrers'), render: (r) => t(r.hasReferrers ? 'common.boolean.yes' : 'common.boolean.no') },
    {
      key: 'actions',
      label: t('common.generic.actions'),
      render: (r) => (
        <div className="document-actions">
          <button className="button-secondary button-sm" onClick={() => setEditor(r)}>
            {t(r.canUpdate === false ? 'common.actions.view' : 'common.actions.edit')}
          </button>
          <button className="button-ghost button-sm" onClick={() => setJson(r)}>
            {t('common.actions.viewJson')}
          </button>
          <button className="button-ghost button-sm" disabled={r.canDelete === false} onClick={() => setDeleting(r)}>
            {t('common.actions.delete')}
          </button>
        </div>
      )
    }
  ];
  return (
    <div className="prompt-documents">
      <PageHeader
        title={k('title')}
        subtitle={k('subtitle')}
        actions={
          <button className="button-primary" onClick={() => setEditor({})}>
            {k('create')}
          </button>
        }
      />
      {error && <ErrorPresenter error={error} onRetry={reload} />}
      <TableFrame
        tableId="prompt-documents"
        columns={columns}
        items={result.items}
        loading={loading}
        emptyMessage={k('empty')}
        onRowClick={setEditor}
        totalRecords={result.totalCount}
        pageNumber={page}
        pageSize={pageSize}
        onPageChange={setPage}
        onPageSizeChange={(v) => {
          setPageSize(v);
          setPage(1);
        }}
        onRefresh={reload}
        sortKey={sort.key}
        sortDir={sort.dir}
        onSort={(key, dir) => {
          setSort({ key, dir });
          setPage(1);
        }}
        filterSlot={
          <>
            <label>
              {k('search')}
              <input
                type="search"
                value={search}
                onChange={(e) => {
                  setSearch(e.target.value);
                  setPage(1);
                }}
                {...tip(k('search'))}
              />
            </label>
            <label>
              {k('visibility')}
              <select
                value={visibility}
                onChange={(e) => {
                  setVisibility(e.target.value);
                  setPage(1);
                }}
              >
                <option value="">{t('common.generic.all')}</option>
                {['Private', 'Tenant'].map((v) => (
                  <option key={v} value={v}>
                    {t(`enums.visibility.${v}`)}
                  </option>
                ))}
              </select>
            </label>
            <label>
              {k('active')}
              <select
                value={active}
                onChange={(e) => {
                  setActive(e.target.value);
                  setPage(1);
                }}
              >
                <option value="">{t('common.generic.all')}</option>
                <option value="true">{t('common.boolean.yes')}</option>
                <option value="false">{t('common.boolean.no')}</option>
              </select>
            </label>
          </>
        }
      />
      {editor && (
        <PromptDocumentEditor
          apiClient={apiClient}
          document={editor}
          onClose={() => setEditor(null)}
          onSaved={() => {
            setEditor(null);
            reload();
          }}
        />
      )}
      {json && <JsonViewerModal open title={k('title')} value={json} onClose={() => setJson(null)} />}
      <ConfirmModal
        open={!!deleting}
        danger
        title={k('delete')}
        message={k('deleteHelp')}
        details={deleting && <CopyableId value={deleting.id} max={80} />}
        onCancel={() => setDeleting(null)}
        onConfirm={async () => {
          try {
            await apiClient.deletePromptDocument(deleting.id, deleting.version ?? deleting.metadataVersion);
            setDeleting(null);
            reload();
          } catch (e) {
            setError(e);
            setDeleting(null);
          }
        }}
      />
    </div>
  );
}
