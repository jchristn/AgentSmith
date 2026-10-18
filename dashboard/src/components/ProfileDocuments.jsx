import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import DataTable from './DataTable';
import Modal from './Modal';
import ConfirmModal from './ConfirmModal';
import { FormField } from './FormFields';
import PromptDocumentEditor from './PromptDocumentEditor';
import CodeBlock from './CodeBlock';
import CopyableId from './CopyableId';
import { ErrorPresenter, LoadingState } from './States';
import { formatNumber } from '../i18n/formatters';
import './PromptDocuments.css';

/** Profile-specific delivery policy for reusable documents and caller-safe compilation previews. */
export default function ProfileDocuments({ apiClient, profileId, onChanged }) {
  const { t, i18n } = useTranslation();
  const k = (name) => t(`documents.${name}`);
  const [result, setResult] = useState(null);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const [editing, setEditing] = useState(null);
  const [discard, setDiscard] = useState(false);
  const [documentMetadata, setDocumentMetadata] = useState(null);
  const loadGeneration = useRef(0);
  const [library, setLibrary] = useState({ items: [], totalCount: 0 });
  const [search, setSearch] = useState('');
  const [libraryPage, setLibraryPage] = useState(1);
  const [libraryLoading, setLibraryLoading] = useState(false);
  const [revisions, setRevisions] = useState([]);
  const [newDocument, setNewDocument] = useState(false);
  const [detaching, setDetaching] = useState(null);
  const [preview, setPreview] = useState(null);
  const [audience, setAudience] = useState('Authenticated');
  const [previewBusy, setPreviewBusy] = useState(false);
  const load = useCallback(async () => {
    const generation = ++loadGeneration.current;
    try {
      const r = await apiClient.listProfileDocuments(profileId);
      if (generation === loadGeneration.current) {
        setResult(r);
        setError(null);
      }
    } catch (e) {
      if (generation === loadGeneration.current) setError(e);
    }
  }, [apiClient, profileId]);
  useEffect(() => {
    setResult(null);
    setEditing(null);
    setPreview(null);
    load();
    return () => {
      loadGeneration.current += 1;
    };
  }, [load]);
  const choosing = !!editing && !editing.id;
  useEffect(() => {
    if (!choosing) return undefined;
    let current = true;
    setLibraryLoading(true);
    apiClient
      .listPromptDocuments({ pageNumber: libraryPage, pageSize: 25, search, active: true })
      .then((r) => current && setLibrary(r))
      .catch((e) => current && setError(e))
      .finally(() => current && setLibraryLoading(false));
    return () => {
      current = false;
    };
  }, [apiClient, choosing, libraryPage, search]);
  useEffect(() => {
    if (!editing?.documentId) {
      setRevisions([]);
      return undefined;
    }
    let current = true;
    setRevisions([]);
    setDocumentMetadata(null);
    apiClient
      .getPromptDocument(editing.documentId)
      .then((r) => current && setDocumentMetadata(r))
      .catch((e) => current && setError(e));
    // Pins select immutable revisions. Enumerate the bounded history rather than silently hiding older versions.
    (async () => {
      const items = [];
      let page = 1;
      for (;;) {
        const r = await apiClient.listPromptDocumentRevisions(editing.documentId, { pageNumber: page, pageSize: 100 });
        items.push(...r.items);
        if (!r.items.length || items.length >= r.totalCount) break;
        page += 1;
      }
      if (current) setRevisions(items);
    })().catch((e) => current && setError(e));
    return () => {
      current = false;
    };
  }, [apiClient, editing?.documentId]);
  const mutate = async (action) => {
    setBusy(true);
    setError(null);
    try {
      await action();
      setEditing(null);
      setDetaching(null);
      setPreview(null);
      await load();
      onChanged?.();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };
  const open = (row) =>
    setEditing(
      row
        ? { ...row }
        : {
            documentId: '',
            deliveryMode: 'Reference',
            revisionPolicy: 'Latest',
            pinnedRevision: null,
            displayOrder: result?.items.length || 0,
            enabled: true,
            allowPublicAccess: false
          }
    );
  const save = () => {
    if (!editing.documentId || (editing.revisionPolicy === 'Pinned' && !editing.pinnedRevision)) return;
    const body = {
      documentId: editing.documentId,
      deliveryMode: editing.deliveryMode,
      revisionPolicy: editing.revisionPolicy,
      pinnedRevision: editing.revisionPolicy === 'Pinned' ? Number(editing.pinnedRevision) : null,
      displayOrder: Number(editing.displayOrder),
      enabled: editing.enabled,
      allowPublicAccess: editing.allowPublicAccess,
      expectedDocumentSetVersion: result.documentSetVersion,
      ...(editing.id ? { expectedVersion: editing.version } : {})
    };
    mutate(() => (editing.id ? apiClient.updateProfileDocument(profileId, editing.id, body) : apiClient.attachProfileDocument(profileId, body)));
  };
  const move = (row, offset) => {
    const ids = result.items.map((r) => r.id);
    const index = ids.indexOf(row.id);
    [ids[index], ids[index + offset]] = [ids[index + offset], ids[index]];
    mutate(() => apiClient.reorderProfileDocuments(profileId, { expectedDocumentSetVersion: result.documentSetVersion, attachmentIds: ids }));
  };
  const field = (name, type = 'text', options = {}) => (
    <FormField
      id={`attachment-${name}`}
      field={{ name, label: k(name), tip: k(`${name}Help`), type, ...options }}
      disabled={busy || (name === 'allowPublicAccess' && documentMetadata?.canAdmin === false && !editing.allowPublicAccess)}
      error={error?.fields?.[name]}
      value={editing[name]}
      onChange={(value) => setEditing((v) => ({ ...v, [name]: value }))}
    />
  );
  const attachedIds = new Set(result?.items.map((r) => r.documentId));
  return (
    <section className="profile-documents" data-testid="profile-documents">
      <div className="panel-header">
        <h3>{k('attachments')}</h3>
        <div className="document-actions">
          <Link className="button button-secondary" to="/dashboard/instructions?tab=documents">
            {k('title')}
          </Link>
          <button className="button-primary" disabled={busy || !result} onClick={() => open()}>
            {k('attach')}
          </button>
        </div>
      </div>
      <p className="field-hint document-help">{k('deliveryHelp')}</p>
      {error && <ErrorPresenter error={error} onRetry={load} />}
      {!result && !error ? (
        <LoadingState />
      ) : (
        <DataTable
          items={result?.items || []}
          emptyMessage={k('noAttachments')}
          onRowClick={open}
          columns={[
            {
              key: 'name',
              label: k('name'),
              render: (r) => (
                <>
                  <strong>{r.name}</strong>
                  <p className="field-hint">{r.description}</p>
                  <CopyableId value={r.documentId} />
                </>
              )
            },
            { key: 'deliveryMode', label: k('deliveryMode'), render: (r) => k(r.deliveryMode === 'Inline' ? 'inline' : 'reference') },
            {
              key: 'revisionPolicy',
              label: k('revisionPolicy'),
              render: (r) => (
                <>
                  {k(r.revisionPolicy === 'Pinned' ? 'pinned' : 'latest')} · {formatNumber(r.resolvedRevision ?? r.revision, {}, i18n.language)}
                </>
              )
            },
            { key: 'contentBytes', label: k('size'), render: (r) => formatNumber(r.contentBytes, {}, i18n.language) },
            { key: 'enabled', label: k('enabled'), render: (r) => t(r.enabled ? 'common.boolean.yes' : 'common.boolean.no') },
            { key: 'allowPublicAccess', label: k('public'), render: (r) => t(r.allowPublicAccess ? 'common.boolean.yes' : 'common.boolean.no') },
            {
              key: 'actions',
              label: t('common.generic.actions'),
              render: (r) => (
                <div className="document-actions">
                  <button className="button-secondary button-sm" disabled={busy} onClick={() => open(r)}>
                    {t('common.actions.edit')}
                  </button>
                  <button
                    className="button-ghost button-sm"
                    disabled={busy || result.items[0].id === r.id}
                    onClick={() => move(r, -1)}
                    aria-label={`${k('up')} ${r.name}`}
                  >
                    {k('up')}
                  </button>
                  <button
                    className="button-ghost button-sm"
                    disabled={busy || result.items.at(-1).id === r.id}
                    onClick={() => move(r, 1)}
                    aria-label={`${k('down')} ${r.name}`}
                  >
                    {k('down')}
                  </button>
                  <button className="button-ghost button-sm" disabled={busy} onClick={() => setDetaching(r)}>
                    {k('detach')}
                  </button>
                </div>
              )
            }
          ]}
        />
      )}
      <div className="document-preview">
        <label htmlFor="document-audience">{k('audience')}</label>
        <div className="document-actions">
          <select
            id="document-audience"
            value={audience}
            onChange={(e) => {
              setAudience(e.target.value);
              setPreview(null);
            }}
          >
            <option value="Authenticated">{k('authenticated')}</option>
            <option value="Public">{k('public')}</option>
          </select>
          <button
            className="button-secondary"
            disabled={previewBusy || busy}
            onClick={async () => {
              setPreviewBusy(true);
              try {
                setPreview(await apiClient.previewProfileDocuments(profileId, audience));
                setError(null);
              } catch (e) {
                setError(e);
              } finally {
                setPreviewBusy(false);
              }
            }}
          >
            {k('preview')}
          </button>
        </div>
        {previewBusy && <LoadingState />}
        {preview && (
          <>
            <p className="field-hint">{k('previewHelp')}</p>
            {preview.issues?.map((issue, i) => (
              <p className="warning-banner" key={i}>
                {issue.message}
              </p>
            ))}
            <CodeBlock value={preview.compiledText || ''} />
            <p>
              {k('size')}: {formatNumber(preview.inlineBytes || 0, {}, i18n.language)} · {k('estimatedTokens')}:{' '}
              {formatNumber(preview.estimatedTokens || 0, {}, i18n.language)}
            </p>
            <CodeBlock value={{ manifest: preview.manifest, segments: preview.segments }} />
          </>
        )}
        <p className="field-hint">
          {k('toolHelp')} <code>{'agent_read_document'}</code>
        </p>
        <CodeBlock value={'{"documentId":"pdc_example","revision":1}'} />
      </div>
      {editing && (
        <Modal
          open
          size="large"
          title={k(editing.id ? 'editAttachment' : 'attach')}
          onClose={() => !busy && setDiscard(true)}
          testId="attachment-editor"
          footer={
            <>
              <button className="button-secondary" disabled={busy} onClick={() => setDiscard(true)}>
                {t('common.actions.cancel')}
              </button>
              <button
                className="button-primary"
                disabled={busy || !editing.documentId || (editing.revisionPolicy === 'Pinned' && !editing.pinnedRevision)}
                onClick={save}
              >
                {t('common.actions.save')}
              </button>
            </>
          }
        >
          {error && <ErrorPresenter error={error} />}
          {!editing.id && (
            <>
              <label htmlFor="attachment-search">{k('search')}</label>
              <input
                id="attachment-search"
                type="search"
                value={search}
                onChange={(e) => {
                  setSearch(e.target.value);
                  setLibraryPage(1);
                }}
              />
              {libraryLoading && <LoadingState />}
              {field('documentId', 'select', {
                required: true,
                options: library.items.filter((d) => !attachedIds.has(d.id)).map((d) => ({ value: d.id, label: `${d.name} · ${d.description}` }))
              })}
              <div className="document-actions">
                <button className="button-secondary" disabled={libraryPage === 1} onClick={() => setLibraryPage((v) => v - 1)}>
                  {t('components.table.prev')}
                </button>
                <button className="button-secondary" disabled={libraryPage * 25 >= library.totalCount} onClick={() => setLibraryPage((v) => v + 1)}>
                  {t('components.table.next')}
                </button>
                <button className="button-secondary" onClick={() => setNewDocument(true)}>
                  {k('create')}
                </button>
              </div>
            </>
          )}
          {editing.id && (
            <>
              <p>{editing.name}</p>
              <CopyableId value={editing.documentId} max={80} />
            </>
          )}
          <div className="form-grid">
            {field('deliveryMode', 'select', {
              required: true,
              options: ['Inline', 'Reference'].map((value) => ({ value, label: k(value === 'Inline' ? 'inline' : 'reference') }))
            })}
            {field('revisionPolicy', 'select', {
              required: true,
              options: ['Latest', 'Pinned'].map((value) => ({ value, label: k(value === 'Latest' ? 'latest' : 'pinned') }))
            })}
            {editing.revisionPolicy === 'Pinned' &&
              field('pinnedRevision', 'select', {
                required: true,
                options: revisions.map((r) => ({ value: r.revision, label: `${k('revision')} ${r.revision}` }))
              })}
            {field('enabled', 'checkbox')}
            {field('allowPublicAccess', 'checkbox')}
          </div>
          <p className="field-hint">{k('deliveryHelp')}</p>
          <p className="warning-banner">{k('publicWarning')}</p>
        </Modal>
      )}
      {newDocument && (
        <PromptDocumentEditor
          apiClient={apiClient}
          onClose={() => setNewDocument(false)}
          onSaved={(doc) => {
            setLibrary((r) => ({ ...r, items: [...r.items, doc] }));
            setEditing((r) => ({ ...r, documentId: doc.id }));
            setNewDocument(false);
          }}
        />
      )}
      <ConfirmModal
        open={discard}
        title={k('discard')}
        message={k('discardHelp')}
        onCancel={() => setDiscard(false)}
        onConfirm={() => {
          setDiscard(false);
          setEditing(null);
        }}
      />
      <ConfirmModal
        open={!!detaching}
        danger
        title={k('detach')}
        message={k('detachHelp')}
        busy={busy}
        onCancel={() => setDetaching(null)}
        onConfirm={() => mutate(() => apiClient.detachProfileDocument(profileId, detaching.id, detaching.version, result.documentSetVersion))}
      />
    </section>
  );
}
