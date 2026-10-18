import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import ConfirmModal from './ConfirmModal';
import { FormField } from './FormFields';
import MarkdownView from './MarkdownView';
import PromptEditor from './PromptEditor';
import CopyableId from './CopyableId';
import { ErrorPresenter, LoadingState } from './States';
import { tip } from './Tooltip';
import './PromptDocuments.css';

/** Decode imported text without silently substituting invalid UTF-8 or changing Markdown whitespace. */
export async function readPromptDocumentFile(file) {
  if (!/\.(md|txt)$/i.test(file.name)) throw new Error('invalidFile');
  if (file.size > 1048576) throw new Error('oversizedFile');
  const text = new TextDecoder('utf-8', { fatal: true })
    .decode(await file.arrayBuffer())
    .replace(/^\uFEFF/, '')
    .replace(/\r\n/g, '\n');
  if (text.includes('\0')) throw new Error('invalidFile');
  return text;
}

/** Managed document editor with immutable history, literal Markdown preview, and shared-use disclosure. */
export default function PromptDocumentEditor({ apiClient, document: initial, onSaved, onClose }) {
  const { t } = useTranslation();
  const k = (name) => t(`documents.${name}`);
  const [draft, setDraft] = useState({ name: '', description: '', markdown: '', visibility: 'Private', active: true });
  const [saved, setSaved] = useState(initial?.id ? initial : null);
  const [loading, setLoading] = useState(!!initial?.id);
  const [error, setError] = useState(null);
  const [fileError, setFileError] = useState(null);
  const [fields, setFields] = useState({});
  const [busy, setBusy] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [discard, setDiscard] = useState(false);
  const [history, setHistory] = useState({ items: [], totalCount: 0 });
  const [historyPage, setHistoryPage] = useState(1);
  const [historical, setHistorical] = useState(null);
  const [referrers, setReferrers] = useState({ items: [] });
  const [retry, setRetry] = useState(0);
  const change = (name, value) => {
    setDraft((d) => ({ ...d, [name]: value }));
    setDirty(true);
  };
  useEffect(() => {
    if (!initial?.id) return undefined;
    let active = true;
    setLoading(true);
    Promise.all([apiClient.getPromptDocument(initial.id), apiClient.getPromptDocumentReferrers(initial.id)])
      .then(async ([metadata, refs]) => {
        const revision = await apiClient.getPromptDocumentRevision(initial.id, metadata.currentRevision);
        if (active) {
          setSaved(metadata);
          setDraft({
            name: metadata.name,
            description: metadata.description,
            markdown: revision.markdown,
            visibility: metadata.visibility || 'Private',
            active: metadata.active
          });
          setReferrers(Array.isArray(refs) ? { items: refs } : refs);
          setError(null);
          setDirty(false);
        }
      })
      .catch((e) => active && setError(e))
      .finally(() => active && setLoading(false));
    return () => {
      active = false;
    };
  }, [apiClient, initial?.id, retry]);
  useEffect(() => {
    if (!initial?.id) return undefined;
    let active = true;
    apiClient
      .listPromptDocumentRevisions(initial.id, { pageNumber: historyPage, pageSize: 10 })
      .then((r) => active && setHistory(r))
      .catch((e) => active && setError(e));
    return () => {
      active = false;
    };
  }, [apiClient, initial?.id, historyPage, retry]);
  const close = () => (dirty ? setDiscard(true) : onClose());
  const importFile = async (file) => {
    if (!file || saved?.canUpdate === false) return;
    setFileError(null);
    try {
      const markdown = await readPromptDocumentFile(file);
      setDraft((d) => ({ ...d, name: file.name, markdown }));
      setDirty(true);
    } catch (e) {
      setFileError(k(e.message === 'oversizedFile' ? 'oversizedFile' : 'invalidFile'));
    }
  };
  const save = async () => {
    const errors = {};
    for (const name of ['name', 'description', 'markdown']) if (!draft[name]) errors[name] = t('common.errors.required');
    setFields(errors);
    if (Object.keys(errors).length) return;
    setBusy(true);
    setError(null);
    try {
      const body = saved ? { ...draft, expectedVersion: saved.metadataVersion ?? saved.version } : draft;
      const result = saved ? await apiClient.updatePromptDocument(saved.id, body) : await apiClient.createPromptDocument(body);
      onSaved(result);
    } catch (e) {
      setError(e);
      setFields(e.fields || {});
    } finally {
      setBusy(false);
    }
  };
  const field = (name, type = 'text', options = {}) => (
    <FormField
      id={`document-${name}`}
      field={{ name, label: k(name), tip: k(`${name}Help`), type, ...options }}
      value={draft[name]}
      error={fields[name]}
      disabled={busy || saved?.canUpdate === false}
      onChange={(v) => change(name, v)}
    />
  );
  const refs = referrers?.items || referrers?.objects || [];
  return (
    <>
      <Modal
        open
        size="wide"
        title={k(saved ? 'edit' : 'create')}
        onClose={close}
        testId="document-editor"
        headerMeta={saved && <CopyableId value={saved.id} max={80} />}
        footer={
          <>
            <button className="button-secondary" onClick={close} disabled={busy}>
              {t('common.actions.cancel')}
            </button>
            <button className="button-primary" onClick={save} disabled={busy || loading || saved?.canUpdate === false}>
              {t('common.actions.save')}
            </button>
          </>
        }
      >
        {error && <ErrorPresenter error={error} onRetry={error.status === 409 ? undefined : () => setRetry((v) => v + 1)} />}
        {error?.status === 409 && (
          <p className="warning-banner">
            {k('conflict')}{' '}
            <button
              className="button-secondary"
              onClick={async () => {
                try {
                  const current = await apiClient.getPromptDocument(saved.id);
                  const revision = await apiClient.getPromptDocumentRevision(saved.id, current.currentRevision);
                  setSaved(current);
                  setHistorical(revision);
                } catch (e) {
                  setError(e);
                }
              }}
            >
              {k('compare')}
            </button>
          </p>
        )}
        {loading ? (
          <LoadingState />
        ) : (
          <>
            {saved && (
              <section className="document-referrers">
                <h3>{k('referrers')}</h3>
                <p className="field-hint">{k('sharedWarning')}</p>
                {refs.map((r) => (
                  <p key={r.agentProfileId || r.profileId}>
                    <CopyableId value={r.agentProfileId || r.profileId} /> {r.profileName || r.name} · {k(r.deliveryMode === 'Inline' ? 'inline' : 'reference')}{' '}
                    · {k(r.revisionPolicy === 'Pinned' ? 'pinned' : 'latest')} {r.allowPublicAccess && k('public')}
                  </p>
                ))}
                {referrers?.hasOtherReferrers && <p>{k('hiddenReferrers')}</p>}
                {refs.some((r) => r.allowPublicAccess) && <p className="warning-banner">{k('publicWarning')}</p>}
              </section>
            )}
            <div className="form-grid">
              {field('name', 'text', { required: true })}
              {field('visibility', 'select', {
                required: true,
                options: ['Private', 'Tenant'].map((value) => ({ value, label: t(`enums.visibility.${value}`) }))
              })}
              {field('description', 'textarea', { required: true, rows: 2 })}
              {field('active', 'checkbox')}
            </div>
            <label
              className="document-upload"
              onDragOver={(e) => e.preventDefault()}
              onDrop={(e) => {
                e.preventDefault();
                importFile(e.dataTransfer.files?.[0]);
              }}
              {...tip(k('uploadHelp'))}
            >
              {k('upload')}
              <input
                type="file"
                accept=".md,.txt,text/markdown,text/plain"
                onChange={(e) => importFile(e.target.files?.[0])}
                disabled={busy || saved?.canUpdate === false}
              />
            </label>
            {fileError && (
              <p className="error-banner" role="alert">
                {fileError}
              </p>
            )}
            <div className="document-edit-grid">
              <div>
                <PromptEditor
                  literal
                  id="document-markdown"
                  label={k('markdown')}
                  value={draft.markdown}
                  onChange={(value) => change('markdown', value)}
                  readOnly={busy || saved?.canUpdate === false}
                  error={fields.markdown}
                />
                <p className="field-hint">{k('literal')}</p>
              </div>
              <section>
                <h3>{k('preview')}</h3>
                <MarkdownView blockImages text={draft.markdown} rawToggle />
              </section>
            </div>
            {saved && (
              <section>
                <h3>{k('history')}</h3>
                <div className="document-actions">
                  {history.items.map((r) => (
                    <button
                      key={r.revision}
                      className="button-secondary"
                      onClick={async () => {
                        try {
                          setHistorical(await apiClient.getPromptDocumentRevision(saved.id, r.revision));
                        } catch (e) {
                          setError(e);
                        }
                      }}
                    >
                      {k('revision')} {r.revision}
                    </button>
                  ))}
                  <button className="button-secondary" disabled={historyPage === 1} onClick={() => setHistoryPage((v) => v - 1)}>
                    {t('components.table.prev')}
                  </button>
                  <button className="button-secondary" disabled={historyPage * 10 >= history.totalCount} onClick={() => setHistoryPage((v) => v + 1)}>
                    {t('components.table.next')}
                  </button>
                </div>
              </section>
            )}
          </>
        )}
      </Modal>
      {historical && (
        <Modal
          open
          size="large"
          title={`${k('revision')} ${historical.revision}`}
          onClose={() => setHistorical(null)}
          footer={
            <button
              className="button-primary"
              disabled={saved?.canUpdate === false}
              onClick={() => {
                change('markdown', historical.markdown);
                setDraft((d) => ({ ...d, name: historical.name || d.name, description: historical.description || d.description }));
                setHistorical(null);
              }}
            >
              {k('restore')}
            </button>
          }
        >
          <MarkdownView blockImages text={historical.markdown} rawToggle />
          <p className="field-hint">{k('restoreHelp')}</p>
        </Modal>
      )}
      <ConfirmModal open={discard} title={k('discard')} message={k('discardHelp')} onCancel={() => setDiscard(false)} onConfirm={onClose} />
    </>
  );
}
